namespace Atom.Web.Browsing.Fingerprints;

/// <summary>
/// Представляет информацию о контексте аудио.
/// </summary>
public class AudioContextInfo
{
    /// <summary>
    /// Базовая задержка аудиосигнала.
    /// </summary>
    public double BaseLatency { get; set; }

    /// <summary>
    /// Задержка выходного сигнала.
    /// </summary>
    public double OutputLatency { get; set; }

    /// <summary>
    /// Частота дискретизации.
    /// </summary>
    public ushort SampleRate { get; set; }
}