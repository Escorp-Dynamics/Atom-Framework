using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Параметры аудиокодека.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AudioCodecParameters
{
    /// <summary>Частота дискретизации (Hz).</summary>
    public required int SampleRate { get; init; }

    /// <summary>Количество каналов.</summary>
    public required int ChannelCount { get; init; }

    /// <summary>Формат семплов.</summary>
    public required AudioSampleFormat SampleFormat { get; init; }

    /// <summary>Битрейт в битах в секунду.</summary>
    public long BitRate { get; init; }

    /// <summary>Размер фрейма (семплов на канал).</summary>
    public int FrameSize { get; init; }

    /// <summary>Channel layout (битовая маска).</summary>
    public ulong ChannelLayout { get; init; }

    /// <summary>Профиль кодека.</summary>
    public int Profile { get; init; }

    /// <summary>Extra data (AudioSpecificConfig для AAC и т.д.).</summary>
    public ReadOnlyMemory<byte> ExtraData { get; init; }
}
