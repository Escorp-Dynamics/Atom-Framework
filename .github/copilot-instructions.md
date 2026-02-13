# Рекомендации для Copilot по кодовой базе Atom

## Архитектура

.NET-фреймворк "Atom" — модульная библиотека с акцентом на zero-allocation, NativeAOT и thread-safety.

| Директория | Назначение |
|------------|------------|
| `Framework/Atom` | Ядро: пулинг (`ObjectPool<T>`), threading (`Locker`, `RateLimiter`), `ValueStringBuilder` |
| `Framework/Atom.Debug` | Логгирование: `ILogger`, `ConsoleLogger`, `FileLoggerProvider` |
| `Framework/Atom.Media` | FFmpeg-обёртки для видео/аудио |
| `Framework/Atom.Net` | Сетевые протоколы: TCP, UDP, QUIC, TLS, HTTPS |
| `Framework/Atom.Net.Browsing` | Браузерная автоматизация: `IWebBrowser`, `IWebPage`, профили |
| `Framework/Atom.SourceGeneration` | Roslyn incremental generators и анализаторы |
| `Framework/Atom.IO.Compression` | Managed Zstandard (RFC 8878) |
| `Framework/Atom.Web` | Email-рассылки, аналитика |
| `Tests/` | Тесты: `Atom.Tests` ↔ `Framework/Atom` |

Общие настройки: [Framework/Directory.Build.props](Framework/Directory.Build.props) — единый источник `TargetFramework`, анализаторов, `TreatWarningsAsErrors`.

## Рабочие процессы

### Сборка и тесты

**Приоритет инструментов IDE:**
- Сборка: задачи VS Code `build (debug)` / `build (release)`, НЕ терминал
- Тесты: `runTests` tool, НЕ `dotnet test`
- Диагностика: `get_errors` tool

**Критично:** Все warnings = errors. Исправить ВСЕ диагностики перед сборкой.

**Эффективность тестов:** Один запуск → анализ → batch-правки → повторный запуск. НЕ перезапускать после каждой мелкой правки.

**Debug vs Release:**
- Debug: `ProjectReference` на локальные проекты, используется для разработки
- Release: `PackageReference` на NuGet, контрольная сборка в конце задачи

### Тестовый шаблон (Atom.Testing)

```csharp
namespace Atom.Threading.Tests;

public class LockerTests(ILogger logger) : BenchmarkTests<LockerTests>(logger)
{
    public LockerTests() : this(ConsoleLogger.Unicode) { }

    #region Basic Functionality Tests
    [TestCase(TestName = "Locker: создание с начальным значением"), Benchmark]
    public void LockerCreatedWithCorrectInitialCount() { /* ... */ }
    #endregion

    #region Thread Safety Tests
    #endregion

    #region Edge Cases
    #endregion

    #region Stress Tests
    #endregion
}
```

**Правила:**
- `TestName` на русском языке
- Регионы: Basic Functionality, Thread Safety, Edge Cases, Stress Tests
- `[Benchmark]` для hot-path методов, но НЕ запускать автоматически

## Стиль кода

### Форматирование
- File-scoped namespaces: `namespace Atom.X;`
- `var` везде, где тип очевиден
- Expression-bodied `=>` для однострочников
- UTF-8 с BOM, без trailing whitespace и final newline

### Порядок членов типа
`private` → `protected` → `internal` → `public`, сначала instance, затем static:
1. Константы → 2. Static readonly поля → 3. Поля → 4. Свойства → 5. События → 6. Конструкторы → 7. Деструктор → 8. Static конструктор → 9. Методы → 10. Вложенные типы

### Pragma-подавления
Все `#pragma warning disable` — в первую строку файла единым блоком:
```csharp
#pragma warning disable MA0051, IDE0251, S3776

namespace Atom.Text;
```

### P/Invoke
```csharp
[LibraryImport("libc", EntryPoint = "strerror", SetLastError = true)]
[DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
private static partial nint ErrorUnix(int errorNo);
```
Использовать `LibraryImport` (НЕ `DllImport`), `nint`/`nuint` вместо `IntPtr`.

### Документация
- XML-комментарии обязательны для public/protected
- Inline-комментарии на русском, объясняющие "почему"
- Магические числа всегда комментировать

## Ограничения AOT

- **Избегать:** рефлексия, `dynamic`, не-trimmer-safe паттерны
- **JSON:** только source generators (`JsonSerializerIsReflectionEnabledByDefault=false`)
- **InvariantGlobalization:** глобализация отключена

## Производительность

Приоритет: lock-free, GC-free, zero-allocation.

- `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>` для буферов
- `stackalloc` до 256-512 байт, `ArrayPool<T>.Shared` для больших
- `ValueStringBuilder` вместо `StringBuilder`
- `[MethodImpl(MethodImplOptions.AggressiveInlining)]` для hot-path
- `Interlocked`/`Volatile` вместо `lock` где возможно

## Анализаторы

Все анализаторы настроены на **error** severity (см. [Directory.Build.props](Framework/Directory.Build.props)):

| Анализатор | Фокус |
|------------|-------|
| `Microsoft.CodeAnalysis.NetAnalyzers` | Стандартные .NET правила |
| `Meziantou.Analyzer` | Качество кода, производительность |
| `Roslynator.Analyzers` | Рефакторинг, стиль |
| `SonarAnalyzer.CSharp` | Безопасность, надёжность |
| `IDisposableAnalyzers` | Корректность `IDisposable` |
| `AsyncFixer` | Async/await паттерны |
| `SmartAnalyzers.MultithreadingAnalyzer` | Многопоточность |
| `Microsoft.VisualStudio.Threading.Analyzers` | VS Threading правила |
