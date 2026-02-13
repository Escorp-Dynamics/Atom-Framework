#pragma warning disable CA1000, CA1815

using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Представляет planar YUV 4:2:0 формат (I420).
/// </summary>
/// <remarks>
/// <para>
/// YUV420P — это planar формат с субдискретизацией 4:2:0:
/// <list type="bullet">
/// <item>Y plane: width × height (luma, полное разрешение)</item>
/// <item>U plane: width/2 × height/2 (Cb, четверть разрешения)</item>
/// <item>V plane: width/2 × height/2 (Cr, четверть разрешения)</item>
/// </list>
/// </para>
/// <para>
/// Это наиболее распространённый формат для видеокодеков (H.264, H.265, VP9, AV1).
/// Экономия памяти: 12 бит/пиксель vs 24 бит/пиксель для RGB.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly partial struct Yuv420P : IPlanarColorSpace<Yuv420P>
{
    #region IPlanarColorSpace Implementation

    /// <inheritdoc/>
    public static int PlaneCount => 3;

    /// <inheritdoc/>
    public static PlaneInfo GetPlaneInfo(int planeIndex) => planeIndex switch
    {
        0 => new PlaneInfo(1, 1, 1), // Y: full resolution, 1 byte/sample
        1 => new PlaneInfo(2, 2, 1), // U: half width, half height
        2 => new PlaneInfo(2, 2, 1), // V: half width, half height
        _ => throw new ArgumentOutOfRangeException(nameof(planeIndex)),
    };

    /// <inheritdoc/>
    public static HardwareAcceleration SupportedAccelerations =>
        HardwareAcceleration.None | HardwareAcceleration.Sse41 | HardwareAcceleration.Avx2;

    #endregion

    #region Helpers

    /// <summary>
    /// Вычисляет размеры Y plane.
    /// </summary>
    public static int GetYPlaneSize(int width, int height) => width * height;

    /// <summary>
    /// Вычисляет размеры U или V plane.
    /// </summary>
    public static int GetChromaPlaneSize(int width, int height) => width / 2 * (height / 2);

    #endregion
}
