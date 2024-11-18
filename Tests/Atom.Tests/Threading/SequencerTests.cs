using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Threading.Tests;

public class SequencerTests(ILogger logger) : BenchmarkTest<SequencerTests>(logger)
{
    private readonly Sequencer sequencer = new(TimeSpan.FromSeconds(1), true);

    public override bool IsBenchmarkDisabled => true;

    public SequencerTests() : this(ConsoleLogger.Unicode) { }

    private async void TestCallback()
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        sequencer.Remove(TestCallback);
    }

    [TestCase(TestName = "Тест проверки секвенсора"), Benchmark]
    public async Task BaseTest()
    {
        sequencer.Add(TestCallback);
        await sequencer.StartAsync();

        await sequencer.WaitAsync(TestCallback);

        sequencer.Interval = TimeSpan.FromSeconds(1);

        sequencer.Add(TestCallback);
        await sequencer.StartAsync();

        await sequencer.WaitAsync(TestCallback);

        Assert.Pass();
    }
}