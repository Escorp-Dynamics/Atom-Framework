using System.Runtime.Versioning;
using Atom.Media.Video;
using Atom.Media.Video.Backends;
using static Atom.Media.Video.Backends.PipeWire.PipeWireNative;

namespace Atom.Media.Video.Tests;

[TestFixture]
[SupportedOSPlatform("linux")]
public class CameraControlTests(ILogger logger) : BenchmarkTests<CameraControlTests>(logger)
{
    public CameraControlTests() : this(ConsoleLogger.Unicode) { }

    // --- CameraControlType enum ---

    [TestCase(TestName = "CameraControlType: содержит 8 значений")]
    public void EnumHasEightValues()
    {
        var values = Enum.GetValues<CameraControlType>();
        Assert.That(values, Has.Length.EqualTo(8));
    }

    [TestCase(CameraControlType.Brightness, 0, TestName = "CameraControlType: Brightness = 0")]
    [TestCase(CameraControlType.Contrast, 1, TestName = "CameraControlType: Contrast = 1")]
    [TestCase(CameraControlType.Saturation, 2, TestName = "CameraControlType: Saturation = 2")]
    [TestCase(CameraControlType.Hue, 3, TestName = "CameraControlType: Hue = 3")]
    [TestCase(CameraControlType.Gamma, 4, TestName = "CameraControlType: Gamma = 4")]
    [TestCase(CameraControlType.Exposure, 5, TestName = "CameraControlType: Exposure = 5")]
    [TestCase(CameraControlType.Gain, 6, TestName = "CameraControlType: Gain = 6")]
    [TestCase(CameraControlType.Sharpness, 7, TestName = "CameraControlType: Sharpness = 7")]
    public void EnumOrdinalValues(CameraControlType control, int expected)
    {
        Assert.That((int)control, Is.EqualTo(expected));
    }

    // --- MapControlToSpaProp ---

    [TestCase(CameraControlType.Brightness, 0x20001u, TestName = "MapControl: Brightness → SPA_PROP_brightness")]
    [TestCase(CameraControlType.Contrast, 0x20002u, TestName = "MapControl: Contrast → SPA_PROP_contrast")]
    [TestCase(CameraControlType.Saturation, 0x20003u, TestName = "MapControl: Saturation → SPA_PROP_saturation")]
    [TestCase(CameraControlType.Hue, 0x20004u, TestName = "MapControl: Hue → SPA_PROP_hue")]
    [TestCase(CameraControlType.Gamma, 0x20005u, TestName = "MapControl: Gamma → SPA_PROP_gamma")]
    [TestCase(CameraControlType.Exposure, 0x20006u, TestName = "MapControl: Exposure → SPA_PROP_exposure")]
    [TestCase(CameraControlType.Gain, 0x20007u, TestName = "MapControl: Gain → SPA_PROP_gain")]
    [TestCase(CameraControlType.Sharpness, 0x20008u, TestName = "MapControl: Sharpness → SPA_PROP_sharpness")]
    public void MapControlToSpaProps(CameraControlType control, uint expectedProp)
    {
        Assert.That(LinuxCameraBackend.MapControlToSpaProp(control), Is.EqualTo(expectedProp));
    }

