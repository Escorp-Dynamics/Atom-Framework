using System.Drawing;
using Atom.Hardware.Input;

namespace Atom.Net.Browsing.WebDriver;

internal static class SettingsExtensions
{
    internal static NavigationSettings Clone(this NavigationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new NavigationSettings
        {
            Kind = settings.Kind,
            Headers = settings.Headers is null ? null : new Dictionary<string, string>(settings.Headers, StringComparer.OrdinalIgnoreCase),
            Proxy = settings.Proxy,
            Body = settings.Body,
            Html = settings.Html,
        };
    }

    internal static WebBrowserSettings Clone(this WebBrowserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new WebBrowserSettings
        {
            Profile = settings.Profile,
            Proxy = settings.Proxy,
            Logger = settings.Logger,
            Display = settings.Display,
            Mouse = settings.Mouse,
            Keyboard = settings.Keyboard,
            UseHeadlessMode = settings.UseHeadlessMode,
            UseIncognitoMode = settings.UseIncognitoMode,
            UseRootlessChromiumBootstrap = settings.UseRootlessChromiumBootstrap,
            Position = settings.Position,
            Size = settings.Size,
            Args = settings.Args is null ? null : [.. settings.Args],
            Device = settings.Device.Clone(),
        };
    }

    internal static WebWindowSettings? Clone(this WebWindowSettings? settings)
    {
        if (settings is null)
            return null;

        return new WebWindowSettings
        {
            Proxy = settings.Proxy,
            UseProxy = settings.UseProxy,
            Mouse = settings.Mouse,
            Keyboard = settings.Keyboard,
            Size = settings.Size,
            Position = settings.Position,
            Device = settings.Device.Clone(),
        };
    }

    internal static WebPageSettings? Clone(this WebPageSettings? settings)
    {
        if (settings is null)
            return null;

        return new WebPageSettings
        {
            Proxy = settings.Proxy,
            UseProxy = settings.UseProxy,
            Mouse = settings.Mouse,
            Keyboard = settings.Keyboard,
            Device = settings.Device.Clone(),
        };
    }

    internal static Device? ResolveDevice(this Device? inheritedDevice, Device? scopedDevice)
        => (scopedDevice ?? inheritedDevice).Clone();

    internal static Size ResolveWindowSize(this WebBrowserSettings browserSettings, WebWindowSettings? windowSettings)
    {
        ArgumentNullException.ThrowIfNull(browserSettings);
        return windowSettings?.Size ?? browserSettings.Size;
    }

    internal static Point ResolveWindowPosition(this WebBrowserSettings browserSettings, WebWindowSettings? windowSettings)
    {
        ArgumentNullException.ThrowIfNull(browserSettings);
        return windowSettings?.Position ?? browserSettings.Position;
    }

    internal static Device? Clone(this Device? source)
    {
        if (source is null)
            return null;

        return new Device
        {
            Name = source.Name,
            ViewportSize = source.ViewportSize,
            DeviceScaleFactor = source.DeviceScaleFactor,
            IsMobile = source.IsMobile,
            HasTouch = source.HasTouch,
            UserAgent = source.UserAgent,
            Platform = source.Platform,
            Locale = source.Locale,
            Timezone = source.Timezone,
            Languages = source.Languages is null ? null : [.. source.Languages],
            Screen = source.Screen.Clone(),
            Geolocation = source.Geolocation.Clone(),
            ClientHints = source.ClientHints.Clone(),
            NetworkInfo = source.NetworkInfo.Clone(),
            WebGL = source.WebGL.Clone(),
            WebGLParams = source.WebGLParams.Clone(),
            SpeechVoices = source.SpeechVoices is null ? null : [.. source.SpeechVoices.Select(static voice => voice.Clone())],
            VirtualMediaDevices = source.VirtualMediaDevices.Clone(),
            HardwareConcurrency = source.HardwareConcurrency,
            CpuCount = source.CpuCount,
            DeviceMemory = source.DeviceMemory,
            MemorySize = source.MemorySize,
            MaxTouchPoints = source.MaxTouchPoints,
            ScreenOrientation = source.ScreenOrientation,
            ColorScheme = source.ColorScheme,
            ReducedMotion = source.ReducedMotion,
            DoNotTrack = source.DoNotTrack,
            GlobalPrivacyControl = source.GlobalPrivacyControl,
            IntlSpoofing = source.IntlSpoofing,
            CanvasNoise = source.CanvasNoise,
            AudioNoise = source.AudioNoise,
            FontFiltering = source.FontFiltering,
            TimerPrecisionMilliseconds = source.TimerPrecisionMilliseconds,
            BatteryCharging = source.BatteryCharging,
            BatteryLevel = source.BatteryLevel,
        };
    }

