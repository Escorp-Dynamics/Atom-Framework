using System.Runtime.Versioning;
using Atom.Media.Audio;
using Atom.Media.Audio.Backends;

namespace Atom.Media.Audio.Tests;

[TestFixture]
[SupportedOSPlatform("linux")]
public class MicrophoneControlTests(ILogger logger) : BenchmarkTests<MicrophoneControlTests>(logger)
{
    public MicrophoneControlTests() : this(ConsoleLogger.Unicode) { }

    // --- MapControlToSpaProp ---

    [TestCase(TestName = "MapControlToSpaProp: Volume → 0x10001")]
    public void MapVolumeToSpaProp()
    {
        Assert.That(LinuxMicrophoneBackend.MapControlToSpaProp(MicrophoneControlType.Volume), Is.EqualTo(0x10001u));
    }

    [TestCase(TestName = "MapControlToSpaProp: Mute → 0x10002")]
    public void MapMuteToSpaProp()
    {
        Assert.That(LinuxMicrophoneBackend.MapControlToSpaProp(MicrophoneControlType.Mute), Is.EqualTo(0x10002u));
    }

    [TestCase(TestName = "MapControlToSpaProp: неизвестный контрол → ArgumentOutOfRangeException")]
    public void MapUnknownControlThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LinuxMicrophoneBackend.MapControlToSpaProp((MicrophoneControlType)99));
    }

    // --- TryMapSpaPropToControl ---

    [TestCase(TestName = "TryMapSpaPropToControl: 0x10001 → Volume")]
    public void TryMapVolumeFromSpaProp()
    {
        Assert.That(LinuxMicrophoneBackend.TryMapSpaPropToControl(0x10001, out var control), Is.True);
        Assert.That(control, Is.EqualTo(MicrophoneControlType.Volume));
    }

    [TestCase(TestName = "TryMapSpaPropToControl: 0x10002 → Mute")]
    public void TryMapMuteFromSpaProp()
    {
        Assert.That(LinuxMicrophoneBackend.TryMapSpaPropToControl(0x10002, out var control), Is.True);
        Assert.That(control, Is.EqualTo(MicrophoneControlType.Mute));
    }

    [TestCase(TestName = "TryMapSpaPropToControl: неизвестный prop → false")]
    public void TryMapUnknownPropReturnsFalse()
    {
        Assert.That(LinuxMicrophoneBackend.TryMapSpaPropToControl(0x99999, out _), Is.False);
    }

    [TestCase(TestName = "TryMapSpaPropToControl: video prop → false")]
    public void TryMapVideoPropReturnsFalse()
    {
        Assert.That(LinuxMicrophoneBackend.TryMapSpaPropToControl(0x20001, out _), Is.False);
    }

    // --- Биекция ---

    [TestCase(TestName = "Биекция: MapControlToSpaProp ↔ TryMapSpaPropToControl")]
    public void ReverseMapBijection()
    {
        foreach (var control in Enum.GetValues<MicrophoneControlType>())
        {
            var propId = LinuxMicrophoneBackend.MapControlToSpaProp(control);
            Assert.That(LinuxMicrophoneBackend.TryMapSpaPropToControl(propId, out var reverse), Is.True);
            Assert.That(reverse, Is.EqualTo(control));
        }
    }

    // --- MicrophoneControlRange ---

    [TestCase(TestName = "ControlRange: значения сохраняются")]
    public void ControlRangeValues()
    {
        var range = new MicrophoneControlRange(0.0f, 1.0f, 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(range.Min, Is.Zero);
            Assert.That(range.Max, Is.EqualTo(1.0f));
            Assert.That(range.Default, Is.EqualTo(0.5f));
        });
    }

    [TestCase(TestName = "ControlRange: record equality")]
    public void ControlRangeEquality()
    {
        var a = new MicrophoneControlRange(0f, 1f, 0.5f);
        var b = new MicrophoneControlRange(0f, 1f, 0.5f);
        Assert.That(a, Is.EqualTo(b));
    }

    // --- MicrophoneControlChangedEventArgs ---

    [TestCase(TestName = "EventArgs: свойства сохраняются")]
    public void EventArgsProperties()
    {
        var args = new MicrophoneControlChangedEventArgs
        {
            Control = MicrophoneControlType.Volume,
            Value = 0.75f,
            Range = new MicrophoneControlRange(0f, 1f, 0.5f),
        };

        Assert.Multiple(() =>
        {
            Assert.That(args.Control, Is.EqualTo(MicrophoneControlType.Volume));
            Assert.That(args.Value, Is.EqualTo(0.75f));
            Assert.That(args.Range, Is.Not.Null);
            Assert.That(args.Range!.Max, Is.EqualTo(1.0f));
        });
    }

    [TestCase(TestName = "EventArgs: Range nullable")]
    public void EventArgsRangeNullable()
    {
        var args = new MicrophoneControlChangedEventArgs
        {
            Control = MicrophoneControlType.Mute,
            Value = 1.0f,
        };

        Assert.That(args.Range, Is.Null);
    }
}
