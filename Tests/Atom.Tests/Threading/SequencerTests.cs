using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Threading.Tests;

public class SequencerTests(ILogger logger) : BenchmarkTest<SequencerTests>(logger)
{
    private static readonly Sequencer sequencer = new(TimeSpan.FromSeconds(3), true);

    public override bool IsBenchmarkDisabled => true;

    public SequencerTests() : this(ConsoleLogger.Unicode) { }

    private async void TestCallback()
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        sequencer.Remove(TestCallback);
    }

    [TestCase(TestName = "Тест проверки секвенсора"), Benchmark]
    public async Task BaseTest()
    {
        sequencer.Add(TestCallback);
        await sequencer.StartAsync();

        var result = await sequencer.WaitAsync(TestCallback);
        Assert.That(result, Is.False);

        await Task.Delay(TimeSpan.FromSeconds(15));

        sequencer.Add(TestCallback);
        await sequencer.StartAsync();

        result = await sequencer.WaitAsync(TestCallback);
        Assert.That(result, Is.False);
    }
}