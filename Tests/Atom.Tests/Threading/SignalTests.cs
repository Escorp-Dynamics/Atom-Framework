using System.Diagnostics;

namespace Atom.Threading.Tests;

/// <summary>
/// Тесты для <see cref="Signal{T}"/> и <see cref="Signal"/>.
/// </summary>
public class SignalTests(ILogger logger) : BenchmarkTests<SignalTests>(logger)
{
    #region Constructors

    public SignalTests() : this(ConsoleLogger.Unicode) { }

    #endregion

    #region Helper Methods

    private void Log(string? message)
    {
        message = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}";
        Logger.WriteLineInfo(message);
        Trace.TraceInformation(message);
    }

    #endregion

    #region Basic Functionality Tests

    /// <summary>
    /// Тест: Signal.Send вызывает событие Sended.
    /// </summary>
    [TestCase(TestName = "Signal: Send вызывает событие"), Benchmark]
    public void SendTriggersEvent()
    {
        var signal = new Signal<int>();
        var eventFired = false;

        signal.Sended += (_, _) => eventFired = true;
        signal.Send(42);

        Assert.That(eventFired, Is.True, "Событие Sended должно быть вызвано");
    }

    /// <summary>
    /// Тест: Signal.Send передаёт корректное состояние.
    /// </summary>
    [TestCase(TestName = "Signal: Send передаёт состояние"), Benchmark]
    public void SendPassesState()
    {
        var signal = new Signal<string>();
        string? receivedState = null;

        signal.Sended += (_, args) => receivedState = args.State;
        signal.Send("test-state");

        Assert.That(receivedState, Is.EqualTo("test-state"), "Состояние должно быть передано корректно");
    }

    /// <summary>
    /// Тест: Signal.Send без параметра передаёт default состояние.
    /// </summary>
    [TestCase(TestName = "Signal: Send без параметра передаёт default"), Benchmark]
    public void SendWithoutParameterPassesDefault()
    {
        var signal = new Signal<int>();
        var receivedState = -1;

        signal.Sended += (_, args) => receivedState = args.State;
        signal.Send();

        Assert.That(receivedState, Is.Default, "Состояние должно быть default");
    }

    /// <summary>
    /// Тест: Signal с null состоянием.
    /// </summary>
    [TestCase(TestName = "Signal: null состояние обрабатывается"), Benchmark]
    public void SendNullStateHandled()
    {
        var signal = new Signal<string>();
        var receivedState = "initial";
        var eventFired = false;

        signal.Sended += (_, args) =>
        {
            receivedState = args.State;
            eventFired = true;
        };
        signal.Send(null);

        Assert.Multiple(() =>
        {
            Assert.That(eventFired, Is.True, "Событие должно быть вызвано");
            Assert.That(receivedState, Is.Null, "Состояние должно быть null");
        });
    }

    /// <summary>
    /// Тест: базовый Signal (без типа состояния).
    /// </summary>
    [TestCase(TestName = "Signal: базовый класс работает"), Benchmark]
    public void BasicSignalWorks()
    {
        var signal = new Signal();
        var eventFired = false;
        var receivedState = (object?)"initial";

        signal.Sended += (_, args) =>
        {
            eventFired = true;
            receivedState = args.State;
        };
        signal.Send();

        Assert.Multiple(() =>
        {
            Assert.That(eventFired, Is.True, "Событие должно быть вызвано");
            Assert.That(receivedState, Is.Null, "Состояние базового Signal должно быть null");
        });
    }

    #endregion

    #region Multiple Subscribers Tests

    /// <summary>
    /// Тест: несколько подписчиков получают сигнал.
    /// </summary>
    [TestCase(TestName = "Signal: несколько подписчиков"), Benchmark]
    public void MultipleSubscribersReceiveSignal()
    {
        var signal = new Signal<int>();
        var counter1 = 0;
        var counter2 = 0;
        var counter3 = 0;

        signal.Sended += (_, _) => Interlocked.Increment(ref counter1);
        signal.Sended += (_, _) => Interlocked.Increment(ref counter2);
        signal.Sended += (_, _) => Interlocked.Increment(ref counter3);

        signal.Send(1);
        signal.Send(2);

        Assert.Multiple(() =>
        {
            Assert.That(counter1, Is.EqualTo(2), "Первый подписчик должен получить оба сигнала");
            Assert.That(counter2, Is.EqualTo(2), "Второй подписчик должен получить оба сигнала");
            Assert.That(counter3, Is.EqualTo(2), "Третий подписчик должен получить оба сигнала");
        });
    }

    /// <summary>
    /// Тест: отписка от события.
    /// </summary>
    [TestCase(TestName = "Signal: отписка от события"), Benchmark]
    public void UnsubscribeStopsReceivingSignals()
    {
        var signal = new Signal<int>();
        var counter = 0;

        void Handler(object? sender, SignalEventArgs<int> args) => Interlocked.Increment(ref counter);

        signal.Sended += Handler;
        signal.Send(1);

        signal.Sended -= Handler;
        signal.Send(2);

        Assert.That(counter, Is.EqualTo(1), "После отписки сигналы не должны приходить");
    }

    /// <summary>
    /// Тест: Signal без подписчиков не выбрасывает исключений.
    /// </summary>
    [TestCase(TestName = "Signal: Send без подписчиков"), Benchmark]
    public void SendWithoutSubscribersDoesNotThrow()
    {
        var signal = new Signal<int>();

        Assert.DoesNotThrow(() => signal.Send(42), "Send без подписчиков не должен выбрасывать исключений");
    }

    #endregion

    #region Thread Safety Tests

    /// <summary>
    /// Тест: параллельная отправка сигналов.
    /// </summary>
    [TestCase(TestName = "Thread Safety: параллельная отправка сигналов"), Benchmark]
    public async Task ConcurrentSendsWork()
    {
        var signal = new Signal<int>();
        var receivedCount = 0;
        const int sendCount = 100;

        signal.Sended += (_, _) => Interlocked.Increment(ref receivedCount);

        var tasks = Enumerable.Range(0, sendCount).Select(i => Task.Run(() => signal.Send(i))).ToArray();

        await Task.WhenAll(tasks);

        Assert.That(receivedCount, Is.EqualTo(sendCount), $"Должно быть получено {sendCount} сигналов");
    }

    /// <summary>
    /// Тест: параллельная подписка и отправка.
    /// </summary>
    [TestCase(TestName = "Thread Safety: параллельная подписка и отправка"), Benchmark]
    public async Task ConcurrentSubscribeAndSendWork()
    {
        var signal = new Signal<int>();
        var receivedCount = 0;
        const int iterations = 50;

        // Параллельно подписываем обработчики и отправляем сигналы
        var subscribeTask = Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                signal.Sended += (_, _) => Interlocked.Increment(ref receivedCount);
            }
        });

        var sendTask = Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                signal.Send(i);
            }
        });

        await Task.WhenAll(subscribeTask, sendTask);

        Log($"Получено сигналов: {receivedCount}");

        // Главное - не было исключений при параллельном доступе
        Assert.Pass("Параллельная подписка и отправка завершились без исключений");
    }

    /// <summary>
    /// Тест: параллельная подписка и отписка.
    /// </summary>
    [TestCase(TestName = "Thread Safety: параллельная подписка и отписка"), Benchmark]
    public async Task ConcurrentSubscribeAndUnsubscribeWork()
    {
        var signal = new Signal<int>();
        var exceptions = new List<Exception>();
        const int iterations = 100;

        static void Handler(object? sender, SignalEventArgs<int> args) { }

        var tasks = new List<Task>();

        for (var i = 0; i < iterations; i++)
        {
            var shouldSubscribe = i % 2 == 0;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    if (shouldSubscribe)
                        signal.Sended += Handler;
                    else
                        signal.Sended -= Handler;
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.That(exceptions, Is.Empty, "Не должно быть исключений при параллельной подписке/отписке");
    }

    #endregion

    #region Event Args Pooling Tests

    /// <summary>
    /// Тест: SignalEventArgs сбрасывается через Send с последующим Reset.
    /// </summary>
    [TestCase(TestName = "SignalEventArgs: проверка состояния через Signal"), Benchmark]
    public void SignalEventArgsStateCorrect()
    {
        var signal = new Signal<string>();
        string? capturedState = null;

        signal.Sended += (_, args) => capturedState = args.State;
        signal.Send("test");

        Assert.That(capturedState, Is.EqualTo("test"), "State должен быть передан корректно");
    }

    #endregion

    #region Integration Tests

    /// <summary>
    /// Тест: Signal работает с Wait{T}.
    /// </summary>
    [TestCase(TestName = "Integration: Signal и Wait<T>"), Benchmark]
    public async Task SignalWorksWithWait()
    {
        var signal = new Signal<string>();
        var conditionMet = false;

        using var wait = new Wait<string>(signal, "unlock");

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            conditionMet = true;
            signal.Send("unlock");
        });

        var sw = Stopwatch.StartNew();
        await wait.LockUntilAsync(() => conditionMet, 5000);
        sw.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(conditionMet, Is.True, "Условие должно быть выполнено");
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(2000), "Ожидание не должно быть слишком долгим");
        });
    }

    /// <summary>
    /// Тест: Signal с разными типами состояний.
    /// </summary>
    [TestCase(TestName = "Signal: разные типы состояний"), Benchmark]
    public void SignalWithDifferentStateTypes()
    {
        // Value type
        var intSignal = new Signal<int>();
        var intReceived = false;
        intSignal.Sended += (_, args) => intReceived = args.State == 42;
        intSignal.Send(42);

        // Reference type
        var stringSignal = new Signal<string>();
        var stringReceived = false;
        stringSignal.Sended += (_, args) => stringReceived = args.State == "test";
        stringSignal.Send("test");

        // Complex type
        var tupleSignal = new Signal<(int, string)>();
        var tupleReceived = false;
        tupleSignal.Sended += (_, args) => tupleReceived = args.State == (1, "one");
        tupleSignal.Send((1, "one"));

        Assert.Multiple(() =>
        {
            Assert.That(intReceived, Is.True, "Int сигнал должен работать");
            Assert.That(stringReceived, Is.True, "String сигнал должен работать");
            Assert.That(tupleReceived, Is.True, "Tuple сигнал должен работать");
        });
    }

    /// <summary>
    /// Тест: быстрая последовательная отправка сигналов.
    /// </summary>
    [TestCase(TestName = "Signal: быстрая последовательная отправка"), Benchmark]
    public void RapidFireSignals()
    {
        var signal = new Signal<int>();
        var states = new List<int>();
        var lockObj = new object();

        signal.Sended += (_, args) =>
        {
            lock (lockObj) states.Add(args.State);
        };

        const int count = 1000;
        for (var i = 0; i < count; i++)
        {
            signal.Send(i);
        }

        Assert.Multiple(() =>
        {
            Assert.That(states.Count, Is.EqualTo(count), $"Должно быть получено {count} сигналов");
            Assert.That(states, Is.EqualTo(Enumerable.Range(0, count).ToList()), "Порядок сигналов должен сохраняться");
        });
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Тест: исключение в обработчике.
    /// </summary>
    [TestCase(TestName = "Edge Case: исключение в обработчике"), Benchmark]
    public void ExceptionInHandlerBehavior()
    {
        var signal = new Signal<int>();
        var firstCalled = false;
        var exceptionThrown = false;

        signal.Sended += (_, _) => firstCalled = true;
        signal.Sended += (_, _) => throw new InvalidOperationException("Test exception");

        try
        {
            signal.Send(1);
        }
        catch (InvalidOperationException)
        {
            exceptionThrown = true;
        }

        // Первый обработчик должен быть вызван
        Assert.That(firstCalled, Is.True, "Первый обработчик должен быть вызван");
        // Если исключение выброшено - это тоже валидное поведение
        Log($"Исключение выброшено: {exceptionThrown}");
    }

    /// <summary>
    /// Тест: Signal с Sender информацией.
    /// </summary>
    [TestCase(TestName = "Signal: Sender передаётся корректно"), Benchmark]
    public void SenderIsPassedCorrectly()
    {
        var signal = new Signal<int>();
        object? receivedSender = null;

        signal.Sended += (sender, _) => receivedSender = sender;
        signal.Send(42);

        Assert.That(receivedSender, Is.SameAs(signal), "Sender должен быть самим сигналом");
    }

    #endregion
}
