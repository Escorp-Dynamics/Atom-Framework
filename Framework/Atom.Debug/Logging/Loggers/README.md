# Loggers

Реализации логгеров для различных целей вывода.

## Классы

### Logger

Абстрактный базовый класс для всех логгеров с асинхронной обработкой.

**Ключевые особенности:**

- Асинхронная очередь обработки сообщений
- Поддержка областей видимости (scopes) через `AsyncLocal<T>`
- Настраиваемые уровни логирования
- Событие `Logged` для перехвата записей

```csharp
public abstract class Logger : ILogger, IDisposable
{
    public event MutableEventHandler<ILogger, MutableEventArgs>? Logged;

    public Logger WithLogLevels(params IEnumerable<LogLevel> logLevels);
    public Logger WithoutLogLevels(params IEnumerable<LogLevel> logLevels);
    public Logger WithCategoryName(bool display);
    public Logger WithDate(bool display, string format);
    public Logger WithTime(bool display, string format);
    public Logger WithEventId(bool display);
    public Logger WithStyling(bool isEnabled);
}
```

### ConsoleLogger

Логгер для вывода в консоль с поддержкой цветового оформления.

```csharp
var logger = new ConsoleLogger("MyCategory")
    .WithTime()
    .WithStyling();

logger.LogInformation("Сообщение");
// Вывод: 03:30:11.123 Сообщение (белым цветом)

logger.LogError("Ошибка");
// Вывод: 03:30:11.124 Ошибка (красным цветом)
```

**Цветовая схема:**

| Уровень | Код цвета | Цвет |
|---------|-----------|------|
| Trace | `dgr` | Тёмно-серый |
| Debug | `gr` | Серый |
| Information | `w` | Белый |
| Warning | `y` | Жёлтый |
| Error | `dr` | Тёмно-красный |
| Critical | `r` | Красный |

### FileLogger

Логгер для записи в файл с автоматическим созданием директорий.

```csharp
var logger = new FileLogger("MyCategory", "logs/app.log")
    .WithDate()
    .WithTime();

logger.LogInformation("Событие");
// Файл: logs/app.log
// Содержимое: 30.11.2025 03:30:11.123 Событие
```

**Особенности:**

- Автоматическое создание директории
- Асинхронная запись (`FileOptions.Asynchronous`)
- Режим добавления (`FileMode.Append`)
- Совместный доступ для чтения (`FileShare.Read`)

### CombinedLogger

Логгер для одновременной записи в несколько целей.

```csharp
var combined = new CombinedLogger("MyCategory");
combined.Add(new ConsoleLogger("MyCategory"));
combined.Add(new FileLogger("MyCategory", "logs/app.log"));

combined.LogInformation("Сообщение");
// Записывается в консоль И в файл
```

**Особенности:**

- Синхронизация областей видимости между всеми логгерами
- Делегирование настроек всем вложенным логгерам
- Потокобезопасное добавление логгеров

## Generic-версии

Все логгеры имеют generic-версии для автоматического определения категории:

```csharp
public class ConsoleLogger<TCategoryName> : ConsoleLogger, ILogger<TCategoryName>
public class FileLogger<TCategoryName> : FileLogger, ILogger<TCategoryName>
public class CombinedLogger<TCategoryName> : CombinedLogger, ILogger<TCategoryName>
```

Пример использования:

```csharp
public class MyService
{
    private readonly ILogger<MyService> _logger;

    public MyService(ILogger<MyService> logger)
    {
        _logger = logger;
    }
}
```
