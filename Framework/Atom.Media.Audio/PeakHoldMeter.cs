namespace Atom.Media.Audio;

/// <summary>
/// Измеритель пикового уровня с удержанием и экспоненциальным затуханием.
/// При поступлении нового пика — мгновенный переход. После удержания
/// (<see cref="HoldTimeMs"/>) — плавное затухание со скоростью
/// <see cref="DecayRateDbPerSecond"/> дБ/с.
/// </summary>
public sealed class PeakHoldMeter
{
    private float holdPeak;
    private long holdTimestamp;

    /// <summary>
    /// Время удержания пика в миллисекундах перед началом затухания.
    /// </summary>
    public float HoldTimeMs { get; set; } = 1500.0f;

    /// <summary>
    /// Скорость затухания пика в децибелах в секунду.
    /// </summary>
    public float DecayRateDbPerSecond { get; set; } = 20.0f;

    /// <summary>
    /// Текущий удерживаемый пиковый уровень (с учётом затухания).
    /// </summary>
    public float HoldPeak => Volatile.Read(ref holdPeak);

    /// <summary>
    /// Обновляет состояние метра новым мгновенным пиком.
    /// </summary>
    /// <param name="instantPeak">Мгновенный пиковый уровень [0.0, 1.0+].</param>
    public void Update(float instantPeak)
    {
        var currentHold = Volatile.Read(ref holdPeak);
        var now = Environment.TickCount64;

        if (instantPeak >= currentHold)
        {
            Volatile.Write(ref holdPeak, instantPeak);
            Volatile.Write(ref holdTimestamp, now);
            return;
        }

        var elapsed = now - Volatile.Read(ref holdTimestamp);
        if (elapsed <= (long)HoldTimeMs) return;

        var decayMs = elapsed - (long)HoldTimeMs;
        var decayDb = DecayRateDbPerSecond * (float)(decayMs / 1000.0);
        var decayLinear = MathF.Pow(10.0f, -decayDb / 20.0f);
        var decayed = currentHold * decayLinear;
        Volatile.Write(ref holdPeak, Math.Max(decayed, instantPeak));
    }

    /// <summary>
    /// Сбрасывает удерживаемый пик в 0.
    /// </summary>
    public void Reset()
    {
        Volatile.Write(ref holdPeak, 0.0f);
        Volatile.Write(ref holdTimestamp, 0L);
    }
}
