# План реализации медиа-кодеков Atom

## Текущее состояние

| Кодек | Контейнер | Компрессия | Реальный формат | Совместимость |
|-------|-----------|------------|-----------------|---------------|
| **PNG** | ✅ Полный | DEFLATE (zlib) | ✅ Да | ✅ Открывается везде |
| **WebP** | ✅ RIFF/WEBP | ARAW (raw) | ❌ Store mode | ❌ Только внутренний |
| **MP4** | ✅ ISO BMFF | AFRM (raw) | ❌ Store mode | ❌ Только внутренний |
| **WebM** | ✅ EBML/Matroska | AFRM (raw) | ❌ Store mode | ❌ Только внутренний |

---

## Архитектура модулей

```
Framework/Atom.Media/
├── Compression/                    # Алгоритмы сжатия
│   ├── Deflate/                    # ✅ ЕСТЬ (через System.IO.Compression)
│   ├── Huffman/                    # 🔧 НУЖНО: универсальный Huffman codec
│   ├── Lz77/                       # 🔧 НУЖНО: LZ77 back-references
│   └── Arithmetic/                 # 🔧 НУЖНО: arithmetic coding (VP8L/H.264)
│
├── Transforms/                     # Математические преобразования
│   ├── Dct/                        # 🔧 НУЖНО: DCT/IDCT (JPEG, H.264, VP8/VP9)
│   ├── Hadamard/                   # 🔧 НУЖНО: WHT (H.264, VP8)
│   └── Wavelet/                    # 📌 ОПЦИОНАЛЬНО: DWT (JPEG 2000)
│
├── Prediction/                     # Предсказание пикселей
│   ├── Intra/                      # 🔧 НУЖНО: внутрикадровое (H.264/VP8/VP9)
│   └── Inter/                      # 🔧 НУЖНО: межкадровое + motion vectors
│
├── Entropy/                        # Энтропийное кодирование
│   ├── Cabac/                      # 🔧 НУЖНО: CABAC (H.264 High Profile)
│   ├── Cavlc/                      # 🔧 НУЖНО: CAVLC (H.264 Baseline)
│   └── BoolCoder/                  # 🔧 НУЖНО: VP8/VP9 boolean coder
│
├── Bitstream/                      # Работа с битовыми потоками
│   ├── BitReader.cs                # 🔧 НУЖНО: универсальный bit reader
│   ├── BitWriter.cs                # 🔧 НУЖНО: универсальный bit writer
│   └── ExpGolomb.cs                # 🔧 НУЖНО: Exp-Golomb кодирование (H.264)
│
├── Quantization/                   # Квантование
│   ├── QuantTable.cs               # 🔧 НУЖНО: таблицы квантования
│   └── Dequant.cs                  # 🔧 НУЖНО: обратное квантование
│
└── Codecs/
    ├── Png/                        # ✅ ГОТОВ (нужны улучшения)
    ├── Webp/                       # 🔧 НУЖНО: VP8L/VP8 реализация
    ├── Mp4/                        # 🔧 НУЖНО: H.264 реализация
    └── Webm/                       # 🔧 НУЖНО: VP9 реализация
```

---

## Модуль 1: Bitstream (Приоритет: КРИТИЧЕСКИЙ)

Фундаментальный модуль для всех кодеков.

### Файлы

```
Bitstream/
├── BitReader.cs          # Чтение произвольного кол-ва бит
├── BitWriter.cs          # Запись произвольного кол-ва бит
├── ExpGolomb.cs          # Exp-Golomb для H.264
├── VlcTable.cs           # Variable Length Codes таблицы
└── VlcDecoder.cs         # VLC декодирование
```

### BitReader.cs

