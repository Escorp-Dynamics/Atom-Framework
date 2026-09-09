using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Atom.Net.Https;

/// <summary>
/// Запоминает объявленные сервером альтернативные службы (RFC 7838, заголовок <c lang="text">alt-svc</c>).
/// </summary>
/// <remarks>
/// ★ Это ЕДИНСТВЕННЫЙ способ, которым HTTP/3 достаётся на практике. Обратиться по HTTP/3 наугад
/// нельзя: узел может его не поддерживать, а UDP-порт — оказаться закрытым, и запрос уйдёт в
/// никуда. Поэтому браузер сначала идёт по HTTP/2, читает в ответе <c lang="text">alt-svc: h3=":443"</c> и
/// ТОЛЬКО ПОСЛЕ ЭТОГО переключается на HTTP/3 — для следующих запросов к тому же узлу.
///
/// Отсутствие такого перехода наблюдаемо: браузер, который на весь сеанс остаётся на HTTP/2 там,
/// где сервер объявил поддержку HTTP/3, ведёт себя не как браузер. И это же потеря скорости —
/// замер на <c lang="text">cloudflare-quic.com</c> даёт 292 мс по HTTP/3 против 586 мс по HTTP/2.
///
/// Хранится не адрес, а факт поддержки и срок его действия: <c lang="text">ma</c> задаёт время жизни записи в
/// секундах, и по его истечении объявление перестаёт быть основанием — сервер вправе выключить
/// HTTP/3 в любой момент.
/// </remarks>
public sealed class AlternativeServiceCache
{
    /// <summary>Через сколько можно снова пробовать HTTP/3 после первой неудачи.</summary>
    private static readonly TimeSpan InitialPenalty = TimeSpan.FromMinutes(5);

    /// <summary>Предел отсрочки: дальше наказание не растёт.</summary>
    private static readonly TimeSpan MaximumPenalty = TimeSpan.FromHours(24);

    /// <summary>Сколько узлов держим в списке неудачных, прежде чем чистить самые старые.</summary>
    private const int MaximumBrokenEntries = 4096;

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Broken> broken = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Разбирает и запоминает объявление сервера.
    /// </summary>
    /// <param name="origin">Узел с портом, к которому относится объявление.</param>
    /// <param name="headerValue">Значение заголовка <c lang="text">alt-svc</c>.</param>
    public void Remember(string origin, string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(headerValue)) return;

        // clear отменяет все прежние объявления узла — и это тоже надо соблюдать, иначе мы
        // продолжим ходить по HTTP/3 туда, где его только что выключили.
        if (headerValue.Contains("clear", StringComparison.OrdinalIgnoreCase))
        {
            entries.TryRemove(origin, out _);
            return;
        }

        if (!TryFindHttp3(headerValue, out var lifetime)) return;

