using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tls;

/// <summary>
/// Перестановка расширений ClientHello, повторяющая поведение браузеров.
/// </summary>
/// <remarks>
/// ★ Находка, переворачивающая привычное представление о сверке отпечатков: настоящий Chrome
/// отправляет расширения В СЛУЧАЙНОМ ПОРЯДКЕ, заново на каждое рукопожатие. Снято собственным
/// приёмником, три подряд подключения одного и того же браузера к одному и тому же узлу:
///
/// <code lang="csharp">
/// ja3_hash d975c2e0a01572fe8f35762b222d25a2
/// ja3_hash caa73154b9c3f246c6558310fbfd0967
/// ja3_hash 1ce6e30da31cff5d4fed5b600cbf207e
/// </code>
///
/// Отсюда следует вывод, ради которого всё и переделано: ПОСТОЯННЫЙ ja3 сам по себе выдаёт
/// подделку. Совпадение с «эталонным» хэшем Chrome означает лишь совпадение с одной случайной
/// перестановкой из шестнадцати факториала; браузер такой хэш дважды подряд не покажет.
/// Зеркала отпечатков (tls.peet.ws и подобные) публикуют одну случайную выборку, и принимать её
/// за эталон — ошибка, которая здесь и была допущена.
///
/// Перемешивается не всё. Правило снято с тех же записей:
///
/// Подставные расширения GREASE стоят ПЕРВЫМ и ПОСЛЕДНИМ и не двигаются — их положение задано
/// библиотекой отдельно от общего списка.
///
/// Дополнение (0x0015) и общий ключ (0x0029) обязаны замыкать сообщение по самой спецификации
/// (RFC 8446, §4.2.11), поэтому закреплены всегда.
///
/// Остальные закрепляются профилем: Firefox поверх QUIC держит в хвосте параметры транспорта и
/// ECH, а всё, что перед ними, перемешивает.
///
/// Кто перемешивает, а кто нет, тоже снято замером, и здравый смысл тут подводит:
///
/// <code lang="csharp">
/// Chrome  поверх TCP  — перемешивает (16 расширений между двумя GREASE)
/// Chrome  поверх QUIC — перемешивает ВСЁ, подставных значений там нет вовсе
/// Firefox поверх TCP  — НЕ перемешивает, порядок постоянен
/// Firefox поверх QUIC — перемешивает, кроме двух последних
/// </code>
///
/// Firefox расходится сам с собой потому, что перестановка в его библиотеке (NSS) по умолчанию
/// выключена, а стек QUIC (neqo) включает её сам.
/// </remarks>
public static class ClientHelloExtensionPermutation
{
    /// <summary>Расширения, которым спецификация предписывает замыкать сообщение.</summary>
    private static readonly ushort[] AlwaysLast = [0x0015, 0x0029];

    /// <summary>
    /// Переставляет расширения, оставляя закреплённые на своих местах.
    /// </summary>
    /// <param name="extensions">Исходный список в порядке профиля.</param>
    /// <param name="anchors">Дополнительно закрепляемые идентификаторы.</param>
    /// <returns>Новый список; исходный не изменяется.</returns>
    /// <remarks>
    /// Закреплённые сохраняют СВОИ индексы, а не сдвигаются в хвост: подставное расширение
    /// посреди списка выглядело бы иначе, чем у браузера, где оно всегда обрамляет сообщение.
    ///
    /// Перестановка равномерна (Фишер — Йетс со случайным источником): предсказуемый порядок
    /// опознавался бы ровно так же, как постоянный.
    /// </remarks>
    public static IReadOnlyList<ITlsExtension> Apply(IEnumerable<ITlsExtension> extensions, IReadOnlyCollection<ushort>? anchors = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        var result = extensions.ToArray();
        if (result.Length < 2) return result;

        // Собираем индексы тех, кого можно двигать. Всё остальное остаётся ровно там, где стояло.
        var movable = new List<int>(result.Length);
        for (var index = 0; index < result.Length; index++)
        {
            if (!IsAnchored(result[index], anchors)) movable.Add(index);
        }

        if (movable.Count < 2) return result;

        // Случайность берётся ОДНИМ обращением к источнику, а не по одному на каждый шаг:
        // рукопожатий на нагрузке тысячи, и лишние вызовы криптографического генератора здесь
        // ничем не окупаются. Четыре байта на шаг делают перекос выборки по модулю неразличимым.
        Span<byte> random = stackalloc byte[64 * sizeof(uint)];
        if (movable.Count * sizeof(uint) > random.Length) random = new byte[movable.Count * sizeof(uint)];
        random = random[..(movable.Count * sizeof(uint))];
        System.Security.Cryptography.RandomNumberGenerator.Fill(random);

        for (var index = movable.Count - 1; index > 0; index--)
        {
            var pick = (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(random[(index * sizeof(uint))..]) % (uint)(index + 1));
            if (pick == index) continue;

            (result[movable[index]], result[movable[pick]]) = (result[movable[pick]], result[movable[index]]);
        }

        return result;
    }

    /// <summary>
    /// Определяет, закреплено ли расширение на своём месте.
    /// </summary>
    /// <param name="extension">Расширение.</param>
    /// <param name="anchors">Дополнительно закрепляемые идентификаторы.</param>
    /// <returns><see langword="true"/>, если двигать его нельзя.</returns>
    /// <remarks>
    /// ★ Подставное расширение опознаётся ПО ТИПУ, а не по идентификатору: значение выбирается в
    /// момент записи из случайного числа конкретного ClientHello, а до этого поле равно нулю — то
    /// есть неотличимо от имени узла (0x0000). Проверка по идентификатору закрепляла бы SNI и
    /// разбрасывала подставные значения по середине сообщения — ровно наоборот тому, что делает
    /// браузер.
    /// </remarks>
    private static bool IsAnchored(ITlsExtension extension, IReadOnlyCollection<ushort>? anchors)
    {
        if (extension is GreaseTlsExtension) return true;

        var id = extension.Id;

        // На случай, если подставное значение всё же задано явно: форма задана RFC 8701.
        if ((id & 0x0F0F) is 0x0A0A && (id >> 8) == (id & 0xFF)) return true;

        if (Array.IndexOf(AlwaysLast, id) >= 0) return true;

        return anchors is { Count: > 0 } && anchors.Contains(id);
    }
}
