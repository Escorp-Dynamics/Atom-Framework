# План реализации медиа-кодеков Atom

## Текущее состояние

| Кодек | Контейнер | Компрессия | Реальный формат | Совместимость |
|-------|-----------|------------|-----------------|---------------|
| **PNG** | ✅ Полный | ✅ DEFLATE (Atom.IO.Compression) | ✅ Да | ✅ Открывается везде |
| **WebP** | ✅ RIFF/WEBP | ✅ VP8L Lossless | ✅ Да | ✅ Chrome, GIMP, ImageMagick |
| **MP4** | ✅ ISO BMFF | AFRM (raw) | ❌ Store mode | ❌ Только внутренний |
| **WebM** | ✅ EBML/Matroska | AFRM (raw) | ❌ Store mode | ❌ Только внутренний |

### Готовые фундаментальные модули

| Модуль | Расположение | Статус | Тесты |
|--------|-------------|--------|-------|
| **BitReader/BitWriter** | `Atom.IO.BitReader` / `Atom.IO.BitWriter` | ✅ Полный | 35/35 |
| **Huffman (8-bit)** | `Atom.IO.Compression.Huffman` | ✅ Полный | 24/24 |
| **Huffman (16-bit)** | `Atom.IO.Compression.Huffman.HuffmanTable16` | ✅ Полный | 24/24 |
| **DEFLATE Decoder** | `Atom.IO.Compression.Deflate.DeflateDecoder` | ✅ Оптимизирован | 53 pass |
| **DEFLATE Encoder** | `Atom.IO.Compression.Deflate.DeflateEncoder` | ✅ Полный | 53 pass |
| **SIMD Histogram** | `Atom.IO.Compression.Huffman.SimdHistogram` | ✅ AVX2/SSE2 | ✅ |
| **Zstd Decoder** | `Atom.IO.Compression.Zstd` | ✅ Полный | ✅ |

#### Характеристики BitReader/BitWriter

- `ref struct`, zero-allocation, 64-bit буфер
- LSB-first и MSB-first режимы
- API: ReadBits(0-32), PeekBits, SkipBits, EnsureBits, AlignToByte, Seek, Reset
- TryFinishWithPadding (RFC 8878)
- Расположение: `Framework/Atom/IO/BitReader.cs`, `Framework/Atom/IO/BitWriter.cs`

#### Характеристики Huffman модуля

- Pointer-based flat lookup tables (не tree-based)
- `HuffmanTable` (8-bit символы) + `HuffmanTable16` (16-bit символы, packed uint)
- `HuffmanTreeBuilder`: BuildDecodeTable, BuildDecodeTable16, BuildFromFrequencies, BuildEncodeCodes
- `HuffmanDecoder`: Decode, TryDecode, DecodeBatch, DecodeBatchUnrolled — для обоих таблиц
- `HuffmanEncoder`: TryEncode, Encode, EncodeBatch, EncodeBatchUnrolled — byte и ushort
- `MaxAlphabetSize` = 2328 (VP8L color cache max)
- Heap fallback для больших алфавитов (>512 символов)
- LSB-first и MSB-first поддержка
- Расположение: `Framework/Atom.IO.Compression/Huffman/`

#### Характеристики DEFLATE (vs zlib-ng)

- Decoder: Fastest ~1.11x, Optimal ~0.93x, SmallestSize ~1.02x (managed vs native zlib-ng)
- Encoder: Fastest ~1.04x, Optimal ~1.31x, SmallestSize ~0.92x
- Size ratios: ≈1.0x (паритет по степени сжатия)
- Оптимизации: tight literal loop, extraBits guards, overlap copy (dist≥8: 32B unroll, dist=1: fill, dist≥4: uint), phantom-bit cleanup, CopyToStream, unified pinned buffer 256KB

---

## Архитектура модулей

