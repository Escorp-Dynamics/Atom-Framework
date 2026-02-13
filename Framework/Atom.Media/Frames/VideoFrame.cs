#pragma warning disable CA1024

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Представляет видеокадр как zero-copy view над буфером.
/// Не владеет памятью — время жизни ограничено scope.
/// </summary>
/// <remarks>
/// Для packed форматов (RGB, BGRA) используйте <see cref="PackedData"/>.
/// Для planar форматов (YUV420P) используйте <see cref="GetPlaneY"/>, <see cref="GetPlaneU"/>, <see cref="GetPlaneV"/>.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly ref struct VideoFrame
{
    // Внутреннее представление: до 4 плоскостей (Y, U, V, A или R, G, B, A)
    private readonly Span<byte> plane0;
    private readonly Span<byte> plane1;
    private readonly Span<byte> plane2;
    private readonly Span<byte> plane3;

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
    /// Количество плоскостей.
    /// </summary>
    public int PlaneCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Info.PixelFormat.GetPlaneCount();
    }

    #region Constructors

    /// <summary>
    /// Создаёт видеокадр для packed формата (RGB, BGRA и т.д.).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VideoFrame(Span<byte> data, int stride, VideoFrameInfo info)
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
    /// Создаёт видеокадр для planar формата с 2 плоскостями (NV12, NV21).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VideoFrame(
        Span<byte> planeY, int strideY,
        Span<byte> planeUv, int strideUv,
        VideoFrameInfo info)
    {
        plane0 = planeY;
        stride0 = strideY;
        plane1 = planeUv;
        stride1 = strideUv;
        plane2 = default;
        plane3 = default;
        stride2 = 0;
        stride3 = 0;
        Info = info;
    }

    /// <summary>
    /// Создаёт видеокадр для planar формата с 3 плоскостями (YUV420P, YUV422P).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VideoFrame(
        Span<byte> planeY, int strideY,
        Span<byte> planeU, int strideU,
        Span<byte> planeV, int strideV,
        VideoFrameInfo info)
    {
        plane0 = planeY;
        stride0 = strideY;
        plane1 = planeU;
        stride1 = strideU;
        plane2 = planeV;
        stride2 = strideV;
        plane3 = default;
        stride3 = 0;
        Info = info;
    }

    /// <summary>
    /// Создаёт видеокадр для planar формата с 4 плоскостями (YUVA).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VideoFrame(
        Span<byte> planeY, int strideY,
        Span<byte> planeU, int strideU,
        Span<byte> planeV, int strideV,
        Span<byte> planeA, int strideA,
        VideoFrameInfo info)
    {
        plane0 = planeY;
        stride0 = strideY;
        plane1 = planeU;
        stride1 = strideU;
        plane2 = planeV;
        stride2 = strideV;
        plane3 = planeA;
        stride3 = strideA;
        Info = info;
    }

    #endregion

    #region Plane Access

    /// <summary>
    /// Возвращает данные для packed формата (единственная плоскость).
    /// </summary>
    public Plane<byte> PackedData
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(plane0, stride0, Width * PixelFormat.GetBytesPerPixel(), Height);
    }

    /// <summary>
    /// Возвращает плоскость Y (luma) для YUV форматов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Plane<byte> GetPlaneY() => new(plane0, stride0, Width, Height);

    /// <summary>
    /// Возвращает плоскость U (Cb) для YUV420P/YUV422P.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Plane<byte> GetPlaneU()
    {
        var (w, h) = GetChromaSize();
        return new Plane<byte>(plane1, stride1, w, h);
    }

    /// <summary>
    /// Возвращает плоскость V (Cr) для YUV420P/YUV422P.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Plane<byte> GetPlaneV()
    {
        var (w, h) = GetChromaSize();
        return new Plane<byte>(plane2, stride2, w, h);
    }

    /// <summary>
    /// Возвращает interleaved UV плоскость для NV12/NV21.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Plane<byte> GetPlaneUV() => new(plane1, stride1, Width, Height / 2);

    /// <summary>
    /// Возвращает плоскость Alpha (если есть).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Plane<byte> GetPlaneAlpha() => new(plane3, stride3, Width, Height);

    /// <summary>
    /// Возвращает плоскость по индексу.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Plane<byte> GetPlane(int index) => index switch
    {
        0 => new Plane<byte>(plane0, stride0, Width, Height),
        1 => new Plane<byte>(plane1, stride1, plane1.Length / stride1 > 0 ? stride1 : Width, plane1.Length / stride1),
        2 => new Plane<byte>(plane2, stride2, plane2.Length / stride2 > 0 ? stride2 : Width, plane2.Length / stride2),
        3 => new Plane<byte>(plane3, stride3, Width, Height),
        _ => default,
    };

    /// <summary>
    /// Возвращает размеры chroma плоскостей для YUV форматов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int Width, int Height) GetChromaSize() => PixelFormat switch
    {
        // 4:2:0 — половина размера по обеим осям
        VideoPixelFormat.Yuv420P or VideoPixelFormat.Yuv420P10Le or
        VideoPixelFormat.Nv12 or VideoPixelFormat.Nv21 or VideoPixelFormat.P010Le
            => (Width / 2, Height / 2),

        // 4:2:2 — половина по ширине
        VideoPixelFormat.Yuv422P or VideoPixelFormat.Yuv422P10Le
            => (Width / 2, Height),

        // 4:4:4 — полный размер
        VideoPixelFormat.Yuv444P or VideoPixelFormat.Yuv444P10Le
            => (Width, Height),

        _ => (0, 0),
    };

    #endregion

    #region Raw Pointer Access

    /// <summary>
    /// Возвращает указатель на данные плоскости 0.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte* GetPointer0()
    {
        fixed (byte* ptr = plane0)
            return ptr;
    }

    /// <summary>
    /// Возвращает указатель на данные плоскости 1.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte* GetPointer1()
    {
        fixed (byte* ptr = plane1)
            return ptr;
    }

    /// <summary>
    /// Возвращает указатель на данные плоскости 2.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte* GetPointer2()
    {
        fixed (byte* ptr = plane2)
            return ptr;
    }

    #endregion
}
