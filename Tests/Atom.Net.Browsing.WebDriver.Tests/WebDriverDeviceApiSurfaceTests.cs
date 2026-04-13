using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[Category("PublicApi")]
public sealed class WebDriverDeviceApiSurfaceTests
{
    [Test]
    public void DevicePresetCatalogSurfaceSpecTest()
    {
        var device = typeof(Device);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireProperty(device, nameof(Device.All));
            PublicApiAssert.RequireProperty(device, nameof(Device.Pixel2));
            PublicApiAssert.RequireProperty(device, nameof(Device.Pixel7));
            PublicApiAssert.RequireProperty(device, nameof(Device.iPhoneX));
            PublicApiAssert.RequireProperty(device, nameof(Device.iPhone14Pro));
            PublicApiAssert.RequireProperty(device, nameof(Device.iPad));
            PublicApiAssert.RequireProperty(device, nameof(Device.MacBookPro14));
            PublicApiAssert.RequireProperty(device, nameof(Device.DesktopFullHd));
        });
    }

    [Test]
    public void DeviceFingerprintSurfaceSpecTest()
    {
        var device = typeof(Device);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireProperty(device, nameof(Device.Name));
            PublicApiAssert.RequireProperty(device, nameof(Device.ViewportSize));
            PublicApiAssert.RequireProperty(device, nameof(Device.DeviceScaleFactor));
            PublicApiAssert.RequireProperty(device, nameof(Device.IsMobile));
            PublicApiAssert.RequireProperty(device, nameof(Device.HasTouch));
            PublicApiAssert.RequireProperty(device, nameof(Device.UserAgent));
            PublicApiAssert.RequireProperty(device, nameof(Device.Platform));
            PublicApiAssert.RequireProperty(device, nameof(Device.Locale));
            PublicApiAssert.RequireProperty(device, nameof(Device.Timezone));
            PublicApiAssert.RequireProperty(device, nameof(Device.Languages));
            PublicApiAssert.RequireProperty(device, nameof(Device.Screen));
            PublicApiAssert.RequireProperty(device, nameof(Device.Geolocation));
            PublicApiAssert.RequireProperty(device, nameof(Device.ClientHints));
            PublicApiAssert.RequireProperty(device, nameof(Device.NetworkInfo));
            PublicApiAssert.RequireProperty(device, nameof(Device.WebGL));
            PublicApiAssert.RequireProperty(device, nameof(Device.WebGLParams));
            PublicApiAssert.RequireProperty(device, nameof(Device.SpeechVoices));
            PublicApiAssert.RequireProperty(device, nameof(Device.VirtualMediaDevices));
            PublicApiAssert.RequireProperty(device, nameof(Device.HardwareConcurrency));
            PublicApiAssert.RequireProperty(device, nameof(Device.CpuCount));
            PublicApiAssert.RequireProperty(device, nameof(Device.DeviceMemory));
            PublicApiAssert.RequireProperty(device, nameof(Device.MemorySize));
            PublicApiAssert.RequireProperty(device, nameof(Device.MaxTouchPoints));
            PublicApiAssert.RequireProperty(device, nameof(Device.ScreenOrientation));
            PublicApiAssert.RequireProperty(device, nameof(Device.ColorScheme));
            PublicApiAssert.RequireProperty(device, nameof(Device.ReducedMotion));
            PublicApiAssert.RequireProperty(device, nameof(Device.DoNotTrack));
            PublicApiAssert.RequireProperty(device, nameof(Device.GlobalPrivacyControl));
            PublicApiAssert.RequireProperty(device, nameof(Device.IntlSpoofing));
            PublicApiAssert.RequireProperty(device, nameof(Device.CanvasNoise));
            PublicApiAssert.RequireProperty(device, nameof(Device.AudioNoise));
            PublicApiAssert.RequireProperty(device, nameof(Device.FontFiltering));
            PublicApiAssert.RequireProperty(device, nameof(Device.TimerPrecisionMilliseconds));
            PublicApiAssert.RequireProperty(device, nameof(Device.BatteryCharging));
            PublicApiAssert.RequireProperty(device, nameof(Device.BatteryLevel));
        });
    }

    [Test]
    public void GeolocationSettingsSurfaceSpecTest()
    {
        var geolocation = typeof(GeolocationSettings);

        Assert.Multiple(() =>
        {
            PublicApiAssert.RequireProperty(geolocation, nameof(GeolocationSettings.Latitude));
            PublicApiAssert.RequireProperty(geolocation, nameof(GeolocationSettings.Longitude));
            PublicApiAssert.RequireProperty(geolocation, nameof(GeolocationSettings.Accuracy));
        });
    }
}