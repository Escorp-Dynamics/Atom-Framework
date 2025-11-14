# Threading

Конкурентные примитивы и вспомогательные классы для построения асинхронных
пайплайнов, соблюдения лимитов и координации задач.

## Структура модуля

| Тип | Назначение |
|-----|------------|
| `Locker` | Облегчённый взаимно-исключающий блокировщик с событиями освобождения |
| `RateLimiter` | Ограничитель скорости выполнения операций через кредиты и окно времени |
| `ResettableCancellationTokenSource` | Переиспользуемый `CancellationTokenSource` для циклических задач |
| `Sequencer` | Шедулер последовательного выполнения коллбэков в разных режимах |
| `Signal` / `Wait` | Примитивы сигнализации и ожидания с поддержкой таймаутов |
| `LockerReleasedEventArgs`, `SequenceEventArgs`, `SignalEventArgs` | Аргументы событий для детального анализа |

## Locker

```csharp
using Atom.Threading;

var locker = new Locker();
locker.Released += (_, _) => Console.WriteLine("Ресурс освобождён");

await using (await locker.AcquireAsync())
{
    // критическая секция
}
```

Locker выдаёт `AsyncDisposable` ручку и уведомляет подписчиков, когда блокировка
становится свободной.

## RateLimiter

```csharp
var limiter = new RateLimiter(maxCount: 5, window: TimeSpan.FromSeconds(1));

for (int i = 0; i < 20; i++)
{
    await limiter.WaitAsync();
    Console.WriteLine($"Запрос {i} отправлен");
}
```

- `WaitAsync` возвращает `ValueTask`, что позволяет избегать лишних аллокаций.
- Событие `RateLimited` можно использовать для логирования превышений лимита.

## ResettableCancellationTokenSource

```csharp
var cts = new ResettableCancellationTokenSource();

while (true)
{
    await DoWorkAsync(cts.Token);
    cts.Reset(); // переиспользуем источник
}
```

Полезно для служб, которые выполняют периодические операции и не хотят каждый
раз создавать новый `CancellationTokenSource`.

## Sequencer

```csharp
var sequencer = new Sequencer(SequenceMode.LoopWithWaiting);

sequencer.Add(async ct =>
{
    await Task.Delay(500, ct);
    Console.WriteLine("tick");
});

sequencer.Start();
await sequencer.WaitAsync(ct => Task.FromResult(true)); // завершение при первом проходе
```

- Режимы: `Loop`, `LoopWithWaiting`, `Manual`.
- Методы `Pause`, `Resume`, `Remove(handler)` позволяют гибко управлять очередью.

## Signal / Wait

```csharp
var signal = new Signal();

_ = Task.Run(async () =>
{
    await Task.Delay(1000);
    signal.Set();
});

var isSet = await signal.WaitAsync(TimeSpan.FromSeconds(2));
Console.WriteLine($"Сигнал получен: {isSet}");
```

Используется как асинхронный аналог `ManualResetEventSlim`, но без блокирующих
вызовов.

## Практические советы

- Все примитивы используют `ValueTask` — комбинируйте их с `ConfigureAwait(false)`
  в серверном коде.
- `RateLimiter` подходит для API-клиентов: перед выполнением запроса вызывайте
  `WaitAsync` и передавайте `CancellationToken`, чтобы поддерживать отмену.
- В `Sequencer` можно добавлять/удалять обработчики на лету — генератор событий
  подстроится автоматически.
- `ResettableCancellationTokenSource` гарантирует, что возобновлённые операции
  не увидят отменённый токен — всегда вызывайте `Reset()` перед новой итерацией.

