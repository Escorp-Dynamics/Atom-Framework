# Atom.IO.Compression

Высокопроизводительная библиотека сжатия данных для .NET, реализующая алгоритм **Zstandard (RFC 8878)** без внешних зависимостей.

## Основные возможности

- **Полностью управляемая реализация** — без нативных библиотек и P/Invoke
- **Потоковый API** — наследует `System.IO.Stream` для интеграции с существующим кодом
- **Поддержка словарей** — форматированные словари (magic 0xEC30A437) и raw-content
- **Контрольная сумма** — xxHash64 (RFC 8878 Content Checksum)
- **Уровни сжатия** — от 0 (без сжатия) до 22 (максимальное сжатие)
- **Асинхронные операции** — полная поддержка `async`/`await`

## Компоненты

### Публичные типы

| Тип | Описание |
|-----|----------|
| `ZstdStream` | Основной потоковый API для сжатия и распаковки |
| `IZstdDictionaryProvider` | Интерфейс провайдера словарей |

### Внутренняя архитектура

Модуль реализует полный стек Zstandard:

- **Энтропийное кодирование**: FSE (Finite State Entropy) и Huffman
- **Хеширование**: xxHash64 для контрольных сумм
- **Поиск совпадений**: Hash-chain матчер с настраиваемой глубиной поиска
- **Блоки**: RAW, RLE и Compressed согласно спецификации

## Использование

### Сжатие данных

```csharp
using System.IO.Compression;
using Atom.IO.Compression;

// Потоковое сжатие
await using var output = File.Create("data.zst");
await using var zstd = new ZstdStream(output, CompressionLevel.Optimal);
await zstd.WriteAsync(data);
```

### Распаковка данных

```csharp
using System.IO.Compression;
using Atom.IO.Compression;

// Потоковая распаковка
await using var input = File.OpenRead("data.zst");
await using var zstd = new ZstdStream(input, CompressionMode.Decompress);
var buffer = new byte[4096];
int bytesRead;
while ((bytesRead = await zstd.ReadAsync(buffer)) > 0)
{
    // Обработка распакованных данных
}
```

### Использование словарей

```csharp
// Создание провайдера словарей
public class MyDictionaryProvider : IZstdDictionaryProvider
{
    private readonly Dictionary<uint, byte[]> _dictionaries = new();

    public bool TryGet(uint dictionaryId, out ReadOnlyMemory<byte> dictionaryBytes)
    {
        if (_dictionaries.TryGetValue(dictionaryId, out var bytes))
        {
            dictionaryBytes = bytes;
            return true;
        }
        dictionaryBytes = default;
        return false;
    }
}

// Использование со словарём
await using var zstd = new ZstdStream(stream, CompressionMode.Decompress)
{
    DictionaryProvider = new MyDictionaryProvider()
};
```

### Расширенные настройки

```csharp
await using var zstd = new ZstdStream(output, compressionLevel: 6, leaveOpen: true)
{
    IsContentChecksumEnabled = true,    // Добавить xxHash64 checksum
    IsSingleSegment = false,            // Потоковый режим
    WindowSize = 8 * 1024 * 1024,       // Размер окна 8 МБ
    DictionaryId = 12345,               // ID словаря
    DictionaryProvider = provider,
    UseDictionaryTablesForSequences = true,
    UseDictionaryHuffmanForLiterals = true,
    UseInterBlockHistory = true,
    FrameContentSize = totalSize        // Опциональный размер контента
};
```

## Структура модуля

```text
Zstd/
├── ZstdStream.cs              # Публичный потоковый API
├── ZstdEncoder.cs             # Внутренний энкодер
├── ZstdDecoder.cs             # Внутренний декодер
├── ZstdMatcher.cs             # Поиск совпадений
├── ZstdSeqEncoder.cs          # Кодирование последовательностей
├── FseCompressor.cs           # FSE кодирование
├── FseDecoder.cs              # FSE декодирование
├── HuffmanDecoder.cs          # Huffman декодирование
├── XxHash64.cs                # Хеширование xxHash64
├── ZstdLengthsTables.cs       # Предопределённые таблицы
├── ZstdEncoderWorkspace.cs    # Пул рабочих буферов энкодера
├── ZstdDecoderWorkspace.cs    # Пул рабочих буферов декодера
└── ...                        # Вспомогательные типы
```

## Совместимость

- Полная совместимость с libzstd и другими реализациями (ZstdSharp, ZstdNet)
- Поддержка skippable-кадров
- Корректная обработка Content Checksum

## Производительность

Реализация оптимизирована для .NET с использованием:

- `Span<T>` и `Memory<T>` для zero-allocation операций
- Пулы буферов (`ZstdEncoderWorkspacePool`, `ZstdDecoderWorkspacePool`)
- SIMD-оптимизации через `System.Numerics.Vector<T>`
- Aggressive inlining критических путей

## Ограничения текущей версии

- Максимальный размер окна: 128 МБ
- Максимальный размер блока: 128 КБ (по RFC 8878)

## Тестирование

Модуль покрыт тестами, включающими:

- Кросс-проверку с референсными реализациями
- Потоковые тесты с различными размерами чанков
- Проверку целостности через SHA256
- Устойчивость к skippable-блокам

## Ссылки

- [RFC 8878 - Zstandard Compression](https://datatracker.ietf.org/doc/html/rfc8878)
- [RFC 9659 - Window Sizes](https://datatracker.ietf.org/doc/html/rfc9659)
- [Zstandard GitHub](https://github.com/facebook/zstd)
