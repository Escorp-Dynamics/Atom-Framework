#pragma warning disable CA1028

namespace Atom.Media;

/// <summary>
/// Возможности кодека.
/// </summary>
[Flags]
public enum CodecCapabilities : byte
{
    /// <summary>Нет возможностей.</summary>
    None = 0,

    /// <summary>Поддерживает декодирование.</summary>
    Decode = 1 << 0,

    /// <summary>Поддерживает кодирование.</summary>
    Encode = 1 << 1,

    /// <summary>Использует аппаратное ускорение.</summary>
    HardwareAccelerated = 1 << 2,

    /// <summary>Поддерживает многопоточное декодирование.</summary>
    MultiThreaded = 1 << 3,

    /// <summary>Поддерживает инкрементальное декодирование.</summary>
    Incremental = 1 << 4,

    /// <summary>Поддерживает lossless режим.</summary>
    Lossless = 1 << 5,
}
