# Atom Framework

Высокопроизводительная библиотека на .NET для построения современных приложений с минимальными аллокациями памяти и поддержкой NativeAOT/Trimming.

## Основные возможности

- **Zero-allocation** API через `Span<T>`, `ReadOnlySpan<T>` и пулинг объектов
- **Thread-safe** примитивы синхронизации и конкурентные коллекции
- **NativeAOT-ready** — все компоненты совместимы с компиляцией AOT
- **Source generators** для автоматизации boilerplate-кода

## Модули

| Модуль | Назначение |
|--------|------------|
| [`Buffers`](Buffers/README.md) | Пулы объектов, `ObjectPool<T>`, атрибут `[Pooled]` |
| [`Collections`](Collections/README.md) | `SparseArray<T>`, расширения коллекций |
| [`Threading`](Threading/README.md) | `Locker`, `RateLimiter`, `Sequencer`, `Signal`, `Wait` |
| [`Text`](Text/README.md) | `ValueStringBuilder`, JSON-контексты, форматирование |
| [`Algorithms`](Algorithms/README.md) | Алгоритмы поиска подстрок (KMP, Boyer-Moore, Rabin-Karp, Z, Aho-Corasick) |
| [`Distribution`](Distribution/README.md) | Определение ОС, работа с терминалом и процессами |
| [`IO`](IO/README.md) | Консольные команды, базовый `Stream` для NativeAOT |
| [`Architect`](Architect/README.md) | Компонентная модель, реактивные свойства, билдеры |

## Быстрый старт

### Пулинг объектов

```csharp
using Atom.Buffers;

// Создание пула
var pool = ObjectPool<StringBuilder>.Create(() => new StringBuilder(1024));

// Аренда и возврат
var sb = pool.Rent();
try
{
    sb.Append("Hello, Atom!");
    Console.WriteLine(sb.ToString());
}
finally
{
    pool.Return(sb, static x => x.Clear());
}
```

### Высокопроизводительный StringBuilder

```csharp
using Atom.Text;

using var builder = new ValueStringBuilder(256);
builder.Append("User: ")
       .Append(42)
       .Append(", Status: ")
       .Append(true);

Console.WriteLine(builder.ToString());
```

### Ограничение частоты запросов

```csharp
using Atom.Threading;

var limiter = new RateLimiter(limit: 10, rate: TimeSpan.FromSeconds(1));

for (int i = 0; i < 50; i++)
{
    await limiter.CallAsync(() => Console.WriteLine($"Request {i}"));
}
```

### Алгоритмы поиска подстрок

```csharp
using Atom.Algorithms.Text;

var algorithm = new BoyerMooreAlgorithm();
var text = "The quick brown fox jumps over the lazy dog";
var pattern = "fox";

bool found = algorithm.Contains(text, pattern, StringComparison.Ordinal);
int count = algorithm.CountOf(text, "the", StringComparison.OrdinalIgnoreCase);
```

### Разрежённый массив

```csharp
using Atom.Collections;

var sparse = new SparseArray<int>(capacity: 100);
sparse[0] = 42;
sparse[50] = 100;

foreach (var value in sparse)
{
    Console.WriteLine(value); // Итерация только по установленным значениям
}

sparse.Release(clearArray: true); // Возврат памяти в пул
```

## Требования

- .NET 8.0+ / .NET 10.0 (рекомендуется)
- C# 12+ (preview features)

## Сборка

```bash
dotnet build Atom.slnx
```

## Тестирование

```bash
dotnet test Atom.slnx
```

## Документация

Подробная документация по каждому модулю находится в соответствующих README.md файлах внутри директорий модулей.

## Лицензия

См. файл LICENSE в корне репозитория