```
Framework/Atom/IO/
├── BitReader.cs              # ✅ ГОТОВ: универсальный bit reader (ref struct, 64-bit buf)
└── BitWriter.cs              # ✅ ГОТОВ: универсальный bit writer (ref struct, 64-bit buf)

Framework/Atom.IO.Compression/
├── Deflate/                  # ✅ ГОТОВ: полный RFC 1951 encoder/decoder
├── Huffman/                  # ✅ ГОТОВ: HuffmanTable (8/16-bit), TreeBuilder, Encoder, Decoder, SimdHistogram
├── Zstd/                     # ✅ ГОТОВ: полный RFC 8878 decoder
└── Properties/

Framework/Atom.Media/
├── Codecs/
│   ├── Png/                  # ✅ ГОТОВ (использует Deflate через System.IO.Compression)
│   ├── Webp/                 # ✅ VP8L lossless реализован (encoder + decoder + pipeline)
│   │   ├── Vp8L/             # ✅ VP8L lossless (9.8 FPS @ 1080p)
│   │   └── Vp8/              # 🔧 НУЖНО: VP8 lossy decoder/encoder
│   ├── Mp4/                  # 🔧 НУЖНО: H.264 реализация
│   └── Webm/                 # 🔧 НУЖНО: VP9 реализация
│
├── Transforms/               # Математические преобразования
│   ├── Dct/                  # 🔧 НУЖНО: DCT/IDCT (VP8, H.264, VP9)
│   └── Hadamard/             # 🔧 НУЖНО: WHT (VP8, H.264)
│
├── Entropy/                  # Энтропийное кодирование
│   ├── BoolCoder/            # 🔧 НУЖНО: VP8/VP9 boolean coder
│   ├── Cavlc/                # 🔧 НУЖНО: CAVLC (H.264 Baseline)
│   └── Cabac/                # 🔧 НУЖНО: CABAC (H.264 High Profile)
│
├── Prediction/               # Предсказание пикселей
│   ├── Intra/                # 🔧 НУЖНО: внутрикадровое (VP8/H.264/VP9)
│   └── Inter/                # 🔧 НУЖНО: межкадровое + motion vectors
│
├── Quantization/             # Квантование
│   ├── QuantTable.cs         # 🔧 НУЖНО: таблицы квантования
│   └── Dequant.cs            # 🔧 НУЖНО: обратное квантование
│
├── Containers/               # ✅ Контейнерные форматы (muxer/demuxer)
├── Frames/                   # ✅ Фреймы (Frame<T>)
├── ColorSpaces/              # ✅ Цветовые пространства
└── Formats/                  # ✅ Форматы пикселей/сэмплов
```

---

## Модуль 1: Bitstream (Статус: ✅ ГОТОВ)

Фундаментальный модуль для всех кодеков. **Реализован в `Atom.IO`.**

### Реализация

| Файл | Статус |
|------|--------|
| `Framework/Atom/IO/BitReader.cs` | ✅ Полный (501 строка) |
| `Framework/Atom/IO/BitWriter.cs` | ✅ Полный (512 строк) |
| `Tests/Atom.Tests/IO/BitStreamTests.cs` | ✅ 35/35 тестов |

### API (реализованный)

```csharp
public ref struct BitReader
{
    // Конструктор с выбором порядка бит
    public BitReader(ReadOnlySpan<byte> data, bool lsbFirst = true);

    // Чтение
    public uint ReadBits(int count);           // 1-32 бит
    public uint PeekBits(int count);           // без продвижения
    public bool ReadBit();                     // 1 бит
    public int ReadSignedBits(int count);      // знаковое
    public void SkipBits(int count);           // пропуск
    public void AlignToByte();                 // выравнивание

    // Bulk
    public byte ReadByte();
    public ushort ReadUInt16();
    public uint ReadUInt32();
    public void ReadBytes(Span<byte> buffer);

    // Состояние
    public int BitPosition { get; }
    public int BytePosition { get; }
    public int RemainingBits { get; }
    public int AvailableBits { get; }
    public bool IsAtEnd { get; }
    public bool IsLsbFirst { get; }

    // Навигация
    public void EnsureBits(int count);
    public void Seek(int bitPosition);
    public void Reset();
}
```

### Зависимости

- Нет внешних зависимостей
- Используется: Huffman, DEFLATE, VP8L, VP8, H.264, Zstd

### Заметки

