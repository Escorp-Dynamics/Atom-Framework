#pragma warning disable S1450, CA1024

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Atom.Media;

/// <summary>
/// Буфер для видеокадра, владеющий памятью.
/// Использует ArrayPool для минимизации аллокаций.
/// </summary>
/// <remarks>
/// В отличие от <see cref="VideoFrame"/> (ref struct), этот класс можно
/// хранить в полях, передавать в async методы и возвращать из методов.
/// </remarks>
public sealed class VideoFrameBuffer : IDisposable
{
    private byte[]? rentedBuffer;
    private int plane0Offset;
    private int plane1Offset;
    private int plane2Offset;
    private int plane0Size;
    private int plane1Size;
    private int plane2Size;
    private int stride0;
    private int stride1;
    private int stride2;
    private bool isDisposed;

    /// <summary>
    /// Метаданные кадра.
    /// </summary>
    public VideoFrameInfo Info { get; private set; }

    /// <summary>
    /// Ширина кадра.
    /// </summary>
    public int Width => Info.Width;

    /// <summary>
    /// Высота кадра.
    /// </summary>
    public int Height => Info.Height;

    /// <summary>
    /// Формат пикселей.
    /// </summary>
    public VideoPixelFormat PixelFormat => Info.PixelFormat;

    /// <summary>
    /// Возвращает true, если буфер выделен.
    /// </summary>
    public bool IsAllocated => rentedBuffer is not null;

    /// <summary>
    /// Общий размер буфера в байтах.
    /// </summary>
    public int TotalSize => plane0Size + plane1Size + plane2Size;

    /// <summary>
    /// Создаёт буфер и выделяет память.
    /// </summary>
    public VideoFrameBuffer(int width, int height, VideoPixelFormat format) => Allocate(width, height, format);

    /// <summary>
    /// Создаёт буфер с метаданными.
    /// </summary>
    public VideoFrameBuffer(VideoFrameInfo info)
    {
        Allocate(info.Width, info.Height, info.PixelFormat);
        Info = info;
    }

    /// <summary>
    /// Создаёт пустой буфер (для последующего вызова Allocate).
    /// </summary>
    public VideoFrameBuffer() { }

