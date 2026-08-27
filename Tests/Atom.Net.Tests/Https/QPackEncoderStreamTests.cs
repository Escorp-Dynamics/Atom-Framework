using System.Linq;
using System.Text;
using Atom.Net.Https.Headers;
using Atom.Net.Https.Headers.QPack;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет применение инструкций встречного потока кодировщика QPACK.
/// </summary>
/// <remarks>
/// ★ Здесь закреплена цепочка из трёх ошибок, из-за которых <c>www.google.com</c> НЕ ОТКРЫВАЛСЯ
/// по HTTP/3 вовсе, а Cloudflare при этом работал — потому что динамической таблицей пользуется
/// скупо.
///
/// Первая: декодировщик создавался с таблицей на 4096 байт, тогда как серверу мы объявляем
/// 65536. Сервер заполнял таблицу на объявленную величину, мы вытесняли записи раньше него.
///
/// Вторая: разворот Required Insert Count не вычитал единицу и опирался на счётчик НАШИХ
/// подтверждений вместо числа принятых вставок. На отстающей таблице получалось отрицательное
/// значение, проверка на блокировку его пропускала, и разбор шёл с заведомо неверной базой.
///
/// Третья, и главная: инструкции разбирались до конца буфера, а вызывающий выбрасывал кусок
/// целиком. Кусок QUIC вполне может разрезать инструкцию пополам — и тогда терялась не только
/// она, но и всё, что шло следом. Таблица расходилась с серверной навсегда.
///
/// Все три выходили наружу ОДНИМ И ТЕМ ЖЕ сообщением — «запись уже вытеснена из таблицы», — то
/// есть жалобой не на ту причину.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class QPackEncoderStreamTests
{
    private const int TestTimeoutMs = 30000;

    [Test]
    public void WholeInstructionsAreApplied()
    {
        var decoder = new QPackDecoder(4096);
        var instructions = BuildInsertions(("x-first", "one"), ("x-second", "two"));

        var consumed = decoder.ApplyEncoderInstructions(instructions);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.EqualTo(instructions.Length), "разобрано должно быть всё");
            Assert.That(decoder.InsertCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void InstructionSplitAcrossChunksIsNotLost()
    {
        // ★ Ровно тот случай, который ломал HTTP/3 у Google: граница куска QUIC приходится на
        // середину инструкции.
        var whole = BuildInsertions(("x-first", "one"), ("x-second", "two"), ("x-third", "three"));
        var cut = whole.Length - 4;

        var decoder = new QPackDecoder(4096);
        var consumed = decoder.ApplyEncoderInstructions(whole.AsSpan(0, cut));

        Assert.That(consumed, Is.LessThan(cut), "неполная инструкция не должна считаться разобранной");

        // Хвост сохраняется вызывающим и дополняется следующим куском — так и работает накопитель.
        var remainder = whole.AsSpan(consumed).ToArray();
        var second = decoder.ApplyEncoderInstructions(remainder);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(remainder.Length));
            Assert.That(decoder.InsertCount, Is.EqualTo(3), "ни одна вставка не должна потеряться");
        });
    }

    [Test]
    public void ByteByByteFeedingEndsUpWithTheSameTable()
    {
        // Крайний случай той же ошибки: каждый байт приходит отдельно. Результат обязан совпасть
        // с разбором целиком — иначе таблица зависит от нарезки, которой мы не управляем.
        var whole = BuildInsertions(("x-a", "1"), ("x-b", "22"), ("x-c", "333"), ("x-d", "4444"));

        var decoder = new QPackDecoder(4096);
        var buffer = new List<byte>();

        foreach (var value in whole)
        {
            buffer.Add(value);

            var consumed = decoder.ApplyEncoderInstructions([.. buffer]);
            buffer.RemoveRange(0, consumed);
        }

        Assert.Multiple(() =>
        {
            Assert.That(decoder.InsertCount, Is.EqualTo(4));
            Assert.That(buffer, Is.Empty, "после последнего байта нерасобранного хвоста быть не должно");
        });
    }

    [Test]
    public void CapacityInstructionIsNotCountedAsInsertion()
    {
        // Установка ёмкости — не вставка, и счётчик от неё расти не должен: на нём держится
        // разворот Required Insert Count.
        var decoder = new QPackDecoder(4096);

        var consumed = decoder.ApplyEncoderInstructions([0x20 | 0x1F, 0x21]);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.EqualTo(2));
            Assert.That(decoder.InsertCount, Is.Zero);
        });
    }

    [Test]
    public void BlockedBlockReportsBlockingInsteadOfEviction()
    {
        // Блок ссылается на вставки, которых у нас ещё нет. Это разрешённое состояние, и сообщать
        // о нём надо именно блокировкой: прежде здесь получалась «вытесненная запись», по которой
        // причину не угадать.
        var decoder = new QPackDecoder(4096);

        // Префикс блока: Required Insert Count = 3 (закодировано как 3), Base = 0.
        byte[] block = [0x03, 0x00, 0x80];

        Assert.That(() => decoder.Decode(block).ToArray(), Throws.InstanceOf<QPackBlockedException>());
    }

    /// <summary>
    /// Собирает последовательность инструкций «вставка без ссылки на имя».
    /// </summary>
    /// <param name="entries">Пары «имя, значение».</param>
    /// <returns>Готовые байты потока кодировщика.</returns>
    /// <remarks>
    /// Форма задана RFC 9204 §4.3.2: префикс <c>01</c>, длина имени в пяти битах, само имя, затем
    /// длина значения в семи битах и значение. Строки без сжатия Huffman — так проще и нагляднее.
    /// </remarks>
    private static byte[] BuildInsertions(params (string Name, string Value)[] entries)
    {
        var bytes = new List<byte>();

        foreach (var (name, value) in entries)
        {
            var nameBytes = Encoding.ASCII.GetBytes(name);
            var valueBytes = Encoding.ASCII.GetBytes(value);

            if (nameBytes.Length >= 0x1F || valueBytes.Length >= 0x7F)
                throw new ArgumentException("Проверка рассчитана на короткие строки", nameof(entries));

            bytes.Add((byte)(0x40 | nameBytes.Length));
            bytes.AddRange(nameBytes);
            bytes.Add((byte)valueBytes.Length);
            bytes.AddRange(valueBytes);
        }

        return [.. bytes];
    }
}