- `ExpGolomb.cs` — нужен только для H.264. Реализовать в Фазе 4.
- `VlcTable.cs` / `VlcDecoder.cs` — не нужны отдельно, Huffman модуль покрывает VLC.

---

## Модуль 2: Huffman (Приоритет: КРИТИЧЕСКИЙ)

Нужен для: PNG (внутри DEFLATE), VP8L, JPEG.

## Модуль 2: Huffman (Статус: ✅ ГОТОВ)

Полный Huffman-модуль с поддержкой 8-bit и 16-bit алфавитов. **Реализован в `Atom.IO.Compression`.**

### Реализация

| Файл | Статус |
|------|--------|
| `Framework/Atom.IO.Compression/Huffman/HuffmanCode.cs` | ✅ Generic struct (Symbol:ushort, Length:byte, Code:uint) |
| `Framework/Atom.IO.Compression/Huffman/HuffmanTable.cs` | ✅ HuffmanTable (byte), HuffmanTable16 (ushort), Buffer wrappers |
| `Framework/Atom.IO.Compression/Huffman/HuffmanDecoder.cs` | ✅ 8-bit + 16-bit Decode/TryDecode/DecodeBatch/DecodeBatchUnrolled |
| `Framework/Atom.IO.Compression/Huffman/HuffmanEncoder.cs` | ✅ 8-bit + 16-bit TryEncode/Encode/EncodeBatch/EncodeBatchUnrolled |
| `Framework/Atom.IO.Compression/Huffman/HuffmanTreeBuilder.cs` | ✅ BuildFromCodeLengths, BuildFromFrequencies, PackageMerge |
| `Framework/Atom.IO.Compression/Huffman/SimdHistogram.cs` | ✅ AVX2/SSE2 подсчёт частот |
| `Tests/Atom.IO.Compression.Tests/Huffman/HuffmanTests.cs` | ✅ 24/24 тестов |

### Ключевые характеристики

- **Packed format**: `(symbol << 8) | length` в `uint*` — один lookup за операцию
- **MaxAlphabetSize**: 2328 (покрывает VP8L: 256+24+2048)
- **Heap fallback**: для алфавитов >512 символов (VP8L distance codes)
- **Pointer-based tables**: `HuffmanTable16` работает с `uint*` для zero-copy

### Зависимости

- BitReader/BitWriter (Atom.IO)
- Используется: DEFLATE, VP8L, Zstd

---

## Модуль 3: LZ77 (Статус: ⚡ ВСТРОЕН)

LZ77 back-reference декодирование **встроено inline** в DeflateDecoder и будет аналогично встроено в VP8L.

### Заметки

- **Отдельный модуль LZ77 НЕ нужен** — overhead вызовов нивелирует преимущества
- **DeflateDecoder**: overlap copy реализован inline с оптимизацией (8-byte Vector copy для distance ≥ 8)
- **VP8L**: будет аналогичный inline overlap copy в Vp8LDecoder
- **LZ77 Encoder** (hash chain): нужен только для DEFLATE encoder и VP8L encoder. Реализовать при необходимости.

---

## Модуль 4: DEFLATE (Статус: ✅ ГОТОВ)

Полная managed-реализация RFC 1951. **Реализован в `Atom.IO.Compression`.**

### Реализация

| Файл | Статус |
|------|--------|
| `Framework/Atom.IO.Compression/Deflate/DeflateDecoder.cs` | ✅ ~1060 строк, unified buffer 256KB |
| `Framework/Atom.IO.Compression/Deflate/DeflateEncoder.cs` | ✅ Stored + Fixed + Dynamic blocks |
| `Framework/Atom.IO.Compression/Deflate/DeflateLevel.cs` | ✅ Fastest/Fast/Default/Optimal/SmallestSize |
| `Tests/Atom.IO.Compression.Tests/Deflate/DeflateTests.cs` | ✅ 53 тестов (+ 7 benchmark-only) |

### Производительность (vs zlib-ng native)

| Уровень | Декодер | Энкодер |
|---------|---------|---------|
| Fastest | ~1.08-1.13x | ~1.00-1.07x |
| Optimal | ~0.92-0.93x | ~1.28-1.34x |
| SmallestSize | ~1.01-1.03x | ~0.84-0.99x |