    [TestCase(TestName = "MapControl: неизвестный контрол → ArgumentOutOfRangeException")]
    public void MapControlUnknownThrows()
    {
        Assert.That(
            () => LinuxCameraBackend.MapControlToSpaProp((CameraControlType)999),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // --- SPA_PROP_ constants ---

    [TestCase(TestName = "SPA_PROP: brightness = 0x20001")]
    public void SpaPropBrightness()
    {
        Assert.That(SPA_PROP_brightness, Is.EqualTo(0x20001u));
    }

    [TestCase(TestName = "SPA_PROP: contrast = 0x20002")]
    public void SpaPropContrast()
    {
        Assert.That(SPA_PROP_contrast, Is.EqualTo(0x20002u));
    }

    [TestCase(TestName = "SPA_PROP: saturation = 0x20003")]
    public void SpaPropSaturation()
    {
        Assert.That(SPA_PROP_saturation, Is.EqualTo(0x20003u));
    }

    [TestCase(TestName = "SPA_PROP: hue = 0x20004")]
    public void SpaPropHue()
    {
        Assert.That(SPA_PROP_hue, Is.EqualTo(0x20004u));
    }

    [TestCase(TestName = "SPA_PROP: gamma = 0x20005")]
    public void SpaPropGamma()
    {
        Assert.That(SPA_PROP_gamma, Is.EqualTo(0x20005u));
    }

    [TestCase(TestName = "SPA_PROP: exposure = 0x20006")]
    public void SpaPropExposure()
    {
        Assert.That(SPA_PROP_exposure, Is.EqualTo(0x20006u));
    }

    [TestCase(TestName = "SPA_PROP: gain = 0x20007")]
    public void SpaPropGain()
    {
        Assert.That(SPA_PROP_gain, Is.EqualTo(0x20007u));
    }

    [TestCase(TestName = "SPA_PROP: sharpness = 0x20008")]
    public void SpaPropSharpness()
    {
        Assert.That(SPA_PROP_sharpness, Is.EqualTo(0x20008u));
    }

    // --- Все контролы последовательны от 0x20001 ---

    [TestCase(TestName = "SPA_PROP: контролы последовательны от 0x20001 до 0x20008")]
    public void SpaPropSequential()
    {
        uint[] props =
        [
            SPA_PROP_brightness,
            SPA_PROP_contrast,
            SPA_PROP_saturation,
            SPA_PROP_hue,
            SPA_PROP_gamma,
            SPA_PROP_exposure,
            SPA_PROP_gain,
            SPA_PROP_sharpness,
        ];

        for (var i = 0; i < props.Length; i++)
        {
            Assert.That(props[i], Is.EqualTo(0x20001u + (uint)i));
        }
    }

    // --- MapControl полнота покрытия enum ---

    [TestCase(TestName = "MapControl: все значения CameraControlType маппятся без исключений")]
    public void MapControlAllEnumValues()
    {
        foreach (var control in Enum.GetValues<CameraControlType>())
        {
            Assert.That(
                () => LinuxCameraBackend.MapControlToSpaProp(control),
                Throws.Nothing,
                control.ToString());
        }
    }

    // --- MapControl биекция ---

    [TestCase(TestName = "MapControl: все значения маппятся в уникальные SPA_PROP")]
    public void MapControlUniqueSpaProps()
    {
        var mapped = Enum.GetValues<CameraControlType>()
            .Select(LinuxCameraBackend.MapControlToSpaProp)
            .ToArray();

        Assert.That(mapped, Is.Unique);
    }

    // --- TryMapSpaPropToControl (обратный маппинг) ---

    [TestCase(0x20001u, CameraControlType.Brightness, TestName = "ReverseMap: 0x20001 → Brightness")]
    [TestCase(0x20002u, CameraControlType.Contrast, TestName = "ReverseMap: 0x20002 → Contrast")]
    [TestCase(0x20003u, CameraControlType.Saturation, TestName = "ReverseMap: 0x20003 → Saturation")]
    [TestCase(0x20004u, CameraControlType.Hue, TestName = "ReverseMap: 0x20004 → Hue")]
    [TestCase(0x20005u, CameraControlType.Gamma, TestName = "ReverseMap: 0x20005 → Gamma")]
    [TestCase(0x20006u, CameraControlType.Exposure, TestName = "ReverseMap: 0x20006 → Exposure")]
    [TestCase(0x20007u, CameraControlType.Gain, TestName = "ReverseMap: 0x20007 → Gain")]
    [TestCase(0x20008u, CameraControlType.Sharpness, TestName = "ReverseMap: 0x20008 → Sharpness")]
    public void TryMapSpaPropToControlKnown(uint propId, CameraControlType expected)
    {
        var result = LinuxCameraBackend.TryMapSpaPropToControl(propId, out var control);
        Assert.That(result, Is.True);
        Assert.That(control, Is.EqualTo(expected));
    }

    [TestCase(0x10000u, TestName = "ReverseMap: 0x10000 → false")]
    [TestCase(0x20000u, TestName = "ReverseMap: 0x20000 → false")]
    [TestCase(0x20009u, TestName = "ReverseMap: 0x20009 → false")]
    [TestCase(0u, TestName = "ReverseMap: 0 → false")]
    public void TryMapSpaPropToControlUnknown(uint propId)
    {
        var result = LinuxCameraBackend.TryMapSpaPropToControl(propId, out _);
        Assert.That(result, Is.False);
    }

    [TestCase(TestName = "ReverseMap: биекция с прямым маппингом")]
    public void ReverseMapBijection()
    {
        foreach (var control in Enum.GetValues<CameraControlType>())
        {
            var propId = LinuxCameraBackend.MapControlToSpaProp(control);
            var success = LinuxCameraBackend.TryMapSpaPropToControl(propId, out var reversed);
            Assert.That(success, Is.True, control.ToString());
            Assert.That(reversed, Is.EqualTo(control), control.ToString());
        }
    }

    // --- CameraControlRange ---

    [TestCase(TestName = "CameraControlRange: значения сохраняются")]
    public void ControlRangeValues()
    {
        var range = new CameraControlRange(Min: 0f, Max: 1f, Default: 0.5f);
        Assert.That(range.Min, Is.Zero);
        Assert.That(range.Max, Is.EqualTo(1f));
        Assert.That(range.Default, Is.EqualTo(0.5f));
    }

    [TestCase(TestName = "CameraControlRange: record equality")]
    public void ControlRangeEquality()
    {
        var a = new CameraControlRange(0f, 100f, 50f);
        var b = new CameraControlRange(0f, 100f, 50f);
        var c = new CameraControlRange(0f, 100f, 75f);

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Is.Not.EqualTo(c));
    }

    // --- CameraControlChangedEventArgs ---

    [TestCase(TestName = "CameraControlChangedEventArgs: свойства сохраняются")]
    public void EventArgsProperties()
    {
        var range = new CameraControlRange(0f, 1f, 0.5f);
        var args = new CameraControlChangedEventArgs
        {
            Control = CameraControlType.Brightness,
            Value = 0.7f,
            Range = range,
        };

        Assert.That(args.Control, Is.EqualTo(CameraControlType.Brightness));
        Assert.That(args.Value, Is.EqualTo(0.7f));
        Assert.That(args.Range, Is.EqualTo(range));
    }

    [TestCase(TestName = "CameraControlChangedEventArgs: Range может быть null")]
    public void EventArgsRangeNullable()
    {
        var args = new CameraControlChangedEventArgs
        {
            Control = CameraControlType.Exposure,
            Value = 0.3f,
        };

        Assert.That(args.Range, Is.Null);
    }
}