```csharp
/// <summary>
/// Высокопроизводительный читатель битового потока.
/// </summary>
/// <remarks>
/// Поддерживает:
/// - LSB/MSB first порядок бит
/// - Чтение 1-32 бит за операцию
/// - Peek без продвижения позиции
/// - Выравнивание на байт
/// </remarks>
public ref struct BitReader
{
    private readonly ReadOnlySpan<byte> data;
    private int bytePosition;
    private int bitPosition;  // 0-7, биты внутри текущего байта
    private ulong buffer;     // 64-bit буфер для быстрого чтения
    private int bufferBits;   // сколько бит в буфере

    // Конструктор
    public BitReader(ReadOnlySpan<byte> data);

    // Основные операции
    public uint ReadBits(int count);           // 1-32 бит
    public uint PeekBits(int count);           // без продвижения
    public bool ReadBit();                     // 1 бит
    public void SkipBits(int count);           // пропуск
    public void AlignToByte();                 // выравнивание

    // Состояние
    public int Position { get; }               // позиция в битах
    public int Remaining { get; }              // оставшиеся биты
    public bool IsAtEnd { get; }
}
```

### BitWriter.cs

```csharp
/// <summary>
/// Высокопроизводительный писатель битового потока.
/// </summary>
public ref struct BitWriter
{
    private readonly Span<byte> data;
    private int bytePosition;
    private int bitPosition;
    private ulong buffer;
    private int bufferBits;

    public BitWriter(Span<byte> data);

    public void WriteBits(uint value, int count);
    public void WriteBit(bool value);
    public void AlignToByte();
    public void Flush();

    public int BytesWritten { get; }
}
```

### Зависимости
- Нет внешних зависимостей
- Используется: Huffman, ExpGolomb, все кодеки

### Тесты
- Чтение/запись 1-32 бит
- LSB/MSB режимы
- Выравнивание
- Edge cases (конец данных)

---

## Модуль 2: Huffman (Приоритет: КРИТИЧЕСКИЙ)

Нужен для: PNG (внутри DEFLATE), VP8L, JPEG.

### Файлы

```
Compression/Huffman/
├── HuffmanTree.cs           # Построение дерева
├── HuffmanDecoder.cs        # Декодирование (lookup table)
├── HuffmanEncoder.cs        # Кодирование (canonical)
├── CanonicalHuffman.cs      # Canonical Huffman codes
└── HuffmanVectors.cs        # SIMD константы
```

### HuffmanTree.cs

```csharp
/// <summary>
/// Дерево Хаффмана с поддержкой canonical codes.
/// </summary>
public sealed class HuffmanTree
{
    // Максимальная длина кода (VP8L: 15, DEFLATE: 15, JPEG: 16)
    public const int MaxCodeLength = 16;

    // Lookup tables для быстрого декодирования
    private readonly ushort[] firstCode;      // первый код каждой длины
    private readonly ushort[] firstSymbol;    // первый символ каждой длины
    private readonly ushort[] symbols;        // отсортированные символы
    private readonly byte[] codeLengths;      // длины кодов

    // Построение из code lengths (как в DEFLATE/VP8L)
    public static HuffmanTree FromCodeLengths(ReadOnlySpan<byte> codeLengths);

    // Построение из частот (для encoder)
    public static HuffmanTree FromFrequencies(ReadOnlySpan<uint> frequencies, int maxLength);

    // Декодирование одного символа
    public int DecodeSymbol(ref BitReader reader);

    // Fast-path: lookup table для коротких кодов (≤8 бит)
    // Для более длинных - дерево
}
```

### HuffmanDecoder.cs

```csharp
/// <summary>
/// Высокооптимизированный декодер Хаффмана.
/// </summary>
/// <remarks>
/// Оптимизации:
/// - 8-bit lookup table для коротких кодов (90%+ случаев)
/// - Branchless декодирование
/// - SIMD пакетное декодирование (где возможно)
/// </remarks>
public readonly ref struct HuffmanDecoder
{
    // 8-bit lookup: [8-bit peek] -> (symbol << 8) | length
    // Если length == 0 → нужен slow path
    private readonly ushort[] fastTable;

    // Slow path для длинных кодов
    private readonly HuffmanTree tree;

    public int Decode(ref BitReader reader);

    // Пакетное декодирование (для DEFLATE literals)
    public int DecodeBatch(ref BitReader reader, Span<byte> output);
}
```

