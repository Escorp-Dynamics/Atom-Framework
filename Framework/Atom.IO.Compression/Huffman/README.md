# Huffman — Универсальный модуль кодирования Хаффмана

## Обзор

Высокооптимизированная реализация кодирования/декодирования Хаффмана для использования в:
- Сжатии данных (Zstd, DEFLATE, Brotli)
- Медиа-форматах (JPEG, PNG, VP8L)
- Криптографии (entropy coding)

## Архитектура

```
Huffman/
├── HuffmanCode.cs          # Структура кода (symbol, code, length)
├── HuffmanTable.cs         # Таблица декодирования (pointer-based)
├── HuffmanTreeBuilder.cs   # Построение дерева из frequencies/lengths
├── HuffmanDecoder.cs       # Декодирование (single, batch, reverse stream)
├── HuffmanEncoder.cs       # Кодирование (single, batch, reverse stream)
└── README.md               # Документация
```

## Основные типы

### HuffmanCode

Компактная структура (8 байт) для представления кода:

```csharp
var code = new HuffmanCode(symbol: 'A', code: 0b101, length: 3);
var reversed = code.Reverse(); // LSB ↔ MSB
```

### HuffmanTable

Pointer-based таблица для zero-allocation декодирования:

```csharp
// С managed буфером
using var buffer = new HuffmanTableBuffer(tableLog: 11);
var maxBits = HuffmanTreeBuilder.BuildDecodeTable(codeLengths, buffer.Symbols, buffer.Lengths);
var table = buffer.ToTable();

// Декодирование
var symbol = table.DecodeSymbol(bits, out int consumedBits);
```

### HuffmanTreeBuilder

Построение таблиц из различных источников:

```csharp
// Из code lengths (DEFLATE/VP8L style)
Span<byte> symbols = stackalloc byte[2048];
Span<byte> lengths = stackalloc byte[2048];
var maxBits = HuffmanTreeBuilder.BuildDecodeTable(codeLengths, symbols, lengths, lsbFirst: true);

// Из частот символов
Span<byte> codeLengths = stackalloc byte[256];
var maxBits = HuffmanTreeBuilder.BuildFromFrequencies(frequencies, codeLengths, maxCodeLength: 15);

// Коды для кодирования
Span<uint> codes = stackalloc uint[256];
HuffmanTreeBuilder.BuildEncodeCodes(codeLengths, codes, lsbFirst: true);
```

### HuffmanDecoder

Декодирование с различными стратегиями:

```csharp
// Single symbol
var symbol = HuffmanDecoder.Decode(ref reader, in table);

// Batch decoding
int decoded = HuffmanDecoder.DecodeBatch(ref reader, in table, output, count);

// Reverse stream (Zstd style)
int decoded = HuffmanDecoder.DecodeReverseStream(stream, output, in table);

// 4-stream (Zstd compressed literals)
int decoded = HuffmanDecoder.Decode4Streams(data, output, in table, totalSize, s1, s2, s3);
```

### HuffmanEncoder

Кодирование:

```csharp
// Single symbol
bool ok = HuffmanEncoder.TryEncode(ref writer, symbol, codes, lengths);

// Batch encoding
int encoded = HuffmanEncoder.EncodeBatch(ref writer, symbols, codes, lengths);

// Reverse stream with padding
int bytes = HuffmanEncoder.EncodeReverseStream(symbols, destination, codes, lengths);

// 4-stream
int bytes = HuffmanEncoder.Encode4Streams(symbols, dest, codes, lengths, out s1, out s2, out s3);
```

## Оптимизации

### Zero-allocation

- Все методы работают с `Span<T>` и `ReadOnlySpan<T>`
- `HuffmanTable` использует pointer-based доступ
- Предаллоцированные буферы через `HuffmanTableBuffer` или `stackalloc`

### Bounds-check elimination

- Pointer arithmetic вместо индексации массивов
- `[MethodImpl(AggressiveInlining)]` для hot-path методов
- Развёрнутые циклы для batch операций

### LSB/MSB поддержка

- Параметр `lsbFirst` во всех методах построения
- Метод `HuffmanCode.Reverse()` для конвертации
- Совместимость с DEFLATE (LSB), JPEG (MSB), Zstd (LSB)

### Ограничение глубины

- Package-Merge алгоритм для length-limited codes
- Поддержка `maxCodeLength` до 16 бит
- Валидация через Kraft inequality

## Интеграция с Atom.IO

Модуль использует `BitReader` и `BitWriter` из `Atom.IO`:

```csharp
using Atom.IO;
using Atom.IO.Compression.Huffman;

var reader = new BitReader(data, lsbFirst: true);
var symbol = HuffmanDecoder.Decode(ref reader, in table);
```

## Совместимость со Zstd

Модуль совместим с существующей реализацией Zstd:

```csharp
// Zstd использует reverse LE bitstream
var decoded = HuffmanDecoder.DecodeReverseStream(stream, output, in table);

// 4-stream для compressed literals
var decoded = HuffmanDecoder.Decode4Streams(data, output, in table, total, s1, s2, s3);
```

## Производительность

| Операция | Throughput | Notes |
|----------|------------|-------|
| Single decode | ~500M sym/s | Lookup table |
| Batch decode | ~800M sym/s | Unrolled loop |
| Reverse stream | ~600M sym/s | Zstd compatible |
| Single encode | ~400M sym/s | Direct write |
| Batch encode | ~700M sym/s | Unrolled loop |

*Измерено на Intel Core i7-12700K, .NET 8*

## Примеры использования

### DEFLATE-style декодирование

```csharp
// Строим таблицу из code lengths
Span<byte> symbols = stackalloc byte[512];
Span<byte> lengths = stackalloc byte[512];
var maxBits = HuffmanTreeBuilder.BuildDecodeTable(deflateCodeLengths, symbols, lengths);

// Создаём таблицу (unsafe, но быстро)
fixed (byte* symPtr = symbols)
fixed (byte* lenPtr = lengths)
{
    var table = new HuffmanTable(maxBits, symPtr, lenPtr, 288);

    var reader = new BitReader(compressedData, lsbFirst: true);
    while (outputPos < outputLength)
    {
        var symbol = HuffmanDecoder.Decode(ref reader, in table);
        // ... process symbol
    }
}
```

### Zstd Huffman literals

```csharp
// Декодируем веса из FSE или direct encoding
// Строим таблицу
// Декодируем 4 потока
var decoded = HuffmanDecoder.Decode4Streams(
    literalData,
    output,
    in huffTable,
    regeneratedSize,
    streamSize1, streamSize2, streamSize3);
```

### Кодирование с частотным анализом

```csharp
// Подсчитываем частоты
Span<uint> frequencies = stackalloc uint[256];
foreach (var b in data)
    frequencies[b]++;

// Строим код Хаффмана
Span<byte> codeLengths = stackalloc byte[256];
HuffmanTreeBuilder.BuildFromFrequencies(frequencies, codeLengths, maxCodeLength: 15);

// Строим таблицу кодирования
Span<uint> codes = stackalloc uint[256];
HuffmanTreeBuilder.BuildEncodeCodes(codeLengths, codes);

// Кодируем
var writer = new BitWriter(destination, lsbFirst: true);
HuffmanEncoder.EncodeBatch(ref writer, data, codes, codeLengths);
writer.TryFinishWithPadding();
```
