# Providers

Провайдеры для создания и управления экземплярами логгеров.

## Классы

### LoggerProvider\<TLogger\>

Абстрактный базовый класс для провайдеров логгеров.

```csharp
public abstract class LoggerProvider<TLogger> : ILoggerProvider
    where TLogger : Logger
{
    protected ConcurrentDictionary<string, TLogger> Loggers { get; }

    public abstract ILogger CreateLogger(string categoryName);
}
```

**Особенности:**

- Кэширование созданных логгеров по имени категории
- Автоматическое освобождение ресурсов при Dispose
- Потокобезопасное создание логгеров

### ConsoleLoggerProvider

Провайдер для создания консольных логгеров.

```csharp
var provider = new ConsoleLoggerProvider();
var logger = provider.CreateLogger("MyCategory");
```

### FileLoggerProvider

Провайдер для создания файловых логгеров.

```csharp
// С директорией по умолчанию (logs/)
var provider = new FileLoggerProvider();

// С указанной директорией
var provider = new FileLoggerProvider("custom/logs/");

// Создание логгера (имя файла генерируется автоматически)
var logger = provider.CreateLogger("MyCategory");
// Файл: custom/logs/MyCategory_30-11-2025_03-30-11.log

// Создание логгера с указанным путём
var logger = provider.CreateLogger("MyCategory", "app.log");
```

## Использование с LoggerFactory

```csharp
using Atom.Debug.Logging;

// Фабрика по умолчанию содержит ConsoleLoggerProvider
var consoleLogger = Logger.Factory.CreateLogger("Console");

// Добавление провайдера файлов
Logger.Factory.AddProvider(new FileLoggerProvider("logs/"));

// Теперь CreateLogger возвращает CombinedLogger
var combinedLogger = Logger.Factory.CreateLogger("Combined");
```

## Интеграция с Microsoft.Extensions.Logging

```csharp
// В Startup.cs или Program.cs
services.AddSingleton<ILoggerFactory>(sp =>
{
    var factory = new LoggerFactory();
    factory.AddProvider(new ConsoleLoggerProvider());
    factory.AddProvider(new FileLoggerProvider("logs/"));
    return factory;
});
```