### Оптимизации

- 64-bit bit buffer, flat packed Huffman tables
- `[SkipLocalsInit]`, unified pinned buffer, overlap copy inlined
- Min-of-batches benchmarking (10×200 iterations)

---

## Модуль 5: VP8L Codec (Статус: ✅ ЗАВЕРШЁН)

WebP Lossless формат. Полностью реализован и оптимизирован.

### Реализация

| Файл | Строки | Статус |
|------|--------|--------|
| `Codecs/Webp/Vp8L/Vp8LDecoder.cs` | 1215 | ✅ Полный, SIMD-оптимизирован |
| `Codecs/Webp/Vp8L/Vp8LEncoder.cs` | 2178 | ✅ Полный, SIMD-оптимизирован |
| `Codecs/Webp/Vp8L/Vp8LTransforms.cs` | 598 | ✅ Forward + Inverse, Parallel.For, V256 |
| `Codecs/Webp/Vp8L/Vp8LPredictors.cs` | 132 | ✅ 14 предикторов |
| `Codecs/Webp/Vp8L/Vp8LColorCache.cs` | 89 | ✅ InsertBatch оптимизация |
| `Codecs/Webp/Vp8L/Vp8LConstants.cs` | 210 | ✅ Таблицы, константы |
| `Codecs/Webp/Vp8L/Vp8LStreamPipeline.cs` | 174 | ✅ Double-buffer pipeline |
| `Tests/Atom.Media.Tests/Codecs/Vp8LEncoder.Tests.cs` | — | ✅ 37/37 тестов |

### Производительность (1080p, Optimize=true)

| Компонент | Время | Доля |
|-----------|-------|------|
| **Pipeline (параллельный)** | **101.6ms (9.8 FPS)** | — |
| **Encoder TOTAL** | **~72-90ms** | 100% |
| — Selection (Parallel.For) | ~2.0ms | 3% |
| — Apply (Mode 11 SIMD) | ~3.0ms | 4% |
| — CrossColor (Parallel.For + SIMD) | ~1.5ms | 2% |
| — LZ77 (chain=8, V256 match) | ~25ms | 34% |
| — BitstreamWrite (merged writes) | ~38ms | 53% |
| **Decoder TOTAL** | **95.4ms** | 100% |
| — DecodePixels (batch literal + backref) | 78.2ms | 82% |
| — InverseTransforms (SIMD + Parallel.For) | 13.3ms | 14% |
| — ReadTransforms | 2.9ms | 3% |
| — WriteToFrame (SIMD shuffle) | 0.9ms | 1% |

### Оптимизации

**Encoder:**

- Parallel.For predictor selection (8×8 tiles)
- Parallel.For CrossColor transform (8×8 tiles)
- Mode 11 (Select) SIMD — int32 per-channel
- LZ77: chain=8, early exit≥8, V256/V128 match, Sse.Prefetch0
- BitstreamWrite: 4-channel literal merge (totalLen≤32 → 1 call), merged len+extra, dist+extra
- BitWriter: 64-bit ulong buffer + 8-byte unaligned flush

**Decoder:**

- DecodeLiteral4Lsb: 1 EnsureBitsLsb(33) + 3 branchless table lookups
- Batch literal loop: inner do-while без 3-way branch
- Bulk backref copy: MemoryCopy (non-overlapping) + scalar (overlapping)
- InsertBatch для color cache
- InverseCrossColor Parallel.For
- InversePredictor SIMD V256 для modes 0,2,3,4,8,9
- InverseSubtractGreen SIMD V256
- WriteToFrame SIMD ARGB→RGBA shuffle
- Workspace pooling (zero-allocation hot paths)

### Файлы

