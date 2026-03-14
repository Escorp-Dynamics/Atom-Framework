using Atom.Media.Audio;

namespace Atom.Media.Audio.Tests;

[TestFixture]
public class PeakHoldMeterTests(ILogger logger) : BenchmarkTests<PeakHoldMeterTests>(logger)
{
    public PeakHoldMeterTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "PeakHold: начальное значение = 0")]
    public void InitialValueZero()
    {
        var meter = new PeakHoldMeter();
        Assert.That(meter.HoldPeak, Is.Zero.Within(0.001f));
    }

    [TestCase(TestName = "PeakHold: значения по умолчанию")]
    public void DefaultValues()
    {
        var meter = new PeakHoldMeter();
        Assert.Multiple(() =>
        {
            Assert.That(meter.HoldTimeMs, Is.EqualTo(1500.0f));
            Assert.That(meter.DecayRateDbPerSecond, Is.EqualTo(20.0f));
        });
    }

    [TestCase(TestName = "PeakHold: мгновенный переход при новом пике")]
    public void ImmediateJumpOnNewPeak()
    {
        var meter = new PeakHoldMeter();

        meter.Update(0.5f);
        Assert.That(meter.HoldPeak, Is.EqualTo(0.5f).Within(0.001f));

        meter.Update(0.8f);
        Assert.That(meter.HoldPeak, Is.EqualTo(0.8f).Within(0.001f));
    }

    [TestCase(TestName = "PeakHold: удержание пика при меньшем значении")]
    public void HoldsPeakOnLowerValue()
    {
        var meter = new PeakHoldMeter();

        meter.Update(0.9f);
        meter.Update(0.3f);

        Assert.That(meter.HoldPeak, Is.EqualTo(0.9f).Within(0.001f));
    }

    [TestCase(TestName = "PeakHold: затухание после holdTime")]
    public void DecayAfterHoldTime()
    {
        var meter = new PeakHoldMeter
        {
            HoldTimeMs = 0,
            DecayRateDbPerSecond = 60.0f,
        };

        meter.Update(1.0f);
        Thread.Sleep(200);
        meter.Update(0.0f);

        Assert.That(meter.HoldPeak, Is.LessThan(1.0f));
        Assert.That(meter.HoldPeak, Is.GreaterThanOrEqualTo(0.0f));
    }

    [TestCase(TestName = "PeakHold: новый пик прерывает затухание")]
    public void NewPeakInterruptsDecay()
    {
        var meter = new PeakHoldMeter { HoldTimeMs = 0, DecayRateDbPerSecond = 60.0f };

        meter.Update(0.5f);
        Thread.Sleep(100);
        meter.Update(0.1f);
        var decayed = meter.HoldPeak;
        Assert.That(decayed, Is.LessThan(0.5f));

        meter.Update(0.8f);
        Assert.That(meter.HoldPeak, Is.EqualTo(0.8f).Within(0.001f));
    }

    [TestCase(TestName = "PeakHold: Reset сбрасывает на ноль")]
    public void ResetClearsHoldPeak()
    {
        var meter = new PeakHoldMeter();
        meter.Update(0.7f);

        meter.Reset();

        Assert.That(meter.HoldPeak, Is.Zero.Within(0.001f));
    }

    [TestCase(TestName = "PeakHold: настраиваемая скорость затухания")]
    public void ConfigurableDecayRate()
    {
        var slowMeter = new PeakHoldMeter { HoldTimeMs = 0, DecayRateDbPerSecond = 10.0f };
        var fastMeter = new PeakHoldMeter { HoldTimeMs = 0, DecayRateDbPerSecond = 120.0f };

        slowMeter.Update(1.0f);
        fastMeter.Update(1.0f);

        Thread.Sleep(200);

        slowMeter.Update(0.0f);
        fastMeter.Update(0.0f);

        Assert.That(fastMeter.HoldPeak, Is.LessThan(slowMeter.HoldPeak));
    }

    [TestCase(TestName = "PeakHold: нулевой пик не поднимает hold")]
    public void ZeroPeakDoesNotRaiseHold()
    {
        var meter = new PeakHoldMeter();
        meter.Update(0.0f);
        Assert.That(meter.HoldPeak, Is.Zero.Within(0.001f));
    }

    [TestCase(TestName = "PeakHold: множественные обновления сохраняют максимум")]
    public void MultipleUpdatesKeepMax()
    {
        var meter = new PeakHoldMeter();

        meter.Update(0.3f);
        meter.Update(0.7f);
        meter.Update(0.5f);
        meter.Update(0.2f);

        Assert.That(meter.HoldPeak, Is.EqualTo(0.7f).Within(0.001f));
    }
}
