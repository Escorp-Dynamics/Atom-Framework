#pragma warning disable CA1024

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Atom.Media;

/// <summary>
/// Буфер для аудиокадра, владеющий памятью.
/// </summary>
public sealed class AudioFrameBuffer : IDisposable
{
    private byte[]? rentedBuffer;
    private int channelStride;
    private bool isDisposed;

    /// <summary>
    /// Метаданные кадра.
    /// </summary>
    public AudioFrameInfo Info { get; private set; }

    /// <summary>
    /// Количество семплов на канал.
    /// </summary>
    public int SampleCount => Info.SampleCount;

    /// <summary>
    /// Количество каналов.
    /// </summary>
    public int ChannelCount => Info.ChannelCount;

    /// <summary>
    /// Формат семплов.
    /// </summary>
    public AudioSampleFormat SampleFormat => Info.SampleFormat;

    /// <summary>
    /// Возвращает true, если буфер выделен.
    /// </summary>
    public bool IsAllocated => rentedBuffer is not null;

    /// <summary>
    /// Общий размер буфера в байтах.
    /// </summary>
    public int TotalSize { get; private set; }

    /// <summary>
    /// Создаёт буфер и выделяет память.
    /// </summary>
    public AudioFrameBuffer(int sampleCount, int channelCount, int sampleRate, AudioSampleFormat format)
        => Allocate(sampleCount, channelCount, sampleRate, format);

    /// <summary>
    /// Создаёт буфер с метаданными.
    /// </summary>
    public AudioFrameBuffer(AudioFrameInfo info)
    {
        Allocate(info.SampleCount, info.ChannelCount, info.SampleRate, info.SampleFormat);
        Info = info;
    }

    /// <summary>
    /// Создаёт пустой буфер.
    /// </summary>
    public AudioFrameBuffer() { }

    /// <summary>
    /// Выделяет память под кадр.
    /// </summary>
    public void Allocate(int sampleCount, int channelCount, int sampleRate, AudioSampleFormat format)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        FreeBuffer();

        var bytesPerSample = format.GetBytesPerSample();

        if (format.IsPlanar())
        {
            const int alignment = 64;
            channelStride = AlignUp(sampleCount * bytesPerSample, alignment);
            TotalSize = channelStride * channelCount;
        }
        else
        {
            channelStride = 0;
            TotalSize = sampleCount * channelCount * bytesPerSample;
        }

        rentedBuffer = ArrayPool<byte>.Shared.Rent(TotalSize);

        Info = new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = channelCount,
            SampleRate = sampleRate,
            SampleFormat = format,
        };
    }

    /// <summary>
    /// Создаёт AudioFrame для работы с данными.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AudioFrame CreateFrame()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (rentedBuffer is null)
            return default;

        var info = Info;

        if (!SampleFormat.IsPlanar())
            return new AudioFrame(rentedBuffer.AsSpan(0, TotalSize), info);

        // Для planar формата возвращаем только стерео (2 канала)
        if (ChannelCount >= 2)
        {
            var bytesPerChannel = info.SampleCount * SampleFormat.GetBytesPerSample();
            return new AudioFrame(
                rentedBuffer.AsSpan(0, bytesPerChannel),
                rentedBuffer.AsSpan(channelStride, bytesPerChannel),
                info);
        }

        return new AudioFrame(rentedBuffer.AsSpan(0, TotalSize), info);
    }

    /// <summary>
    /// Возвращает AudioFrame для работы с данными.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AudioFrame AsFrame() => CreateFrame();

    /// <summary>
    /// Возвращает ReadOnlyAudioFrame для чтения данных.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyAudioFrame AsReadOnlyFrame()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (rentedBuffer is null)
            return default;

        var info = Info;

        if (!SampleFormat.IsPlanar())
            return new ReadOnlyAudioFrame(rentedBuffer.AsSpan(0, TotalSize), info);

        // Для planar формата создаём с отдельными каналами
        var bytesPerChannel = info.SampleCount * SampleFormat.GetBytesPerSample();

        if (ChannelCount >= 2)
        {
            return new ReadOnlyAudioFrame(
                rentedBuffer.AsSpan(0, bytesPerChannel),
                rentedBuffer.AsSpan(channelStride, bytesPerChannel),
                info);
        }

        // Mono
        return new ReadOnlyAudioFrame(rentedBuffer.AsSpan(0, bytesPerChannel), info);
    }

    /// <summary>
    /// Возвращает Span на данные указанного канала (для planar форматов).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetChannel(int channel)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!SampleFormat.IsPlanar() || rentedBuffer is null)
            return default;

        var bytesPerChannel = Info.SampleCount * SampleFormat.GetBytesPerSample();
        return rentedBuffer.AsSpan(channel * channelStride, bytesPerChannel);
    }

    /// <summary>
    /// Возвращает Span на все данные буфера.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetRawData()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return rentedBuffer is not null ? rentedBuffer.AsSpan(0, TotalSize) : default;
    }

    /// <summary>
    /// Возвращает Memory на все данные буфера.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<byte> GetRawMemory()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return (rentedBuffer?.AsMemory(0, TotalSize)) ?? default;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        FreeBuffer();
    }

    private void FreeBuffer()
    {
        if (rentedBuffer is null) return;
        ArrayPool<byte>.Shared.Return(rentedBuffer);
        rentedBuffer = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlignUp(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);
}
