#pragma warning disable CA1000, CA1815

using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Представляет semi-planar YUV 4:2:0 формат (NV12).
/// </summary>
/// <remarks>
/// <para>
/// NV12 — это semi-planar формат с субдискретизацией 4:2:0:
/// <list type="bullet">
/// <item>Y plane: width × height (luma, полное разрешение)</item>
/// <item>UV plane: width × height/2 (Cb+Cr interleaved, половина высоты)</item>
/// </list>
/// </para>
/// <para>
/// В UV plane данные идут парами: U0V0 U1V1 U2V2 ...
/// Широко используется в аппаратном декодировании (DXVA, VAAPI, NVDEC).
/// </para>
/// <para>
/// Отличие от YUV420P: UV хранятся interleaved, а не в отдельных planes.
/// Преимущество: лучшая cache locality при доступе к UV.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly partial struct Nv12 : IPlanarColorSpace<Nv12>
{
    #region IPlanarColorSpace Implementation

    /// <inheritdoc/>
    public static int PlaneCount => 2;

    /// <inheritdoc/>
    public static PlaneInfo GetPlaneInfo(int planeIndex) => planeIndex switch
    {
        0 => new PlaneInfo(1, 1, 1), // Y: full resolution, 1 byte/sample
        1 => new PlaneInfo(1, 2, 1), // UV: full width (interleaved), half height
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
    /// Вычисляет размеры UV plane (interleaved).
    /// </summary>
    public static int GetUvPlaneSize(int width, int height) => width * (height / 2);

    #endregion
}
