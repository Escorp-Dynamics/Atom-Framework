#pragma warning disable IDE0010, S109, MA0051, S3776, IDE0051

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media;

/// <summary>
/// PNG фильтры для декодирования изображений.
/// </summary>
/// <remarks>
/// SIMD реализации (SSE2/AVX2) для PNG фильтров оказались медленнее скалярной версии
/// из-за сложной зависимости данных между пикселями. Скалярная реализация лучше
/// использует кеш и branch prediction современных процессоров.
/// SIMD методы оставлены для справки и возможных будущих оптимизаций.
/// </remarks>
public sealed partial class PngCodec
{
    #region Defilter Dispatcher

    /// <summary>
    /// Применяет PNG фильтры для восстановления данных изображения.
    /// </summary>
    /// <remarks>
    /// SIMD реализации (SSE2/AVX2) для PNG фильтров оказались медленнее скалярной версии
    /// из-за сложной зависимости данных между пикселями (особенно в Paeth filter).
    /// Скалярная реализация лучше использует кеш и branch prediction.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void DefilterPng(ReadOnlySpan<byte> filtered, Plane<byte> output, PngIhdr header)
    {
        var width = header.Width;
        var height = header.Height;
        var bpp = header.FilterBpp;
        var rowBytes = CalculateRowBytes(width, header.BitsPerPixel);
        var filteredRowSize = rowBytes + 1; // +1 для filter byte

        _ = Acceleration; // Используем поле для совместимости API

        // Скалярная реализация оптимальна для PNG фильтров
        // благодаря лучшему использованию кеша и предсказанию ветвлений
        DefilterScalar(filtered, output, height, rowBytes, filteredRowSize, bpp);
    }

    #endregion

    #region Scalar Implementation

