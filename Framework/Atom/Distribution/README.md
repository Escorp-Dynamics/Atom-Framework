# Distribution

API низкого уровня для работы с операционной системой: определение платформы,
запуск системных процессов, управление пакетными менеджерами и безопасное
использование повышенных привилегий.

## Основные типы

- `Distributive` / `Platform` / `OS` — перечисления, описывающие поддерживаемые
  операционные системы и платформы (Windows, Linux, macOS, Android, iOS, браузер).
- `Terminal` — потокобезопасный фасад над системной консолью. Позволяет запускать
  команды, отслеживать процессы и эскалировать права через `sudo`.
- `ProcessInfo` — обёртка над `System.Diagnostics.Process`, содержащая методы
  чтения вывода, ожидания завершения и отмены исполнения.
- `PackageManager` — модель для описания менеджеров пакетов (apt, yum, brew и др.).

## Быстрый старт: определение окружения

```csharp
using Atom.Distribution;

if (Platform.IsLinux)
{
    Console.WriteLine($"Дистрибутив: {Distributive.Detect()}");
}
```

## Работа с терминалом

```csharp
using Atom.Distribution;

await using var terminal = new Terminal(Distributive.Linux)
{
    RootPassword = Environment.GetEnvironmentVariable("SUDO_PASSWORD"),
};

// Запускаем команду и ждём завершения
var result = await terminal.RunAsAdministratorAndWaitAsync("apt-get update", CancellationToken.None);
Console.WriteLine($"Команда завершилась: {result}");

// Анализируем активные процессы терминала
foreach (var process in terminal.RunningProcesses)
{
    Console.WriteLine($"PID {process.Id}: {process.CommandLine}");
}
```

### Управление процессами

```csharp
await using var terminal = new Terminal(Distributive.Windows);
var processInfo = terminal.Run("dotnet --info");

string stdout = await processInfo.ReadOutputAsync();
string stderr = await processInfo.ReadErrorAsync();
bool completed = await processInfo.WaitForEndingAsync(CancellationToken.None);
```

`ProcessInfo` обеспечивает асинхронный API, возвращающий `ValueTask`, поэтому
его удобно использовать в высоконагруженных сервисах без лишних аллокаций.

## Менеджеры пакетов

```csharp
using Atom.Distribution;

var manager = PackageManager.Resolve(Distributive.Detect());
Console.WriteLine($"Используется пакетный менеджер: {manager.Name}");
Console.WriteLine($"Команда установки: {manager.InstallCommand} package-name");
```

## Практические рекомендации

- `Terminal` реализует `IDisposable`: оборачивайте его в `using`/`await using`,
  чтобы автоматически высвобождать запущенные процессы.
- Для асинхронной работы используйте методы `RunAndWaitAsync` и `RunAsAdministratorAndWaitAsync`.
  Они возвращают `ValueTask<bool>` и позволяют отменять операцию через `CancellationToken`.
- `RootPassword` можно не задавать, если команда не требует привилегий. При
  необходимости `Terminal` сам добавит `sudo` для Linux/Unix систем.
- `RunningProcesses` возвращает снимок на момент вызова — его можно использовать
  для отображения прогресса или принудительного завершения процессов.