    /// <summary>
    /// Выделяет память под кадр.
    /// </summary>
    public void Allocate(int width, int height, VideoPixelFormat format)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        FreeBuffer();
        CalculatePlaneLayout(width, height, format);
        AllocateBuffer();
        Info = new VideoFrameInfo { Width = width, Height = height, PixelFormat = format };
    }

    /// <summary>
    /// Обновляет метаданные кадра (PTS, duration, keyframe и т.д.).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateInfo(VideoFrameInfo info)
    {
        if (info.Width != Info.Width || info.Height != Info.Height || info.PixelFormat != Info.PixelFormat)
            throw new ArgumentException("Frame dimensions or format mismatch", nameof(info));

        Info = info;
    }

    /// <summary>
    /// Создаёт VideoFrame для работы с данными (ref struct view).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VideoFrame CreateFrame()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (rentedBuffer is null)
            return default;

        var info = Info;
        var planeCount = PixelFormat.GetPlaneCount();

        return planeCount switch
        {
            1 => new VideoFrame(rentedBuffer.AsSpan(plane0Offset, plane0Size), stride0, info),
            2 => new VideoFrame(
                rentedBuffer.AsSpan(plane0Offset, plane0Size), stride0,
                rentedBuffer.AsSpan(plane1Offset, plane1Size), stride1,
                info),
            _ => new VideoFrame(
                rentedBuffer.AsSpan(plane0Offset, plane0Size), stride0,
                rentedBuffer.AsSpan(plane1Offset, plane1Size), stride1,
                rentedBuffer.AsSpan(plane2Offset, plane2Size), stride2,
                info),
        };
    }

    /// <summary>
    /// Возвращает VideoFrame для работы с данными.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VideoFrame AsFrame() => CreateFrame();

    /// <summary>
    /// Возвращает ReadOnlyVideoFrame для чтения данных.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyVideoFrame AsReadOnlyFrame()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (rentedBuffer is null)
            return default;

        var info = Info;
        var planeCount = PixelFormat.GetPlaneCount();

        return planeCount switch
        {
            1 => new ReadOnlyVideoFrame(
                rentedBuffer.AsSpan(plane0Offset, plane0Size), stride0, info),
            2 => new ReadOnlyVideoFrame(
                rentedBuffer.AsSpan(plane0Offset, plane0Size), stride0,
                rentedBuffer.AsSpan(plane1Offset, plane1Size), stride1,
                info),
            _ => new ReadOnlyVideoFrame(
                rentedBuffer.AsSpan(plane0Offset, plane0Size), stride0,
                rentedBuffer.AsSpan(plane1Offset, plane1Size), stride1,
                rentedBuffer.AsSpan(plane2Offset, plane2Size), stride2,
                info),
        };
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

    /// <summary>
    /// Копирует данные из другого кадра.
    /// </summary>
    public void CopyFrom(VideoFrame source)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (source.Width != Width || source.Height != Height)
            throw new ArgumentException("Frame dimensions mismatch", nameof(source));

        var dst = CreateFrame();
        var planeCount = PixelFormat.GetPlaneCount();

        for (var i = 0; i < planeCount; i++)
            source.GetPlane(i).Data.CopyTo(dst.GetPlane(i).Data);
    }

    /// <summary>
    /// Очищает буфер (заполняет нулями).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        rentedBuffer?.AsSpan(0, TotalSize).Clear();
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

    private void AllocateBuffer()
    {
        var totalSize = plane0Size + plane1Size + plane2Size;
        rentedBuffer = ArrayPool<byte>.Shared.Rent(totalSize);
    }

    private void CalculatePlaneLayout(int width, int height, VideoPixelFormat format)
    {
        // Выравнивание по 64 байта для SIMD операций
        const int alignment = 64;

        var planeCount = format.GetPlaneCount();

        if (planeCount == 1)
            CalculatePackedLayout(width, height, format, alignment);
        else if (planeCount == 2)
            CalculateSemiPlanarLayout(width, height, alignment);
        else
            CalculatePlanarLayout(width, height, format, alignment);
    }

    private void CalculatePackedLayout(int width, int height, VideoPixelFormat format, int alignment)
    {
        var bpp = format.GetBytesPerPixel();
        stride0 = AlignUp(width * bpp, alignment);
        plane0Size = stride0 * height;
        plane0Offset = 0;

        stride1 = stride2 = 0;
        plane1Size = plane2Size = 0;
        plane1Offset = plane2Offset = 0;
    }

    private void CalculateSemiPlanarLayout(int width, int height, int alignment)
    {
        stride0 = AlignUp(width, alignment);
        plane0Size = stride0 * height;
        plane0Offset = 0;

        stride1 = AlignUp(width, alignment);
        plane1Size = stride1 * (height / 2);
        plane1Offset = plane0Size;

        stride2 = 0;
        plane2Size = 0;
        plane2Offset = 0;
    }

    private void CalculatePlanarLayout(int width, int height, VideoPixelFormat format, int alignment)
    {
        stride0 = AlignUp(width, alignment);
        plane0Size = stride0 * height;
        plane0Offset = 0;

        var (chromaW, chromaH) = GetChromaSize(width, height, format);
        stride1 = AlignUp(chromaW, alignment);
        plane1Size = stride1 * chromaH;
        plane1Offset = plane0Size;

        stride2 = stride1;
        plane2Size = plane1Size;
        plane2Offset = plane1Offset + plane1Size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlignUp(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

    private static (int Width, int Height) GetChromaSize(int width, int height, VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Yuv420P or VideoPixelFormat.Yuv420P10Le => (width / 2, height / 2),
        VideoPixelFormat.Yuv422P or VideoPixelFormat.Yuv422P10Le => (width / 2, height),
        VideoPixelFormat.Yuv444P or VideoPixelFormat.Yuv444P10Le => (width, height),
        _ => (0, 0),
    };
}