### Зависимости
- BitReader/BitWriter
- Используется: DEFLATE, VP8L, JPEG

### Тесты
- Построение дерева из code lengths
- Canonical Huffman корректность
- Декодирование референсных данных
- Производительность (пакетное vs поштучное)

---

## Модуль 3: LZ77 (Приоритет: КРИТИЧЕСКИЙ)

Нужен для: DEFLATE (PNG), VP8L.

### Файлы

```
Compression/Lz77/
├── Lz77Decoder.cs           # Декодирование back-references
├── Lz77Encoder.cs           # Поиск совпадений (hash chain)
├── Lz77Match.cs             # Структура match (distance, length)
├── HashChain.cs             # Hash chain для быстрого поиска
└── Lz77Constants.cs         # Константы (min/max length/distance)
```

### Lz77Decoder.cs

```csharp
/// <summary>
/// Декодер LZ77 back-references.
/// </summary>
public static class Lz77Decoder
{
    /// <summary>
    /// Копирует данные с учётом overlapping (length > distance).
    /// </summary>
    /// <remarks>
    /// КРИТИЧНО: при length > distance нужно побайтовое копирование,
    /// т.к. источник и назначение перекрываются!
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CopyMatch(Span<byte> output, int position, int distance, int length)
    {
        var src = position - distance;

        if (distance >= length)
        {
            // Быстрый путь: нет перекрытия
            output.Slice(src, length).CopyTo(output.Slice(position, length));
        }
        else
        {
            // Медленный путь: перекрытие, побайтово
            for (var i = 0; i < length; i++)
                output[position + i] = output[src + i];
        }
    }
}
```

### Lz77Encoder.cs

```csharp
/// <summary>
/// LZ77 encoder с hash chain.
/// </summary>
public sealed class Lz77Encoder
{
    // Hash chain для поиска совпадений
    private readonly int[] head;      // head[hash] → position
    private readonly int[] prev;      // prev[position] → previous with same hash

    // Параметры
    private readonly int minMatch;    // DEFLATE: 3, VP8L: 2
    private readonly int maxMatch;    // DEFLATE: 258, VP8L: 4096
    private readonly int maxDistance; // DEFLATE: 32768, VP8L: зависит от image size
    private readonly int chainLimit;  // максимум проверок в цепочке

    // Поиск лучшего match
    public Lz77Match FindMatch(ReadOnlySpan<byte> data, int position, int maxLength);

    // Greedy vs Lazy matching
    public Lz77Match FindMatchLazy(ReadOnlySpan<byte> data, int position);
}
```

### Зависимости
- Нет внешних зависимостей
- Используется: DEFLATE, VP8L

### Тесты
- Overlapping copy корректность
- Hash chain collision handling
- Match finding accuracy
- Производительность поиска

---

## Модуль 4: DEFLATE (Приоритет: ВЫСОКИЙ)

PNG использует DEFLATE. Сейчас через System.IO.Compression, но для полного контроля нужна своя реализация.

### Файлы

```
Compression/Deflate/
├── DeflateDecoder.cs        # RFC 1951 декодер
├── DeflateEncoder.cs        # RFC 1951 энкодер
├── DeflateBlock.cs          # Типы блоков (stored, fixed, dynamic)
├── DeflateTables.cs         # Статические таблицы Хаффмана
└── DeflateVectors.cs        # SIMD оптимизации
```

### DeflateDecoder.cs

```csharp
/// <summary>
/// DEFLATE декодер (RFC 1951).
/// </summary>
/// <remarks>
/// Типы блоков:
/// - 00: Stored (несжатый)
/// - 01: Fixed Huffman (статические таблицы)
/// - 10: Dynamic Huffman (таблицы в потоке)
/// </remarks>
public ref struct DeflateDecoder
{
    private BitReader bits;
    private HuffmanDecoder literalDecoder;
    private HuffmanDecoder distanceDecoder;

    // Декодирование в output
    public int Decode(ReadOnlySpan<byte> input, Span<byte> output);

    // Декодирование одного блока
    private int DecodeBlock(Span<byte> output, ref int outPos);

    // Чтение dynamic Huffman tables
    private void ReadDynamicTables();
}
```

