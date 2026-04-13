using System.Drawing;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает полную модель browser fingerprint для подмены окружения страницы.
/// </summary>
public class Device
{
    private ClientHintsSettings? clientHints;
    private bool isMobile;
    private ScreenSettings? screen;
    private Size viewportSize;

    /// <summary>
    /// Получает набор предопределённых устройств.
    /// </summary>
    public static IEnumerable<Device> All =>
    [
        Pixel2,
        Pixel7,
        iPhoneX,
        iPhone14Pro,
        iPad,
        MacBookPro14,
        DesktopFullHd,
    ];

    /// <summary>
    /// Получает конфигурацию устройства Pixel 2.
    /// </summary>
    public static Device Pixel2 => new()
    {
        Name = "Pixel 2",
        ViewportSize = new Size(411, 731),
        DeviceScaleFactor = 2.625,
        IsMobile = true,
        HasTouch = true,
        UserAgent = "Mozilla/5.0 (Linux; Android 11; Pixel 2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36",
        Platform = "Linux armv8l",
        Locale = "en-US",
        Timezone = "America/New_York",
        Languages = ["en-US", "en"],
        Screen = new ScreenSettings
        {
            Width = 411,
            Height = 731,
            AvailWidth = 411,
            AvailHeight = 731,
            ColorDepth = 24,
            PixelDepth = 24,
        },
        ClientHints = new ClientHintsSettings
        {
            Platform = "Android",
            PlatformVersion = "11.0.0",
            Mobile = true,
            Model = "Pixel 2",
            Architecture = "arm",
            Bitness = "64",
            Brands = [new("Chromium", "131"), new("Not_A Brand", "24")],
        },
        NetworkInfo = new NetworkInfoSettings
        {
            EffectiveType = "4g",
            Type = "cellular",
            Downlink = 10,
            Rtt = 75,
        },
        WebGL = new WebGLSettings
        {
            Vendor = "Google Inc. (ARM)",
            Renderer = "ANGLE (ARM, Mali-G78)",
            UnmaskedVendor = "ARM",
            UnmaskedRenderer = "Mali-G78",
        },
        Geolocation = new GeolocationSettings
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Accuracy = 25,
        },
        SpeechVoices =
        [
            new SpeechVoiceSettings { Name = "Android English", Lang = "en-US", VoiceUri = new Uri("urn:voice:android-en-us"), IsDefault = true },
        ],
        HardwareConcurrency = 8,
        DeviceMemory = 4,
        MaxTouchPoints = 5,
        ScreenOrientation = "portrait-primary",
        ColorScheme = "light",
        ReducedMotion = false,
        DoNotTrack = false,
        GlobalPrivacyControl = false,
        IntlSpoofing = true,
        CanvasNoise = true,
        AudioNoise = true,
        FontFiltering = true,
        TimerPrecisionMilliseconds = 1,
        BatteryCharging = true,
        BatteryLevel = 1,
    };

    /// <summary>
    /// Получает конфигурацию устройства Pixel 7.
    /// </summary>
    public static Device Pixel7 => new()
    {
        Name = "Pixel 7",
        ViewportSize = new Size(412, 915),
        DeviceScaleFactor = 2.625,
        IsMobile = true,
        HasTouch = true,
        UserAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36",
        Platform = "Linux armv8l",
        Locale = "en-US",
        Timezone = "America/New_York",
        Languages = ["en-US", "en"],
        Screen = new ScreenSettings
        {
            Width = 412,
            Height = 915,
            AvailWidth = 412,
            AvailHeight = 915,
            ColorDepth = 24,
            PixelDepth = 24,
        },
        ClientHints = new ClientHintsSettings
        {
            Platform = "Android",
            PlatformVersion = "14.0.0",
            Mobile = true,
            Model = "Pixel 7",
            Architecture = "arm",
            Bitness = "64",
            Brands = [new("Chromium", "131"), new("Not_A Brand", "24")],
        },
        NetworkInfo = new NetworkInfoSettings
        {
            EffectiveType = "5g",
            Type = "cellular",
            Downlink = 25,
            Rtt = 40,
        },
        WebGL = new WebGLSettings
        {
            Vendor = "Google Inc. (ARM)",
            Renderer = "ANGLE (ARM, Mali-G710)",
            UnmaskedVendor = "ARM",
            UnmaskedRenderer = "Mali-G710",
        },
        Geolocation = new GeolocationSettings
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Accuracy = 20,
        },
        SpeechVoices =
        [
            new SpeechVoiceSettings { Name = "Android English", Lang = "en-US", VoiceUri = new Uri("urn:voice:android-en-us"), IsDefault = true },
        ],
        HardwareConcurrency = 8,
        DeviceMemory = 8,
        MaxTouchPoints = 5,
        ScreenOrientation = "portrait-primary",
        ColorScheme = "light",
        ReducedMotion = false,
        DoNotTrack = false,
        GlobalPrivacyControl = false,
        IntlSpoofing = true,
        CanvasNoise = true,
        AudioNoise = true,
        FontFiltering = true,
        TimerPrecisionMilliseconds = 1,
        BatteryCharging = true,
        BatteryLevel = 1,
    };