```
Codecs/Webp/Vp8L/
├── Vp8LDecoder.cs           # ✅ Основной декодер (1215 строк)
├── Vp8LEncoder.cs           # ✅ Основной энкодер (2178 строк)
├── Vp8LTransforms.cs        # ✅ Color transforms (4 типа, forward + inverse)
├── Vp8LPredictors.cs        # ✅ Spatial predictors (14 типов)
├── Vp8LColorCache.cs        # ✅ Color cache (hash table + InsertBatch)
├── Vp8LConstants.cs         # ✅ Константы, таблицы
└── Vp8LStreamPipeline.cs    # ✅ Double-buffer encode/decode pipeline
```

> **Примечание:** `Vp8LBitReader` и `Vp8LHuffman` НЕ нужны — используем готовые:
>
> - `BitReader` из `Atom.IO` (LSB-first режим ✅)
> - `HuffmanDecoder` + `HuffmanTable16` из `Atom.IO.Compression` (16-bit алфавиты ✅)

### Vp8LDecoder.cs — ✅ РЕАЛИЗОВАН

Полный VP8L декодер (1215 строк). Алгоритм:

1. Читает заголовок (width, height, alpha hint, version)
2. Читает transforms (SubtractGreen, Predictor, CrossColor, ColorIndexing)
3. Строит Huffman trees для каждого meta-code (5 деревьев × N групп)
4. Декодирует pixels: literal ARGB (batch) или back-reference (LZ77 bulk copy)
5. Применяет inverse transforms в обратном порядке

Зависимости: BitReader (Atom.IO, LSB-first), HuffmanDecoder + HuffmanTable16 (Atom.IO.Compression)

### Vp8LEncoder.cs — ✅ РЕАЛИЗОВАН

Полный VP8L энкодер (2178 строк). Pipeline:

1. Frame → ARGB буфер (SIMD конвертация)
2. Predictor selection (14 режимов per 8×8 tile, Parallel.For)
3. Forward transforms: Predictor → CrossColor → SubtractGreen
4. LZ77 hash-chain (window=8192, chain=8, V256 match)
5. Huffman кодирование (5 таблиц, merged writes)
6. RIFF/WEBP bitstream output

### Vp8LTransforms.cs — ✅ РЕАЛИЗОВАН

Forward + Inverse transforms (598 строк):

- SubtractGreen: V256 byte-add/sub
- Predictor: 14 режимов, SIMD V256 для modes 0,2,3,4,8,9
- CrossColor: Parallel.For + SIMD coefficient accumulation
- ColorIndexing: palette unpacking с bit-packing

### Vp8LPredictors.cs — ✅ РЕАЛИЗОВАН

14 предикторов VP8L (132 строки). ✅ Реализован.

Modes 0-13: ARGB_BLACK, Left, Top, TopRight, TopLeft, Average(L,TR), Average(L,TL),
Average(L,T), Average(TL,T), Average(T,TR), Average(Avg(L,TL),Avg(T,TR)),
Select (gradient), ClampAddSubtractFull, ClampAddSubtractHalf.

### Зависимости — ✅ все готовы

- **BitReader** (Atom.IO) — LSB-first mode ✅
- **HuffmanDecoder + HuffmanTable/Table16** (Atom.IO.Compression) ✅
- LZ77 back-references — inline bulk copy (MemoryCopy + scalar overlap)
- Используется: WebpCodec ✅

### Тесты — ✅ 37/37

- Full decode/encode round-trip (множество тестовых изображений)
- Pipeline encode+decode
- Различные размеры (1×1, 256×256, 1920×1080)
- RGBA/RGB форматы

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

### Фаза 1: Фундамент — ✅ ЗАВЕРШЕНА

1. ~~**Bitstream** — BitReader, BitWriter~~ ✅ `Atom.IO`
2. ~~**Huffman** — HuffmanTable, HuffmanDecoder, HuffmanEncoder, TreeBuilder~~ ✅ `Atom.IO.Compression`
3. ~~**LZ77** — inline в DeflateDecoder~~ ✅
4. ~~**DEFLATE** — DeflateDecoder, DeflateEncoder~~ ✅ `Atom.IO.Compression`

### Фаза 2: VP8L — ✅ ЗАВЕРШЕНА

