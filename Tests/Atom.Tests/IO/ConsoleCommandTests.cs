using Atom.Debug;

namespace Atom.IO.Tests;

/// <summary>
/// Тесты для ConsoleCommand и связанных классов.
/// </summary>
[TestFixture, Parallelizable(ParallelScope.All)]
public class ConsoleCommandTests(ILogger logger) : BenchmarkTests<ConsoleCommandTests>(logger)
{
    public ConsoleCommandTests() : this(ConsoleLogger.Unicode) { }

    #region Test Command Implementation

    private sealed class TestCommand : ConsoleCommand
    {
        public override string Name { get; protected set; } = "Test Command";
        public bool ExecutionCalled { get; private set; }
        public bool CancellationCalled { get; private set; }
        public IEnumerable<string>? LastArgs { get; private set; }

        public TestCommand(string alias) : base(alias) { }
        public TestCommand(IEnumerable<string> aliases) : base(aliases) { }

        protected override async ValueTask<bool> OnExecutionAsync(ExecuteCommandEventArgs e)
        {
            ExecutionCalled = true;
            LastArgs = e.Args;
            e.IsSuccess = true;
            return await base.OnExecutionAsync(e);
        }

        protected override async ValueTask<bool> OnCancellingAsync(ExecuteCommandEventArgs e)
        {
            CancellationCalled = true;
            LastArgs = e.Args;
            e.IsSuccess = true;
            return await base.OnCancellingAsync(e);
        }
    }

    private sealed class NonCancellableCommand : ConsoleCommand
    {
        public override string Name { get; protected set; } = "Non-Cancellable Command";

        public NonCancellableCommand(string alias) : base(alias) => IsCancellable = false;
    }

    #endregion

    #region Constructor Tests