#pragma warning disable IDE1006
    /// <summary>
    /// Получает конфигурацию устройства iPhone X.
    /// </summary>
    public static Device iPhoneX => new()
    {
        Name = "iPhone X",
        ViewportSize = new Size(375, 812),
        DeviceScaleFactor = 3,
        IsMobile = true,
        HasTouch = true,
        UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 16_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.0 Mobile/15E148 Safari/604.1",
        Platform = "iPhone",
        Locale = "en-US",
        Timezone = "America/New_York",
        Languages = ["en-US", "en"],
        Screen = new ScreenSettings
        {
            Width = 375,
            Height = 812,
            AvailWidth = 375,
            AvailHeight = 812,
            ColorDepth = 24,
            PixelDepth = 24,
        },
        ClientHints = new ClientHintsSettings
        {
            Platform = "iOS",
            PlatformVersion = "16.0.0",
            Mobile = true,
            Model = "iPhone",
        },
        NetworkInfo = new NetworkInfoSettings
        {
            EffectiveType = "4g",
            Type = "cellular",
            Downlink = 8,
            Rtt = 85,
        },
        WebGL = new WebGLSettings
        {
            Vendor = "Apple Inc.",
            Renderer = "Apple GPU",
            UnmaskedVendor = "Apple",
            UnmaskedRenderer = "Apple GPU",
        },
        Geolocation = new GeolocationSettings
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Accuracy = 25,
        },
        SpeechVoices =
        [
            new SpeechVoiceSettings { Name = "Samantha", Lang = "en-US", VoiceUri = new Uri("urn:voice:apple-samantha"), IsDefault = true },
        ],
        HardwareConcurrency = 6,
        DeviceMemory = 3,
        MaxTouchPoints = 5,
        ScreenOrientation = "portrait-primary",
        ColorScheme = "light",
        ReducedMotion = false,
        DoNotTrack = false,
        GlobalPrivacyControl = false,
        IntlSpoofing = true,
        CanvasNoise = true,
        AudioNoise = true,
        FontFiltering = true,
        TimerPrecisionMilliseconds = 1,
        BatteryCharging = true,
        BatteryLevel = 1,
    };

    /// <summary>
    /// Получает конфигурацию устройства iPhone 14 Pro.
    /// </summary>
    public static Device iPhone14Pro => new()
    {
        Name = "iPhone 14 Pro",
        ViewportSize = new Size(393, 852),
        DeviceScaleFactor = 3,
        IsMobile = true,
        HasTouch = true,
        UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1",
        Platform = "iPhone",
        Locale = "en-US",
        Timezone = "America/New_York",
        Languages = ["en-US", "en"],
        Screen = new ScreenSettings
        {
            Width = 393,
            Height = 852,
            AvailWidth = 393,
            AvailHeight = 852,
            ColorDepth = 24,
            PixelDepth = 24,
        },
        ClientHints = new ClientHintsSettings
        {
            Platform = "iOS",
            PlatformVersion = "17.0.0",
            Mobile = true,
            Model = "iPhone",
        },
        NetworkInfo = new NetworkInfoSettings
        {
            EffectiveType = "5g",
            Type = "cellular",
            Downlink = 20,
            Rtt = 45,
        },
        WebGL = new WebGLSettings
        {
            Vendor = "Apple Inc.",
            Renderer = "Apple GPU",
            UnmaskedVendor = "Apple",
            UnmaskedRenderer = "Apple GPU",
        },
        Geolocation = new GeolocationSettings
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Accuracy = 20,
        },
        SpeechVoices =
        [
            new SpeechVoiceSettings { Name = "Samantha", Lang = "en-US", VoiceUri = new Uri("urn:voice:apple-samantha"), IsDefault = true },
        ],
        HardwareConcurrency = 6,
        DeviceMemory = 6,
        MaxTouchPoints = 5,
        ScreenOrientation = "portrait-primary",
        ColorScheme = "light",
        ReducedMotion = false,
        DoNotTrack = false,
        GlobalPrivacyControl = false,
        IntlSpoofing = true,
        CanvasNoise = true,
        AudioNoise = true,
        FontFiltering = true,
        TimerPrecisionMilliseconds = 1,
        BatteryCharging = true,
        BatteryLevel = 1,
    };

    /// <summary>
    /// Получает конфигурацию устройства iPad.
    /// </summary>
    public static Device iPad => new()
    {
        Name = "iPad",
        ViewportSize = new Size(768, 1024),
        DeviceScaleFactor = 2,
        IsMobile = true,
        HasTouch = true,
        UserAgent = "Mozilla/5.0 (iPad; CPU OS 16_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.0 Mobile/15E148 Safari/604.1",
        Platform = "iPad",
        Locale = "en-US",
        Timezone = "America/New_York",
        Languages = ["en-US", "en"],
        Screen = new ScreenSettings
        {
            Width = 768,
            Height = 1024,
            AvailWidth = 768,
            AvailHeight = 1024,
            ColorDepth = 24,
            PixelDepth = 24,
        },
        ClientHints = new ClientHintsSettings
        {
            Platform = "iOS",
            PlatformVersion = "16.0.0",
            Mobile = true,
            Model = "iPad",
        },
        NetworkInfo = new NetworkInfoSettings
        {
            EffectiveType = "wifi",
            Type = "wifi",
            Downlink = 30,
            Rtt = 20,
        },
        WebGL = new WebGLSettings
        {
            Vendor = "Apple Inc.",
            Renderer = "Apple GPU",
            UnmaskedVendor = "Apple",
            UnmaskedRenderer = "Apple GPU",
        },
        Geolocation = new GeolocationSettings
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Accuracy = 30,
        },
        SpeechVoices =
        [
            new SpeechVoiceSettings { Name = "Samantha", Lang = "en-US", VoiceUri = new Uri("urn:voice:apple-samantha"), IsDefault = true },
        ],
        HardwareConcurrency = 8,
        DeviceMemory = 4,
        MaxTouchPoints = 5,
        ScreenOrientation = "portrait-primary",
        ColorScheme = "light",
        ReducedMotion = false,
        DoNotTrack = false,
        GlobalPrivacyControl = false,
        IntlSpoofing = true,
        CanvasNoise = true,
        AudioNoise = true,
        FontFiltering = true,
        TimerPrecisionMilliseconds = 1,
        BatteryCharging = true,
        BatteryLevel = 1,
    };