1. ~~**VP8L Decoder** — декодер WebP Lossless~~ ✅ 1215 строк, SIMD, 37/37 тестов
2. ~~**VP8L Encoder** — энкодер WebP Lossless~~ ✅ 2178 строк, SIMD, Parallel.For, LZ77
3. ~~**WebP Integration** — интеграция с WebpCodec~~ ✅ Encode/Decode через VP8L
4. ~~**VP8L Transforms** — forward + inverse, 4 типа~~ ✅ 598 строк, V256, Parallel.For
5. ~~**VP8L Pipeline** — double-buffer encode/decode~~ ✅ 174 строки, 9.8 FPS (1080p)

**Результат**: Pipeline 9.8 FPS (1080p), Encoder ~72-90ms, Decoder 95.4ms

### Фаза 3: VP8 Lossy

1. **VP8 Boolean Decoder**
2. **VP8 DCT/IDCT**
3. **VP8 Intra Prediction**
4. **VP8 Loop Filter**
5. **VP8 Full Decoder**

### Фаза 4: H.264 Baseline

1. **NAL Parser** + **Exp-Golomb**
2. **CAVLC**
3. **H.264 DCT** + **Prediction** + **Deblocking**
4. **H.264 Full I-frame Decoder**

### Фаза 5: Containers

1. **RIFF** — WebP container (простой, можно раньше при необходимости)
2. **ISO Base Media** — MP4 box parsing
3. **EBML/Matroska** — WebM parsing

### Фаза 6: Encoders + Оптимизация

1. VP8 Encoder
2. H.264 Encoder (I-frames only)
3. SIMD оптимизации (AVX2/SSE4.1) для transforms/prediction

### Фаза 7: VP9 (Опционально)

1. VP9 Decoder
2. VP9 Encoder

---

## Метрики успеха

| Кодек | Decode | Encode | Совместимость | Статус |
|-------|--------|--------|---------------|--------|
| PNG | ✅ Полный | ✅ Полный | Все приложения | ✅ Готов |
| WebP Lossless | ✅ Полный (95.4ms@1080p) | ✅ Полный (72-90ms@1080p) | Chrome, GIMP, ImageMagick | ✅ Готов |
| WebP Lossy | ❌ Не начат | ❌ Не начат | Chrome, GIMP | Фаза 3 |
| MP4 H.264 | ❌ Не начат | ❌ Не начат | VLC, ffmpeg, браузеры | Фаза 4 |
| WebM VP9 | ❌ Не начат | ❌ Не начат | Chrome, Firefox | Фаза 7 |

---

## Referece Materials

### Спецификации

- **PNG**: ISO/IEC 15948, RFC 2083
- **DEFLATE**: RFC 1951
- **WebP**: <https://developers.google.com/speed/webp/docs/riff_container>
- **VP8L**: <https://developers.google.com/speed/webp/docs/webp_lossless_bitstream_specification>
- **VP8**: RFC 6386
- **H.264**: ITU-T H.264 (free access via ITU)
- **VP9**: <https://www.webmproject.org/vp9/>

### Reference Implementations

- **libwebp**: BSD license, reference VP8/VP8L
- **x264**: GPL, high-quality H.264 encoder
- **libaom/libvpx**: BSD, VP8/VP9/AV1

---

## Следующий шаг

Начать с **Модуля 6: VP8 Lossy** (Фаза 3) — Boolean Arithmetic Coder, DCT/IDCT 4×4, WHT, Intra Prediction, Loop Filter, Quantization.
Файлы создаются в `Framework/Atom.Media/Codecs/Webp/Vp8/`.

Зависимости: BitReader/BitWriter (✅ готов). VP8 BoolCoder — отдельная реализация (range coder с 8-bit вероятностями).

### Альтернативный порядок

Если приоритет — H.264 (MP4 совместимость), можно начать с **Модуля 7: H.264 Baseline** (Фаза 4):

- NAL Parser + Exp-Golomb → `Framework/Atom.Media/Codecs/H264/`
- CAVLC → `Framework/Atom.Media/Codecs/H264/H264Cavlc.cs`
- DCT + Prediction + Deblocking

Или **Модуль 9: Container Parsers** (Фаза 5) для полноценной совместимости MP4/WebM с внешним софтом.
