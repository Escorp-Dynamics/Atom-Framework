# Zstd — Внутренняя реализация Zstandard

Эта директория содержит полную управляемую реализацию алгоритма сжатия Zstandard (RFC 8878) без внешних зависимостей.

## Архитектура

### Публичный API

| Файл | Описание |
|------|----------|
| `ZstdStream.cs` | Потоковая обёртка, наследующая `System.IO.Stream`. Основная точка входа для пользователей. |
| `IZstdDictionaryProvider.cs` | Интерфейс для предоставления словарей по DictionaryId. |

### Кодирование (Encoder)

| Файл | Описание |
|------|----------|
| `ZstdEncoder.cs` | Основной энкодер: формирование кадров, блоков (RAW/RLE/Compressed), checksum. |
| `ZstdEncoderSettings.cs` | Настройки энкодера (уровень сжатия, размер окна, словарь и т.д.). |
| `ZstdMatcher.cs` | Поиск совпадений (матчей) с использованием hash-chain алгоритма. |
| `ZstdSeqEncoder.cs` | Кодирование последовательностей (LL/ML/OF) в FSE-битстрим. |
| `ZstdMatchParams.cs` | Параметры матчера для различных уровней сжатия. |
| `ZstdEncoderWorkspace.cs` | Рабочие буферы энкодера (переиспользуемые для снижения аллокаций). |
| `ZstdEncoderWorkspacePool.cs` | Пул рабочих буферов энкодера. |

### Декодирование (Decoder)

| Файл | Описание |
|------|----------|
| `ZstdDecoder.cs` | Основной декодер: разбор кадров, блоков, применение checksum. |
| `ZstdDecoderWorkspace.cs` | Рабочие буферы декодера. |
| `ZstdDecoderWorkspacePool.cs` | Пул рабочих буферов декодера. |

### Энтропийное кодирование

#### FSE (Finite State Entropy)

| Файл | Описание |
|------|----------|
| `FseCompressor.cs` | FSE-компрессор для кодирования символов. |
| `FseDecoder.cs` | FSE-декодер для декодирования символов. |
| `FseSymbolTransform.cs` | Структура трансформации символов FSE. |

#### Huffman

| Файл | Описание |
|------|----------|
| `HuffmanDecoder.cs` | Huffman-декодер для литералов (поддержка FSE-сжатых и direct-весов). |
| `HuffmanDecodeTable.cs` | Таблица декодирования Huffman. |

### Битовые операции

| Файл | Описание |
|------|----------|
| `LittleEndianBitWriter.cs` | Запись бит в формате little-endian (для FSE-кодирования). |
| `LittleEndianReverseBitReader.cs` | Чтение бит в обратном порядке (для FSE-декодирования). |
| `ForwardBitReader.cs` | Прямое чтение бит (для заголовков и таблиц). |

### Хеширование

| Файл | Описание |
|------|----------|
| `XxHash64.cs` | Инкрементальная реализация xxHash64 для Content Checksum. |

### Предопределённые данные

| Файл | Описание |
|------|----------|
| `ZstdLengthsTables.cs` | Предопределённые FSE-таблицы для LL/ML/OF (RFC 8878). |
| `ZstdPredef.cs` | Дополнительные предопределённые константы. |

### Вспомогательные типы

| Файл | Описание |
|------|----------|
| `ZstdSeq.cs` | Структура последовательности (LiteralLength, MatchLength, Offset). |
| `ZstdBlockKind.cs` | Перечисление типов блоков (Raw, Rle, Compressed). |
| `RepKind.cs` | Перечисление типов повторных смещений (Rep1, Rep2, Rep3). |

## Формат Zstandard

### Структура кадра

```text
┌──────────────────┬──────────────────┬─────────────────┬─────────────────┐
│   Magic Number   │  Frame Header    │     Blocks      │    Checksum     │
│   (4 bytes)      │  (2-14 bytes)    │  (variable)     │  (0 or 4 bytes) │
└──────────────────┴──────────────────┴─────────────────┴─────────────────┘
```

### Типы блоков

- **Raw (0)** — несжатые данные
- **RLE (1)** — один повторяющийся байт
- **Compressed (2)** — сжатые данные (литералы + последовательности)

### Сжатый блок

```text
┌──────────────────┬──────────────────────┐
│  Literals Section │  Sequences Section   │
│  (Huffman/Raw/RLE)│  (FSE-encoded)       │
└──────────────────┴──────────────────────┘
```

## Уровни сжатия

| Уровень | WindowLog | HashLog | SearchDepth | TargetLength |
|---------|-----------|---------|-------------|--------------|
| 1       | 19        | 17      | 2           | 24           |
| 2-5     | 19        | 17      | 4           | 32           |
| 6-9     | 23        | 20      | 6           | 48           |
| 10-15   | 24        | 21      | 12          | 64           |
| 16-19   | 25        | 22      | 20          | 96           |
| 20+     | 26        | 23      | 32          | 128          |

## Оптимизации

- **Zero-allocation paths** — использование `Span<T>` и `stackalloc`
- **Pooled workspaces** — переиспользование буферов через пулы
- **SIMD** — векторизация RLE-детекции через `System.Numerics.Vector<T>`
- **Aggressive inlining** — критические пути помечены `[MethodImpl(MethodImplOptions.AggressiveInlining)]`
- **Bloom-filter эвристика** — быстрая проверка сжимаемости блока

## Соответствие спецификации

Реализация соответствует:

- [RFC 8878 — Zstandard Compression](https://datatracker.ietf.org/doc/html/rfc8878)
- [RFC 9659 — Window Size Limits](https://datatracker.ietf.org/doc/html/rfc9659)

### Поддерживаемые возможности

- ✅ RAW/RLE/Compressed блоки
- ✅ Skippable frames
- ✅ Content Checksum (xxHash64)
- ✅ Dictionary support (форматированные и raw)
- ✅ Single-segment mode
- ✅ Frame Content Size
- ✅ Window Descriptor

### Ограничения

- Максимальный размер окна: 128 МБ (`MaxWindowSize`)
- Максимальный размер блока: 128 КБ (`MaxRawBlockSize`)
