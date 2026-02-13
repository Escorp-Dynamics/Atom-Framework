using System.Runtime.Intrinsics;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// SSE4.1 векторные константы для HSV (Vector128).
/// </summary>
internal static class HsvSse41Vectors
{
    #region Int32 Constants

    /// <summary>Вектор нулей int32.</summary>
    public static Vector128<int> ZeroI32 { get; } = Vector128<int>.Zero;

    /// <summary>Вектор единиц int32.</summary>
    public static Vector128<int> OneI32 { get; } = Vector128.Create(1);

    /// <summary>Вектор двоек int32.</summary>
    public static Vector128<int> TwoI32 { get; } = Vector128.Create(2);

    /// <summary>Вектор троек int32.</summary>
    public static Vector128<int> ThreeI32 { get; } = Vector128.Create(3);

    /// <summary>Вектор четвёрок int32.</summary>
    public static Vector128<int> FourI32 { get; } = Vector128.Create(4);

    /// <summary>Вектор пятёрок int32.</summary>
    public static Vector128<int> FiveI32 { get; } = Vector128.Create(5);

    /// <summary>Константа 6 int32.</summary>
    public static Vector128<int> C6I32 { get; } = Vector128.Create(6);

    /// <summary>Константа 63 int32.</summary>
    public static Vector128<int> C63I32 { get; } = Vector128.Create(63);

    /// <summary>Константа 255 int32.</summary>
    public static Vector128<int> C255I32 { get; } = Vector128.Create(255);

    #endregion

    #region Float Constants

    /// <summary>Вектор нулей float.</summary>
    public static Vector128<float> ZeroF { get; } = Vector128<float>.Zero;

    /// <summary>Вектор единиц float.</summary>
    public static Vector128<float> OneF { get; } = Vector128.Create(1f);

    /// <summary>Вектор двоек float.</summary>
    public static Vector128<float> TwoF { get; } = Vector128.Create(2f);

    /// <summary>Константа 43f.</summary>
    public static Vector128<float> C43F { get; } = Vector128.Create(43f);

    /// <summary>Константа 85f.</summary>
    public static Vector128<float> C85F { get; } = Vector128.Create(85f);

    /// <summary>Константа 171f.</summary>
    public static Vector128<float> C171F { get; } = Vector128.Create(171f);

    /// <summary>Константа 255f.</summary>
    public static Vector128<float> C255F { get; } = Vector128.Create(255f);

    /// <summary>Константа 256f.</summary>
    public static Vector128<float> C256F { get; } = Vector128.Create(256f);

    /// <summary>Константа 512f.</summary>
    public static Vector128<float> C512F { get; } = Vector128.Create(512f);

    /// <summary>Константа 768f.</summary>
    public static Vector128<float> C768F { get; } = Vector128.Create(768f);

    /// <summary>Константа 1024f.</summary>
    public static Vector128<float> C1024F { get; } = Vector128.Create(1024f);

    /// <summary>Константа 1536f.</summary>
    public static Vector128<float> C1536F { get; } = Vector128.Create(1536f);

    /// <summary>Константа 65025f (255 × 255).</summary>
    public static Vector128<float> C65025F { get; } = Vector128.Create(65025f);

    /// <summary>Константа 32512f ((65025 - 1) / 2).</summary>
    public static Vector128<float> C32512F { get; } = Vector128.Create(32512f);

    /// <summary>Константа 0.5f для округления.</summary>
    public static Vector128<float> HalfF { get; } = Vector128.Create(0.5f);

    /// <summary>Масштаб h6: h * 1536 / 255 ≈ h * 6.0235294.</summary>
    public static Vector128<float> H6ScaleF { get; } = Vector128.Create(1536f / 255f);

    /// <summary>Обратный масштаб h: h6 * 255 / 1536 ≈ h6 * 0.166015625.</summary>
    public static Vector128<float> HFromH6ScaleF { get; } = Vector128.Create(255f / 1536f);

    #endregion

    #region Shuffle Masks - RGBA32/BGRA32