### Зависимости
- BitReader
- HuffmanDecoder
- Lz77Decoder (для back-references)
- Используется: PNG

### Тесты
- Stored blocks
- Fixed Huffman
- Dynamic Huffman
- Сравнение с System.IO.Compression

---

## Модуль 5: VP8L Codec (Приоритет: ВЫСОКИЙ)

WebP Lossless формат.

### Файлы

```
Codecs/Webp/Vp8L/
├── Vp8LDecoder.cs           # Основной декодер
├── Vp8LEncoder.cs           # Основной энкодер
├── Vp8LBitReader.cs         # LSB-first bit reader
├── Vp8LHuffman.cs           # VP8L Huffman (meta-codes)
├── Vp8LTransforms.cs        # Color transforms
├── Vp8LPredictors.cs        # Spatial predictors (14 типов)
├── Vp8LColorCache.cs        # Color cache (hash table)
└── Vp8LConstants.cs         # Константы
```

### Vp8LDecoder.cs

```csharp
/// <summary>
/// VP8L (WebP Lossless) декодер.
/// </summary>
/// <remarks>
/// Алгоритм:
/// 1. Читаем заголовок (width, height, alpha hint)
/// 2. Читаем transforms (SubtractGreen, Predictor, CrossColor, ColorIndexing)
/// 3. Строим Huffman trees для каждого meta-code
/// 4. Декодируем pixels: literal ARGB или back-reference
/// 5. Применяем inverse transforms в обратном порядке
/// </remarks>
public ref struct Vp8LDecoder
{
    // Transforms (до 4 штук)
    private readonly Vp8LTransform[] transforms;
    private int transformCount;

    // Huffman groups (до 256 meta-codes × 5 trees)
    // 5 trees: green/length, red, blue, alpha, distance
    private readonly HuffmanTree[,] huffmanTrees;

    // Color cache
    private readonly Vp8LColorCache colorCache;

    public CodecResult Decode(ReadOnlySpan<byte> input, Span<Rgba32> output);
}
```

### Vp8LTransforms.cs

```csharp
/// <summary>
/// VP8L color transforms.
/// </summary>
public static class Vp8LTransforms
{
    // SubtractGreen: G хранится отдельно, R и B = R-G, B-G
    public static void InverseSubtractGreen(Span<Rgba32> pixels);

    // Predictor: 14 типов предсказания (Left, Top, TopLeft, Average, Paeth, etc.)
    public static void InversePredictor(Span<Rgba32> pixels, ReadOnlySpan<byte> modes, int width);

    // CrossColor: предсказание одного канала из другого
    public static void InverseCrossColor(Span<Rgba32> pixels, ReadOnlySpan<byte> multipliers, int width);

    // ColorIndexing: palette-based
    public static void InverseColorIndexing(ReadOnlySpan<Rgba32> palette, Span<byte> indices, Span<Rgba32> output);
}
```

### Vp8LPredictors.cs

```csharp
/// <summary>
/// 14 предикторов VP8L.
/// </summary>
public static class Vp8LPredictors
{
    // 0: ARGB_BLACK (0xff000000)
    // 1: Left pixel
    // 2: Top pixel
    // 3: TopRight pixel
    // 4: TopLeft pixel
    // 5: Average(Left, TopRight)
    // 6: Average(Left, TopLeft)
    // 7: Average(Left, Top)
    // 8: Average(TopLeft, Top)
    // 9: Average(Top, TopRight)
    // 10: Average(Average(Left, TopLeft), Average(Top, TopRight))
    // 11: Select (gradient detection)
    // 12: ClampAddSubtractFull
    // 13: ClampAddSubtractHalf

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 Predict(int mode, Rgba32 left, Rgba32 top, Rgba32 topLeft, Rgba32 topRight);
}
```

