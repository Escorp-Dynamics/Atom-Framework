using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.Fingerprints;

/// <summary>
/// Представляет информацию об оборудовании.
/// </summary>
public class HardwareInfo
{
    /// <summary>
    /// Количество потоков ЦП.
    /// </summary>
    [JsonPropertyName("hardwareConcurrency")]
    public byte ThreadCount { get; set; }

    /// <summary>
    /// Размер оперативной памяти (в гигабайтах).
    /// </summary>
    /// <value></value>
    [JsonPropertyName("deviceMemory")]
    public byte MemorySize { get; set; }

    /// <summary>
    /// Указывает, поддерживается ли технология HLS.
    /// </summary>
    public bool IsHLSSupported { get; set; }

    /// <summary>
    /// Информация о контексте аудио.
    /// </summary>
    public AudioContextInfo AudioContext { get; set; } = new();
}