#pragma warning disable CA1024, CA2225

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Readonly версия <see cref="VideoFrame"/>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly ref struct ReadOnlyVideoFrame
{
    private readonly ReadOnlySpan<byte> plane0;
    private readonly ReadOnlySpan<byte> plane1;
    private readonly ReadOnlySpan<byte> plane2;
    private readonly ReadOnlySpan<byte> plane3;

    private readonly int stride0;
    private readonly int stride1;
    private readonly int stride2;
    private readonly int stride3;

    /// <summary>
    /// Метаданные кадра.
    /// </summary>
    public readonly VideoFrameInfo Info;

    /// <summary>
    /// Ширина кадра.
    /// </summary>
    public int Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Info.Width;
    }

    /// <summary>
    /// Высота кадра.
    /// </summary>
    public int Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Info.Height;
    }

    /// <summary>
    /// Формат пикселей.
    /// </summary>
    public VideoPixelFormat PixelFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Info.PixelFormat;
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
    /// Создаёт readonly видеокадр из VideoFrame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyVideoFrame(in VideoFrame frame)
    {
        plane0 = frame.GetPlane(0).Data;
        stride0 = frame.GetPlane(0).Stride;
        plane1 = frame.GetPlane(1).Data;
        stride1 = frame.GetPlane(1).Stride;
        plane2 = frame.GetPlane(2).Data;
        stride2 = frame.GetPlane(2).Stride;
        plane3 = frame.GetPlane(3).Data;
        stride3 = frame.GetPlane(3).Stride;
        Info = frame.Info;
    }

    /// <summary>
    /// Создаёт readonly видеокадр для packed формата.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyVideoFrame(ReadOnlySpan<byte> data, int stride, VideoFrameInfo info)
    {
        plane0 = data;
        stride0 = stride;
        plane1 = default;
        plane2 = default;
        plane3 = default;
        stride1 = 0;
        stride2 = 0;
        stride3 = 0;
        Info = info;
    }

    /// <summary>
    /// Создаёт readonly видеокадр для 2-плоскостных форматов (NV12, NV21, P010).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyVideoFrame(
        ReadOnlySpan<byte> plane0, int stride0,
        ReadOnlySpan<byte> plane1, int stride1,
        VideoFrameInfo info)
    {
        this.plane0 = plane0;
        this.stride0 = stride0;
        this.plane1 = plane1;
        this.stride1 = stride1;
        plane2 = default;
        plane3 = default;
        stride2 = 0;
        stride3 = 0;
        Info = info;
    }

    /// <summary>
    /// Создаёт readonly видеокадр для 3-плоскостных форматов (YUV420P, YUV422P, YUV444P).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyVideoFrame(
        ReadOnlySpan<byte> plane0, int stride0,
        ReadOnlySpan<byte> plane1, int stride1,
        ReadOnlySpan<byte> plane2, int stride2,
        VideoFrameInfo info)
    {
        this.plane0 = plane0;
        this.stride0 = stride0;
        this.plane1 = plane1;
        this.stride1 = stride1;
        this.plane2 = plane2;
        this.stride2 = stride2;
        plane3 = default;
        stride3 = 0;
        Info = info;
    }

    /// <summary>
    /// Возвращает данные для packed формата.
    /// </summary>
    public ReadOnlyPlane<byte> PackedData
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(plane0, stride0, Width * PixelFormat.GetBytesPerPixel(), Height);
    }

    /// <summary>
    /// Возвращает плоскость Y.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyPlane<byte> GetPlaneY() => new(plane0, stride0, Width, Height);

    /// <summary>
    /// Возвращает плоскость U (Cb) для YUV форматов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyPlane<byte> GetPlaneU()
    {
        var (w, h) = GetChromaSize();
        return new ReadOnlyPlane<byte>(plane1, stride1, w, h);
    }

    /// <summary>
    /// Возвращает плоскость V (Cr) для YUV форматов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyPlane<byte> GetPlaneV()
    {
        var (w, h) = GetChromaSize();
        return new ReadOnlyPlane<byte>(plane2, stride2, w, h);
    }

    /// <summary>
    /// Возвращает interleaved UV плоскость для NV12/NV21.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyPlane<byte> GetPlaneUV() => new(plane1, stride1, Width, Height / 2);

    /// <summary>
    /// Возвращает размеры chroma плоскостей.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int Width, int Height) GetChromaSize() => PixelFormat switch
    {
        VideoPixelFormat.Yuv420P or VideoPixelFormat.Yuv420P10Le or
        VideoPixelFormat.Nv12 or VideoPixelFormat.Nv21 or VideoPixelFormat.P010Le
            => (Width / 2, Height / 2),
        VideoPixelFormat.Yuv422P or VideoPixelFormat.Yuv422P10Le
            => (Width / 2, Height),
        VideoPixelFormat.Yuv444P or VideoPixelFormat.Yuv444P10Le
            => (Width, Height),
        _ => (0, 0),
    };

    /// <summary>
    /// Неявное преобразование из VideoFrame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ReadOnlyVideoFrame(in VideoFrame frame) => new(frame);
}
