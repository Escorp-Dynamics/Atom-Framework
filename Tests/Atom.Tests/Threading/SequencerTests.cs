using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Threading.Tests;

public class SequencerTests(ILogger logger) : BenchmarkTest<SequencerTests>(logger)
{
    private readonly Sequencer manualSequencer = new(TimeSpan.FromSeconds(1), SequenceMode.Manual);
    private readonly Sequencer loopSequencer = new(TimeSpan.FromSeconds(1), SequenceMode.Loop);
    private readonly Sequencer loopWithWaitingSequencer = new(TimeSpan.FromSeconds(1), SequenceMode.LoopWithWaiting);

    public override bool IsBenchmarkDisabled => true;

    public SequencerTests() : this(ConsoleLogger.Unicode)
    {
        manualSequencer.Started += args => Log("Секвенция запущена");
        manualSequencer.Stopped += args => Log("Секвенция остановлена");
        manualSequencer.Added += args => Log($"Задача добавлена: {args.Task?.Method.Name}");
        manualSequencer.Updated += args => Log($"Задача обновлена {args.Task?.Method.Name}");
        manualSequencer.Removed += args => Log($"Задача удалена {args.Task?.Method.Name}");
        manualSequencer.Sequence += args => Log($"Выполнение {args.Task?.Method.Name}");
        manualSequencer.Failed += args => Log(args.Exception?.Message);
        manualSequencer.Paused += args => Log($"Задача приостановлена {args.Task?.Method.Name}");
        manualSequencer.Resumed += args => Log($"Задача возобновлена {args.Task?.Method.Name}");

        loopSequencer.Started += args => Log("Секвенция запущена");
        loopSequencer.Stopped += args => Log("Секвенция остановлена");
        loopSequencer.Added += args => Log($"Задача добавлена {args.Task?.Method.Name}");
        loopSequencer.Updated += args => Log($"Задача обновлена {args.Task?.Method.Name}");
        loopSequencer.Removed += args => Log($"Задача удалена {args.Task?.Method.Name}");
        loopSequencer.Sequence += args => Log($"Выполнение {args.Task?.Method.Name}");
        loopSequencer.Failed += args => Log(args.Exception?.Message);
        loopSequencer.Paused += args => Log($"Задача приостановлена {args.Task?.Method.Name}");
        loopSequencer.Resumed += args => Log($"Задача возобновлена {args.Task?.Method.Name}");

        loopWithWaitingSequencer.Started += args => Log("Секвенция запущена");
        loopWithWaitingSequencer.Stopped += args => Log("Секвенция остановлена");
        loopWithWaitingSequencer.Added += args => Log($"Задача добавлена {args.Task?.Method.Name}");
        loopWithWaitingSequencer.Updated += args => Log($"Задача обновлена {args.Task?.Method.Name}");
        loopWithWaitingSequencer.Removed += args => Log($"Задача удалена {args.Task?.Method.Name}");
        loopWithWaitingSequencer.Sequence += args => Log($"Выполнение {args.Task?.Method.Name}");
        loopWithWaitingSequencer.Failed += args => Log(args.Exception?.Message);
        loopWithWaitingSequencer.Paused += args => Log($"Задача приостановлена {args.Task?.Method.Name}");
        loopWithWaitingSequencer.Resumed += args => Log($"Задача возобновлена {args.Task?.Method.Name}");
    }

    private void Log(string? message)
    {
        message = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}";
        Logger.WriteLineInfo(message);
        Trace.TraceInformation(message);
    }

    private ValueTask TestCallback()
    {
        Log("TestCallback()");
        return ValueTask.CompletedTask;
    }

    private async ValueTask TestCallbackAsync()
    {
        Log("TestCallbackAsync(): START");
        await Task.Delay(TimeSpan.FromSeconds(3));
        Log("TestCallbackAsync(): END");
    }

    [TestCase(TestName = "Тест проверки секвенсора (Manual)"), Benchmark]
    public async Task ManualTest()
    {
        manualSequencer.Add(TestCallback);
        manualSequencer.Add(TestCallbackAsync);

        manualSequencer.Start();

        var result = await manualSequencer.WaitAsync(TestCallback);
        Assert.That(result, Is.False);

        result = await manualSequencer.WaitAsync(TestCallbackAsync);
        Assert.That(result, Is.False);
    }

    [TestCase(TestName = "Тест проверки секвенсора (Loop)"), Benchmark]
    public async Task LoopTest()
    {
        loopSequencer.Add(TestCallback);
        loopSequencer.Add(TestCallbackAsync);

        loopSequencer.Start();

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            loopSequencer.Remove(TestCallback);
        });

        var result = await loopSequencer.WaitAsync(TestCallback);
        Assert.That(result, Is.False);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            loopSequencer.Stop();
        });

        result = await loopSequencer.WaitAsync(TestCallbackAsync);
        Assert.That(result, Is.True);
    }

    [TestCase(TestName = "Тест проверки секвенсора (LoopWithWaiting)"), Benchmark]
    public async Task LoopWithWaitingTest()
    {
        loopWithWaitingSequencer.Add(TestCallback);
        loopWithWaitingSequencer.Add(TestCallbackAsync);

        loopWithWaitingSequencer.Start();

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            loopWithWaitingSequencer.Stop();
        });

        var result = await loopWithWaitingSequencer.WaitAsync(TestCallback);
        Assert.That(result, Is.True);
    }

    [TestCase(TestName = "Тест проверки секвенсора с паузой (Manual)"), Benchmark]
    public async Task ManualWithPauseTest()
    {
        manualSequencer.Add(TestCallback);
        manualSequencer.Add(TestCallbackAsync);

        manualSequencer.Start();
        manualSequencer.Pause(TestCallbackAsync);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            manualSequencer.Resume(TestCallbackAsync);
        });

        var result = await manualSequencer.WaitAsync(TestCallback);
        Assert.That(result, Is.False);

        result = await manualSequencer.WaitAsync(TestCallbackAsync);
        Assert.That(result, Is.False);
    }

    [TestCase(TestName = "Тест проверки секвенсора с паузой (Loop)"), Benchmark]
    public async Task LoopWithPauseTest()
    {
        loopSequencer.Add(TestCallback);
        loopSequencer.Add(TestCallbackAsync);

        loopSequencer.Start();
        loopSequencer.Pause(TestCallbackAsync);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            loopSequencer.Resume(TestCallbackAsync);
        });

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            loopSequencer.Remove(TestCallback);
        });

        var result = await loopSequencer.WaitAsync(TestCallback);
        Assert.That(result, Is.False);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            loopSequencer.Stop();
        });

        result = await loopSequencer.WaitAsync(TestCallbackAsync);
        Assert.That(result, Is.True);
    }

    [TestCase(TestName = "Тест проверки секвенсора с паузой (LoopWithWaiting)"), Benchmark]
    public async Task LoopWithWaitingWithPauseTest()
    {
        loopWithWaitingSequencer.Add(TestCallback);
        loopWithWaitingSequencer.Add(TestCallbackAsync);

        loopWithWaitingSequencer.Start();
        loopWithWaitingSequencer.Pause(TestCallbackAsync);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            loopWithWaitingSequencer.Resume(TestCallbackAsync);
        });

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            loopWithWaitingSequencer.Stop();
        });

        var result = await loopWithWaitingSequencer.WaitAsync(TestCallback);
        Assert.That(result, Is.True);
    }
}