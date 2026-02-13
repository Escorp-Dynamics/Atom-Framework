# Atom.Debug

[![NuGet](https://img.shields.io/nuget/v/Escorp.Atom.Debug.svg)](https://www.nuget.org/packages/Escorp.Atom.Debug/)

Высокопроизводительный модуль логирования для .NET, совместимый с `Microsoft.Extensions.Logging`. Предоставляет расширяемую инфраструктуру для записи событий в консоль, файлы и комбинированные журналы с поддержкой асинхронной обработки, областей видимости (scopes) и стилизации вывода.

## Возможности

- 🚀 **Асинхронная обработка** — неблокирующая запись логов через очередь
- 🎨 **Стилизация вывода** — цветовое оформление консольных сообщений
- 📁 **Файловое логирование** — автоматическое создание директорий и файлов
- 🔗 **Комбинированные журналы** — одновременная запись в несколько целей
- 🔄 **Области видимости (Scopes)** — контекстная информация с поддержкой вложенности
- ⚙️ **Fluent API** — удобная настройка через цепочку методов
- ✅ **Совместимость с ILogger** — полная поддержка `Microsoft.Extensions.Logging`

## Установка

```bash
dotnet add package Escorp.Atom.Debug
```

## Быстрый старт

### Консольное логирование

```csharp
using Atom.Debug.Logging;
using Microsoft.Extensions.Logging;

// Создание логгера через фабрику
var logger = Logger.Factory.CreateLogger("MyApp");

// Базовое логирование
logger.LogInformation("Приложение запущено");
logger.LogWarning("Предупреждение: {Message}", "низкий уровень памяти");
logger.LogError(exception, "Произошла ошибка");
```

### Настройка через Fluent API

```csharp
var logger = new ConsoleLogger("MyCategory")
    .WithTime()                                    // Вывод времени
    .WithDate()                                    // Вывод даты
    .WithCategoryName()                            // Вывод названия категории
    .WithStyling()                                 // Цветовое оформление
    .WithLogLevels(LogLevel.Warning, LogLevel.Error); // Только Warning и Error
```

### Области видимости (Scopes)

```csharp
using (logger.BeginScope("[Transaction]"))
{
    logger.LogInformation("Начало транзакции");

    using (logger.BeginScope("[Step 1]"))
    {
        logger.LogDebug("Выполнение шага 1");
    }

    logger.LogInformation("Транзакция завершена");
}
// Вывод: 03:30:11.123 [Transaction] Начало транзакции
// Вывод: 03:30:11.124 [Transaction] [Step 1] Выполнение шага 1
// Вывод: 03:30:11.125 [Transaction] Транзакция завершена
```

### Файловое логирование

```csharp
var fileLogger = new FileLogger("MyCategory", "logs/app.log")
    .WithDate()
    .WithTime();

fileLogger.LogInformation("Событие записано в файл");
```

### Комбинированное логирование

```csharp
// Добавление провайдера файлового логирования
Logger.Factory.AddProvider(new FileLoggerProvider("logs/"));

// Создание комбинированного логгера (консоль + файл)
var combinedLogger = Logger.Factory.CreateLogger("MyApp");
combinedLogger.LogInformation("Сообщение записано в консоль и файл");
```

## Архитектура

```text
Atom.Debug/
├── Logging/
│   ├── Logger.cs                    # Базовый класс логгера
│   ├── LoggerEventArgs.cs           # Аргументы события логирования
│   ├── LoggerFactory.cs             # Фабрика создания логгеров
│   ├── ScopeContext.cs              # Контекст области видимости
│   ├── ScopeContextEventArgs.cs     # Аргументы события контекста
│   ├── Loggers/
│   │   ├── ConsoleLogger.cs         # Консольный логгер
│   │   ├── FileLogger.cs            # Файловый логгер
│   │   └── CombinedLogger.cs        # Комбинированный логгер
│   └── Providers/
│       ├── LoggerProvider.cs        # Базовый провайдер
│       ├── ConsoleLoggerProvider.cs # Провайдер консольных логгеров
│       └── FileLoggerProvider.cs    # Провайдер файловых логгеров
```

## Ключевые типы

### Логгеры

| Тип | Описание |
|-----|----------|
| `Logger` | Абстрактный базовый класс с асинхронной обработкой |
| `Logger<T>` | Generic-версия с автоматическим именем категории |
| `ConsoleLogger` | Вывод в консоль со стилизацией |
| `ConsoleLogger<T>` | Generic-версия консольного логгера |
| `FileLogger` | Запись в файл с автосозданием директорий |
| `FileLogger<T>` | Generic-версия файлового логгера |
| `CombinedLogger` | Запись в несколько целей одновременно |
| `CombinedLogger<T>` | Generic-версия комбинированного логгера |

### Провайдеры

| Тип | Описание |
|-----|----------|
| `LoggerProvider<T>` | Базовый абстрактный провайдер |
| `ConsoleLoggerProvider` | Создаёт консольные логгеры |
| `FileLoggerProvider` | Создаёт файловые логгеры |

### Вспомогательные классы

| Тип | Описание |
|-----|----------|
| `LoggerFactory` | Фабрика для создания логгеров |
| `LoggerEventArgs<T>` | Аргументы события записи в журнал |
| `ScopeContext` | Контекст области видимости |
| `ScopeContextEventArgs` | Аргументы события контекста |

## Настройка логгера

### Методы конфигурации

| Метод | Описание |
|-------|----------|
| `WithLogLevels(params LogLevel[])` | Включает указанные уровни логирования |
| `WithoutLogLevels(params LogLevel[])` | Отключает указанные уровни логирования |
| `WithCategoryName()` / `WithoutCategoryName()` | Вывод названия категории |
| `WithDate()` / `WithoutDate()` | Вывод даты события |
| `WithDate(string format)` | Вывод даты с указанным форматом |
| `WithTime()` / `WithoutTime()` | Вывод времени события |
| `WithTime(string format)` | Вывод времени с указанным форматом |
| `WithEventId()` / `WithoutEventId()` | Вывод идентификатора события |
| `WithStyling()` / `WithoutStyling()` | Цветовое оформление |

### Уровни логирования

Поддерживаются стандартные уровни `Microsoft.Extensions.Logging.LogLevel`:

| Уровень | Цвет (консоль) | Описание |
|---------|----------------|----------|
| `Trace` | Тёмно-серый | Детальная отладочная информация |
| `Debug` | Серый | Отладочная информация |
| `Information` | Белый | Информационные сообщения |
| `Warning` | Жёлтый | Предупреждения |
| `Error` | Тёмно-красный | Ошибки |
| `Critical` | Красный | Критические ошибки |

## События

### Logger.Logged

Событие `Logged` позволяет перехватывать записи в журнал:

```csharp
logger.Logged += (sender, args) =>
{
    // Модификация или отмена записи
    if (args is LoggerEventArgs<string> logArgs)
    {
        if (logArgs.State?.Contains("secret") == true)
        {
            args.Cancel(); // Отмена записи
        }
    }
};
```

## Интеграция с Microsoft.Extensions.Logging

```csharp
// Использование с DI
services.AddSingleton<ILoggerFactory>(Logger.Factory);
services.AddSingleton(typeof(ILogger<>), typeof(ConsoleLogger<>));
```

## Производительность

- Неблокирующая асинхронная запись через очередь
- Пулинг объектов `ScopeContext` через `ObjectPool<T>`
- Пулинг аргументов событий через `MutableEventArgs.Rent<T>()`
- Использование `ValueStringBuilder` для эффективной работы со строками

## Лицензия

MIT License. Copyright © Escorp Dynamics
