#pragma warning disable IDE0032

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Readonly версия <see cref="AudioFrame"/>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly ref struct ReadOnlyAudioFrame
{
    private readonly ReadOnlySpan<byte> plane0;
    private readonly ReadOnlySpan<byte> plane1;

    /// <summary>
    /// Метаданные кадра.
    /// </summary>
    public readonly AudioFrameInfo Info;

    /// <summary>
    /// Количество семплов на канал.
    /// </summary>
    public int SampleCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Info.SampleCount;
    }

    /// <summary>
    /// Количество каналов.
    /// </summary>
    public int ChannelCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Info.ChannelCount;
    }

    /// <summary>
    /// Возвращает true, если кадр пуст.
    /// </summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => plane0.IsEmpty;
    }

    /// <summary>
    /// Создаёт readonly аудиокадр для interleaved формата.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyAudioFrame(ReadOnlySpan<byte> interleavedData, AudioFrameInfo info)
    {
        plane0 = interleavedData;
        plane1 = default;
        Info = info;
    }

    /// <summary>
    /// Создаёт readonly аудиокадр для planar формата (stereo).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyAudioFrame(ReadOnlySpan<byte> channel0, ReadOnlySpan<byte> channel1, AudioFrameInfo info)
    {
        plane0 = channel0;
        plane1 = channel1;
        Info = info;
    }

    /// <summary>
    /// Возвращает interleaved данные.
    /// </summary>
    public ReadOnlySpan<byte> InterleavedData
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => plane0;
    }

    /// <summary>
    /// Возвращает данные канала по индексу (для planar форматов).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> GetChannel(int index) => index switch
    {
        0 => plane0,
        1 => plane1,
        _ => default,
    };

    /// <summary>
    /// Возвращает interleaved данные как типизированный Span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<T> GetInterleavedAs<T>() where T : unmanaged
        => MemoryMarshal.Cast<byte, T>(plane0);
}