    /// <summary>
    /// Скалярная реализация PNG дефильтрации с pointer арифметикой.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void DefilterScalar(ReadOnlySpan<byte> filtered, Plane<byte> output, int height, int rowBytes, int filteredRowSize, int bpp)
    {
        // Используем stackalloc для prevRow
        var prevRowBuffer = stackalloc byte[rowBytes];

        // Инициализируем нулями (для первой строки prev = 0)
        new Span<byte>(prevRowBuffer, rowBytes).Clear();

        fixed (byte* pFiltered = filtered)
        {
            var pSrc = pFiltered;

            for (var y = 0; y < height; y++)
            {
                var filterType = *pSrc;
                var pRowData = pSrc + 1;
                var outRow = output.GetRow(y);

                fixed (byte* pOut = outRow)
                {
                    switch (filterType)
                    {
                        case FilterNone:
                            Buffer.MemoryCopy(pRowData, pOut, rowBytes, rowBytes);
                            break;

                        case FilterSub:
                            DefilterSubScalarFast(pRowData, pOut, rowBytes, bpp);
                            break;

                        case FilterUp:
                            DefilterUpScalarFast(pRowData, prevRowBuffer, pOut, rowBytes);
                            break;

                        case FilterAverage:
                            DefilterAverageScalarFast(pRowData, prevRowBuffer, pOut, rowBytes, bpp);
                            break;

                        case FilterPaeth:
                            DefilterPaethScalarFast(pRowData, prevRowBuffer, pOut, rowBytes, bpp);
                            break;
                    }

                    // Копируем текущую строку в prevRow
                    Buffer.MemoryCopy(pOut, prevRowBuffer, rowBytes, rowBytes);
                }

                pSrc += filteredRowSize;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DefilterSubScalarFast(byte* src, byte* dst, int length, int bpp)
    {
        // Первые bpp байт — без left
        var i = 0;
        for (; i < bpp; i++)
        {
            dst[i] = src[i];
        }

        // Остальные — с зависимостью от left
        // Развёртка по 4 байта для лучшего ILP
        var end4 = length - 3;
        for (; i < end4; i += 4)
        {
            dst[i] = (byte)(src[i] + dst[i - bpp]);
            dst[i + 1] = (byte)(src[i + 1] + dst[i + 1 - bpp]);
            dst[i + 2] = (byte)(src[i + 2] + dst[i + 2 - bpp]);
            dst[i + 3] = (byte)(src[i + 3] + dst[i + 3 - bpp]);
        }

        // Хвост
        for (; i < length; i++)
        {
            dst[i] = (byte)(src[i] + dst[i - bpp]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DefilterUpScalarFast(byte* src, byte* prev, byte* dst, int length)
    {
        // Развёртка по 8 байт
        var i = 0;
        var end8 = length - 7;
        for (; i < end8; i += 8)
        {
            dst[i] = (byte)(src[i] + prev[i]);
            dst[i + 1] = (byte)(src[i + 1] + prev[i + 1]);
            dst[i + 2] = (byte)(src[i + 2] + prev[i + 2]);
            dst[i + 3] = (byte)(src[i + 3] + prev[i + 3]);
            dst[i + 4] = (byte)(src[i + 4] + prev[i + 4]);
            dst[i + 5] = (byte)(src[i + 5] + prev[i + 5]);
            dst[i + 6] = (byte)(src[i + 6] + prev[i + 6]);
            dst[i + 7] = (byte)(src[i + 7] + prev[i + 7]);
        }

        // Хвост
        for (; i < length; i++)
        {
            dst[i] = (byte)(src[i] + prev[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DefilterAverageScalarFast(byte* src, byte* prev, byte* dst, int length, int bpp)
    {
        // Первые bpp байт — без left
        var i = 0;
        for (; i < bpp; i++)
        {
            dst[i] = (byte)(src[i] + (prev[i] >> 1));
        }

        // Остальные — развёртка по 4
        var end4 = length - 3;
        for (; i < end4; i += 4)
        {
            dst[i] = (byte)(src[i] + ((dst[i - bpp] + prev[i]) >> 1));
            dst[i + 1] = (byte)(src[i + 1] + ((dst[i + 1 - bpp] + prev[i + 1]) >> 1));
            dst[i + 2] = (byte)(src[i + 2] + ((dst[i + 2 - bpp] + prev[i + 2]) >> 1));
            dst[i + 3] = (byte)(src[i + 3] + ((dst[i + 3 - bpp] + prev[i + 3]) >> 1));
        }

        // Хвост
        for (; i < length; i++)
        {
            dst[i] = (byte)(src[i] + ((dst[i - bpp] + prev[i]) >> 1));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DefilterPaethScalarFast(byte* src, byte* prev, byte* dst, int length, int bpp)
    {
        // Первые bpp байт — без left и upLeft (left=0, upLeft=0)
        var i = 0;
        for (; i < bpp; i++)
        {
            // PaethPredictor(0, prev[i], 0) = prev[i] когда pa >= pb
            dst[i] = (byte)(src[i] + prev[i]);
        }

        // Остальные — развёртка по 4
        var end4 = length - 3;
        for (; i < end4; i += 4)
        {
            dst[i] = (byte)(src[i] + PaethPredictorFast(dst[i - bpp], prev[i], prev[i - bpp]));
            dst[i + 1] = (byte)(src[i + 1] + PaethPredictorFast(dst[i + 1 - bpp], prev[i + 1], prev[i + 1 - bpp]));
            dst[i + 2] = (byte)(src[i + 2] + PaethPredictorFast(dst[i + 2 - bpp], prev[i + 2], prev[i + 2 - bpp]));
            dst[i + 3] = (byte)(src[i + 3] + PaethPredictorFast(dst[i + 3 - bpp], prev[i + 3], prev[i + 3 - bpp]));
        }

        // Хвост
        for (; i < length; i++)
        {
            dst[i] = (byte)(src[i] + PaethPredictorFast(dst[i - bpp], prev[i], prev[i - bpp]));
        }
    }

    // Оставляем старые методы для совместимости с SIMD реализациями
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DefilterSubScalar(ReadOnlySpan<byte> data, Span<byte> output, int bpp)
    {
        for (var i = 0; i < bpp && i < data.Length; i++)
            output[i] = data[i];

        for (var i = bpp; i < data.Length; i++)
            output[i] = (byte)(data[i] + output[i - bpp]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DefilterUpScalar(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prev, Span<byte> output)
    {
        for (var i = 0; i < data.Length; i++)
            output[i] = (byte)(data[i] + prev[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DefilterAverageScalar(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prev, Span<byte> output, int bpp)
    {
        for (var i = 0; i < bpp && i < data.Length; i++)
            output[i] = (byte)(data[i] + (prev[i] >> 1));

        for (var i = bpp; i < data.Length; i++)
            output[i] = (byte)(data[i] + ((output[i - bpp] + prev[i]) >> 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DefilterPaethScalar(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prev, Span<byte> output, int bpp)
    {
        for (var i = 0; i < bpp && i < data.Length; i++)
            output[i] = (byte)(data[i] + PaethPredictor(0, prev[i], 0));

        for (var i = bpp; i < data.Length; i++)
            output[i] = (byte)(data[i] + PaethPredictor(output[i - bpp], prev[i], prev[i - bpp]));
    }

    /// <summary>
    /// PNG Paeth predictor.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = p - a;
        pa = (pa ^ (pa >> 31)) - (pa >> 31);
        var pb = p - b;
        pb = (pb ^ (pb >> 31)) - (pb >> 31);
        var pc = p - c;
        pc = (pc ^ (pc >> 31)) - (pc >> 31);

        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    #endregion

    #region SSE2 Implementation

    /// <summary>
    /// SSE2-оптимизированная реализация PNG дефильтрации.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void DefilterSse2(ReadOnlySpan<byte> filtered, Plane<byte> output, int height, int rowBytes, int filteredRowSize, int bpp)
    {
        Span<byte> prevRowBuffer = stackalloc byte[rowBytes + 16]; // +16 для выравнивания
        prevRowBuffer.Clear();
        var prevRow = prevRowBuffer[..rowBytes];

        for (var y = 0; y < height; y++)
        {
            var filteredRow = filtered.Slice(y * filteredRowSize, filteredRowSize);
            var filterType = filteredRow[0];
            var rowData = filteredRow[1..];
            var outRow = output.GetRow(y)[..rowBytes];

            switch (filterType)
            {
                case FilterNone:
                    rowData.CopyTo(outRow);
                    break;

                case FilterSub:
                    // Sub имеет зависимость по данным — используем скаляр для первых bpp байт
                    DefilterSubScalar(rowData, outRow, bpp);
                    break;

                case FilterUp:
                    DefilterUpSse2(rowData, prevRow, outRow);
                    break;

                case FilterAverage:
                    DefilterAverageScalar(rowData, prevRow, outRow, bpp);
                    break;

                case FilterPaeth:
                    DefilterPaethScalar(rowData, prevRow, outRow, bpp);
                    break;
            }

            outRow.CopyTo(prevRow);
        }
    }

    /// <summary>
    /// SSE2-оптимизированный Up фильтр.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void DefilterUpSse2(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prev, Span<byte> output)
    {
        var i = 0;
        var length = data.Length;

        fixed (byte* pData = data)
        fixed (byte* pPrev = prev)
        fixed (byte* pOut = output)
        {
            // 16 байт за итерацию
            while (i + 16 <= length)
            {
                var vData = Sse2.LoadVector128(pData + i);
                var vPrev = Sse2.LoadVector128(pPrev + i);
                var vResult = Sse2.Add(vData, vPrev);
                Sse2.Store(pOut + i, vResult);
                i += 16;
            }

            // Скалярный хвост
            while (i < length)
            {
                pOut[i] = (byte)(pData[i] + pPrev[i]);
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation

    /// <summary>
    /// AVX2-оптимизированная реализация PNG дефильтрации.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void DefilterAvx2(ReadOnlySpan<byte> filtered, Plane<byte> output, int height, int rowBytes, int filteredRowSize, int bpp)
    {
        Span<byte> prevRowBuffer = stackalloc byte[rowBytes + 32]; // +32 для выравнивания
        prevRowBuffer.Clear();
        var prevRow = prevRowBuffer[..rowBytes];

        for (var y = 0; y < height; y++)
        {
            var filteredRow = filtered.Slice(y * filteredRowSize, filteredRowSize);
            var filterType = filteredRow[0];
            var rowData = filteredRow[1..];
            var outRow = output.GetRow(y)[..rowBytes];

            switch (filterType)
            {
                case FilterNone:
                    rowData.CopyTo(outRow);
                    break;

                case FilterSub:
                    // Sub имеет зависимость по данным — bpp-aware скалярная обработка
                    DefilterSubScalar(rowData, outRow, bpp);
                    break;

                case FilterUp:
                    DefilterUpAvx2(rowData, prevRow, outRow);
                    break;

                case FilterAverage:
                    if (bpp == 4)
                        DefilterAverageBpp4Avx2(rowData, prevRow, outRow);
                    else
                        DefilterAverageScalar(rowData, prevRow, outRow, bpp);
                    break;

                case FilterPaeth:
                    if (bpp == 4)
                        DefilterPaethBpp4Avx2(rowData, prevRow, outRow);
                    else
                        DefilterPaethScalar(rowData, prevRow, outRow, bpp);
                    break;
            }

            outRow.CopyTo(prevRow);
        }
    }

    /// <summary>
    /// AVX2-оптимизированный Up фильтр.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void DefilterUpAvx2(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prev, Span<byte> output)
    {
        var i = 0;
        var length = data.Length;

        fixed (byte* pData = data)
        fixed (byte* pPrev = prev)
        fixed (byte* pOut = output)
        {
            // 32 байта за итерацию
            while (i + 32 <= length)
            {
                var vData = Avx.LoadVector256(pData + i);
                var vPrev = Avx.LoadVector256(pPrev + i);
                var vResult = Avx2.Add(vData, vPrev);
                Avx.Store(pOut + i, vResult);
                i += 32;
            }

            // 16 байт (SSE2 fallback)
            while (i + 16 <= length)
            {
                var vData = Sse2.LoadVector128(pData + i);
                var vPrev = Sse2.LoadVector128(pPrev + i);
                var vResult = Sse2.Add(vData, vPrev);
                Sse2.Store(pOut + i, vResult);
                i += 16;
            }

            // Скалярный хвост
            while (i < length)
            {
                pOut[i] = (byte)(pData[i] + pPrev[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// AVX2-оптимизированный Sub фильтр для bpp=3 (RGB24).
    /// Использует 4-byte групповую обработку с маскированием.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void DefilterSubBpp3Avx2(ReadOnlySpan<byte> data, Span<byte> output)
    {
        // Для bpp=3 зависимость: out[i] = data[i] + out[i-3]
        // Обрабатываем по 12 байт (4 пикселя RGB) за раз, развёртывая зависимость

        var i = 0;
        var length = data.Length;

        fixed (byte* pData = data)
        fixed (byte* pOut = output)
        {
            // Первые 3 байта — без left
            if (length >= 3)
            {
                pOut[0] = pData[0];
                pOut[1] = pData[1];
                pOut[2] = pData[2];
                i = 3;
            }

            // Обрабатываем по 3 байта с зависимостью
            // Можно развернуть цикл для лучшего ILP
            while (i + 12 <= length)
            {
                // Пиксель 1
                pOut[i] = (byte)(pData[i] + pOut[i - 3]);
                pOut[i + 1] = (byte)(pData[i + 1] + pOut[i - 2]);
                pOut[i + 2] = (byte)(pData[i + 2] + pOut[i - 1]);

                // Пиксель 2
                pOut[i + 3] = (byte)(pData[i + 3] + pOut[i]);
                pOut[i + 4] = (byte)(pData[i + 4] + pOut[i + 1]);
                pOut[i + 5] = (byte)(pData[i + 5] + pOut[i + 2]);

                // Пиксель 3
                pOut[i + 6] = (byte)(pData[i + 6] + pOut[i + 3]);
                pOut[i + 7] = (byte)(pData[i + 7] + pOut[i + 4]);
                pOut[i + 8] = (byte)(pData[i + 8] + pOut[i + 5]);

                // Пиксель 4
                pOut[i + 9] = (byte)(pData[i + 9] + pOut[i + 6]);
                pOut[i + 10] = (byte)(pData[i + 10] + pOut[i + 7]);
                pOut[i + 11] = (byte)(pData[i + 11] + pOut[i + 8]);

                i += 12;
            }

            // Хвост
            while (i < length)
            {
                pOut[i] = (byte)(pData[i] + pOut[i - 3]);
                i++;
            }
        }
    }

    /// <summary>
    /// AVX2-оптимизированный Sub фильтр для bpp=4 (RGBA32).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void DefilterSubBpp4Avx2(ReadOnlySpan<byte> data, Span<byte> output)
    {
        var i = 0;
        var length = data.Length;

        fixed (byte* pData = data)
        fixed (byte* pOut = output)
        {
            // Первые 4 байта — без left
            if (length >= 4)
            {
                Unsafe.WriteUnaligned(pOut, Unsafe.ReadUnaligned<uint>(pData));
                i = 4;
            }

            // Обрабатываем по 16 байт (4 пикселя RGBA) с развёрнутой зависимостью
            while (i + 16 <= length)
            {
                // Загружаем 4 uint32
                var d0 = Unsafe.ReadUnaligned<uint>(pData + i);
                var d1 = Unsafe.ReadUnaligned<uint>(pData + i + 4);
                var d2 = Unsafe.ReadUnaligned<uint>(pData + i + 8);
                var d3 = Unsafe.ReadUnaligned<uint>(pData + i + 12);

                // Добавляем предыдущий пиксель
                var prev = Unsafe.ReadUnaligned<uint>(pOut + i - 4);

                // Развёртываем зависимость
                var r0 = AddBytes(d0, prev);
                var r1 = AddBytes(d1, r0);
                var r2 = AddBytes(d2, r1);
                var r3 = AddBytes(d3, r2);

                // Записываем результат
                Unsafe.WriteUnaligned(pOut + i, r0);
                Unsafe.WriteUnaligned(pOut + i + 4, r1);
                Unsafe.WriteUnaligned(pOut + i + 8, r2);
                Unsafe.WriteUnaligned(pOut + i + 12, r3);

                i += 16;
            }

            // Хвост
            while (i + 4 <= length)
            {
                var d = Unsafe.ReadUnaligned<uint>(pData + i);
                var prev = Unsafe.ReadUnaligned<uint>(pOut + i - 4);
                Unsafe.WriteUnaligned(pOut + i, AddBytes(d, prev));
                i += 4;
            }

            while (i < length)
            {
                pOut[i] = (byte)(pData[i] + pOut[i - 4]);
                i++;
            }
        }
    }

    /// <summary>
    /// Складывает два uint32 побайтово (с переполнением).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AddBytes(uint a, uint b)
    {
        // Побайтовое сложение без переноса между байтами
        var mask = 0x00FF00FFu;
        var lowA = a & mask;
        var lowB = b & mask;
        var highA = (a >> 8) & mask;
        var highB = (b >> 8) & mask;
        return ((lowA + lowB) & mask) | (((highA + highB) & mask) << 8);
    }

    /// <summary>
    /// AVX2-оптимизированный Average фильтр для bpp=4.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void DefilterAverageBpp4Avx2(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prev, Span<byte> output)
    {
        var i = 0;
        var length = data.Length;

        fixed (byte* pData = data)
        fixed (byte* pPrev = prev)
        fixed (byte* pOut = output)
        {
            // Первые 4 байта — без left
            if (length >= 4)
            {
                for (var j = 0; j < 4; j++)
                {
                    pOut[j] = (byte)(pData[j] + (pPrev[j] >> 1));
                }
                i = 4;
            }

            // Обработка по 4 байта с зависимостью
            while (i + 4 <= length)
            {
                var d = Unsafe.ReadUnaligned<uint>(pData + i);
                var up = Unsafe.ReadUnaligned<uint>(pPrev + i);
                var left = Unsafe.ReadUnaligned<uint>(pOut + i - 4);

                // avg = (left + up) >> 1, побайтово
                var avg = AverageBytes(left, up);
                var result = AddBytes(d, avg);

                Unsafe.WriteUnaligned(pOut + i, result);
                i += 4;
            }

            // Хвост
            while (i < length)
            {
                var left = i >= 4 ? pOut[i - 4] : (byte)0;
                var up = pPrev[i];
                pOut[i] = (byte)(pData[i] + ((left + up) >> 1));
                i++;
            }
        }
    }

    /// <summary>
    /// Вычисляет среднее двух uint32 побайтово.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AverageBytes(uint a, uint b) =>
        // (a + b) >> 1 побайтово
        // Используем SWAR trick: ((a ^ b) >> 1) + (a & b)
        ((a ^ b) & 0xFEFEFEFE) >> 1;

    /// <summary>
    /// AVX2-оптимизированный Paeth фильтр для bpp=4.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void DefilterPaethBpp4Avx2(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prev, Span<byte> output)
    {
        var i = 0;
        var length = data.Length;

        fixed (byte* pData = data)
        fixed (byte* pPrev = prev)
        fixed (byte* pOut = output)
        {
            // Первые 4 байта — без left и upLeft
            if (length >= 4)
            {
                for (var j = 0; j < 4; j++)
                {
                    pOut[j] = (byte)(pData[j] + PaethPredictor(0, pPrev[j], 0));
                }
                i = 4;
            }

            // Обработка по пикселям (Paeth имеет сложную зависимость)
            while (i + 4 <= length)
            {
                // Развёрнутая обработка 4 байт (1 RGBA пиксель)
                pOut[i] = (byte)(pData[i] + PaethPredictor(pOut[i - 4], pPrev[i], pPrev[i - 4]));
                pOut[i + 1] = (byte)(pData[i + 1] + PaethPredictor(pOut[i - 3], pPrev[i + 1], pPrev[i - 3]));
                pOut[i + 2] = (byte)(pData[i + 2] + PaethPredictor(pOut[i - 2], pPrev[i + 2], pPrev[i - 2]));
                pOut[i + 3] = (byte)(pData[i + 3] + PaethPredictor(pOut[i - 1], pPrev[i + 3], pPrev[i - 1]));
                i += 4;
            }

            // Хвост
            while (i < length)
            {
                var left = pOut[i - 4];
                var up = pPrev[i];
                var upLeft = pPrev[i - 4];
                pOut[i] = (byte)(pData[i] + PaethPredictor(left, up, upLeft));
                i++;
            }
        }
    }

    #endregion
}
