using Atom.Media.Video;

namespace Atom.Media.Video.Tests;

[TestFixture]
public class VirtualCameraTests(ILogger logger) : BenchmarkTests<VirtualCameraTests>(logger)
{
    public VirtualCameraTests() : this(ConsoleLogger.Unicode) { }

    // --- VirtualCameraSettings ---

    [TestCase(TestName = "Настройки: частота кадров по умолчанию 30")]
    public void SettingsDefaultFrameRate()
    {
        var settings = new VirtualCameraSettings { Width = 1920, Height = 1080 };
        Assert.That(settings.FrameRate, Is.EqualTo(30));
    }

    [TestCase(TestName = "Настройки: формат пикселей по умолчанию Yuv420P")]
    public void SettingsDefaultPixelFormat()
    {
        var settings = new VirtualCameraSettings { Width = 640, Height = 480 };
        Assert.That(settings.PixelFormat, Is.EqualTo(VideoPixelFormat.Yuv420P));
    }

    [TestCase(TestName = "Настройки: имя камеры по умолчанию 'Virtual Camera'")]
    public void SettingsDefaultName()
    {
        var settings = new VirtualCameraSettings { Width = 640, Height = 480 };
        Assert.That(settings.Name, Is.EqualTo("Virtual Camera"));
    }