#pragma warning restore IDE1006

    /// <summary>
    /// Получает конфигурацию устройства MacBook Pro 14.
    /// </summary>
    public static Device MacBookPro14 => new()
    {
        Name = "MacBook Pro 14",
        ViewportSize = new Size(1512, 982),
        DeviceScaleFactor = 2,
        IsMobile = false,
        HasTouch = false,
        UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        Platform = "MacIntel",
        Locale = "en-US",
        Timezone = "America/New_York",
        Languages = ["en-US", "en"],
        Screen = new ScreenSettings
        {
            Width = 1512,
            Height = 982,
            AvailWidth = 1512,
            AvailHeight = 957,
            ColorDepth = 24,
            PixelDepth = 24,
        },
        ClientHints = new ClientHintsSettings
        {
            Platform = "macOS",
            PlatformVersion = "14.0.0",
            Mobile = false,
            Architecture = "x86",
            Bitness = "64",
            Brands = [new("Chromium", "131"), new("Not_A Brand", "24")],
        },
        NetworkInfo = new NetworkInfoSettings
        {
            EffectiveType = "wifi",
            Type = "wifi",
            Downlink = 100,
            Rtt = 10,
        },
        WebGL = new WebGLSettings
        {
            Vendor = "Google Inc. (Apple)",
            Renderer = "ANGLE (Apple, Apple M1 Pro)",
            UnmaskedVendor = "Apple",
            UnmaskedRenderer = "Apple M1 Pro",
        },
        WebGLParams = new WebGLParamsSettings
        {
            MaxTextureSize = 16384,
            MaxRenderbufferSize = 16384,
            MaxViewportDims = [16384, 16384],
            MaxVaryingVectors = 30,
            MaxVertexUniformVectors = 1024,
            MaxFragmentUniformVectors = 1024,
        },
        Geolocation = new GeolocationSettings
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Accuracy = 50,
        },
        SpeechVoices =
        [
            new SpeechVoiceSettings { Name = "Samantha", Lang = "en-US", VoiceUri = new Uri("urn:voice:apple-samantha"), IsDefault = true },
            new SpeechVoiceSettings { Name = "Alex", Lang = "en-US", VoiceUri = new Uri("urn:voice:apple-alex") },
        ],
        HardwareConcurrency = 10,
        DeviceMemory = 16,
        MaxTouchPoints = 0,
        ScreenOrientation = "landscape-primary",
        ColorScheme = "light",
        ReducedMotion = false,
        DoNotTrack = false,
        GlobalPrivacyControl = false,
        IntlSpoofing = true,
        CanvasNoise = true,
        AudioNoise = true,
        FontFiltering = true,
        TimerPrecisionMilliseconds = 0.5,
        BatteryCharging = true,
        BatteryLevel = 1,
    };

    /// <summary>
    /// Получает универсальную конфигурацию desktop Full HD.
    /// </summary>
    public static Device DesktopFullHd => new()
    {
        Name = "Desktop Full HD",
        ViewportSize = new Size(1920, 1080),
        DeviceScaleFactor = 1,
        IsMobile = false,
        HasTouch = false,
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        Platform = "Win32",
        Locale = "en-US",
        Timezone = "America/New_York",
        Languages = ["en-US", "en"],
        Screen = new ScreenSettings
        {
            Width = 1920,
            Height = 1080,
            AvailWidth = 1920,
            AvailHeight = 1040,
            ColorDepth = 24,
            PixelDepth = 24,
        },
        ClientHints = new ClientHintsSettings
        {
            Platform = "Windows",
            PlatformVersion = "10.0.0",
            Mobile = false,
            Architecture = "x86",
            Bitness = "64",
            Brands = [new("Chromium", "131"), new("Not_A Brand", "24")],
        },
        NetworkInfo = new NetworkInfoSettings
        {
            EffectiveType = "ethernet",
            Type = "ethernet",
            Downlink = 100,
            Rtt = 8,
        },
        WebGL = new WebGLSettings
        {
            Vendor = "Google Inc. (NVIDIA)",
            Renderer = "ANGLE (NVIDIA, NVIDIA GeForce RTX 3070)",
            UnmaskedVendor = "NVIDIA",
            UnmaskedRenderer = "NVIDIA GeForce RTX 3070",
        },
        WebGLParams = new WebGLParamsSettings
        {
            MaxTextureSize = 16384,
            MaxRenderbufferSize = 16384,
            MaxViewportDims = [16384, 16384],
            MaxVaryingVectors = 30,
            MaxVertexUniformVectors = 1024,
            MaxFragmentUniformVectors = 1024,
        },
        Geolocation = new GeolocationSettings
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Accuracy = 50,
        },
        SpeechVoices =
        [
            new SpeechVoiceSettings { Name = "Microsoft David", Lang = "en-US", VoiceUri = new Uri("urn:voice:microsoft-david"), IsDefault = true },
            new SpeechVoiceSettings { Name = "Microsoft Zira", Lang = "en-US", VoiceUri = new Uri("urn:voice:microsoft-zira") },
        ],
        HardwareConcurrency = 8,
        DeviceMemory = 16,
        MaxTouchPoints = 0,
        ScreenOrientation = "landscape-primary",
        ColorScheme = "light",
        ReducedMotion = false,
        DoNotTrack = false,
        GlobalPrivacyControl = false,
        IntlSpoofing = true,
        CanvasNoise = true,
        AudioNoise = true,
        FontFiltering = true,
        TimerPrecisionMilliseconds = 0.5,
        BatteryCharging = true,
        BatteryLevel = 1,
    };

    /// <summary>
    /// Получает или задаёт имя устройства.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Получает или задаёт размер viewport в CSS-пикселях.
    /// </summary>
    public Size ViewportSize
    {
        get => screen is { Width: > 0, Height: > 0 }
            ? new Size(screen.Width.Value, screen.Height.Value)
            : viewportSize;
        set
        {
            viewportSize = value;

            if (value.IsEmpty)
            {
                return;
            }

            screen ??= new ScreenSettings();
            screen.Width = value.Width;
            screen.Height = value.Height;
            screen.AvailWidth ??= value.Width;
            screen.AvailHeight ??= value.Height;
        }
    }

    /// <summary>
    /// Получает или задаёт коэффициент масштабирования устройства.
    /// </summary>
    public double DeviceScaleFactor { get; set; }

    /// <summary>
    /// Получает или задаёт признак мобильного устройства.
    /// </summary>
    public bool IsMobile
    {
        get => clientHints?.Mobile ?? isMobile;
        set
        {
            isMobile = value;
            clientHints ??= new ClientHintsSettings();
            clientHints.Mobile = value;
        }
    }

    /// <summary>
    /// Получает или задаёт признак наличия сенсорного ввода.
    /// </summary>
    public bool HasTouch { get; set; }

    /// <summary>
    /// Получает или задаёт User-Agent устройства.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Получает или задаёт значение navigator.platform.
    /// </summary>
    public string? Platform { get; set; }

    /// <summary>
    /// Получает или задаёт локаль браузера.
    /// </summary>
    public string? Locale { get; set; }

    /// <summary>
    /// Получает или задаёт IANA-часовой пояс.
    /// </summary>
    public string? Timezone { get; set; }

    /// <summary>
    /// Получает или задаёт список предпочитаемых языков браузера.
    /// </summary>
    public IEnumerable<string>? Languages { get; set; }

    /// <summary>
    /// Получает или задаёт модель экрана и viewport-свойств.
    /// </summary>
    public ScreenSettings? Screen
    {
        get => screen;
        set
        {
            screen = value;
            if (value is { Width: > 0, Height: > 0 })
            {
                viewportSize = new Size(value.Width.Value, value.Height.Value);
            }
        }
    }

    /// <summary>
    /// Получает или задаёт координаты и точность геолокации.
    /// </summary>
    public GeolocationSettings? Geolocation { get; set; }

    /// <summary>
    /// Получает или задаёт low/high entropy client hints.
    /// </summary>
    public ClientHintsSettings? ClientHints
    {
        get => clientHints;
        set
        {
            clientHints = value;
            if (value?.Mobile is bool mobile)
            {
                isMobile = mobile;
            }
        }
    }

    /// <summary>
    /// Получает или задаёт модель navigator.connection.
    /// </summary>
    public NetworkInfoSettings? NetworkInfo { get; set; }

    /// <summary>
    /// Получает или задаёт вендора и renderer для WebGL.
    /// </summary>
    public WebGLSettings? WebGL { get; set; }

    /// <summary>
    /// Получает или задаёт расширенные лимиты WebGL.
    /// </summary>
    public WebGLParamsSettings? WebGLParams { get; set; }

    /// <summary>
    /// Получает или задаёт список голосов Speech Synthesis.
    /// </summary>
    public IEnumerable<SpeechVoiceSettings>? SpeechVoices { get; set; }

    /// <summary>
    /// Получает или задаёт таб-локальную модель media devices.
    /// </summary>
    public VirtualMediaDevicesSettings? VirtualMediaDevices { get; set; }

    /// <summary>
    /// Получает или задаёт navigator.hardwareConcurrency.
    /// </summary>
    public int? HardwareConcurrency { get; set; }

    /// <summary>
    /// Получает или задаёт число виртуальных CPU.
    /// </summary>
    public int CpuCount
    {
        get => HardwareConcurrency ?? 0;
        set => HardwareConcurrency = value > 0 ? value : null;
    }

    /// <summary>
    /// Получает или задаёт navigator.deviceMemory в гигабайтах.
    /// </summary>
    public double? DeviceMemory { get; set; }

    /// <summary>
    /// Получает или задаёт объём памяти устройства в гигабайтах.
    /// </summary>
    public int MemorySize
    {
        get => DeviceMemory is > 0 ? (int)Math.Round(DeviceMemory.Value) : 0;
        set => DeviceMemory = value > 0 ? value : null;
    }

    /// <summary>
    /// Получает или задаёт navigator.maxTouchPoints.
    /// </summary>
    public int MaxTouchPoints { get; set; }

    /// <summary>
    /// Получает или задаёт ориентацию экрана.
    /// </summary>
    public string? ScreenOrientation { get; set; }

    /// <summary>
    /// Получает или задаёт prefers-color-scheme.
    /// </summary>
    public string? ColorScheme { get; set; }

    /// <summary>
    /// Получает или задаёт prefers-reduced-motion.
    /// </summary>
    public bool? ReducedMotion { get; set; }

    /// <summary>
    /// Получает или задаёт navigator.doNotTrack.
    /// </summary>
    public bool? DoNotTrack { get; set; }

    /// <summary>
    /// Получает или задаёт navigator.globalPrivacyControl.
    /// </summary>
    public bool? GlobalPrivacyControl { get; set; }

    /// <summary>
    /// Получает или задаёт признак подмены Intl.* под локаль устройства.
    /// </summary>
    public bool IntlSpoofing { get; set; }

    /// <summary>
    /// Получает или задаёт признак детерминированного canvas-noise.
    /// </summary>
    public bool CanvasNoise { get; set; }

    /// <summary>
    /// Получает или задаёт признак детерминированного audio-noise.
    /// </summary>
    public bool AudioNoise { get; set; }

    /// <summary>
    /// Получает или задаёт признак фильтрации font fingerprint.
    /// </summary>
    public bool FontFiltering { get; set; }

    /// <summary>
    /// Получает или задаёт шаг округления таймеров в миллисекундах.
    /// </summary>
    public double? TimerPrecisionMilliseconds { get; set; }

    /// <summary>
    /// Получает или задаёт состояние батареи.
    /// </summary>
    public bool? BatteryCharging { get; set; }

    /// <summary>
    /// Получает или задаёт уровень заряда батареи в диапазоне [0, 1].
    /// </summary>
    public double? BatteryLevel { get; set; }
}