    [TestCase(TestName = "ConsoleCommand: конструктор с одним псевдонимом")]
    public void ConstructorWithSingleAlias()
    {
        var command = new TestCommand("test");
        Assert.Multiple(() =>
        {
            Assert.That(command.Aliases, Contains.Item("test"));
            Assert.That(command.Aliases.Count(), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "ConsoleCommand: конструктор с несколькими псевдонимами")]
    public void ConstructorWithMultipleAliases()
    {
        var command = new TestCommand(["test", "t", "tst"]);
        Assert.Multiple(() =>
        {
            Assert.That(command.Aliases, Contains.Item("test"));
            Assert.That(command.Aliases, Contains.Item("t"));
            Assert.That(command.Aliases, Contains.Item("tst"));
            Assert.That(command.Aliases.Count(), Is.EqualTo(3));
        });
    }

    [TestCase(TestName = "ConsoleCommand: Name устанавливается")]
    public void NameIsSet()
    {
        var command = new TestCommand("test");
        Assert.That(command.Name, Is.EqualTo("Test Command"));
    }

    [TestCase(TestName = "ConsoleCommand: IsCancellable по умолчанию true")]
    public void IsCancellableDefaultTrue()
    {
        var command = new TestCommand("test");
        Assert.That(command.IsCancellable, Is.True);
    }

    [TestCase(TestName = "ConsoleCommand: IsCancellable можно отключить")]
    public void IsCancellableCanBeDisabled()
    {
        var command = new NonCancellableCommand("test");
        Assert.That(command.IsCancellable, Is.False);
    }

    #endregion

    #region TryParseAsync Tests

    [TestCase(TestName = "TryParseAsync: успешный парсинг простой команды")]
    public async Task TryParseAsyncSimpleCommand()
    {
        var command = new TestCommand("test");
        var result = await command.TryParseAsync("test");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IsCancellation, Is.False);
            Assert.That(result.Args, Is.Empty);
        });
    }

    [TestCase(TestName = "TryParseAsync: команда с аргументами")]
    public async Task TryParseAsyncCommandWithArgs()
    {
        var command = new TestCommand("test");
        var result = await command.TryParseAsync("test arg1 arg2 arg3");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Args.Count, Is.EqualTo(3));
            Assert.That(result.Args[0], Is.EqualTo("arg1"));
            Assert.That(result.Args[1], Is.EqualTo("arg2"));
            Assert.That(result.Args[2], Is.EqualTo("arg3"));
        });
    }

    [TestCase(TestName = "TryParseAsync: команда с кавычками в аргументах")]
    public async Task TryParseAsyncCommandWithQuotedArgs()
    {
        var command = new TestCommand("test");
        // Примечание: парсер удаляет кавычки, но не делает полный shell-парсинг (т.е. разделяет по пробелам до обработки кавычек)
        var result = await command.TryParseAsync("test \"arg1\" 'arg2'");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Args.Count, Is.EqualTo(2));
            Assert.That(result.Args[0], Is.EqualTo("arg1"));
            Assert.That(result.Args[1], Is.EqualTo("arg2"));
        });
    }

    [TestCase(TestName = "TryParseAsync: команда отмены")]
    public async Task TryParseAsyncCancellationCommand()
    {
        var command = new TestCommand("test");
        var result = await command.TryParseAsync("-test");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IsCancellation, Is.True);
        });
    }

    [TestCase(TestName = "TryParseAsync: команда отмены для неотменяемой команды")]
    public async Task TryParseAsyncCancellationNonCancellable()
    {
        var command = new NonCancellableCommand("test");
        var result = await command.TryParseAsync("-test");

        Assert.That(result.IsSuccess, Is.False);
    }

    [TestCase(TestName = "TryParseAsync: неизвестная команда")]
    public async Task TryParseAsyncUnknownCommand()
    {
        var command = new TestCommand("test");
        var result = await command.TryParseAsync("unknown");

        Assert.That(result.IsSuccess, Is.False);
    }

    [TestCase(TestName = "TryParseAsync: пустая строка")]
    public async Task TryParseAsyncEmptyString()
    {
        var command = new TestCommand("test");
        var result = await command.TryParseAsync("");

        Assert.That(result.IsSuccess, Is.False);
    }

    [TestCase(TestName = "TryParseAsync: строка из пробелов")]
    public async Task TryParseAsyncWhitespaceString()
    {
        var command = new TestCommand("test");
        var result = await command.TryParseAsync("   ");

        Assert.That(result.IsSuccess, Is.False);
    }

    [TestCase(TestName = "TryParseAsync: null строка")]
    public async Task TryParseAsyncNullString()
    {
        var command = new TestCommand("test");
        var result = await command.TryParseAsync(null!);

        Assert.That(result.IsSuccess, Is.False);
    }

    [TestCase(TestName = "TryParseAsync: регистронезависимый поиск псевдонима")]
    public async Task TryParseAsyncCaseInsensitive()
    {
        var command = new TestCommand("test");

        var result1 = await command.TryParseAsync("TEST");
        var result2 = await command.TryParseAsync("Test");
        var result3 = await command.TryParseAsync("TeSt");

        Assert.Multiple(() =>
        {
            Assert.That(result1.IsSuccess, Is.True);
            Assert.That(result2.IsSuccess, Is.True);
            Assert.That(result3.IsSuccess, Is.True);
        });
    }

    [TestCase(TestName = "TryParseAsync: множественные псевдонимы")]
    public async Task TryParseAsyncMultipleAliases()
    {
        var command = new TestCommand(["test", "t", "tst"]);

        var result1 = await command.TryParseAsync("test arg1");
        var result2 = await command.TryParseAsync("t arg1");
        var result3 = await command.TryParseAsync("tst arg1");

        Assert.Multiple(() =>
        {
            Assert.That(result1.IsSuccess, Is.True);
            Assert.That(result2.IsSuccess, Is.True);
            Assert.That(result3.IsSuccess, Is.True);
        });
    }

    [TestCase(TestName = "TryParseAsync: с CancellationToken")]
    public async Task TryParseAsyncWithCancellationToken()
    {
        var command = new TestCommand("test");
        using var cts = new CancellationTokenSource();

        var result = await command.TryParseAsync("test", cts.Token);

        Assert.That(result.IsSuccess, Is.True);
    }

    #endregion

    #region ExecuteAsync Tests

    [TestCase(TestName = "ExecuteAsync: базовое выполнение")]
    public async Task ExecuteAsyncBasic()
    {
        var command = new TestCommand("test");
        var result = await command.ExecuteAsync(["arg1", "arg2"]);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(command.ExecutionCalled, Is.True);
            Assert.That(command.LastArgs, Is.Not.Null);
            Assert.That(command.LastArgs!.Count(), Is.EqualTo(2));
        });
    }

    [TestCase(TestName = "ExecuteAsync: без аргументов")]
    public async Task ExecuteAsyncNoArgs()
    {
        var command = new TestCommand("test");
        var result = await command.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(command.ExecutionCalled, Is.True);
        });
    }

    [TestCase(TestName = "ExecuteAsync: с CancellationToken")]
    public async Task ExecuteAsyncWithCancellationToken()
    {
        var command = new TestCommand("test");
        using var cts = new CancellationTokenSource();

        var result = await command.ExecuteAsync(["arg1"], cts.Token);

        Assert.That(result, Is.True);
    }

    [TestCase(TestName = "ExecuteAsync: params синтаксис")]
    public async Task ExecuteAsyncParamsSyntax()
    {
        var command = new TestCommand("test");
        var result = await command.ExecuteAsync("arg1", "arg2", "arg3");

        Assert.That(result, Is.True);
    }

    #endregion

    #region CancelAsync Tests

    [TestCase(TestName = "CancelAsync: базовая отмена")]
    public async Task CancelAsyncBasic()
    {
        var command = new TestCommand("test");
        var result = await command.CancelAsync(["arg1"]);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(command.CancellationCalled, Is.True);
        });
    }

    [TestCase(TestName = "CancelAsync: без аргументов")]
    public async Task CancelAsyncNoArgs()
    {
        var command = new TestCommand("test");
        var result = await command.CancelAsync();

        Assert.That(result, Is.True);
    }

    [TestCase(TestName = "CancelAsync: с CancellationToken")]
    public async Task CancelAsyncWithCancellationToken()
    {
        var command = new TestCommand("test");
        using var cts = new CancellationTokenSource();

        var result = await command.CancelAsync(["arg1"], cts.Token);

        Assert.That(result, Is.True);
    }

    #endregion

    #region Event Tests

    [TestCase(TestName = "Events: Parsing вызывается")]
    public async Task EventParsingIsCalled()
    {
        var command = new TestCommand("test");
        var parsingCalled = false;
        ParseCommandEventArgs? eventArgs = null;

        command.Parsing += (sender, e) =>
        {
            parsingCalled = true;
            eventArgs = e;
            return ValueTask.CompletedTask;
        };

        await command.TryParseAsync("test arg1 arg2");

        Assert.Multiple(() =>
        {
            Assert.That(parsingCalled, Is.True);
            Assert.That(eventArgs, Is.Not.Null);
            Assert.That(eventArgs!.Args.Count(), Is.EqualTo(2));
        });
    }

    [TestCase(TestName = "Events: Execution вызывается")]
    public async Task EventExecutionIsCalled()
    {
        var command = new TestCommand("test");
        var executionCalled = false;
        ExecuteCommandEventArgs? eventArgs = null;

        command.Execution += (sender, e) =>
        {
            executionCalled = true;
            eventArgs = e;
            e.IsSuccess = true;
            return ValueTask.CompletedTask;
        };

        await command.ExecuteAsync(["arg1"]);

        Assert.Multiple(() =>
        {
            Assert.That(executionCalled, Is.True);
            Assert.That(eventArgs, Is.Not.Null);
            Assert.That(eventArgs!.Args.Count(), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "Events: Cancelling вызывается")]
    public async Task EventCancellingIsCalled()
    {
        var command = new TestCommand("test");
        var cancellingCalled = false;

        command.Cancelling += (sender, e) =>
        {
            cancellingCalled = true;
            e.IsSuccess = true;
            return ValueTask.CompletedTask;
        };

        await command.CancelAsync(["arg1"]);

        Assert.That(cancellingCalled, Is.True);
    }

    [TestCase(TestName = "Events: множественные подписчики")]
    public async Task EventMultipleSubscribers()
    {
        var command = new TestCommand("test");
        var callCount = 0;

        command.Execution += (sender, e) =>
        {
            Interlocked.Increment(ref callCount);
            e.IsSuccess = true;
            return ValueTask.CompletedTask;
        };

        command.Execution += (sender, e) =>
        {
            Interlocked.Increment(ref callCount);
            e.IsSuccess = true;
            return ValueTask.CompletedTask;
        };

        await command.ExecuteAsync();

        Assert.That(callCount, Is.EqualTo(2));
    }

    #endregion

    #region ConsoleCommandParseResult Tests

    [TestCase(TestName = "ConsoleCommandParseResult: Failed статическое свойство")]
    public void ParseResultFailedProperty()
    {
        var failed = ConsoleCommandParseResult.Failed;

        Assert.Multiple(() =>
        {
            Assert.That(failed.IsSuccess, Is.False);
            Assert.That(failed.IsCancellation, Is.False);
            Assert.That(failed.Args, Is.Empty);
        });
    }

    [TestCase(TestName = "ConsoleCommandParseResult: создание успешного результата")]
    public void ParseResultSuccessCreation()
    {
        var result = new ConsoleCommandParseResult(true, ["arg1", "arg2"], false);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IsCancellation, Is.False);
            Assert.That(result.Args.Count, Is.EqualTo(2));
        });
    }

    [TestCase(TestName = "ConsoleCommandParseResult: создание результата отмены")]
    public void ParseResultCancellationCreation()
    {
        var result = new ConsoleCommandParseResult(true, [], true);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IsCancellation, Is.True);
        });
    }

    #endregion

    #region ExecuteCommandEventArgs Tests

    [TestCase(TestName = "ExecuteCommandEventArgs: создание с аргументами")]
    public void ExecuteCommandEventArgsCreation()
    {
        var args = new ExecuteCommandEventArgs(["arg1", "arg2"]);

        Assert.Multiple(() =>
        {
            Assert.That(args.Args.Count(), Is.EqualTo(2));
            Assert.That(args.IsSuccess, Is.False); // По умолчанию false
            Assert.That(args.IsCancelled, Is.False);
        });
    }

    [TestCase(TestName = "ExecuteCommandEventArgs: IsSuccess можно установить")]
    public void ExecuteCommandEventArgsIsSuccessSettable()
    {
        var args = new ExecuteCommandEventArgs([])
        {
            IsSuccess = true
        };

        Assert.That(args.IsSuccess, Is.True);
    }

    #endregion

    #region ParseCommandEventArgs Tests

    [TestCase(TestName = "ParseCommandEventArgs: создание")]
    public void ParseCommandEventArgsCreation()
    {
        var args = new ParseCommandEventArgs("test arg1", ["arg1"]);

        Assert.Multiple(() =>
        {
            Assert.That(args.Origin, Is.EqualTo("test arg1"));
            Assert.That(args.Args.Count(), Is.EqualTo(1));
        });
    }

    #endregion

    #region Edge Cases Tests

    [TestCase(TestName = "Edge: аргументы с пустыми строками")]
    public async Task EdgeEmptyStringArgs()
    {
        var command = new TestCommand("test");
        var result = await command.TryParseAsync("test \"\" ''");

        // Пустые аргументы должны быть отфильтрованы
        Assert.That(result.IsSuccess, Is.True);
    }

    [TestCase(TestName = "Edge: команда с лишними пробелами")]
    public async Task EdgeExtraSpaces()
    {
        var command = new TestCommand("test");
        var result = await command.TryParseAsync("  test   arg1   arg2  ");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Args.Count, Is.EqualTo(2));
        });
    }

    [TestCase(TestName = "Edge: команда только с дефисом")]
    public async Task EdgeOnlyDash()
    {
        var command = new TestCommand("test");
        var result = await command.TryParseAsync("-");

        Assert.That(result.IsSuccess, Is.False);
    }

    [TestCase(TestName = "Edge: специальные символы в аргументах")]
    public async Task EdgeSpecialCharsInArgs()
    {
        var command = new TestCommand("test");
        var result = await command.TryParseAsync("test @#$% &*()");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Args.Count, Is.EqualTo(2));
        });
    }

    #endregion

    #region Thread Safety Tests

    [TestCase(TestName = "Thread Safety: параллельный парсинг")]
    public async Task ThreadSafetyParallelParsing()
    {
        var command = new TestCommand("test");
        const int iterations = 100;
        var tasks = new Task<ConsoleCommandParseResult>[iterations];

        for (var i = 0; i < iterations; i++)
        {
            var index = i;
            tasks[i] = command.TryParseAsync($"test arg{index}").AsTask();
        }

        var results = await Task.WhenAll(tasks);

        Assert.That(results, Has.All.Matches<ConsoleCommandParseResult>(r => r.IsSuccess));
    }

    [TestCase(TestName = "Thread Safety: параллельное выполнение")]
    public async Task ThreadSafetyParallelExecution()
    {
        var command = new TestCommand("test");
        const int iterations = 100;
        var tasks = new Task<bool>[iterations];

        for (var i = 0; i < iterations; i++)
        {
            var index = i;
            tasks[i] = command.ExecuteAsync([$"arg{index}"]).AsTask();
        }

        var results = await Task.WhenAll(tasks);

        Assert.That(results, Has.All.True);
    }

    #endregion

    #region Integration Tests

    [TestCase(TestName = "Integration: полный цикл парсинг -> выполнение")]
    public async Task IntegrationFullCycle()
    {
        var command = new TestCommand("greet");

        // Парсим команду
        var parseResult = await command.TryParseAsync("greet World");
        Assert.That(parseResult.IsSuccess, Is.True);

        // Выполняем команду
        var executeResult = await command.ExecuteAsync(parseResult.Args);
        Assert.That(executeResult, Is.True);

        // Проверяем, что аргументы переданы
        Assert.That(command.LastArgs!.First(), Is.EqualTo("World"));
    }

    [TestCase(TestName = "Integration: цикл парсинг -> отмена")]
    public async Task IntegrationCancellationCycle()
    {
        var command = new TestCommand("task");

        // Парсим команду отмены
        var parseResult = await command.TryParseAsync("-task arg1");
        Assert.Multiple(() =>
        {
            Assert.That(parseResult.IsSuccess, Is.True);
            Assert.That(parseResult.IsCancellation, Is.True);
        });

        // Отменяем команду
        var cancelResult = await command.CancelAsync(parseResult.Args);
        Assert.That(cancelResult, Is.True);
    }

    #endregion
}