        entries[origin] = new Entry(Stopwatch.GetTimestamp(), lifetime);
    }

    /// <summary>
    /// Сообщает, объявлял ли узел поддержку HTTP/3.
    /// </summary>
    /// <param name="origin">Узел с портом.</param>
    /// <returns><see langword="true"/>, если объявление есть и не истекло.</returns>
    public bool SupportsHttp3(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || !entries.TryGetValue(origin, out var entry)) return false;

        if (Stopwatch.GetElapsedTime(entry.Remembered) >= entry.Lifetime)
        {
            entries.TryRemove(origin, out _);
            return false;
        }

        return !IsBroken(origin);
    }

    /// <summary>
    /// Отмечает, что обращение по HTTP/3 к узлу не удалось.
    /// </summary>
    /// <param name="origin">Узел с портом.</param>
    /// <remarks>
    /// ★ Прежде здесь объявление просто ЗАБЫВАЛОСЬ — и это не работало вовсе, потому что тот же
    /// заголовок <c lang="text">alt-svc</c> приходит в ответе по HTTP/2 и тут же записывает его обратно.
    /// Получался вечный круг: каждый следующий запрос снова пробовал HTTP/3, снова ждал полного
    /// времени ожидания соединения и снова откатывался.
    ///
    /// Замер на <c lang="text">www.nature.com</c> (объявляет <c lang="text">h3</c>, но UDP до него не доходит):
    /// первый запрос 611 мс, каждый следующий — РОВНО 10 секунд, то есть время ожидания целиком.
    /// На тысячах узлов, где HTTP/3 объявлен и недоступен — а это обычное дело за корпоративным
    /// экраном, у части провайдеров и всегда при работе через прокси, — модуль работал бы в
    /// десятки раз медленнее без единой ошибки в журнале.
    ///
    /// Браузеры решают это списком «сломанных» альтернатив с растущей отсрочкой; здесь так же:
    /// пять минут после первой неудачи, дальше удвоение до суток. Успешное соединение отсрочку
    /// снимает.
    /// </remarks>
    public void MarkHttp3Broken(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return;

        broken.AddOrUpdate(
            origin,
            static _ => new Broken(Stopwatch.GetTimestamp(), InitialPenalty),
            static (_, previous) =>
            {
                var doubled = previous.Penalty + previous.Penalty;

                return new Broken(Stopwatch.GetTimestamp(), doubled > MaximumPenalty ? MaximumPenalty : doubled);
            });

        if (broken.Count > MaximumBrokenEntries) EvictExpiredBroken();
    }

    /// <summary>
    /// Снимает отметку о неудаче: HTTP/3 к узлу снова работает.
    /// </summary>
    /// <param name="origin">Узел с портом.</param>
    public void MarkHttp3Working(string origin)
    {
        if (!string.IsNullOrWhiteSpace(origin)) broken.TryRemove(origin, out _);
    }

    /// <summary>
    /// Сообщает, отложены ли попытки HTTP/3 к узлу.
    /// </summary>
    /// <param name="origin">Узел с портом.</param>
    /// <returns><see langword="true"/>, если пробовать рано.</returns>
    public bool IsBroken(string origin)
    {
        if (!broken.TryGetValue(origin, out var mark)) return false;

        if (Stopwatch.GetElapsedTime(mark.Marked) < mark.Penalty) return true;

        // Срок вышел — даём ещё попытку, но отметку НЕ удаляем: если она снова не удастся,
        // отсрочка обязана вырасти, а не начаться заново с пяти минут.
        return false;
    }

    /// <summary>
    /// Забывает объявление узла.
    /// </summary>
    /// <param name="origin">Узел с портом.</param>
    public void Forget(string origin) => entries.TryRemove(origin, out _);

    /// <summary>
    /// Убирает из списка неудачных те записи, чей срок давно вышел.
    /// </summary>
    /// <remarks>
    /// Ключ здесь — узел, а узлов за долгую работу бывают десятки тысяч. Без чистки список рос бы
    /// вместе с числом посещённых сайтов и никогда не уменьшался.
    /// </remarks>
    private void EvictExpiredBroken()
    {
        foreach (var pair in broken)
        {
            if (Stopwatch.GetElapsedTime(pair.Value.Marked) >= pair.Value.Penalty) broken.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>
    /// Ищет в объявлении службу HTTP/3 и её срок жизни.
    /// </summary>
    /// <param name="headerValue">Значение заголовка.</param>
    /// <param name="lifetime">Срок жизни записи.</param>
    /// <returns><see langword="true"/>, если HTTP/3 объявлен.</returns>
    /// <remarks>
    /// Заголовок перечисляет службы через запятую, каждая — вида <c lang="text">протокол="узел:порт"</c> с
    /// необязательными параметрами. Нас интересует только протокол <c lang="text">h3</c>; его черновые
    /// версии (<c lang="text">h3-29</c> и подобные) намеренно НЕ принимаются: мы говорим на окончательной.
    /// </remarks>
    private static bool TryFindHttp3(string headerValue, out TimeSpan lifetime)
    {
        lifetime = TimeSpan.FromHours(24);

        var found = false;

        foreach (var service in headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = service.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0) continue;

            if (!service.AsSpan(0, equals).Trim().Equals("h3", StringComparison.OrdinalIgnoreCase)) continue;

            found = true;

            foreach (var parameter in service.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!parameter.StartsWith("ma=", StringComparison.OrdinalIgnoreCase)) continue;

                if (long.TryParse(parameter.AsSpan(3), CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                    lifetime = TimeSpan.FromSeconds(seconds);
            }

            break;
        }

        return found;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct Entry(long Remembered, TimeSpan Lifetime);

    /// <summary>Отметка о неудачной попытке HTTP/3 и текущая отсрочка.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct Broken(long Marked, TimeSpan Penalty);
}
