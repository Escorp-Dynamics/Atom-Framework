using Atom.Media.Audio;

namespace Atom.Media.Audio.Tests;

[TestFixture]
public class VirtualMicrophoneTests(ILogger logger) : BenchmarkTests<VirtualMicrophoneTests>(logger)
{
    public VirtualMicrophoneTests() : this(ConsoleLogger.Unicode) { }

    // --- VirtualMicrophoneSettings ---

    [TestCase(TestName = "Настройки: частота дискретизации по умолчанию 48000")]
    public void SettingsDefaultSampleRate()
    {
        var settings = new VirtualMicrophoneSettings();
        Assert.That(settings.SampleRate, Is.EqualTo(48000));
    }

    [TestCase(TestName = "Настройки: каналы по умолчанию 1")]
    public void SettingsDefaultChannels()
    {
        var settings = new VirtualMicrophoneSettings();
        Assert.That(settings.Channels, Is.EqualTo(1));
    }

    [TestCase(TestName = "Настройки: формат семплов по умолчанию F32")]
    public void SettingsDefaultSampleFormat()
    {
        var settings = new VirtualMicrophoneSettings();
        Assert.That(settings.SampleFormat, Is.EqualTo(AudioSampleFormat.F32));
    }

    [TestCase(TestName = "Настройки: имя микрофона по умолчанию 'Virtual Microphone'")]
    public void SettingsDefaultName()
    {
        var settings = new VirtualMicrophoneSettings();
        Assert.That(settings.Name, Is.EqualTo("Virtual Microphone"));
    }

    [TestCase(TestName = "Настройки: DeviceId по умолчанию null")]
    public void SettingsDefaultDeviceIdNull()
    {
        var settings = new VirtualMicrophoneSettings();
        Assert.That(settings.DeviceId, Is.Null);
    }

    [TestCase(TestName = "Настройки: LatencyMs по умолчанию 10")]
    public void SettingsDefaultLatency()
    {
        var settings = new VirtualMicrophoneSettings();
        Assert.That(settings.LatencyMs, Is.EqualTo(10));
    }

    [TestCase(TestName = "Настройки: пользовательская латентность")]
    public void SettingsCustomLatency()
    {
        var settings = new VirtualMicrophoneSettings { LatencyMs = 5 };
        Assert.That(settings.LatencyMs, Is.EqualTo(5));
    }

    [TestCase(TestName = "Настройки: пользовательские значения")]
    public void SettingsCustomValues()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 44100,
            Channels = 2,
            SampleFormat = AudioSampleFormat.S16,
            Name = "Test Mic",
            DeviceId = "device-123",
            Vendor = "TestVendor",
            Model = "TestModel",
        };

        Assert.Multiple(() =>
        {
            Assert.That(settings.SampleRate, Is.EqualTo(44100));
            Assert.That(settings.Channels, Is.EqualTo(2));
            Assert.That(settings.SampleFormat, Is.EqualTo(AudioSampleFormat.S16));
            Assert.That(settings.Name, Is.EqualTo("Test Mic"));
            Assert.That(settings.DeviceId, Is.EqualTo("device-123"));
            Assert.That(settings.Vendor, Is.EqualTo("TestVendor"));
            Assert.That(settings.Model, Is.EqualTo("TestModel"));
        });
    }

    [TestCase(TestName = "Настройки: ExtraProperties")]
    public void SettingsExtraProperties()
    {
        var extras = new Dictionary<string, string> { ["custom.key"] = "value" };
        var settings = new VirtualMicrophoneSettings { ExtraProperties = extras };

        Assert.That(settings.ExtraProperties, Is.Not.Null);
        Assert.That(settings.ExtraProperties!["custom.key"], Is.EqualTo("value"));
    }

    // --- VirtualMicrophoneException ---

    [TestCase(TestName = "Исключение: сообщение сохраняется")]
    public void ExceptionMessage()
    {
        var ex = new VirtualMicrophoneException("test error");
        Assert.That(ex.Message, Is.EqualTo("test error"));
    }

    [TestCase(TestName = "Исключение: inner exception сохраняется")]
    public void ExceptionWithInner()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new VirtualMicrophoneException("outer", inner);

        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Is.EqualTo("outer"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        });
    }

    // --- MicrophoneControlType ---

    [TestCase(TestName = "ControlType: Volume = 0")]
    public void ControlTypeVolume()
    {
        Assert.That((int)MicrophoneControlType.Volume, Is.Zero);
    }

    [TestCase(TestName = "ControlType: Mute = 1")]
    public void ControlTypeMute()
    {
        Assert.That((int)MicrophoneControlType.Mute, Is.EqualTo(1));
    }

    // --- MicrophoneControlRange ---

    [TestCase(TestName = "ControlRange: Min/Max/Default сохраняются")]
    public void ControlRangeValues()
    {
        var range = new MicrophoneControlRange(0.0f, 1.0f, 0.75f);

        Assert.Multiple(() =>
        {
            Assert.That(range.Min, Is.Zero);
            Assert.That(range.Max, Is.EqualTo(1.0f));
            Assert.That(range.Default, Is.EqualTo(0.75f));
        });
    }

    [TestCase(TestName = "ControlRange: record equality")]
    public void ControlRangeEquality()
    {
        var a = new MicrophoneControlRange(0.0f, 1.0f, 0.5f);
        var b = new MicrophoneControlRange(0.0f, 1.0f, 0.5f);
        Assert.That(a, Is.EqualTo(b));
    }

    // --- MicrophoneControlChangedEventArgs ---

    [TestCase(TestName = "ControlChanged: свойства сохраняются")]
    public void ControlChangedArgs()
    {
        var range = new MicrophoneControlRange(0.0f, 1.0f, 0.5f);
        var args = new MicrophoneControlChangedEventArgs
        {
            Control = MicrophoneControlType.Volume,
            Value = 0.8f,
            Range = range,
        };

        Assert.Multiple(() =>
        {
            Assert.That(args.Control, Is.EqualTo(MicrophoneControlType.Volume));
            Assert.That(args.Value, Is.EqualTo(0.8f));
            Assert.That(args.Range, Is.EqualTo(range));
        });
    }
}