### Зависимости
- BitReader (LSB-first mode)
- HuffmanDecoder
- Lz77Decoder
- Используется: WebpCodec

### Тесты
- Каждый predictor отдельно
- Каждый transform отдельно
- Color cache
- Full decode/encode round-trip

---

## Модуль 6: VP8 Codec (Приоритет: СРЕДНИЙ)

WebP Lossy формат (базируется на VP8 video intra-frame).

### Файлы

```
Codecs/Webp/Vp8/
├── Vp8Decoder.cs            # Основной декодер
├── Vp8Encoder.cs            # Основной энкодер
├── Vp8BoolDecoder.cs        # Boolean arithmetic decoder
├── Vp8BoolEncoder.cs        # Boolean arithmetic encoder
├── Vp8Dct.cs                # 4x4 DCT/IDCT, WHT
├── Vp8Prediction.cs         # Intra prediction modes
├── Vp8Quantization.cs       # Quantization/Dequantization
├── Vp8LoopFilter.cs         # Deblocking filter
├── Vp8Macroblock.cs         # 16x16 macroblock structure
└── Vp8Constants.cs          # Константы, probability tables
```

### Vp8BoolDecoder.cs

```csharp
/// <summary>
/// VP8 Boolean Arithmetic Decoder (range coder).
/// </summary>
/// <remarks>
/// VP8 использует 8-bit range coding с вероятностями 1-255.
/// </remarks>
public ref struct Vp8BoolDecoder
{
    private uint range;   // текущий диапазон (256 изначально)
    private uint value;   // текущее значение
    private int bits;     // оставшиеся биты
    private int pos;      // позиция в данных

    public Vp8BoolDecoder(ReadOnlySpan<byte> data);

    // Декодирование бита с вероятностью prob/256
    public bool DecodeBit(int prob);

    // Декодирование литерала (n бит)
    public int DecodeLiteral(int n);

    // Декодирование signed value
    public int DecodeSigned(int n);
}
```

### Vp8Dct.cs

```csharp
/// <summary>
/// VP8 DCT/IDCT transformations.
/// </summary>
public static class Vp8Dct
{
    // 4x4 DCT (integer approximation)
    public static void Dct4x4(ReadOnlySpan<short> input, Span<short> output);

    // 4x4 IDCT
    public static void Idct4x4(ReadOnlySpan<short> input, Span<short> output);

    // 4x4 Walsh-Hadamard Transform (для DC коэффициентов)
    public static void Wht4x4(ReadOnlySpan<short> input, Span<short> output);

    // SIMD версии
    public static void Idct4x4Sse41(ReadOnlySpan<short> input, Span<short> output);
    public static void Idct4x4Avx2(ReadOnlySpan<short> input, Span<short> output);
}
```

### Vp8Prediction.cs

```csharp
/// <summary>
/// VP8 Intra Prediction Modes.
/// </summary>
public static class Vp8Prediction
{
    // 16x16 modes (4 типа)
    public static void Predict16x16DC(ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, Span<byte> output);
    public static void Predict16x16V(ReadOnlySpan<byte> above, Span<byte> output);
    public static void Predict16x16H(ReadOnlySpan<byte> left, Span<byte> output);
    public static void Predict16x16TM(ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, byte topLeft, Span<byte> output);

    // 8x8 modes для Chroma (4 типа)
    // 4x4 modes (10 типов): B_DC_PRED, B_TM_PRED, B_VE_PRED, B_HE_PRED,
    //                       B_RD_PRED, B_VR_PRED, B_LD_PRED, B_VL_PRED,
    //                       B_HD_PRED, B_HU_PRED
}
```

### Зависимости
- Vp8BoolDecoder/Encoder
- Vp8Dct
- Используется: WebpCodec (lossy mode)

