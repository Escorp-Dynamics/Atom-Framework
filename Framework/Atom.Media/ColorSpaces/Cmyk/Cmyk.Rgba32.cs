#pragma warning disable CA1000, CA2208, IDE0004, MA0051, S3776, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Cmyk ↔ Rgba32.
/// </summary>
public readonly partial struct Cmyk
{
    #region SIMD Constants (Rgba32)

    /// <summary>Реализованные ускорители для Cmyk ↔ Rgba32.</summary>
    private const HardwareAcceleration Rgba32Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion (Rgba32)

    /// <summary>Конвертирует Rgba32 в Cmyk (альфа отбрасывается).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cmyk FromRgba32(Rgba32 rgba) => FromRgb24(new Rgb24(rgba.R, rgba.G, rgba.B));

    /// <summary>Конвертирует Cmyk в Rgba32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgba32 ToRgba32()
    {
        var rgb = ToRgb24();
        return new Rgba32(rgb.R, rgb.G, rgb.B, 255);
    }

    #endregion

    #region Batch Conversion (Cmyk ↔ Rgba32)

    /// <summary>Пакетная конвертация Rgba32 → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Cmyk> destination) =>
        FromRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Rgba32 → Cmyk с явным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Cmyk> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgba32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Rgba32* srcPtr = source)
            fixed (Cmyk* dstPtr = destination)
            {
                FromRgba32Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        FromRgba32Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Cmyk → Rgba32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToRgba32(ReadOnlySpan<Cmyk> source, Span<Rgba32> destination) =>
        ToRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Cmyk → Rgba32 с явным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ToRgba32(ReadOnlySpan<Cmyk> source, Span<Rgba32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgba32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Cmyk* srcPtr = source)
            fixed (Rgba32* dstPtr = destination)
            {
                ToRgba32Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        ToRgba32Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations (Rgba32)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromRgba32Core(ReadOnlySpan<Rgba32> source, Span<Cmyk> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                FromRgba32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromRgba32Sse41(source, destination);
                break;
            default:
                FromRgba32Scalar(source, destination);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToRgba32Core(ReadOnlySpan<Cmyk> source, Span<Rgba32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                ToRgba32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                ToRgba32Sse41(source, destination);
                break;
            default:
                ToRgba32Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing (Rgba32)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromRgba32Parallel(Rgba32* source, Cmyk* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromRgba32Core(new ReadOnlySpan<Rgba32>(source + start, size), new Span<Cmyk>(destination + start, size), selected);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToRgba32Parallel(Cmyk* source, Rgba32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToRgba32Core(new ReadOnlySpan<Cmyk>(source + start, size), new Span<Rgba32>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementations (Rgba32)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromRgba32Scalar(ReadOnlySpan<Rgba32> source, Span<Cmyk> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromRgba32(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToRgba32Scalar(ReadOnlySpan<Cmyk> source, Span<Rgba32> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = src++->ToRgba32();
        }
    }

    #endregion

    #region SSE41 Implementation (Rgba32)

    /// <summary>
    /// RGBA→CMYK с полноценным SIMD.
    /// SSE не имеет Gather, поэтому используем scalar LUT + SIMD математику.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgba32Sse41(ReadOnlySpan<Rgba32> source, Span<Cmyk> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        fixed (ushort* lutPtr = InverseTable)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            var c255 = CmykSse41Vectors.C255I;
            var c1 = CmykSse41Vectors.C1I;
            var packMask = CmykSse41Vectors.PackInt32ToByte;
            var shuffleR = CmykSse41Vectors.ShuffleRgbaR;
            var shuffleG = CmykSse41Vectors.ShuffleRgbaG;
            var shuffleB = CmykSse41Vectors.ShuffleRgbaB;

            // 4 пикселя за итерацию (16 байт RGBA → 16 байт CMYK)
            while (i + 4 <= count)
            {
                // Загрузка 4 пикселей RGBA = 16 байт
                var rgba = Sse2.LoadVector128(src);

                // Деинтерлейс RGBA → R, G, B
                var rBytes = Ssse3.Shuffle(rgba, shuffleR);
                var gBytes = Ssse3.Shuffle(rgba, shuffleG);
                var bBytes = Ssse3.Shuffle(rgba, shuffleB);

                // Конвертация в int32
                var rI = Sse41.ConvertToVector128Int32(rBytes);
                var gI = Sse41.ConvertToVector128Int32(gBytes);
                var bI = Sse41.ConvertToVector128Int32(bBytes);

                // max = Max(R, G, B)
                var maxRG = Sse41.Max(rI, gI);
                var maxRGB = Sse41.Max(maxRG, bI);

                // K = 255 - max
                var kI = Sse2.Subtract(c255, maxRGB);

                // Scalar LUT lookup для invMax
                var max0 = maxRGB.GetElement(0);
                var max1 = maxRGB.GetElement(1);
                var max2 = maxRGB.GetElement(2);
                var max3 = maxRGB.GetElement(3);

                var inv0 = max0 > 0 ? lutPtr[max0] : 0;
                var inv1 = max1 > 0 ? lutPtr[max1] : 0;
                var inv2 = max2 > 0 ? lutPtr[max2] : 0;
                var inv3 = max3 > 0 ? lutPtr[max3] : 0;

                var invMax = Vector128.Create(inv0, inv1, inv2, inv3);

                // C = (max - R) * invMax / 65536 * 255
                var diffR = Sse2.Subtract(maxRGB, rI);
                var diffG = Sse2.Subtract(maxRGB, gI);
                var diffB = Sse2.Subtract(maxRGB, bI);

                var cProd = Sse41.MultiplyLow(diffR, invMax);
                var mProd = Sse41.MultiplyLow(diffG, invMax);
                var yProd = Sse41.MultiplyLow(diffB, invMax);

                // 2-шаговое деление с FLOOR для LOSSLESS:
                // Шаг 1: temp = cProd >> 8 (Q8, floor)
                // Шаг 2: C = (temp * 255) >> 8 (floor)

                var cProd8 = Sse2.ShiftRightArithmetic(cProd, 8);
                var mProd8 = Sse2.ShiftRightArithmetic(mProd, 8);
                var yProd8 = Sse2.ShiftRightArithmetic(yProd, 8);

                var cScaled = Sse2.ShiftRightArithmetic(Sse41.MultiplyLow(cProd8, c255), 8);
                var mScaled = Sse2.ShiftRightArithmetic(Sse41.MultiplyLow(mProd8, c255), 8);
                var yScaled = Sse2.ShiftRightArithmetic(Sse41.MultiplyLow(yProd8, c255), 8);

                cScaled = Sse41.Min(cScaled, c255);
                mScaled = Sse41.Min(mScaled, c255);
                yScaled = Sse41.Min(yScaled, c255);

                // LOSSLESS компенсация: проверяем round-trip и корректируем ±1
                var c128v = CmykSse41Vectors.C128I;
                var invC = Sse2.Subtract(c255, cScaled);
                var invM = Sse2.Subtract(c255, mScaled);
                var invY = Sse2.Subtract(c255, yScaled);

                var rProdCheck = Sse41.MultiplyLow(invC, maxRGB);
                var gProdCheck = Sse41.MultiplyLow(invM, maxRGB);
                var bProdCheck = Sse41.MultiplyLow(invY, maxRGB);

                var rProd128c = Sse2.Add(rProdCheck, c128v);
                var gProd128c = Sse2.Add(gProdCheck, c128v);
                var bProd128c = Sse2.Add(bProdCheck, c128v);

                var r2 = Sse2.ShiftRightArithmetic(Sse2.Add(rProd128c, Sse2.ShiftRightArithmetic(rProd128c, 8)), 8);
                var g2 = Sse2.ShiftRightArithmetic(Sse2.Add(gProd128c, Sse2.ShiftRightArithmetic(gProd128c, 8)), 8);
                var b2 = Sse2.ShiftRightArithmetic(Sse2.Add(bProd128c, Sse2.ShiftRightArithmetic(bProd128c, 8)), 8);

                // Двойная коррекция: +1 если r2 > r, -1 если r2 < r
                var cZero = Vector128<int>.Zero;

                // +1 коррекция
                var maskRGt = Sse2.CompareGreaterThan(r2, rI);
                var maskGGt = Sse2.CompareGreaterThan(g2, gI);
                var maskBGt = Sse2.CompareGreaterThan(b2, bI);
                var maskCLt255 = Sse2.CompareGreaterThan(c255, cScaled);
                var maskMLt255 = Sse2.CompareGreaterThan(c255, mScaled);
                var maskYLt255 = Sse2.CompareGreaterThan(c255, yScaled);
                var addC = Sse2.And(Sse2.And(maskRGt, maskCLt255), c1);
                var addM = Sse2.And(Sse2.And(maskGGt, maskMLt255), c1);
                var addY = Sse2.And(Sse2.And(maskBGt, maskYLt255), c1);

                // -1 коррекция
                var maskRLt = Sse2.CompareGreaterThan(rI, r2);
                var maskGLt = Sse2.CompareGreaterThan(gI, g2);
                var maskBLt = Sse2.CompareGreaterThan(bI, b2);
                var maskCGt0 = Sse2.CompareGreaterThan(cScaled, cZero);
                var maskMGt0 = Sse2.CompareGreaterThan(mScaled, cZero);
                var maskYGt0 = Sse2.CompareGreaterThan(yScaled, cZero);
                var subC = Sse2.And(Sse2.And(maskRLt, maskCGt0), c1);
                var subM = Sse2.And(Sse2.And(maskGLt, maskMGt0), c1);
                var subY = Sse2.And(Sse2.And(maskBLt, maskYGt0), c1);

                cScaled = Sse2.Subtract(Sse2.Add(cScaled, addC), subC);
                mScaled = Sse2.Subtract(Sse2.Add(mScaled, addM), subM);
                yScaled = Sse2.Subtract(Sse2.Add(yScaled, addY), subY);

                // === ВТОРАЯ ИТЕРАЦИЯ КОРРЕКЦИИ ===
                // 2-step деление теряет точность vs Q16, иногда нужна двойная коррекция
                invC = Sse2.Subtract(c255, cScaled);
                invM = Sse2.Subtract(c255, mScaled);
                invY = Sse2.Subtract(c255, yScaled);

                rProdCheck = Sse41.MultiplyLow(invC, maxRGB);
                gProdCheck = Sse41.MultiplyLow(invM, maxRGB);
                bProdCheck = Sse41.MultiplyLow(invY, maxRGB);

                rProd128c = Sse2.Add(rProdCheck, c128v);
                gProd128c = Sse2.Add(gProdCheck, c128v);
                bProd128c = Sse2.Add(bProdCheck, c128v);

                r2 = Sse2.ShiftRightArithmetic(Sse2.Add(rProd128c, Sse2.ShiftRightArithmetic(rProd128c, 8)), 8);
                g2 = Sse2.ShiftRightArithmetic(Sse2.Add(gProd128c, Sse2.ShiftRightArithmetic(gProd128c, 8)), 8);
                b2 = Sse2.ShiftRightArithmetic(Sse2.Add(bProd128c, Sse2.ShiftRightArithmetic(bProd128c, 8)), 8);

                // +1 коррекция
                maskRGt = Sse2.CompareGreaterThan(r2, rI);
                maskGGt = Sse2.CompareGreaterThan(g2, gI);
                maskBGt = Sse2.CompareGreaterThan(b2, bI);
                maskCLt255 = Sse2.CompareGreaterThan(c255, cScaled);
                maskMLt255 = Sse2.CompareGreaterThan(c255, mScaled);
                maskYLt255 = Sse2.CompareGreaterThan(c255, yScaled);
                addC = Sse2.And(Sse2.And(maskRGt, maskCLt255), c1);
                addM = Sse2.And(Sse2.And(maskGGt, maskMLt255), c1);
                addY = Sse2.And(Sse2.And(maskBGt, maskYLt255), c1);

                // -1 коррекция
                maskRLt = Sse2.CompareGreaterThan(rI, r2);
                maskGLt = Sse2.CompareGreaterThan(gI, g2);
                maskBLt = Sse2.CompareGreaterThan(bI, b2);
                maskCGt0 = Sse2.CompareGreaterThan(cScaled, cZero);
                maskMGt0 = Sse2.CompareGreaterThan(mScaled, cZero);
                maskYGt0 = Sse2.CompareGreaterThan(yScaled, cZero);
                subC = Sse2.And(Sse2.And(maskRLt, maskCGt0), c1);
                subM = Sse2.And(Sse2.And(maskGLt, maskMGt0), c1);
                subY = Sse2.And(Sse2.And(maskBLt, maskYGt0), c1);

                cScaled = Sse2.Subtract(Sse2.Add(cScaled, addC), subC);
                mScaled = Sse2.Subtract(Sse2.Add(mScaled, addM), subM);
                yScaled = Sse2.Subtract(Sse2.Add(yScaled, addY), subY);

                var cOut = Ssse3.Shuffle(cScaled.AsByte(), packMask);
                var mOut = Ssse3.Shuffle(mScaled.AsByte(), packMask);
                var yOut = Ssse3.Shuffle(yScaled.AsByte(), packMask);
                var kOut = Ssse3.Shuffle(kI.AsByte(), packMask);

                var cm = Sse2.UnpackLow(cOut, mOut);
                var yk = Sse2.UnpackLow(yOut, kOut);
                var cmyk = Sse2.UnpackLow(cm.AsUInt16(), yk.AsUInt16()).AsByte();
                Sse2.Store(dst, cmyk);

                src += 16;
                dst += 16;
                i += 4;
            }

            // Остаток scalar
            while (i < count)
            {
                destination[i] = FromRgba32(source[i]);
                i++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgba32Sse41(ReadOnlySpan<Cmyk> source, Span<Rgba32> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            // Q16 integer: R = (255 - C) * (255 - K) / 255
            var c255 = CmykSse41Vectors.C255I;
            var c128 = CmykSse41Vectors.C128I;
            var shuffleC = CmykSse41Vectors.ShuffleCmykC;
            var shuffleM = CmykSse41Vectors.ShuffleCmykM;
            var shuffleY = CmykSse41Vectors.ShuffleCmykY;
            var shuffleK = CmykSse41Vectors.ShuffleCmykK;
            var packMask = CmykSse41Vectors.PackInt32ToByte;
            var allFF = CmykSse41Vectors.AllFF;

            // 4 пикселя за итерацию
            while (i + 4 <= count)
            {
                // Загрузка 4 пикселей CMYK = 16 байт
                var cmyk = Sse2.LoadVector128(src);

                // Деинтерлейс CMYK → C, M, Y, K
                var cBytes = Ssse3.Shuffle(cmyk, shuffleC);
                var mBytes = Ssse3.Shuffle(cmyk, shuffleM);
                var yBytes = Ssse3.Shuffle(cmyk, shuffleY);
                var kBytes = Ssse3.Shuffle(cmyk, shuffleK);

                // Конвертация в int32
                var cI = Sse41.ConvertToVector128Int32(cBytes);
                var mI = Sse41.ConvertToVector128Int32(mBytes);
                var yI = Sse41.ConvertToVector128Int32(yBytes);
                var kI = Sse41.ConvertToVector128Int32(kBytes);

                // invC = 255 - C, invK = 255 - K
                var invC = Sse2.Subtract(c255, cI);
                var invM = Sse2.Subtract(c255, mI);
                var invY = Sse2.Subtract(c255, yI);
                var invK = Sse2.Subtract(c255, kI);

                // rProd = invC * invK (max 255*255 = 65025)
                var rProd = Sse41.MultiplyLow(invC, invK);
                var gProd = Sse41.MultiplyLow(invM, invK);
                var bProd = Sse41.MultiplyLow(invY, invK);

                // Деление на 255 с округлением: (x + 128 + ((x + 128) >> 8)) >> 8
                var rProd128 = Sse2.Add(rProd, c128);
                var gProd128 = Sse2.Add(gProd, c128);
                var bProd128 = Sse2.Add(bProd, c128);

                var rI = Sse2.ShiftRightArithmetic(Sse2.Add(rProd128, Sse2.ShiftRightArithmetic(rProd128, 8)), 8);
                var gI = Sse2.ShiftRightArithmetic(Sse2.Add(gProd128, Sse2.ShiftRightArithmetic(gProd128, 8)), 8);
                var bI = Sse2.ShiftRightArithmetic(Sse2.Add(bProd128, Sse2.ShiftRightArithmetic(bProd128, 8)), 8);

                // Упаковка и интерлив RGBA32
                var rBytesOut = Ssse3.Shuffle(rI.AsByte(), packMask);
                var gBytesOut = Ssse3.Shuffle(gI.AsByte(), packMask);
                var bBytesOut = Ssse3.Shuffle(bI.AsByte(), packMask);
                var aBytesOut = allFF;

                // SIMD интерлив RGBA
                var rg = Sse2.UnpackLow(rBytesOut, gBytesOut);
                var ba = Sse2.UnpackLow(bBytesOut, aBytesOut);
                var rgba = Sse2.UnpackLow(rg.AsUInt16(), ba.AsUInt16()).AsByte();

                // Записываем 4 пикселя = 16 байт
                Sse2.Store(dst, rgba);

                src += 16;
                dst += 16;
                i += 4;
            }

            // Остаток scalar
            while (i < count)
            {
                destination[i] = source[i].ToRgba32();
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Rgba32)

    /// <summary>
    /// RGBA→CMYK с AVX2. 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgba32Avx2(ReadOnlySpan<Rgba32> source, Span<Cmyk> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        fixed (ushort* lutPtr = InverseTable)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            var c255_256 = CmykAvx2Vectors.C255I;
            var c1_256 = CmykAvx2Vectors.C1I;
            var packMask = CmykSse41Vectors.PackInt32ToByte;
            var shuffleR = CmykSse41Vectors.ShuffleRgbaR;
            var shuffleG = CmykSse41Vectors.ShuffleRgbaG;
            var shuffleB = CmykSse41Vectors.ShuffleRgbaB;

            // 8 пикселей за итерацию (32 байт RGBA → 32 байт CMYK)
            while (i + 8 <= count)
            {
                var rgbaLo = Sse2.LoadVector128(src);
                var rgbaHi = Sse2.LoadVector128(src + 16);

                var rLoBytes = Ssse3.Shuffle(rgbaLo, shuffleR);
                var gLoBytes = Ssse3.Shuffle(rgbaLo, shuffleG);
                var bLoBytes = Ssse3.Shuffle(rgbaLo, shuffleB);
                var rHiBytes = Ssse3.Shuffle(rgbaHi, shuffleR);
                var gHiBytes = Ssse3.Shuffle(rgbaHi, shuffleG);
                var bHiBytes = Ssse3.Shuffle(rgbaHi, shuffleB);

                var rLoI = Sse41.ConvertToVector128Int32(rLoBytes);
                var gLoI = Sse41.ConvertToVector128Int32(gLoBytes);
                var bLoI = Sse41.ConvertToVector128Int32(bLoBytes);
                var rHiI = Sse41.ConvertToVector128Int32(rHiBytes);
                var gHiI = Sse41.ConvertToVector128Int32(gHiBytes);
                var bHiI = Sse41.ConvertToVector128Int32(bHiBytes);

                var rI = Vector256.Create(rLoI, rHiI);
                var gI = Vector256.Create(gLoI, gHiI);
                var bI = Vector256.Create(bLoI, bHiI);

                var maxRG = Avx2.Max(rI, gI);
                var maxRGB = Avx2.Max(maxRG, bI);
                var kI = Avx2.Subtract(c255_256, maxRGB);

                var max0 = maxRGB.GetElement(0);
                var max1 = maxRGB.GetElement(1);
                var max2 = maxRGB.GetElement(2);
                var max3 = maxRGB.GetElement(3);
                var max4 = maxRGB.GetElement(4);
                var max5 = maxRGB.GetElement(5);
                var max6 = maxRGB.GetElement(6);
                var max7 = maxRGB.GetElement(7);

                var inv0 = max0 > 0 ? lutPtr[max0] : 0;
                var inv1 = max1 > 0 ? lutPtr[max1] : 0;
                var inv2 = max2 > 0 ? lutPtr[max2] : 0;
                var inv3 = max3 > 0 ? lutPtr[max3] : 0;
                var inv4 = max4 > 0 ? lutPtr[max4] : 0;
                var inv5 = max5 > 0 ? lutPtr[max5] : 0;
                var inv6 = max6 > 0 ? lutPtr[max6] : 0;
                var inv7 = max7 > 0 ? lutPtr[max7] : 0;

                var invMax = Vector256.Create(inv0, inv1, inv2, inv3, inv4, inv5, inv6, inv7);

                var diffR = Avx2.Subtract(maxRGB, rI);
                var diffG = Avx2.Subtract(maxRGB, gI);
                var diffB = Avx2.Subtract(maxRGB, bI);

                var cProd = Avx2.MultiplyLow(diffR, invMax);
                var mProd = Avx2.MultiplyLow(diffG, invMax);
                var yProd = Avx2.MultiplyLow(diffB, invMax);

                // 2-шаговое деление с FLOOR для LOSSLESS

                var cProd8 = Avx2.ShiftRightArithmetic(cProd, 8);
                var mProd8 = Avx2.ShiftRightArithmetic(mProd, 8);
                var yProd8 = Avx2.ShiftRightArithmetic(yProd, 8);

                var cScaled = Avx2.ShiftRightArithmetic(Avx2.MultiplyLow(cProd8, c255_256), 8);
                var mScaled = Avx2.ShiftRightArithmetic(Avx2.MultiplyLow(mProd8, c255_256), 8);
                var yScaled = Avx2.ShiftRightArithmetic(Avx2.MultiplyLow(yProd8, c255_256), 8);

                cScaled = Avx2.Min(cScaled, c255_256);
                mScaled = Avx2.Min(mScaled, c255_256);
                yScaled = Avx2.Min(yScaled, c255_256);

                // LOSSLESS компенсация: проверяем round-trip и корректируем ±1
                var c128v = CmykAvx2Vectors.C128I;
                var invC = Avx2.Subtract(c255_256, cScaled);
                var invM = Avx2.Subtract(c255_256, mScaled);
                var invY = Avx2.Subtract(c255_256, yScaled);

                var rProdCheck = Avx2.MultiplyLow(invC, maxRGB);
                var gProdCheck = Avx2.MultiplyLow(invM, maxRGB);
                var bProdCheck = Avx2.MultiplyLow(invY, maxRGB);

                var rProd128c = Avx2.Add(rProdCheck, c128v);
                var gProd128c = Avx2.Add(gProdCheck, c128v);
                var bProd128c = Avx2.Add(bProdCheck, c128v);

                var r2 = Avx2.ShiftRightArithmetic(Avx2.Add(rProd128c, Avx2.ShiftRightArithmetic(rProd128c, 8)), 8);
                var g2 = Avx2.ShiftRightArithmetic(Avx2.Add(gProd128c, Avx2.ShiftRightArithmetic(gProd128c, 8)), 8);
                var b2 = Avx2.ShiftRightArithmetic(Avx2.Add(bProd128c, Avx2.ShiftRightArithmetic(bProd128c, 8)), 8);

                // Двойная коррекция: +1 если r2 > r, -1 если r2 < r
                var cZero = Vector256<int>.Zero;

                // +1 коррекция
                var maskRGt = Avx2.CompareGreaterThan(r2, rI);
                var maskGGt = Avx2.CompareGreaterThan(g2, gI);
                var maskBGt = Avx2.CompareGreaterThan(b2, bI);
                var maskCLt255 = Avx2.CompareGreaterThan(c255_256, cScaled);
                var maskMLt255 = Avx2.CompareGreaterThan(c255_256, mScaled);
                var maskYLt255 = Avx2.CompareGreaterThan(c255_256, yScaled);
                var addC = Avx2.And(Avx2.And(maskRGt, maskCLt255), c1_256);
                var addM = Avx2.And(Avx2.And(maskGGt, maskMLt255), c1_256);
                var addY = Avx2.And(Avx2.And(maskBGt, maskYLt255), c1_256);

                // -1 коррекция
                var maskRLt = Avx2.CompareGreaterThan(rI, r2);
                var maskGLt = Avx2.CompareGreaterThan(gI, g2);
                var maskBLt = Avx2.CompareGreaterThan(bI, b2);
                var maskCGt0 = Avx2.CompareGreaterThan(cScaled, cZero);
                var maskMGt0 = Avx2.CompareGreaterThan(mScaled, cZero);
                var maskYGt0 = Avx2.CompareGreaterThan(yScaled, cZero);
                var subC = Avx2.And(Avx2.And(maskRLt, maskCGt0), c1_256);
                var subM = Avx2.And(Avx2.And(maskGLt, maskMGt0), c1_256);
                var subY = Avx2.And(Avx2.And(maskBLt, maskYGt0), c1_256);

                cScaled = Avx2.Subtract(Avx2.Add(cScaled, addC), subC);
                mScaled = Avx2.Subtract(Avx2.Add(mScaled, addM), subM);
                yScaled = Avx2.Subtract(Avx2.Add(yScaled, addY), subY);

                // === ВТОРАЯ ИТЕРАЦИЯ КОРРЕКЦИИ ===
                // 2-step деление теряет точность vs Q16, иногда нужна двойная коррекция
                invC = Avx2.Subtract(c255_256, cScaled);
                invM = Avx2.Subtract(c255_256, mScaled);
                invY = Avx2.Subtract(c255_256, yScaled);

                rProdCheck = Avx2.MultiplyLow(invC, maxRGB);
                gProdCheck = Avx2.MultiplyLow(invM, maxRGB);
                bProdCheck = Avx2.MultiplyLow(invY, maxRGB);

                rProd128c = Avx2.Add(rProdCheck, c128v);
                gProd128c = Avx2.Add(gProdCheck, c128v);
                bProd128c = Avx2.Add(bProdCheck, c128v);

                r2 = Avx2.ShiftRightArithmetic(Avx2.Add(rProd128c, Avx2.ShiftRightArithmetic(rProd128c, 8)), 8);
                g2 = Avx2.ShiftRightArithmetic(Avx2.Add(gProd128c, Avx2.ShiftRightArithmetic(gProd128c, 8)), 8);
                b2 = Avx2.ShiftRightArithmetic(Avx2.Add(bProd128c, Avx2.ShiftRightArithmetic(bProd128c, 8)), 8);

                // +1 коррекция
                maskRGt = Avx2.CompareGreaterThan(r2, rI);
                maskGGt = Avx2.CompareGreaterThan(g2, gI);
                maskBGt = Avx2.CompareGreaterThan(b2, bI);
                maskCLt255 = Avx2.CompareGreaterThan(c255_256, cScaled);
                maskMLt255 = Avx2.CompareGreaterThan(c255_256, mScaled);
                maskYLt255 = Avx2.CompareGreaterThan(c255_256, yScaled);
                addC = Avx2.And(Avx2.And(maskRGt, maskCLt255), c1_256);
                addM = Avx2.And(Avx2.And(maskGGt, maskMLt255), c1_256);
                addY = Avx2.And(Avx2.And(maskBGt, maskYLt255), c1_256);

                // -1 коррекция
                maskRLt = Avx2.CompareGreaterThan(rI, r2);
                maskGLt = Avx2.CompareGreaterThan(gI, g2);
                maskBLt = Avx2.CompareGreaterThan(bI, b2);
                maskCGt0 = Avx2.CompareGreaterThan(cScaled, cZero);
                maskMGt0 = Avx2.CompareGreaterThan(mScaled, cZero);
                maskYGt0 = Avx2.CompareGreaterThan(yScaled, cZero);
                subC = Avx2.And(Avx2.And(maskRLt, maskCGt0), c1_256);
                subM = Avx2.And(Avx2.And(maskGLt, maskMGt0), c1_256);
                subY = Avx2.And(Avx2.And(maskBLt, maskYGt0), c1_256);

                cScaled = Avx2.Subtract(Avx2.Add(cScaled, addC), subC);
                mScaled = Avx2.Subtract(Avx2.Add(mScaled, addM), subM);
                yScaled = Avx2.Subtract(Avx2.Add(yScaled, addY), subY);

                var cLoOut = Ssse3.Shuffle(cScaled.GetLower().AsByte(), packMask);
                var mLoOut = Ssse3.Shuffle(mScaled.GetLower().AsByte(), packMask);
                var yLoOut = Ssse3.Shuffle(yScaled.GetLower().AsByte(), packMask);
                var kLoOut = Ssse3.Shuffle(kI.GetLower().AsByte(), packMask);
                var cHiOut = Ssse3.Shuffle(cScaled.GetUpper().AsByte(), packMask);
                var mHiOut = Ssse3.Shuffle(mScaled.GetUpper().AsByte(), packMask);
                var yHiOut = Ssse3.Shuffle(yScaled.GetUpper().AsByte(), packMask);
                var kHiOut = Ssse3.Shuffle(kI.GetUpper().AsByte(), packMask);

                var cmLo = Sse2.UnpackLow(cLoOut, mLoOut);
                var ykLo = Sse2.UnpackLow(yLoOut, kLoOut);
                var cmykLo = Sse2.UnpackLow(cmLo.AsUInt16(), ykLo.AsUInt16()).AsByte();
                Sse2.Store(dst, cmykLo);

                var cmHi = Sse2.UnpackLow(cHiOut, mHiOut);
                var ykHi = Sse2.UnpackLow(yHiOut, kHiOut);
                var cmykHi = Sse2.UnpackLow(cmHi.AsUInt16(), ykHi.AsUInt16()).AsByte();
                Sse2.Store(dst + 16, cmykHi);

                src += 32;
                dst += 32;
                i += 8;
            }

            // SSE41 fallback
            if (i + 4 <= count)
            {
                FromRgba32Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                destination[i] = FromRgba32(source[i]);
                i++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgba32Avx2(ReadOnlySpan<Cmyk> source, Span<Rgba32> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;
            var i = 0;

            // Q16 integer: R = (255 - C) * (255 - K) / 255
            var c255 = CmykAvx2Vectors.C255I;
            var c128 = CmykAvx2Vectors.C128I;
            var shuffleC = CmykSse41Vectors.ShuffleCmykC;
            var shuffleM = CmykSse41Vectors.ShuffleCmykM;
            var shuffleY = CmykSse41Vectors.ShuffleCmykY;
            var shuffleK = CmykSse41Vectors.ShuffleCmykK;
            var packMask = CmykSse41Vectors.PackInt32ToByte;
            var allFF = CmykSse41Vectors.AllFF;

            // 8 пикселей за итерацию
            while (i + 8 <= count)
            {
                // Загрузка 8 пикселей CMYK = 32 байта (2x SSE loads)
                var cmykLo = Sse2.LoadVector128(src);
                var cmykHi = Sse2.LoadVector128(src + 16);

                var cLoB = Ssse3.Shuffle(cmykLo, shuffleC);
                var mLoB = Ssse3.Shuffle(cmykLo, shuffleM);
                var yLoB = Ssse3.Shuffle(cmykLo, shuffleY);
                var kLoB = Ssse3.Shuffle(cmykLo, shuffleK);
                var cHiB = Ssse3.Shuffle(cmykHi, shuffleC);
                var mHiB = Ssse3.Shuffle(cmykHi, shuffleM);
                var yHiB = Ssse3.Shuffle(cmykHi, shuffleY);
                var kHiB = Ssse3.Shuffle(cmykHi, shuffleK);

                var cI = Vector256.Create(Sse41.ConvertToVector128Int32(cLoB), Sse41.ConvertToVector128Int32(cHiB));
                var mI = Vector256.Create(Sse41.ConvertToVector128Int32(mLoB), Sse41.ConvertToVector128Int32(mHiB));
                var yI = Vector256.Create(Sse41.ConvertToVector128Int32(yLoB), Sse41.ConvertToVector128Int32(yHiB));
                var kI = Vector256.Create(Sse41.ConvertToVector128Int32(kLoB), Sse41.ConvertToVector128Int32(kHiB));

                // invC = 255 - C, invK = 255 - K
                var invC = Avx2.Subtract(c255, cI);
                var invM = Avx2.Subtract(c255, mI);
                var invY = Avx2.Subtract(c255, yI);
                var invK = Avx2.Subtract(c255, kI);

                // rProd = invC * invK (max 255*255 = 65025)
                var rProd = Avx2.MultiplyLow(invC, invK);
                var gProd = Avx2.MultiplyLow(invM, invK);
                var bProd = Avx2.MultiplyLow(invY, invK);

                // Деление на 255 с округлением: (x + 128 + ((x + 128) >> 8)) >> 8
                var rProd128 = Avx2.Add(rProd, c128);
                var gProd128 = Avx2.Add(gProd, c128);
                var bProd128 = Avx2.Add(bProd, c128);

                var rI2 = Avx2.ShiftRightArithmetic(Avx2.Add(rProd128, Avx2.ShiftRightArithmetic(rProd128, 8)), 8);
                var gI2 = Avx2.ShiftRightArithmetic(Avx2.Add(gProd128, Avx2.ShiftRightArithmetic(gProd128, 8)), 8);
                var bI2 = Avx2.ShiftRightArithmetic(Avx2.Add(bProd128, Avx2.ShiftRightArithmetic(bProd128, 8)), 8);

                // Упаковка и интерлив RGBA (8 пикселей = 32 байта)
                var rLoOut = Ssse3.Shuffle(rI2.GetLower().AsByte(), packMask);
                var gLoOut = Ssse3.Shuffle(gI2.GetLower().AsByte(), packMask);
                var bLoOut = Ssse3.Shuffle(bI2.GetLower().AsByte(), packMask);
                var rHiOut = Ssse3.Shuffle(rI2.GetUpper().AsByte(), packMask);
                var gHiOut = Ssse3.Shuffle(gI2.GetUpper().AsByte(), packMask);
                var bHiOut = Ssse3.Shuffle(bI2.GetUpper().AsByte(), packMask);
                var aBytes = allFF;

                // SIMD интерлив RGBA для первых 4 пикселей
                var rgLo = Sse2.UnpackLow(rLoOut, gLoOut);
                var baLo = Sse2.UnpackLow(bLoOut, aBytes);
                var rgbaLo = Sse2.UnpackLow(rgLo.AsUInt16(), baLo.AsUInt16()).AsByte();
                Sse2.Store(dst, rgbaLo);

                // SIMD интерлив RGBA для следующих 4 пикселей
                var rgHi = Sse2.UnpackLow(rHiOut, gHiOut);
                var baHi = Sse2.UnpackLow(bHiOut, aBytes);
                var rgbaHi = Sse2.UnpackLow(rgHi.AsUInt16(), baHi.AsUInt16()).AsByte();
                Sse2.Store(dst + 16, rgbaHi);

                src += 32;
                dst += 32;
                i += 8;
            }

            // Остаток через SSE41
            if (i < count)
            {
                ToRgba32Sse41(source[i..], destination[i..]);
            }
        }
    }

    #endregion

    #region Conversion Operators (Rgba32)

    /// <summary>Явная конвертация Rgba32 → Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Cmyk(Rgba32 rgba) => FromRgba32(rgba);

    /// <summary>Явная конвертация Cmyk → Rgba32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Rgba32(Cmyk cmyk) => cmyk.ToRgba32();

    #endregion
}
