# Collections

Высокопроизводительные структуры данных и расширения коллекций, которые
минимизируют выделения памяти и делают работу с разрежёнными данными удобной.

## Что внутри

| Тип | Назначение | Особенности |
|-----|------------|-------------|
| `SparseArray<T>` | Разрежённый массив фиксированной длины | Итерируем только занятые индексы, поддерживает аренду из `ArrayPool` |
| `SparseSpan<T>` | Span-представление разрежённых данных | Извлечение поддиапазонов без копирования |
| `SparseEnumerator<T>` | Структурный перечислитель | Используется внутренними коллекциями без бокса |
| `CollectionsExtensions` | Расширения для стандартных коллекций | `AddRange`, `RemoveWhere`, `GetOrAdd`, и др. |

## Быстрый старт: `SparseArray<T>`

```csharp
using Atom.Collections;

var storage = new SparseArray<int>(capacity: 32);

storage[0] = 42;           // AddOrUpdate(0, 42)
storage.Add(100);          // Добавляет значение на первый свободный индекс

foreach (var value in storage)
{
    Console.WriteLine(value); // Перебираем только реально установленные элементы
}

ReadOnlySpan<int> used = storage.GetIndexes();
Console.WriteLine($"Использовано {used.Length} индексов");

// Возвращаем память в пул
storage.Release(clearArray: true);
```

### Потокобезопасность и повторное использование

- `SparseArray<T>` безопасен для чтения из нескольких потоков, если запись
  синхронизирована вызывающей стороной. Методы `AddOrUpdate`/`AddRange` используют
  `Interlocked`, чтобы корректно обновлять `currentIndex`.
- Чтобы полностью очистить массив, вызовите `Reset()` и затем записывайте новые
  значения. `Release()` возвращает внутренние буферы в `ArrayPool`.

## Пример: обработка событий журнала

```csharp
var events = new SparseArray<AuditEvent>(capacity: 128);

foreach (var audit in incomingEvents)
{
    if (!audit.IsCritical) continue;
    events.Add(audit);
}

ReadOnlySpan<int> indexes = events.GetIndexes();
for (int i = 0; i < indexes.Length; i++)
{
    var evtIndex = indexes[i];
    Process(events[evtIndex]);
}
```

## Расширения коллекций

```csharp
using Atom.Collections;

var list = new List<int> { 1, 2, 3 };
list.AddRange(Enumerable.Range(4, 3));

var dictionary = new Dictionary<string, int>();
dictionary.GetOrAdd("requests", _ => 0);
dictionary["requests"] += 1;

var filtered = list.RemoveWhere(x => x % 2 == 0); // удаляет чётные элементы
```

### Советы

- Используйте `SparseSpan<T>` для работы с установленными значениями как со span —
  это удобно в высокопроизводительных пайплайнах.
- Все расширения коллекций принимают `IEnumerable<T>` и возвращают исходную
  коллекцию, что позволяет строить fluent-цепочки вызовов.
- При завершении работы с крупным `SparseArray<T>` обязательно вызывайте `Release`
  — это возвращает арендованные массивы в пул и позволяет избежать утечек.