### Тесты
- Boolean decoder/encoder round-trip
- DCT/IDCT accuracy (compare with reference)
- Prediction modes
- Full decode/encode

---

## Модуль 7: H.264/AVC Baseline (Приоритет: СРЕДНИЙ)

Для MP4 контейнера.

### Файлы

```
Codecs/H264/
├── H264Decoder.cs           # Основной декодер
├── H264Encoder.cs           # Основной энкодер
├── H264Cavlc.cs             # CAVLC entropy coding
├── H264Cabac.cs             # CABAC entropy coding (High Profile)
├── H264Dct.cs               # 4x4/8x8 DCT
├── H264Prediction.cs        # Intra/Inter prediction
├── H264Deblock.cs           # Deblocking filter
├── H264MotionVector.cs      # Motion estimation/compensation
├── H264Nal.cs               # NAL unit parsing
├── H264Sps.cs               # Sequence Parameter Set
├── H264Pps.cs               # Picture Parameter Set
├── H264Slice.cs             # Slice header/data
├── H264Macroblock.cs        # Macroblock structure
├── H264ExpGolomb.cs         # Exp-Golomb coding
└── H264Constants.cs         # Константы
```

### H264Nal.cs

```csharp
/// <summary>
/// NAL Unit parser.
/// </summary>
/// <remarks>
/// NAL types:
/// - 1: Non-IDR slice
/// - 5: IDR slice
/// - 6: SEI
/// - 7: SPS
/// - 8: PPS
/// </remarks>
public static class H264Nal
{
    // Поиск start codes (0x00 0x00 0x01 или 0x00 0x00 0x00 0x01)
    public static int FindStartCode(ReadOnlySpan<byte> data);

    // Парсинг NAL header
    public static NalHeader ParseHeader(byte firstByte);

    // Emulation prevention (0x03 byte removal)
    public static int RemoveEmulationPrevention(ReadOnlySpan<byte> input, Span<byte> output);
}
```

### H264Cavlc.cs

```csharp
/// <summary>
/// CAVLC (Context-Adaptive Variable-Length Coding).
/// </summary>
/// <remarks>
/// Используется в H.264 Baseline/Main Profile для residual data.
/// </remarks>
public ref struct H264Cavlc
{
    // Декодирование одного 4x4 блока
    public void DecodeResidualBlock(ref BitReader reader, Span<short> coeffs, int nC);

    // Кодирование одного 4x4 блока
    public void EncodeResidualBlock(ref BitWriter writer, ReadOnlySpan<short> coeffs);

    // VLC tables
    private static readonly byte[][] CoeffTokenTable;
    private static readonly byte[][] TotalZerosTable;
    private static readonly byte[][] RunBeforeTable;
}
```

### Зависимости
- BitReader/BitWriter
- ExpGolomb
- DCT
- Используется: Mp4Codec

### Тесты
- NAL parsing
- Exp-Golomb encoding/decoding
- CAVLC round-trip
- Intra prediction modes
- Full I-frame decode/encode

---

## Модуль 8: VP9 Codec (Приоритет: НИЗКИЙ)

Для WebM контейнера. Сложнее VP8.

### Файлы

```
Codecs/Vp9/
├── Vp9Decoder.cs
├── Vp9Encoder.cs
├── Vp9BoolDecoder.cs        # Range coder
├── Vp9Transform.cs          # 4x4 to 32x32 transforms
├── Vp9Prediction.cs         # Intra/Inter prediction
├── Vp9Superblock.cs         # 64x64 superblock
├── Vp9Tile.cs               # Tile-based decoding
├── Vp9LoopFilter.cs
└── Vp9Constants.cs
```

### Особенности VP9
- Superblocks 64x64 (vs macroblocks 16x16 в VP8)
- Transforms: 4x4, 8x8, 16x16, 32x32
- 10 intra modes (vs 4+10 в VP8)
- Tile-based параллельное декодирование
- Reference frame management (3 refs + golden)

### Зависимости
- Range coder (похож на VP8)
- Большие transforms
- Используется: WebmCodec

