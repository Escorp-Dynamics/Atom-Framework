using System.Diagnostics;

namespace Atom.Net.Quic;

/// <summary>
/// Оценка времени оборота и таймер повторной передачи (RFC 9002).
/// </summary>
/// <remarks>
/// QUIC работает поверх UDP, где потеря пакета — обычное дело, а не сбой. Без обнаружения потерь
/// один потерянный пакет подвешивает запрос до общего таймаута: сервер ждёт данных, которых не
/// получил, а мы ждём ответа, которого не будет.
///
/// Обнаружение здесь двойное, как и предписано. По подтверждениям: пакет считается потерянным,
/// когда подтверждён пакет с номером на три больше, — переупорядочивание глубже трёх пакетов
/// встречается редко. И по времени: если подтверждений нет дольше расчётного срока, отправляем
/// данные заново, не дожидаясь ничего.
///
/// Срок считается из измеренного времени оборота, а не берётся константой: на быстром канале
/// константа означала бы задержку в разы больше нужной, на медленном — лавину лишних повторов.
/// </remarks>
public sealed class QuicLossRecovery
{
    /// <summary>Порог переупорядочивания в пакетах (RFC 9002, §6.1.1).</summary>
    public const int PacketReorderingThreshold = 3;

    /// <summary>Начальная оценка оборота до первого измерения.</summary>
    private static readonly TimeSpan InitialRtt = TimeSpan.FromMilliseconds(333);

    /// <summary>Наименьший допустимый срок ожидания подтверждения.</summary>
    private static readonly TimeSpan MinimumProbeTimeout = TimeSpan.FromMilliseconds(10);

    private TimeSpan minimumRtt = TimeSpan.MaxValue;

    /// <summary>Сглаженная оценка времени оборота.</summary>
    public TimeSpan SmoothedRtt { get; private set; } = InitialRtt;

    /// <summary>Разброс времени оборота.</summary>
    public TimeSpan RttVariation { get; private set; } = InitialRtt / 2;

    /// <summary>Наименьшее наблюдавшееся время оборота.</summary>
    public TimeSpan MinimumRtt => minimumRtt == TimeSpan.MaxValue ? InitialRtt : minimumRtt;

    /// <summary>Сколько раз подряд срабатывал таймер без подтверждений.</summary>
    public int ProbeCount { get; private set; }

    /// <summary>
    /// Учитывает измеренное время оборота.
    /// </summary>
    /// <param name="latestRtt">Время от отправки подтверждённого пакета до получения подтверждения.</param>
    /// <param name="ackDelay">Задержка, о которой сообщил партнёр.</param>
    /// <remarks>
    /// Задержка партнёра вычитается: она не относится к сети и, если её не убрать, срок ожидания
    /// раздувается на величину чужой неторопливости. Вычитать её можно лишь до предела
    /// наименьшего наблюдавшегося оборота — иначе завышенная задержка сделала бы оценку
    /// отрицательной.
    /// </remarks>
    public void OnRttSample(TimeSpan latestRtt, TimeSpan ackDelay)
    {
        if (latestRtt <= TimeSpan.Zero) return;

        if (latestRtt < minimumRtt) minimumRtt = latestRtt;

        var adjusted = latestRtt;
        if (adjusted - ackDelay >= MinimumRtt) adjusted -= ackDelay;

        if (minimumRtt == latestRtt && ProbeCount is 0 && SmoothedRtt == InitialRtt)
        {
            SmoothedRtt = adjusted;
            RttVariation = adjusted / 2;
            return;
        }

        // Экспоненциальное сглаживание с весами 1/8 и 1/4 — те же, что в TCP и в RFC 9002.
        var difference = SmoothedRtt > adjusted ? SmoothedRtt - adjusted : adjusted - SmoothedRtt;

        RttVariation = (RttVariation * 3 / 4) + (difference / 4);
        SmoothedRtt = (SmoothedRtt * 7 / 8) + (adjusted / 8);
    }

    /// <summary>
    /// Возвращает срок ожидания подтверждения.
    /// </summary>
    /// <param name="peerMaxAckDelay">Наибольшая задержка подтверждения, объявленная партнёром.</param>
    /// <returns>Срок с учётом накопленных неудачных попыток.</returns>
    /// <remarks>
    /// Срок удваивается с каждой неудачной попыткой: если ответа нет, дело, скорее всего, не в
    /// единичной потере, и частые повторы только усугубляют перегрузку.
    /// </remarks>
    public TimeSpan GetProbeTimeout(TimeSpan peerMaxAckDelay)
    {
        var variation = RttVariation * 4;
        if (variation < MinimumProbeTimeout) variation = MinimumProbeTimeout;

        var timeout = SmoothedRtt + variation + peerMaxAckDelay;

        for (var attempt = 0; attempt < ProbeCount && attempt < 8; attempt++) timeout *= 2;

        return timeout;
    }

    /// <summary>
    /// Отмечает срабатывание таймера без подтверждений.
    /// </summary>
    public void OnProbeTimeout() => ProbeCount++;

    /// <summary>
    /// Сбрасывает счётчик попыток: подтверждение получено.
    /// </summary>
    public void OnAckReceived() => ProbeCount = 0;

    /// <summary>
    /// Определяет, истёк ли срок ожидания для пакета, отправленного в указанный момент.
    /// </summary>
    /// <param name="sentTimestamp">Метка времени отправки.</param>
    /// <param name="peerMaxAckDelay">Наибольшая задержка подтверждения партнёра.</param>
    /// <returns><see langword="true"/>, если пора повторять.</returns>
    public bool IsProbeDue(long sentTimestamp, TimeSpan peerMaxAckDelay)
        => Stopwatch.GetElapsedTime(sentTimestamp) >= GetProbeTimeout(peerMaxAckDelay);
}