    [TestCase(TestName = "Настройки: кастомные значения сохраняются")]
    public void SettingsCustomValues()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 3840,
            Height = 2160,
            FrameRate = 60,
            PixelFormat = VideoPixelFormat.Nv12,
            Name = "Test Camera",
        };

        Assert.That(settings.Width, Is.EqualTo(3840));
        Assert.That(settings.Height, Is.EqualTo(2160));
        Assert.That(settings.FrameRate, Is.EqualTo(60));
        Assert.That(settings.PixelFormat, Is.EqualTo(VideoPixelFormat.Nv12));
        Assert.That(settings.Name, Is.EqualTo("Test Camera"));
    }

    [TestCase(TestName = "Настройки: record equality работает")]
    public void SettingsEquality()
    {
        var a = new VirtualCameraSettings { Width = 1920, Height = 1080, FrameRate = 30 };
        var b = new VirtualCameraSettings { Width = 1920, Height = 1080, FrameRate = 30 };
        var c = new VirtualCameraSettings { Width = 1280, Height = 720, FrameRate = 30 };

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Is.Not.EqualTo(c));
    }

    [TestCase(TestName = "Настройки: with-выражение создаёт изменённую копию")]
    public void SettingsWithExpression()
    {
        var original = new VirtualCameraSettings { Width = 1920, Height = 1080 };
        var modified = original with { FrameRate = 60 };

        Assert.That(modified.Width, Is.EqualTo(1920));
        Assert.That(modified.Height, Is.EqualTo(1080));
        Assert.That(modified.FrameRate, Is.EqualTo(60));
        Assert.That(original.FrameRate, Is.EqualTo(30));
    }

    // --- VirtualCameraException ---

    [TestCase(TestName = "Исключение: создание с сообщением")]
    public void ExceptionWithMessage()
    {
        var ex = new VirtualCameraException("тест");
        Assert.That(ex.Message, Is.EqualTo("тест"));
        Assert.That(ex.InnerException, Is.Null);
    }

    [TestCase(TestName = "Исключение: создание с сообщением и inner exception")]
    public void ExceptionWithInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new VirtualCameraException("outer", inner);

        Assert.That(ex.Message, Is.EqualTo("outer"));
        Assert.That(ex.InnerException, Is.SameAs(inner));
    }

    [TestCase(TestName = "Исключение: наследуется от NativeException")]
    public void ExceptionInheritsNativeException()
    {
        var ex = new VirtualCameraException("test");
        Assert.That(ex, Is.InstanceOf<NativeException>());
    }

    // --- Метаданные камеры ---

    [TestCase(TestName = "Метаданные: null по умолчанию")]
    public void MetadataDefaultsAreNull()
    {
        var settings = new VirtualCameraSettings { Width = 640, Height = 480 };

        Assert.That(settings.Vendor, Is.Null);
        Assert.That(settings.Model, Is.Null);
        Assert.That(settings.SerialNumber, Is.Null);
        Assert.That(settings.Description, Is.Null);
        Assert.That(settings.FirmwareVersion, Is.Null);
        Assert.That(settings.UsbVendorId, Is.Null);
        Assert.That(settings.UsbProductId, Is.Null);
        Assert.That(settings.BusType, Is.Null);
        Assert.That(settings.FormFactor, Is.Null);
        Assert.That(settings.IconName, Is.Null);
        Assert.That(settings.ExtraProperties, Is.Null);
    }

    [TestCase(TestName = "Метаданные: кастомные значения сохраняются")]
    public void MetadataCustomValues()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 1920,
            Height = 1080,
            Vendor = "Escorp Dynamics",
            Model = "Atom VCam Pro",
            SerialNumber = "SN-2026-001",
            Description = "Виртуальная камера для стриминга",
            FirmwareVersion = "1.0.0",
            UsbVendorId = 0x046D,
            UsbProductId = 0x0825,
            BusType = "usb",
            FormFactor = "webcam",
            IconName = "camera-web",
        };

        Assert.That(settings.Vendor, Is.EqualTo("Escorp Dynamics"));
        Assert.That(settings.Model, Is.EqualTo("Atom VCam Pro"));
        Assert.That(settings.SerialNumber, Is.EqualTo("SN-2026-001"));
        Assert.That(settings.Description, Is.EqualTo("Виртуальная камера для стриминга"));
        Assert.That(settings.FirmwareVersion, Is.EqualTo("1.0.0"));
        Assert.That(settings.UsbVendorId, Is.EqualTo(0x046D));
        Assert.That(settings.UsbProductId, Is.EqualTo(0x0825));
        Assert.That(settings.BusType, Is.EqualTo("usb"));
        Assert.That(settings.FormFactor, Is.EqualTo("webcam"));
        Assert.That(settings.IconName, Is.EqualTo("camera-web"));
    }

    [TestCase(TestName = "Метаданные: USB VID/PID корректно хранят граничные значения")]
    public void UsbVidPidValues()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 640,
            Height = 480,
            UsbVendorId = 0xFFFF,
            UsbProductId = 0x0000,
        };

        Assert.That(settings.UsbVendorId, Is.EqualTo(0xFFFF));
        Assert.That(settings.UsbProductId, Is.Zero);
    }

    [TestCase(TestName = "Метаданные: ExtraProperties передаёт произвольные свойства")]
    public void ExtraPropertiesValues()
    {
        var extras = new Dictionary<string, string>
        {
            ["custom.property"] = "custom-value",
            ["node.latency"] = "256/48000",
        };

        var settings = new VirtualCameraSettings
        {
            Width = 640,
            Height = 480,
            ExtraProperties = extras,
        };

        Assert.That(settings.ExtraProperties, Is.Not.Null);
        Assert.That(settings.ExtraProperties, Has.Count.EqualTo(2));
        Assert.That(settings.ExtraProperties!["custom.property"], Is.EqualTo("custom-value"));
        Assert.That(settings.ExtraProperties["node.latency"], Is.EqualTo("256/48000"));
    }

    [TestCase(TestName = "Метаданные: record equality учитывает метаданные")]
    public void MetadataAffectsEquality()
    {
        var a = new VirtualCameraSettings { Width = 640, Height = 480, Vendor = "A" };
        var b = new VirtualCameraSettings { Width = 640, Height = 480, Vendor = "A" };
        var c = new VirtualCameraSettings { Width = 640, Height = 480, Vendor = "B" };

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Is.Not.EqualTo(c));
    }

    [TestCase(TestName = "Метаданные: with-выражение копирует метаданные")]
    public void MetadataWithExpression()
    {
        var original = new VirtualCameraSettings
        {
            Width = 640,
            Height = 480,
            Vendor = "Escorp",
            Model = "V1",
        };

        var modified = original with { Model = "V2" };

        Assert.That(modified.Vendor, Is.EqualTo("Escorp"));
        Assert.That(modified.Model, Is.EqualTo("V2"));
        Assert.That(original.Model, Is.EqualTo("V1"));
    }

    [TestCase(TestName = "Метаданные: частичное заполнение — остальные null")]
    public void MetadataPartialFill()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 640,
            Height = 480,
            Vendor = "Test",
        };

        Assert.That(settings.Vendor, Is.EqualTo("Test"));
        Assert.That(settings.Model, Is.Null);
        Assert.That(settings.SerialNumber, Is.Null);
        Assert.That(settings.Description, Is.Null);
        Assert.That(settings.FirmwareVersion, Is.Null);
    }

    // --- VirtualCamera.CreateAsync ---

    [TestCase(TestName = "CreateAsync: null настройки выбрасывают ArgumentNullException")]
    public void CreateAsyncNullSettingsThrows()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await VirtualCamera.CreateAsync(settings: null!));
    }

    // --- CameraControlType enum ---

    [TestCase(TestName = "CameraControlType: все 8 значений определены")]
    public void CameraControlTypeValues()
    {
        var values = Enum.GetValues<CameraControlType>();
        Assert.That(values, Has.Length.EqualTo(8));
    }

    [TestCase(TestName = "CameraControlType: значения уникальны")]
    public void CameraControlTypeUnique()
    {
        var values = Enum.GetValues<CameraControlType>().Cast<int>().ToArray();
        Assert.That(values, Is.Unique);
    }
}