---

## Модуль 9: Container Parsers (Приоритет: СРЕДНИЙ)

Правильный парсинг контейнерных форматов.

### Файлы

```
Containers/
├── IsoBaseMedia/            # MP4/MOV/M4A (ISO 14496-12)
│   ├── BoxParser.cs
│   ├── BoxTypes.cs
│   ├── FtypBox.cs
│   ├── MoovBox.cs
│   ├── MdatBox.cs
│   ├── TrakBox.cs
│   └── StblBox.cs
│
├── Matroska/                # WebM/MKV (EBML)
│   ├── EbmlReader.cs
│   ├── EbmlElement.cs
│   ├── MatroskaIds.cs
│   ├── Segment.cs
│   ├── Cluster.cs
│   └── SimpleBlock.cs
│
└── Riff/                    # WebP/AVI/WAV
    ├── RiffReader.cs
    ├── RiffChunk.cs
    └── RiffConstants.cs
```

---

## Приоритизация реализации

### Фаза 1: Фундамент (1-2 недели)
1. **Bitstream** — BitReader, BitWriter
2. **Huffman** — HuffmanTree, HuffmanDecoder
3. **LZ77** — Lz77Decoder (только декодирование)

### Фаза 2: VP8L (2-3 недели)
4. **VP8L Decoder** — полный декодер WebP Lossless
5. **VP8L Encoder** — базовый энкодер (без оптимизаций)
6. **WebP Integration** — интеграция с WebpCodec

### Фаза 3: VP8 Lossy (2-3 недели)
7. **VP8 Boolean Decoder**
8. **VP8 DCT/IDCT**
9. **VP8 Intra Prediction**
10. **VP8 Loop Filter**
11. **VP8 Full Decoder**

### Фаза 4: H.264 Baseline (3-4 недели)
12. **NAL Parser**
13. **Exp-Golomb**
14. **CAVLC**
15. **H.264 DCT**
16. **H.264 Intra Prediction**
17. **H.264 Deblocking**
18. **H.264 Full Decoder**

### Фаза 5: Containers (1 неделя)
19. **ISO Base Media** — MP4 box parsing
20. **EBML/Matroska** — WebM parsing

### Фаза 6: Encoders + Оптимизация (2-3 недели)
21. VP8 Encoder
22. H.264 Encoder (I-frames only изначально)
23. SIMD оптимизации (AVX2/SSE4.1)

### Фаза 7: VP9 (Опционально, 3-4 недели)
24. VP9 Decoder
25. VP9 Encoder

---

## Метрики успеха

| Кодек | Decode | Encode | Совместимость |
|-------|--------|--------|---------------|
| PNG | ✅ Полный | ✅ Полный | Все приложения |
| WebP Lossless | ✅ Полный | ✅ Полный | Chrome, GIMP, ImageMagick |
| WebP Lossy | ✅ Полный | ⚠️ Базовый | Chrome, GIMP |
| MP4 H.264 | ✅ I-frames | ⚠️ I-frames | VLC, ffmpeg, браузеры |
| WebM VP9 | ⚠️ Базовый | ❌ | Chrome, Firefox |

---

## Referece Materials

### Спецификации
- **PNG**: ISO/IEC 15948, RFC 2083
- **DEFLATE**: RFC 1951
- **WebP**: https://developers.google.com/speed/webp/docs/riff_container
- **VP8L**: https://developers.google.com/speed/webp/docs/webp_lossless_bitstream_specification
- **VP8**: RFC 6386
- **H.264**: ITU-T H.264 (free access via ITU)
- **VP9**: https://www.webmproject.org/vp9/

### Reference Implementations
- **libwebp**: BSD license, reference VP8/VP8L
- **x264**: GPL, high-quality H.264 encoder
- **libaom/libvpx**: BSD, VP8/VP9/AV1

---

## Следующий шаг

Начать с **Модуля 1: Bitstream** (BitReader/BitWriter), так как это фундамент для всех остальных модулей.