    private static ScreenSettings? Clone(this ScreenSettings? source)
    {
        if (source is null)
            return null;

        return new ScreenSettings
        {
            Width = source.Width,
            Height = source.Height,
            AvailWidth = source.AvailWidth,
            AvailHeight = source.AvailHeight,
            ColorDepth = source.ColorDepth,
            PixelDepth = source.PixelDepth,
        };
    }

    private static GeolocationSettings? Clone(this GeolocationSettings? source)
    {
        if (source is null)
            return null;

        return new GeolocationSettings
        {
            Latitude = source.Latitude,
            Longitude = source.Longitude,
            Accuracy = source.Accuracy,
        };
    }

    private static ClientHintsSettings? Clone(this ClientHintsSettings? source)
    {
        if (source is null)
            return null;

        return new ClientHintsSettings
        {
            Brands = source.Brands is null ? null : [.. source.Brands.Select(static brand => brand.Clone())],
            FullVersionList = source.FullVersionList is null ? null : [.. source.FullVersionList.Select(static brand => brand.Clone())],
            Platform = source.Platform,
            PlatformVersion = source.PlatformVersion,
            Mobile = source.Mobile,
            Architecture = source.Architecture,
            Model = source.Model,
            Bitness = source.Bitness,
        };
    }

    private static ClientHintBrand Clone(this ClientHintBrand source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ClientHintBrand(source.Brand, source.Version);
    }

    private static NetworkInfoSettings? Clone(this NetworkInfoSettings? source)
    {
        if (source is null)
            return null;

        return new NetworkInfoSettings
        {
            EffectiveType = source.EffectiveType,
            Type = source.Type,
            Rtt = source.Rtt,
            Downlink = source.Downlink,
            EnableDataSaving = source.EnableDataSaving,
        };
    }

    private static WebGLSettings? Clone(this WebGLSettings? source)
    {
        if (source is null)
            return null;

        return new WebGLSettings
        {
            Vendor = source.Vendor,
            Renderer = source.Renderer,
            UnmaskedVendor = source.UnmaskedVendor,
            UnmaskedRenderer = source.UnmaskedRenderer,
            Version = source.Version,
            ShadingLanguageVersion = source.ShadingLanguageVersion,
        };
    }

    private static WebGLParamsSettings? Clone(this WebGLParamsSettings? source)
    {
        if (source is null)
            return null;

        return new WebGLParamsSettings
        {
            MaxTextureSize = source.MaxTextureSize,
            MaxRenderbufferSize = source.MaxRenderbufferSize,
            MaxViewportDims = source.MaxViewportDims is null ? null : [.. source.MaxViewportDims],
            MaxVaryingVectors = source.MaxVaryingVectors,
            MaxVertexUniformVectors = source.MaxVertexUniformVectors,
            MaxFragmentUniformVectors = source.MaxFragmentUniformVectors,
        };
    }

    private static SpeechVoiceSettings Clone(this SpeechVoiceSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SpeechVoiceSettings
        {
            Name = source.Name,
            Lang = source.Lang,
            VoiceUri = source.VoiceUri,
            UseLocalService = source.UseLocalService,
            IsDefault = source.IsDefault,
        };
    }

    private static VirtualMediaDevicesSettings? Clone(this VirtualMediaDevicesSettings? source)
    {
        if (source is null)
            return null;

        return new VirtualMediaDevicesSettings
        {
            AudioInputEnabled = source.AudioInputEnabled,
            AudioInputLabel = source.AudioInputLabel,
            AudioInputBrowserDeviceId = source.AudioInputBrowserDeviceId,
            VideoInputEnabled = source.VideoInputEnabled,
            VideoInputLabel = source.VideoInputLabel,
            VideoInputBrowserDeviceId = source.VideoInputBrowserDeviceId,
            AudioOutputEnabled = source.AudioOutputEnabled,
            AudioOutputLabel = source.AudioOutputLabel,
            GroupId = source.GroupId,
        };
    }
}