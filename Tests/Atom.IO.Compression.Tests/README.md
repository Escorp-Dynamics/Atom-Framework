# Atom.IO.Compression.Tests

Тестовый проект для модуля сжатия Atom.IO.Compression.

## Структура тестов

```text
Zstd/
├── ZstdStreamTests.cs    # Основные тесты потокового API
├── AtomZstdCodec.cs      # Адаптер ZstdStream для кросс-тестов
├── ZstdSharpCodec.cs     # Адаптер ZstdSharp для сравнения
├── ZstdNetCodec.cs       # Адаптер ZstdNet для сравнения
├── ICodec.cs             # Интерфейс кодека
└── DataFactory.cs        # Генератор тестовых данных
```

## Тестовые сценарии

### CompressionStreamTest

Потоковое сжатие и распаковка с различными параметрами:

- **Кодеки**: Atom, ZstdSharp, ZstdNet
- **Уровни сжатия**: 1, 3, 6, 9
- **Размеры IO-чанков**: 1, 257, 4096, 65536 байт
- **Наборы данных**: нули, повторяющийся байт, псевдорандом, текст
- **Размеры данных**: 0, 1, 17, 4KB, 64KB, 128KB, 256KB, 1MB

### ContentIntegrityHashTest

Проверка целостности данных через SHA256:

- Сжатие → распаковка → сравнение хешей

### SkippableBlocksToleranceTest

Устойчивость к skippable-фреймам в потоке:

- Вставка skippable-блоков до и после корректного кадра
- Проверка корректной распаковки

### CrossStreamedRobustnessTest (отключен)

Кросс-проверка между разными реализациями:

- Encoder(A) → Decoder(B) для всех комбинаций кодеков

## Запуск тестов

```bash
# Все тесты
dotnet test Tests/Atom.IO.Compression.Tests

# Только Atom-тесты
dotnet test Tests/Atom.IO.Compression.Tests --filter "Name~Atom"

# С подробным выводом
dotnet test Tests/Atom.IO.Compression.Tests -v n
```

## Бенчмарки

Проект поддерживает бенчмарки через BenchmarkDotNet:

- `AtomZstdStreamTest` — базовый бенчмарк
- `ZstdNetStreamTest` — сравнение с ZstdNet
- `ZstdSharpStreamTest` — сравнение с ZstdSharp

## Зависимости

- **ZstdNet** — для кросс-проверки с нативной реализацией
- **ZstdSharp** — для кросс-проверки с управляемой реализацией
- **Atom.Testing** — базовый фреймворк тестирования Atom