    /// <summary>RGBA32: извлечение R.</summary>
    public static Vector128<byte> ShuffleRgbaR { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGBA32: извлечение G.</summary>
    public static Vector128<byte> ShuffleRgbaG { get; } = Vector128.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGBA32: извлечение B.</summary>
    public static Vector128<byte> ShuffleRgbaB { get; } = Vector128.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>BGRA32: извлечение R.</summary>
    public static Vector128<byte> ShuffleBgraR { get; } = Vector128.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>BGRA32: извлечение G.</summary>
    public static Vector128<byte> ShuffleBgraG { get; } = Vector128.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>BGRA32: извлечение B.</summary>
    public static Vector128<byte> ShuffleBgraB { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Shuffle Masks - RGB24

    /// <summary>RGB24: извлечение R из bytes0.</summary>
    public static Vector128<byte> ShuffleR0 { get; } = Vector128.Create(
        0, 3, 6, 9, 12, 15, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGB24: извлечение R из bytes1.</summary>
    public static Vector128<byte> ShuffleR1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 2, 5,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGB24: извлечение G из bytes0.</summary>
    public static Vector128<byte> ShuffleG0 { get; } = Vector128.Create(
        1, 4, 7, 10, 13, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGB24: извлечение G из bytes1.</summary>
    public static Vector128<byte> ShuffleG1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0, 3, 6,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGB24: извлечение B из bytes0.</summary>
    public static Vector128<byte> ShuffleB0 { get; } = Vector128.Create(
        2, 5, 8, 11, 14, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGB24: извлечение B из bytes1.</summary>
    public static Vector128<byte> ShuffleB1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 1, 4, 7,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Pack/Interleave Masks

    /// <summary>Упаковка int32 → byte.</summary>
    public static Vector128<byte> PackInt32ToByte { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>HSV interleave: HS → первые 16 байт.</summary>
    public static Vector128<byte> HsvToHsvMask0 { get; } = Vector128.Create(
        0, 1, 0x80, 2, 3, 0x80, 4, 5, 0x80, 6, 7, 0x80, 8, 9, 0x80, 10);

    /// <summary>HSV interleave: V → первые 16 байт.</summary>
    public static Vector128<byte> VToHsvMask0 { get; } = Vector128.Create(
        0x80, 0x80, 0, 0x80, 0x80, 1, 0x80, 0x80, 2, 0x80, 0x80, 3, 0x80, 0x80, 4, 0x80);

    /// <summary>HSV interleave: HS → оставшиеся 8 байт.</summary>
    public static Vector128<byte> HsvToHsvMask1 { get; } = Vector128.Create(
        11, 0x80, 12, 13, 0x80, 14, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>HSV interleave: V → оставшиеся 8 байт.</summary>
    public static Vector128<byte> VToHsvMask1 { get; } = Vector128.Create(
        0x80, 5, 0x80, 0x80, 6, 0x80, 0x80, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region 3-Byte Format Shuffle Masks (HSV/RGB24)

    /// <summary>Извлечение H (позиции 0,3,6,9) из 3-byte HSV.</summary>
    public static Vector128<byte> Shuffle3ByteToChannel0 { get; } = Vector128.Create(
        0, 3, 6, 9, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение S (позиции 1,4,7,10) из 3-byte HSV.</summary>
    public static Vector128<byte> Shuffle3ByteToChannel1 { get; } = Vector128.Create(
        1, 4, 7, 10, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение V (позиции 2,5,8,11) из 3-byte HSV.</summary>
    public static Vector128<byte> Shuffle3ByteToChannel2 { get; } = Vector128.Create(
        2, 5, 8, 11, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Interleave 3-byte: R0R1R2R3 G0G1G2G3 B0B1B2B3 → R0G0B0 R1G1B1 R2G2B2 R3G3B3.</summary>
    public static Vector128<byte> Shuffle3ByteInterleave { get; } = Vector128.Create(
        0, 4, 8, 1, 5, 9, 2, 6, 10, 3, 7, 11, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Gray16 ↔ HSV Shuffle Masks

    /// <summary>Извлечение V из 3-байтового HSV (позиции 2,5,8,11,14).</summary>
    public static Vector128<byte> ShuffleHsvToV { get; } = Vector128.Create(
        2, 5, 8, 11, 14, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение V из HSV (вторая часть, позиции 1,4,7).</summary>
    public static Vector128<byte> ShuffleHsvToV2 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 1, 4, 7,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Shuffle для извлечения старших байт из Gray16 (байты 1,3,5,7,9,11,13,15).</summary>
    public static Vector128<byte> ShuffleGray16ToHighByte { get; } = Vector128.Create(
        1, 3, 5, 7, 9, 11, 13, 15,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Маска для HSV с H=0, S=0, V=gray: только V-компонента (паттерн 0).</summary>
    public static Vector128<byte> HsvGrayMask0 { get; } = Vector128.Create(
        0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0);

    /// <summary>Маска для HSV с H=0, S=0, V=gray: только V-компонента (паттерн 1).</summary>
    public static Vector128<byte> HsvGrayMask1 { get; } = Vector128.Create(
        0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0);

    #endregion

    #region Gray8 ↔ HSV (4-byte HSV: [H16][S8][V8])

    /// <summary>Shuffle для извлечения V из 4-байтового HSV (позиции 3, 7, 11, 15).</summary>
    public static Vector128<byte> ShuffleHsv4ToV { get; } = Vector128.Create(
        3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Shuffle для Gray8 → 4-байтового HSV: [0, 0, 0, V0, 0, 0, 0, V1, ...].</summary>
    public static Vector128<byte> ShuffleGray8ToHsv4 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0, 0x80, 0x80, 0x80, 1, 0x80, 0x80, 0x80, 2, 0x80, 0x80, 0x80, 3);

    #endregion
}