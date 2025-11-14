# IO

Инфраструктура ввода-вывода для приложений Atom: консольные команды,
расширяемый стрим с поддержкой NativeAOT, модели разбора аргументов и заготовки
для реализации собственных систем сжатия.

## Консольные команды

### Базовый сценарий

```csharp
using Atom.IO;

public sealed class EchoCommand : ConsoleCommand
{
    public override string Name => "echo";

    protected override void Execute(ConsoleCommandParseResult result)
    {
        // Аргументы доступны в result.Arguments, опции — в Flags
        Console.WriteLine(string.Join(' ', result.Arguments));
    }
}

var cmd = new EchoCommand();
cmd.Execute("echo", new[] { "hello", "world" });
```

### Реакция на события парсинга

```csharp
var command = new EchoCommand();
command.ParseCommand += (_, args) => Console.WriteLine($"Raw: {args.CommandLine}");
command.ExecuteCommand += (_, args) => Console.WriteLine($"Exit code: {args.ExitCode}");

await command.RunAsync("echo hello", CancellationToken.None);
```

`ConsoleCommandParseResult` содержит:

- `Flags` — коллекцию именованных опций (`--verbose`, `-o` и т. д.).
- `Arguments` — позиционные параметры.
- `Errors` — сообщения валидации.

## Потоки (`Atom.IO.Stream`)

Базовый абстрактный класс, упрощающий реализацию собственного `System.IO.Stream`:

- Сворачивает переопределения к минимуму (`Read(Span<byte>)`, `Write(ReadOnlySpan<byte>)`,
  асинхронные варианты и `Flush`).
- Автоматически реализует все комбинированные методы (`CopyTo`, `ReadAsync`
  поверх `Memory<byte>` и т. д.).
- Предназначен для NativeAOT — отсутствуют виртуальные вызовы, которые нельзя
  слинковать.

### Пример собственной реализации

```csharp
public sealed class InMemoryStream : Atom.IO.Stream
{
    private readonly MemoryStream inner = new();

    public override bool CanRead => true;
    public override bool CanWrite => true;

    public override int Read(Span<byte> buffer) => inner.Read(buffer);
    public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token)
        => new(inner.Read(buffer.Span));
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token)
    {
        inner.Write(buffer.Span);
        return ValueTask.CompletedTask;
    }
}
```

## Консольные утилиты

- `ConsoleCommandParseResult` — подробно описывает результат парсинга.
- `ExecuteCommandEventArgs` / `ParseCommandEventArgs` — аргументы событий для
  обработки жизненного цикла команды.
- `InputEventArgs` — модель данных при взаимодействии с пользователем.

## Практические советы

- Наследуйте `Stream` для интеграции с NativeAOT: он уже отключает неиспользуемые
  функции `Seek`, `Position`, `Timeout` и т. д.
- Для сложных консольных приложений используйте события `ParseCommand`/`ExecuteCommand`
  — так вы сможете собирать телеметрию и обрабатывать ошибки.
- Реализуйте методы `CopyTo` и `CopyToAsync`, если нужна оптимизация под ваш
  сценарий (по умолчанию используется версия из базового класса).
