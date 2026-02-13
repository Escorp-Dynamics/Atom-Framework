# Deflate Module

Высокопроизводительная реализация алгоритма Deflate (RFC 1951).

## Особенности

- **Drop-in замена** `System.IO.Compression.DeflateStream`
- **SIMD-оптимизация** LZ77 matching и Huffman декодирования
- **Zero-allocation** hot path через workspace pooling
- **Переиспользование** универсального Huffman модуля

## Структура

| Файл                  | Описание                               |
| --------------------- | -------------------------------------- |
| `DeflateStream.cs`    | Публичный API (Stream-обёртка)         |
| `DeflateDecoder.cs`   | Декодирование блоков (BTYPE 0/1/2)     |
| `DeflateEncoder.cs`   | LZ77 + Huffman кодирование             |
| `DeflateTables.cs`    | Fixed Huffman таблицы (RFC 1951)       |
| `DeflateWorkspace.cs` | Переиспользуемые буферы                |

## Формат Deflate (RFC 1951)

### Типы блоков

| BTYPE | Описание             |
| ----- | -------------------- |
| 0     | Stored (без сжатия)  |
| 1     | Fixed Huffman codes  |
| 2     | Dynamic Huffman codes|

### Алфавит литералов/длин (286 символов)

| Код     | Значение         |
| ------- | ---------------- |
| 0-255   | Литералы (байты) |
| 256     | End of block     |
| 257-285 | Длины (3-258)    |

### Алфавит дистанций (30 символов)

| Код   | Дистанция                       |
| ----- | ------------------------------- |
| 0-3   | 1-4                             |
| 4-5   | 5-8 (+1 extra bit)              |
| ...   | ...                             |
| 28-29 | 16385-32768 (+13 extra bits)    |

## Использование

```csharp
// Сжатие
using var compressed = new MemoryStream();
using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
{
    deflate.Write(data);
}

// Распаковка
compressed.Position = 0;
using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
var result = new byte[originalSize];
deflate.ReadExactly(result);
```

## Совместимость

- PNG (zlib = deflate + 2-byte header + 4-byte Adler32)
- ZIP (raw deflate)
- HTTP Content-Encoding: deflate
- gzip (deflate + gzip header/trailer)
