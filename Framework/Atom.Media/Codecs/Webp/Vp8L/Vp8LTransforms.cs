#pragma warning disable S109, MA0051, S3776

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Atom.Media;

/// <summary>
/// VP8L преобразования изображения: прямые (forward) и обратные (inverse).
/// </summary>
/// <remarks>
/// Все преобразования работают in-place над буфером ARGB (uint32 packed).
/// Порядок каналов: alpha[31:24], red[23:16], green[15:8], blue[7:0].
/// Прямые преобразования применяются при кодировании, обратные — при декодировании.
/// </remarks>
internal static class Vp8LTransforms
{
    #region SubtractGreen (Forward)

    /// <summary>
    /// Прямое преобразование SubtractGreen: вычитает green из red и blue.
    /// SIMD-оптимизация: 8 пикселей за итерацию (AVX2) или 4 (SSE2/NEON).
    /// </summary>
    /// <param name="pixels">Буфер пикселей ARGB.</param>
    internal static void ForwardSubtractGreen(Span<uint> pixels)
    {
        ref var pixelRef = ref MemoryMarshal.GetReference(pixels);
        var count = pixels.Length;
        var i = 0;

        // SIMD: extract green byte, place in blue+red positions, byte-subtract
        // Byte layout per pixel (little-endian uint32 ARGB): [B, G, R, A]
        // greenSub bytes: [G, 0, G, 0] → subtract → [B-G, G-0, R-G, A-0] = [B-G, G, R-G, A] ✓
        if (Vector256.IsHardwareAccelerated)
        {
            var greenMask = Vector256.Create(0x000000FFu);
            for (; i + 8 <= count; i += 8)
            {
                var px = Vector256.LoadUnsafe(ref pixelRef, (nuint)i);
                var green32 = (px >>> 8) & greenMask;
                var greenSub = green32 | (green32 << 16);
                var result = (px.AsByte() - greenSub.AsByte()).AsUInt32();
                result.StoreUnsafe(ref pixelRef, (nuint)i);
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            var greenMask = Vector128.Create(0x000000FFu);
            for (; i + 4 <= count; i += 4)
            {
                var px = Vector128.LoadUnsafe(ref pixelRef, (nuint)i);
                var green32 = (px >>> 8) & greenMask;
                var greenSub = green32 | (green32 << 16);
                var result = (px.AsByte() - greenSub.AsByte()).AsUInt32();
                result.StoreUnsafe(ref pixelRef, (nuint)i);
            }
        }

        // Scalar tail
        for (; i < count; i++)
        {
            var argb = Unsafe.Add(ref pixelRef, i);
            var green = (argb >> 8) & 0xFF;
            var red = ((argb >> 16) - green) & 0xFF;
            var blue = (argb - green) & 0xFF;
            Unsafe.Add(ref pixelRef, i) = (argb & 0xFF00FF00) | (red << 16) | blue;
        }
    }

    #endregion

    #region SubtractGreen (Inverse)

    /// <summary>
    /// Обратное преобразование SubtractGreen: добавляет green к red и blue.
    /// </summary>
    /// <param name="pixels">Буфер пикселей ARGB.</param>
    internal static void InverseSubtractGreen(Span<uint> pixels)
    {
        ref var pixelRef = ref MemoryMarshal.GetReference(pixels);
        var count = pixels.Length;
        var i = 0;

        // SIMD: зеркало ForwardSubtractGreen, но byte-add вместо byte-subtract
        // greenAdd bytes: [G, 0, G, 0] → add → [B+G, G+0, R+G, A+0] = [B+G, G, R+G, A] ✓
        if (Vector256.IsHardwareAccelerated)
        {
            var greenMask = Vector256.Create(0x000000FFu);
            for (; i + 8 <= count; i += 8)
            {
                var px = Vector256.LoadUnsafe(ref pixelRef, (nuint)i);
                var green32 = (px >>> 8) & greenMask;
                var greenAdd = green32 | (green32 << 16);
                var result = (px.AsByte() + greenAdd.AsByte()).AsUInt32();
                result.StoreUnsafe(ref pixelRef, (nuint)i);
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            var greenMask = Vector128.Create(0x000000FFu);
            for (; i + 4 <= count; i += 4)
            {
                var px = Vector128.LoadUnsafe(ref pixelRef, (nuint)i);
                var green32 = (px >>> 8) & greenMask;
                var greenAdd = green32 | (green32 << 16);
                var result = (px.AsByte() + greenAdd.AsByte()).AsUInt32();
                result.StoreUnsafe(ref pixelRef, (nuint)i);
            }
        }

        // Scalar tail
        for (; i < count; i++)
        {
            var argb = Unsafe.Add(ref pixelRef, i);
            var green = (argb >> 8) & 0xFF;
            var red = ((argb >> 16) + green) & 0xFF;
            var blue = (argb + green) & 0xFF;
            Unsafe.Add(ref pixelRef, i) = (argb & 0xFF00FF00) | (red << 16) | blue;
        }
    }

    #endregion

    #region Predictor (Inverse)

    /// <summary>
    /// Обратное преобразование Predictor: добавляет предсказанное значение к residual.
    /// </summary>
    /// <param name="pixels">Буфер пикселей ARGB (будет модифицирован in-place).</param>
    /// <param name="width">Ширина изображения.</param>
    /// <param name="height">Высота изображения.</param>
    /// <param name="predictorImage">Данные предиктора (sub-image с пониженным разрешением). Green компонент = mode.</param>
    /// <param name="sizeBits">Размер блока = 1 &lt;&lt; sizeBits.</param>
    internal static void InversePredictor(
        Span<uint> pixels,
        int width, int height,
        ReadOnlySpan<uint> predictorImage,
        int sizeBits)
    {
        var blockSize = 1 << sizeBits;
        var tilesPerRow = DivRoundUp(width, blockSize);

        ref var pxRef = ref MemoryMarshal.GetReference(pixels);
        ref var predRef = ref MemoryMarshal.GetReference(predictorImage);

        // Первый пиксель (0,0): предиктор = 0xff000000
        if (pixels.Length > 0)
        {
            pxRef = AddArgb(pxRef, 0xFF000000);
        }

        // Первая строка: предиктор = L (mode 1) — serial dependency
        for (var x = 1; x < width; x++)
        {
            ref var cur = ref Unsafe.Add(ref pxRef, x);
            cur = AddArgb(cur, Unsafe.Add(ref pxRef, x - 1));
        }

        // Остальные строки — tile-scan с SIMD для mode 2 (T)
        for (var y = 1; y < height; y++)
        {
            var rowOffset = y * width;
            var tileY = y >> sizeBits;

            // Первый пиксель в строке: предиктор = T (mode 2)
            ref var firstPx = ref Unsafe.Add(ref pxRef, rowOffset);
            firstPx = AddArgb(firstPx, Unsafe.Add(ref pxRef, rowOffset - width));

            // Iterate by tiles for mode-specific SIMD
            var x = 1;
            while (x < width)
            {
                var tileX = x >> sizeBits;
                var tileIndex = (tileY * tilesPerRow) + tileX;
                var mode = (int)((Unsafe.Add(ref predRef, tileIndex) >> 8) & 0xF);
                var tileEnd = Math.Min((tileX + 1) << sizeBits, width);

                if (mode is 0 or 2 or 3 or 4 or 8 or 9)
                {
                    // Modes without horizontal dependency → SIMD byte-add
                    // Mode 0: Black (0xFF000000)
                    // Mode 2: T (top)
                    // Mode 3: TR (top-right)
                    // Mode 4: TL (top-left)
                    // Mode 8: Average2(TL, T)
                    // Mode 9: Average2(T, TR)
                    var simdX = x;
                    var fefe256 = Vector256.Create(0xFEFEFEFEu);
                    var fefe128 = Vector128.Create(0xFEFEFEFEu);

                    if (Vector256.IsHardwareAccelerated)
                    {
                        for (; simdX + 8 <= tileEnd; simdX += 8)
                        {
                            var pos = rowOffset + simdX;
                            var cur = Vector256.LoadUnsafe(ref pxRef, (nuint)pos);
                            var pred = mode switch
                            {
                                0 => Vector256.Create(0xFF000000u),
                                2 => Vector256.LoadUnsafe(ref pxRef, (nuint)(pos - width)),
                                3 => Vector256.LoadUnsafe(ref pxRef, (nuint)(pos - width + 1)),
                                4 => Vector256.LoadUnsafe(ref pxRef, (nuint)(pos - width - 1)),
                                8 => Average2V256(
                                    Vector256.LoadUnsafe(ref pxRef, (nuint)(pos - width - 1)),
                                    Vector256.LoadUnsafe(ref pxRef, (nuint)(pos - width)),
                                    fefe256),
                                _ => Average2V256(
                                    Vector256.LoadUnsafe(ref pxRef, (nuint)(pos - width)),
                                    Vector256.LoadUnsafe(ref pxRef, (nuint)(pos - width + 1)),
                                    fefe256),
                            };
                            (cur.AsByte() + pred.AsByte()).AsUInt32().StoreUnsafe(ref pxRef, (nuint)pos);
                        }
                    }

                    if (Vector128.IsHardwareAccelerated)
                    {
                        for (; simdX + 4 <= tileEnd; simdX += 4)
                        {
                            var pos = rowOffset + simdX;
                            var cur = Vector128.LoadUnsafe(ref pxRef, (nuint)pos);
                            var pred = mode switch
                            {
                                0 => Vector128.Create(0xFF000000u),
                                2 => Vector128.LoadUnsafe(ref pxRef, (nuint)(pos - width)),
                                3 => Vector128.LoadUnsafe(ref pxRef, (nuint)(pos - width + 1)),
                                4 => Vector128.LoadUnsafe(ref pxRef, (nuint)(pos - width - 1)),
                                8 => Average2V128(
                                    Vector128.LoadUnsafe(ref pxRef, (nuint)(pos - width - 1)),
                                    Vector128.LoadUnsafe(ref pxRef, (nuint)(pos - width)),
                                    fefe128),
                                _ => Average2V128(
                                    Vector128.LoadUnsafe(ref pxRef, (nuint)(pos - width)),
                                    Vector128.LoadUnsafe(ref pxRef, (nuint)(pos - width + 1)),
                                    fefe128),
                            };
                            (cur.AsByte() + pred.AsByte()).AsUInt32().StoreUnsafe(ref pxRef, (nuint)pos);
                        }
                    }

                    // Scalar tail
                    for (; simdX < tileEnd; simdX++)
                    {
                        var pos = rowOffset + simdX;
                        var top = Unsafe.Add(ref pxRef, pos - width);
                        var topLeft = Unsafe.Add(ref pxRef, pos - width - 1);
                        var topRight = simdX < width - 1
                            ? Unsafe.Add(ref pxRef, pos - width + 1)
                            : Unsafe.Add(ref pxRef, rowOffset - width);
                        var predicted = mode switch
                        {
                            0 => 0xFF000000u,
                            2 => top,
                            3 => topRight,
                            4 => topLeft,
                            8 => Vp8LPredictors.Average2(topLeft, top),
                            _ => Vp8LPredictors.Average2(top, topRight),
                        };
                        Unsafe.Add(ref pxRef, pos) = AddArgb(Unsafe.Add(ref pxRef, pos), predicted);
                    }

                    x = tileEnd;
                }
                else
                {
                    // Modes with horizontal dependency: specialized inner loops per-mode
                    // (eliminates Predict switch dispatch inside hot loop)
                    switch (mode)
                    {
                        case 1: // L
                            for (; x < tileEnd; x++)
                            {
                                var pos = rowOffset + x;
                                Unsafe.Add(ref pxRef, pos) = AddArgb(
                                    Unsafe.Add(ref pxRef, pos), Unsafe.Add(ref pxRef, pos - 1));
                            }
                            break;

                        case 7: // Avg(L, T)
                            for (; x < tileEnd; x++)
                            {
                                var pos = rowOffset + x;
                                Unsafe.Add(ref pxRef, pos) = AddArgb(
                                    Unsafe.Add(ref pxRef, pos),
                                    Vp8LPredictors.Average2(
                                        Unsafe.Add(ref pxRef, pos - 1),
                                        Unsafe.Add(ref pxRef, pos - width)));
                            }
                            break;

                        case 11: // Select(L, T, TL)
                            for (; x < tileEnd; x++)
                            {
                                var pos = rowOffset + x;
                                var left = Unsafe.Add(ref pxRef, pos - 1);
                                var top = Unsafe.Add(ref pxRef, pos - width);
                                var topLeft = Unsafe.Add(ref pxRef, pos - width - 1);
                                Unsafe.Add(ref pxRef, pos) = AddArgb(
                                    Unsafe.Add(ref pxRef, pos),
                                    Vp8LPredictors.Select(left, top, topLeft));
                            }
                            break;

                        case 5: // Avg(Avg(L,TR), T)
                            for (; x < tileEnd; x++)
                            {
                                var pos = rowOffset + x;
                                var topRight = x < width - 1
                                    ? Unsafe.Add(ref pxRef, pos - width + 1)
                                    : Unsafe.Add(ref pxRef, rowOffset - width);
                                Unsafe.Add(ref pxRef, pos) = AddArgb(
                                    Unsafe.Add(ref pxRef, pos),
                                    Vp8LPredictors.Average2(
                                        Vp8LPredictors.Average2(Unsafe.Add(ref pxRef, pos - 1), topRight),
                                        Unsafe.Add(ref pxRef, pos - width)));
                            }
                            break;

                        case 6: // Avg(L, TL)
                            for (; x < tileEnd; x++)
                            {
                                var pos = rowOffset + x;
                                Unsafe.Add(ref pxRef, pos) = AddArgb(
                                    Unsafe.Add(ref pxRef, pos),
                                    Vp8LPredictors.Average2(
                                        Unsafe.Add(ref pxRef, pos - 1),
                                        Unsafe.Add(ref pxRef, pos - width - 1)));
                            }
                            break;

                        case 10: // Avg(Avg(L,TL), Avg(T,TR))
                            for (; x < tileEnd; x++)
                            {
                                var pos = rowOffset + x;
                                var topRight = x < width - 1
                                    ? Unsafe.Add(ref pxRef, pos - width + 1)
                                    : Unsafe.Add(ref pxRef, rowOffset - width);
                                Unsafe.Add(ref pxRef, pos) = AddArgb(
                                    Unsafe.Add(ref pxRef, pos),
                                    Vp8LPredictors.Average2(
                                        Vp8LPredictors.Average2(
                                            Unsafe.Add(ref pxRef, pos - 1),
                                            Unsafe.Add(ref pxRef, pos - width - 1)),
                                        Vp8LPredictors.Average2(
                                            Unsafe.Add(ref pxRef, pos - width),
                                            topRight)));
                            }
                            break;

                        default: // 12 (ClampAddSubFull), 13 (ClampAddSubHalf)
                            for (; x < tileEnd; x++)
                            {
                                var pos = rowOffset + x;
                                var left = Unsafe.Add(ref pxRef, pos - 1);
                                var top = Unsafe.Add(ref pxRef, pos - width);
                                var topLeft = Unsafe.Add(ref pxRef, pos - width - 1);
                                var topRight = x < width - 1
                                    ? Unsafe.Add(ref pxRef, pos - width + 1)
                                    : Unsafe.Add(ref pxRef, rowOffset - width);
                                var predicted = Vp8LPredictors.Predict(mode, left, top, topLeft, topRight);
                                Unsafe.Add(ref pxRef, pos) = AddArgb(Unsafe.Add(ref pxRef, pos), predicted);
                            }
                            break;
                    }
                }
            }
        }
    }

    #endregion

    #region Color Transform (Inverse)

    /// <summary>
    /// Обратное преобразование Color (CrossColor): корректирует red и blue на основе green.
    /// </summary>
    /// <param name="pixels">Буфер пикселей ARGB.</param>
    /// <param name="width">Ширина изображения.</param>
    /// <param name="height">Высота изображения.</param>
    /// <param name="colorImage">Sub-image с ColorTransformElement данными.</param>
    /// <param name="sizeBits">Размер блока.</param>
    internal static unsafe void InverseColorTransform(
        Span<uint> pixels,
        int width, int height,
        ReadOnlySpan<uint> colorImage,
        int sizeBits)
    {
        var tpr = DivRoundUp(width, 1 << sizeBits);
        var sb = sizeBits;
        var w = width;

        fixed (uint* pixPtr = pixels)
        fixed (uint* ctPtr = colorImage)
        {
            var pp = pixPtr;
            var ct = ctPtr;

            Parallel.For(0, height, y =>
            {
                ref var pxRef = ref Unsafe.AsRef<uint>(pp);
                ref var ctRef = ref Unsafe.AsRef<uint>(ct);

                var rowOffset = y * w;
                var tileY = y >> sb;
                var x = 0;

                while (x < w)
                {
                    var tileX = x >> sb;
                    var tileIndex = (tileY * tpr) + tileX;
                    var tileEnd = Math.Min((tileX + 1) << sb, w);

                    var cte = Unsafe.Add(ref ctRef, tileIndex);
                    var greenToRed = (int)(sbyte)(cte & 0xFF);
                    var greenToBlue = (int)(sbyte)((cte >> 8) & 0xFF);
                    var redToBlue = (int)(sbyte)((cte >> 16) & 0xFF);

                    if (Vector256.IsHardwareAccelerated)
                    {
                        var vG2R256 = Vector256.Create(greenToRed);
                        var vG2B256 = Vector256.Create(greenToBlue);
                        var vR2B256 = Vector256.Create(redToBlue);
                        var vMask256 = Vector256.Create(0xFF);
                        var vSignExt256 = Vector256.Create(0x80);
                        var vGreenAlphaMask256 = Vector256.Create(0xFF00FF00u);

                        for (; x + 8 <= tileEnd; x += 8)
                        {
                            var pos = rowOffset + x;
                            var px = Vector256.LoadUnsafe(ref pxRef, (nuint)pos).AsInt32();

                            var green = (px >>> 8) & vMask256;
                            var signedGreen = (green ^ vSignExt256) - vSignExt256;

                            var red = (px >>> 16) & vMask256;
                            var blue = px & vMask256;

                            red = (red + ((vG2R256 * signedGreen) >> 5)) & vMask256;

                            var signedRed = (red ^ vSignExt256) - vSignExt256;
                            blue = (blue + ((vG2B256 * signedGreen) >> 5) + ((vR2B256 * signedRed) >> 5)) & vMask256;

                            var result = (px.AsUInt32() & vGreenAlphaMask256) | (red.AsUInt32() << 16) | blue.AsUInt32();
                            result.StoreUnsafe(ref pxRef, (nuint)pos);
                        }
                    }

                    if (Vector128.IsHardwareAccelerated)
                    {
                        var vG2R = Vector128.Create(greenToRed);
                        var vG2B = Vector128.Create(greenToBlue);
                        var vR2B = Vector128.Create(redToBlue);
                        var vMask = Vector128.Create(0xFF);
                        var vSignExt = Vector128.Create(0x80);
                        var vGreenAlphaMask = Vector128.Create(0xFF00FF00u);

                        for (; x + 4 <= tileEnd; x += 4)
                        {
                            var pos = rowOffset + x;
                            var px = Vector128.LoadUnsafe(ref pxRef, (nuint)pos).AsInt32();

                            var green = (px >>> 8) & vMask;
                            var signedGreen = (green ^ vSignExt) - vSignExt;

                            var red = (px >>> 16) & vMask;
                            var blue = px & vMask;

                            red = (red + ((vG2R * signedGreen) >> 5)) & vMask;

                            var signedRed = (red ^ vSignExt) - vSignExt;
                            blue = (blue + ((vG2B * signedGreen) >> 5) + ((vR2B * signedRed) >> 5)) & vMask;

                            var result = (px.AsUInt32() & vGreenAlphaMask) | (red.AsUInt32() << 16) | blue.AsUInt32();
                            result.StoreUnsafe(ref pxRef, (nuint)pos);
                        }
                    }

                    // Scalar tail
                    for (; x < tileEnd; x++)
                    {
                        var pos = rowOffset + x;
                        var argb = Unsafe.Add(ref pxRef, pos);

                        var green = (int)((argb >> 8) & 0xFF);
                        var red = (int)((argb >> 16) & 0xFF);
                        var blue = (int)(argb & 0xFF);

                        var signedGreen = (sbyte)(byte)green;

                        red += (greenToRed * signedGreen) >> 5;
                        red &= 0xFF;

                        blue += (greenToBlue * signedGreen) >> 5;
                        blue += (redToBlue * (sbyte)(byte)red) >> 5;
                        blue &= 0xFF;

                        Unsafe.Add(ref pxRef, pos) = (argb & 0xFF00FF00) | ((uint)red << 16) | (uint)blue;
                    }
                }
            });
        }
    }

    #endregion

    #region Color Indexing (Inverse)

    /// <summary>
    /// Обратное преобразование Color Indexing: заменяет индексы на цвета из палитры.
    /// </summary>
    /// <param name="pixels">Буфер пикселей (индексы → будут заменены на ARGB).</param>
    /// <param name="width">Ширина оригинального изображения.</param>
    /// <param name="height">Высота изображения.</param>
    /// <param name="palette">Таблица цветов (до 256 записей).</param>
    /// <param name="widthBits">Биты упаковки пикселей (0=none, 1=2px, 2=4px, 3=8px).</param>
    internal static void InverseColorIndexing(
        Span<uint> pixels,
        int width, int height,
        ReadOnlySpan<uint> palette,
        int widthBits)
    {
        if (widthBits == 0)
        {
            // Нет упаковки — каждый пиксель содержит индекс в green канале
            for (var i = 0; i < width * height; i++)
            {
                var index = (int)((pixels[i] >> 8) & 0xFF);
                pixels[i] = index < palette.Length ? palette[index] : 0;
            }

            return;
        }

        // С упаковкой — несколько индексов в одном пиксельном green байте
        var pixelsPerByte = 1 << widthBits;
        var bitsPerIndex = 8 >> widthBits;
        var indexMask = (1 << bitsPerIndex) - 1;

        // Работаем снизу вверх / справа налево чтобы не затирать ещё не прочитанные данные
        var packedWidth = DivRoundUp(width, pixelsPerByte);

        for (var y = height - 1; y >= 0; y--)
        {
            // Читаем упакованные данные из начала строки
            for (var packedX = packedWidth - 1; packedX >= 0; packedX--)
            {
                var packed = pixels[(y * packedWidth) + packedX];
                var greenByte = (int)((packed >> 8) & 0xFF);

                for (var bit = pixelsPerByte - 1; bit >= 0; bit--)
                {
                    var x = (packedX * pixelsPerByte) + bit;
                    if (x >= width)
                    {
                        continue;
                    }

                    var index = (greenByte >> (bit * bitsPerIndex)) & indexMask;
                    pixels[(y * width) + x] = index < palette.Length ? palette[index] : 0;
                }
            }
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Покомпонентное сложение двух ARGB пикселей (mod 256 на каждый канал).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint AddArgb(uint a, uint b)
    {
        // Складываем каждый из 4 каналов с маскированием до 8 бит
        var alpha = (((a >> 24) + (b >> 24)) & 0xFF) << 24;
        var red = (((a >> 16) + (b >> 16)) & 0xFF) << 16;
        var green = (((a >> 8) + (b >> 8)) & 0xFF) << 8;
        var blue = (a + b) & 0xFF;
        return alpha | red | green | blue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<uint> Average2V256(Vector256<uint> a, Vector256<uint> b, Vector256<uint> fefe) =>
        (a & b) + (((a ^ b) & fefe) >>> 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> Average2V128(Vector128<uint> a, Vector128<uint> b, Vector128<uint> fefe) =>
        (a & b) + (((a ^ b) & fefe) >>> 1);

    /// <summary>
    /// Целочисленное деление с округлением вверх.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int DivRoundUp(int num, int den) => (num + den - 1) / den;

    #endregion
}