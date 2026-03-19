namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Настройки изолированного контекста браузера.
/// </summary>
/// <remarks>
/// <para>
/// При использовании с <see cref="WebDriverBrowser.CreateContextAsync"/> создаётся
/// отдельный процесс браузера с собственным профилем (OS-level изоляция).
/// </para>
/// <para>
/// При использовании с <see cref="WebDriverBrowser.OpenIsolatedTabAsync"/> изоляция
/// обеспечивается расширением в рамках одного окна: виртуальные cookies, namespace-обёртки
/// для localStorage/sessionStorage/IndexedDB/Cache API, подмена navigator-свойств
/// и fingerprint-защита через MAIN world инъекцию.
/// </para>
/// </remarks>
public sealed class TabContextSettings
{
    /// <summary>
    /// Прокси-сервер для контекста (например, <c>"socks5://127.0.0.1:9050"</c>, <c>"http://proxy:8080"</c>).
    /// </summary>
    /// <remarks>
    /// Для <see cref="WebDriverBrowser.CreateContextAsync"/> — передаётся через CLI-аргументы.
    /// Для <see cref="WebDriverBrowser.OpenIsolatedTabAsync"/> — работает только в Firefox
    /// (через <c>proxy.onRequest</c> API). В Chromium per-tab прокси недоступен на уровне расширения.
    /// </remarks>
    public string? Proxy { get; init; }

    /// <summary>
    /// Пользовательский User-Agent.
    /// </summary>
    /// <remarks>
    /// Подменяется на двух уровнях: HTTP-заголовок (через <c>declarativeNetRequest</c> / <c>webRequest</c>)
    /// и <c>navigator.userAgent</c> (через MAIN world инъекцию).
    /// </remarks>
    public string? UserAgent { get; init; }

    /// <summary>
    /// Локаль браузера (например, <c>"en-US"</c>, <c>"ru-RU"</c>).
    /// </summary>
    public string? Locale { get; init; }

    /// <summary>
    /// Часовой пояс IANA (например, <c>"Europe/Moscow"</c>, <c>"America/New_York"</c>).
    /// Устанавливается через переменную окружения <c>TZ</c> процесса браузера.
    /// </summary>
    public string? Timezone { get; init; }

    /// <summary>
    /// Платформа для <c>navigator.platform</c> (например, <c>"Win32"</c>, <c>"Linux x86_64"</c>).
    /// </summary>
    public string? Platform { get; init; }

    /// <summary>
    /// Список языков для <c>navigator.languages</c> (например, <c>["en-US", "en"]</c>).
    /// </summary>
    public IReadOnlyList<string>? Languages { get; init; }

    /// <summary>
    /// Параметры экрана для подмены <c>screen.width</c>, <c>screen.height</c>, <c>screen.colorDepth</c>.
    /// </summary>
    public ScreenSettings? Screen { get; init; }

    /// <summary>
    /// Параметры WebGL для подмены <c>UNMASKED_VENDOR_WEBGL</c> и <c>UNMASKED_RENDERER_WEBGL</c>.
    /// </summary>
    public WebGLSettings? WebGL { get; init; }

    /// <summary>
    /// Включает детерминированный шум Canvas fingerprint.
    /// Каждый контекст получает уникальный отпечаток на основе contextId.
    /// </summary>
    public bool CanvasNoise { get; init; }

    /// <summary>
    /// Политика WebRTC: <c>"disable"</c> — полностью отключить,
    /// <c>"relay-only"</c> — разрешить только relay (TURN), предотвращая утечку IP.
    /// </summary>
    public string? WebRtcPolicy { get; init; }

    /// <summary>
    /// Начальный URL, открываемый при запуске контекста.
    /// Если не указан, используется discovery URL моста.
    /// </summary>
    public Uri? StartUrl { get; init; }

    /// <summary>
    /// Дополнительные аргументы командной строки браузера.
    /// </summary>
    public IEnumerable<string>? Arguments { get; init; }

    /// <summary>
    /// Координаты геолокации для подмены <c>navigator.geolocation</c>.
    /// </summary>
    public GeolocationSettings? Geolocation { get; init; }

    /// <summary>
    /// Список разрешённых семейств шрифтов для anti-fingerprinting.
    /// Если указан, <c>document.fonts.check()</c> будет возвращать <see langword="true"/> только для этих шрифтов.
    /// </summary>
    public IReadOnlyList<string>? AllowedFonts { get; init; }

    /// <summary>
    /// Включает шум AudioContext-fingerprint (аналогично <see cref="CanvasNoise"/>).
    /// Каждый контекст получает уникальный аудио-отпечаток на основе contextId.
    /// </summary>
    public bool AudioNoise { get; init; }

    /// <summary>
    /// Подмена <c>navigator.hardwareConcurrency</c> (число логических ядер).
    /// </summary>
    public int? HardwareConcurrency { get; init; }

    /// <summary>
    /// Подмена <c>navigator.deviceMemory</c> (ГБ оперативной памяти).
    /// </summary>
    public double? DeviceMemory { get; init; }

    /// <summary>
    /// Скрывает реальный уровень заряда батареи.
    /// <c>navigator.getBattery()</c> будет возвращать: charging=true, level=1.0.
    /// </summary>
    public bool BatteryProtection { get; init; }

    /// <summary>
    /// Все вызовы <c>navigator.permissions.query()</c> возвращают <c>"prompt"</c>
    /// для предотвращения fingerprinting через реальные permission states.
    /// </summary>
    public bool PermissionsProtection { get; init; }

    /// <summary>
    /// Подмена Client Hints (<c>navigator.userAgentData</c> и HTTP-заголовков <c>Sec-CH-UA-*</c>).
    /// </summary>
    public ClientHintsSettings? ClientHints { get; init; }

    /// <summary>
    /// Подмена Network Information API (<c>navigator.connection</c>).
    /// </summary>
    public NetworkInfoSettings? NetworkInfo { get; init; }

    /// <summary>
    /// Подмена <c>speechSynthesis.getVoices()</c> — возвращает только указанные голоса.
    /// Если указан пустой список, <c>getVoices()</c> вернёт пустой массив.
    /// </summary>
    public IReadOnlyList<SpeechVoiceSettings>? SpeechVoices { get; init; }

    /// <summary>
    /// Подмена <c>navigator.mediaDevices.enumerateDevices()</c>.
    /// Возвращает стандартный набор устройств (1 audioinput, 1 videoinput, 1 audiooutput).
    /// </summary>
    public bool MediaDevicesProtection { get; init; }

    /// <summary>
    /// Виртуальные media devices для tab-local browser injection.
    /// Позволяет подменить tab-local <c>enumerateDevices()</c> и маршрутизировать
    /// <c>getUserMedia()</c> на реальные browser-visible устройства,
    /// которыми управляет внешняя C# сторона.
    /// </summary>
    public VirtualMediaDevicesSettings? VirtualMediaDevices { get; init; }

    /// <summary>
    /// Расширенные параметры WebGL (<c>MAX_TEXTURE_SIZE</c>, <c>MAX_RENDERBUFFER_SIZE</c> и др.).
    /// </summary>
    public WebGLParamsSettings? WebGLParams { get; init; }

    /// <summary>
    /// Значение <c>navigator.doNotTrack</c> (<c>"1"</c>, <c>"0"</c>, или <see langword="null"/>).
    /// </summary>
    public string? DoNotTrack { get; init; }

    /// <summary>
    /// Значение <c>navigator.globalPrivacyControl</c>.
    /// </summary>
    public bool? GlobalPrivacyControl { get; init; }

    /// <summary>
    /// Принудительно использует подменённую локаль во всех конструкторах <c>Intl.*</c>
    /// (<c>NumberFormat</c>, <c>ListFormat</c>, <c>RelativeTimeFormat</c>, <c>PluralRules</c>)
    /// когда вызывающий код не передаёт локаль явно.
    /// Требует установленного <see cref="Locale"/>.
    /// </summary>
    public bool IntlSpoofing { get; init; }

    /// <summary>
    /// Ориентация экрана для подмены <c>screen.orientation.type</c>
    /// (например, <c>"portrait-primary"</c>, <c>"landscape-primary"</c>).
    /// </summary>
    public string? ScreenOrientation { get; init; }

    /// <summary>
    /// Цветовая схема для <c>matchMedia('(prefers-color-scheme: ...)')</c>: <c>"light"</c> или <c>"dark"</c>.
    /// </summary>
    public string? ColorScheme { get; init; }

    /// <summary>
    /// Включает <c>prefers-reduced-motion: reduce</c> для <c>matchMedia</c>.
    /// </summary>
    public bool? ReducedMotion { get; init; }

    /// <summary>
    /// Точность таймеров в миллисекундах для <c>performance.now()</c>.
    /// Например, значение <c>100</c> округлит результат до ближайших 100 мс.
    /// </summary>
    public double? TimerPrecisionMs { get; init; }

    /// <summary>
    /// Защита WebSocket: <c>"block"</c> — полностью запретить WebSocket,
    /// <c>"same-origin"</c> — разрешить только same-origin соединения.
    /// </summary>
    public string? WebSocketProtection { get; init; }

    /// <summary>
    /// Добавляет шум к <c>WebGLRenderingContext.readPixels()</c>
    /// для рандомизации WebGL-хешей.
    /// </summary>
    public bool WebGLNoise { get; init; }

    /// <summary>
    /// Подменяет результат <c>navigator.storage.estimate()</c>.
    /// Значение в байтах (например <c>2_147_483_648</c> для 2 ГБ).
    /// </summary>
    public long? StorageQuota { get; init; }

    /// <summary>
    /// Подменяет <c>navigator.keyboard.getLayoutMap()</c> на фиксированный набор
    /// клавиш для указанной раскладки (например <c>"en-US"</c>).
    /// </summary>
    public string? KeyboardLayout { get; init; }

    /// <summary>
    /// Перезаписывает ICE-кандидаты WebRTC для сокрытия локальных IP.
    /// <c>"sanitize"</c> — заменяет приватные IP на <c>0.0.0.0</c>,
    /// <c>"block"</c> — подавляет все кандидаты.
    /// </summary>
    public string? WebRtcIcePolicy { get; init; }

    /// <summary>
    /// Подменяет <c>navigator.plugins</c> и <c>navigator.mimeTypes</c>
    /// стандартным набором Chrome (PDF Viewer).
    /// </summary>
    public bool PluginSpoofing { get; init; }

    /// <summary>
    /// Заменяет <c>webkitSpeechRecognition</c> / <c>SpeechRecognition</c>
    /// на заглушку, не раскрывающую реальные возможности распознавания.
    /// </summary>
    public bool SpeechRecognitionProtection { get; init; }

    /// <summary>
    /// Подменяет <c>navigator.maxTouchPoints</c> и управляет <c>TouchEvent</c>.
    /// </summary>
    public int? MaxTouchPoints { get; init; }

    /// <summary>
    /// Подменяет <c>AudioContext.sampleRate</c> / <c>BaseAudioContext.sampleRate</c>.
    /// </summary>
    public int? AudioSampleRate { get; init; }

    /// <summary>
    /// Подменяет <c>AudioContext.destination.maxChannelCount</c>.
    /// </summary>
    public int? AudioChannelCount { get; init; }

    /// <summary>
    /// Подменяет <c>navigator.pdfViewerEnabled</c>.
    /// </summary>
    public bool? PdfViewerEnabled { get; init; }

    /// <summary>
    /// Подменяет <c>Notification.permission</c> (<c>"default"</c>, <c>"denied"</c>, <c>"granted"</c>).
    /// </summary>
    public string? NotificationPermission { get; init; }

    /// <summary>
    /// <c>navigator.getGamepads()</c> возвращает пустой массив.
    /// </summary>
    public bool GamepadProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.bluetooth</c>, <c>navigator.usb</c>,
    /// <c>navigator.serial</c> и <c>navigator.hid</c>.
    /// </summary>
    public bool HardwareApiProtection { get; init; }

    /// <summary>
    /// Ограничивает <c>PerformanceObserver</c> и <c>performance.getEntries()</c>
    /// пустыми результатами.
    /// </summary>
    public bool PerformanceProtection { get; init; }

    /// <summary>
    /// Подменяет <c>document.referrer</c>.
    /// </summary>
    public string? DocumentReferrer { get; init; }

    /// <summary>
    /// Подменяет <c>history.length</c>.
    /// </summary>
    public int? HistoryLength { get; init; }

    /// <summary>
    /// Блокирует <c>DeviceOrientationEvent</c> и <c>DeviceMotionEvent</c>.
    /// </summary>
    public bool DeviceMotionProtection { get; init; }

    /// <summary>
    /// Блокирует <c>AmbientLightSensor</c> API.
    /// </summary>
    public bool AmbientLightProtection { get; init; }

    /// <summary>
    /// Подменяет <c>navigator.connection.rtt</c> (мс) без необходимости указывать <see cref="NetworkInfo"/>.
    /// </summary>
    public int? ConnectionRtt { get; init; }

    /// <summary>
    /// Подменяет <c>navigator.connection.downlink</c> (Мбит/с) без необходимости указывать <see cref="NetworkInfo"/>.
    /// </summary>
    public double? ConnectionDownlink { get; init; }

    /// <summary>
    /// Подменяет <c>navigator.mediaCapabilities.decodingInfo</c> — всегда <c>supported/smooth/powerEfficient</c>.
    /// </summary>
    public bool MediaCapabilitiesProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.clipboard.read</c> / <c>readText</c>.
    /// </summary>
    public bool ClipboardProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.share</c> / <c>canShare</c>.
    /// </summary>
    public bool WebShareProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.wakeLock.request</c>.
    /// </summary>
    public bool WakeLockProtection { get; init; }

    /// <summary>
    /// Блокирует <c>IdleDetector</c> API.
    /// </summary>
    public bool IdleDetectionProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.credentials.get</c> / <c>create</c>.
    /// </summary>
    public bool CredentialProtection { get; init; }

    /// <summary>
    /// Блокирует <c>PaymentRequest</c> конструктор.
    /// </summary>
    public bool PaymentProtection { get; init; }

    /// <summary>
    /// Подменяет <c>navigator.storage.estimate()</c> — используемый объём (usage).
    /// </summary>
    public long? StorageEstimateUsage { get; init; }

    /// <summary>
    /// Блокирует File System Access API (<c>showOpenFilePicker</c>, <c>showSaveFilePicker</c>, <c>showDirectoryPicker</c>).
    /// </summary>
    public bool FileSystemAccessProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.sendBeacon</c> — тихо возвращает <see langword="true"/>.
    /// </summary>
    public bool BeaconProtection { get; init; }

    /// <summary>
    /// Подменяет <c>document.visibilityState</c> и блокирует событие <c>visibilitychange</c>.
    /// </summary>
    public string? VisibilityStateOverride { get; init; }

    /// <summary>
    /// Подменяет <c>screen.colorDepth</c> и <c>screen.pixelDepth</c>.
    /// </summary>
    public int? ColorDepth { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.getInstalledRelatedApps</c> — возвращает пустой массив.
    /// </summary>
    public bool InstalledAppsProtection { get; init; }

    /// <summary>
    /// Нормализует метрики шрифтов через <c>getComputedStyle</c> для защиты от fingerprinting.
    /// </summary>
    public bool FontMetricsProtection { get; init; }

    /// <summary>
    /// Подменяет <c>window.crossOriginIsolated</c> и блокирует <c>SharedArrayBuffer</c>.
    /// </summary>
    public bool? CrossOriginIsolationOverride { get; init; }

    /// <summary>
    /// Добавляет jitter к <c>performance.now()</c> (мс).
    /// </summary>
    public double? PerformanceNowJitter { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.windowControlsOverlay</c>.
    /// </summary>
    public bool WindowControlsOverlayProtection { get; init; }

    /// <summary>
    /// Блокирует <c>screen.orientation.lock()</c>.
    /// </summary>
    public bool ScreenOrientationLockProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.keyboard.getLayoutMap()</c>.
    /// </summary>
    public bool KeyboardApiProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.usb</c>, <c>navigator.hid</c>, <c>navigator.serial</c>.
    /// </summary>
    public bool UsbHidSerialProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.presentation</c>.
    /// </summary>
    public bool PresentationApiProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.contacts</c>.
    /// </summary>
    public bool ContactsApiProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.bluetooth</c>.
    /// </summary>
    public bool BluetoothProtection { get; init; }

    /// <summary>
    /// Блокирует <c>EyeDropper</c> API.
    /// </summary>
    public bool EyeDropperProtection { get; init; }

    /// <summary>
    /// Блокирует <c>window.getScreenDetails()</c> (Multi-Screen Window Placement).
    /// </summary>
    public bool MultiScreenProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.ink</c>.
    /// </summary>
    public bool InkApiProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.virtualKeyboard</c>.
    /// </summary>
    public bool VirtualKeyboardProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.nfc</c> (Web NFC).
    /// </summary>
    public bool NfcProtection { get; init; }

    /// <summary>
    /// Блокирует <c>window.launchQueue</c> (File Handling API).
    /// </summary>
    public bool FileHandlingProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.xr</c> (WebXR).
    /// </summary>
    public bool WebXrProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.ml</c> (Web Neural Network API).
    /// </summary>
    public bool WebNnProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.scheduling</c> (Scheduling API).
    /// </summary>
    public bool SchedulingProtection { get; init; }

    /// <summary>
    /// Блокирует <c>document.requestStorageAccess()</c> и <c>document.hasStorageAccess()</c>.
    /// </summary>
    public bool StorageAccessProtection { get; init; }

    /// <summary>
    /// Блокирует Content Indexing API (<c>registration.index</c>).
    /// </summary>
    public bool ContentIndexProtection { get; init; }

    /// <summary>
    /// Блокирует <c>registration.sync</c> и <c>registration.periodicSync</c> (Background Sync API).
    /// </summary>
    public bool BackgroundSyncProtection { get; init; }

    /// <summary>
    /// Скрывает <c>window.cookieStore</c> (Cookie Store API).
    /// </summary>
    public bool CookieStoreProtection { get; init; }

    /// <summary>
    /// Скрывает <c>navigator.locks</c> (Web Locks API).
    /// </summary>
    public bool WebLocksProtection { get; init; }

    /// <summary>
    /// Скрывает <c>BarcodeDetector</c>, <c>FaceDetector</c>, <c>TextDetector</c> (Shape Detection API).
    /// </summary>
    public bool ShapeDetectionProtection { get; init; }

    /// <summary>
    /// Блокирует <c>WebTransport</c>.
    /// </summary>
    public bool WebTransportProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.getInstalledRelatedApps()</c>.
    /// </summary>
    public bool RelatedAppsProtection { get; init; }

    /// <summary>
    /// Скрывает <c>window.getDigitalGoodsService()</c> (Digital Goods API).
    /// </summary>
    public bool DigitalGoodsProtection { get; init; }

    /// <summary>
    /// Скрывает <c>PressureObserver</c> (Compute Pressure API).
    /// </summary>
    public bool ComputePressureProtection { get; init; }

    /// <summary>
    /// Блокирует <c>showDirectoryPicker()</c>, <c>showOpenFilePicker()</c>, <c>showSaveFilePicker()</c>.
    /// </summary>
    public bool FileSystemPickerProtection { get; init; }

    /// <summary>
    /// Скрывает <c>navigator.windowControlsOverlay</c> (Display Override API).
    /// </summary>
    public bool DisplayOverrideProtection { get; init; }

    /// <summary>
    /// Подменяет <c>BatteryManager.level</c> указанным значением (0.0–1.0). Дополняет <see cref="BatteryProtection"/>.
    /// </summary>
    public double? BatteryLevelOverride { get; init; }

    /// <summary>
    /// Блокирует <c>DocumentPictureInPicture</c> и <c>PictureInPictureWindow</c>.
    /// </summary>
    public bool PictureInPictureProtection { get; init; }

    /// <summary>
    /// Скрывает <c>navigator.devicePosture</c> (Device Posture API).
    /// </summary>
    public bool DevicePostureProtection { get; init; }

    /// <summary>
    /// Блокирует WebAuthn (FIDO) через <c>credentials.get/create</c> с <c>publicKey</c>.
    /// </summary>
    public bool WebAuthnProtection { get; init; }

    /// <summary>
    /// Блокирует Federated Credential Management (FedCM) через <c>credentials.get</c> с <c>identity</c>.
    /// </summary>
    public bool FedCmProtection { get; init; }

    /// <summary>
    /// Блокирует <c>window.queryLocalFonts()</c> (Local Font Access API).
    /// </summary>
    public bool LocalFontAccessProtection { get; init; }

    /// <summary>
    /// Блокирует <c>navigator.getAutoplayPolicy()</c>.
    /// </summary>
    public bool AutoplayPolicyProtection { get; init; }

    /// <summary>
    /// Скрывает <c>window.LaunchParams</c> (Launch Handler API).
    /// </summary>
    public bool LaunchHandlerProtection { get; init; }

    /// <summary>
    /// Блокирует <c>document.browsingTopics()</c> (Topics API, Privacy Sandbox).
    /// </summary>
    public bool TopicsApiProtection { get; init; }

    /// <summary>
    /// Блокирует Attribution Reporting API (заголовки <c>Attribution-Reporting-*</c>).
    /// </summary>
    public bool AttributionReportingProtection { get; init; }

    /// <summary>
    /// Блокирует <c>HTMLFencedFrameElement</c> и <c>window.fence</c> (Fenced Frames API).
    /// </summary>
    public bool FencedFrameProtection { get; init; }

    /// <summary>
    /// Блокирует <c>window.sharedStorage</c> (Shared Storage API).
    /// </summary>
    public bool SharedStorageProtection { get; init; }

    /// <summary>
    /// Блокирует <c>privateAggregation</c> (Private Aggregation API).
    /// </summary>
    public bool PrivateAggregationProtection { get; init; }

    /// <summary>
    /// Блокирует Web OTP API (<c>OTPCredential</c>).
    /// </summary>
    public bool WebOtpProtection { get; init; }

    /// <summary>
    /// Блокирует Web MIDI API (<c>navigator.requestMIDIAccess</c>).
    /// </summary>
    public bool WebMidiProtection { get; init; }

    /// <summary>
    /// Блокирует WebCodecs API (<c>VideoEncoder</c>, <c>VideoDecoder</c>, <c>AudioEncoder</c>, <c>AudioDecoder</c>).
    /// </summary>
    public bool WebCodecsProtection { get; init; }

    /// <summary>
    /// Блокирует Navigation API (<c>window.navigation</c>).
    /// </summary>
    public bool NavigationApiProtection { get; init; }

    /// <summary>
    /// Блокирует Screen Capture API (<c>getDisplayMedia</c>).
    /// </summary>
    public bool ScreenCaptureProtection { get; init; }

    /// <summary>
    /// Создаёт профиль anti-detect с включёнными защитами по умолчанию.
    /// </summary>
    /// <param name="userAgent">User-Agent строка.</param>
    /// <param name="proxy">Прокси-сервер (опционально).</param>
    /// <param name="locale">Локаль (по умолчанию <c>"en-US"</c>).</param>
    /// <param name="timezone">Часовой пояс IANA (по умолчанию <c>"America/New_York"</c>).</param>
#pragma warning disable MA0051 // Factory initializer grows with each protection
    public static TabContextSettings CreateAntiDetectProfile(
        string userAgent,
        string? proxy = null,
        string? locale = null,
        string? timezone = null)
    {
        locale ??= "en-US"; timezone ??= "America/New_York";
        return new TabContextSettings
        {
            Proxy = proxy,
            UserAgent = userAgent,
            Locale = locale,
            Timezone = timezone,
            Platform = "Win32",
            Languages = [locale, locale[..2]],
            CanvasNoise = true,
            AudioNoise = true,
            WebRtcPolicy = "disable",
            BatteryProtection = true,
            PermissionsProtection = true,
            HardwareConcurrency = 4,
            DeviceMemory = 8,
            AllowedFonts = ["Arial", "Verdana", "Helvetica", "Times New Roman", "Courier New", "Georgia"],
            ClientHints = new ClientHintsSettings { Platform = "Windows", Mobile = false, Brands = [new("Chromium", "128"), new("Not;A=Brand", "24")] },
            NetworkInfo = new NetworkInfoSettings(),
            SpeechVoices = [new() { Name = "Microsoft David", Lang = "en-US" }, new() { Name = "Microsoft Zira", Lang = "en-US" }],
            MediaDevicesProtection = true,
            IntlSpoofing = true,
            ColorScheme = "light",
            TimerPrecisionMs = 100,
            WebSocketProtection = "same-origin",
            WebGLNoise = true,
            KeyboardLayout = "en-US",
            WebRtcIcePolicy = "sanitize",
            PluginSpoofing = true,
            SpeechRecognitionProtection = true,
            PdfViewerEnabled = true,
            GamepadProtection = true,
            HardwareApiProtection = true,
            PerformanceProtection = true,
            DeviceMotionProtection = true,
            AmbientLightProtection = true,
            MediaCapabilitiesProtection = true,
            ClipboardProtection = true,
            WebShareProtection = true,
            WakeLockProtection = true,
            IdleDetectionProtection = true,
            CredentialProtection = true,
            PaymentProtection = true,
            FileSystemAccessProtection = true,
            BeaconProtection = true,
            InstalledAppsProtection = true,
            FontMetricsProtection = true,
            WindowControlsOverlayProtection = true,
            ScreenOrientationLockProtection = true,
            KeyboardApiProtection = true,
            UsbHidSerialProtection = true,
            PresentationApiProtection = true,
            ContactsApiProtection = true,
            BluetoothProtection = true,
            EyeDropperProtection = true,
            MultiScreenProtection = true,
            InkApiProtection = true,
            VirtualKeyboardProtection = true,
            NfcProtection = true,
            FileHandlingProtection = true,
            WebXrProtection = true,
            WebNnProtection = true,
            SchedulingProtection = true,
            StorageAccessProtection = true,
            ContentIndexProtection = true,
            BackgroundSyncProtection = true,
            CookieStoreProtection = true,
            WebLocksProtection = true,
            ShapeDetectionProtection = true,
            WebTransportProtection = true,
            RelatedAppsProtection = true,
            DigitalGoodsProtection = true,
            ComputePressureProtection = true,
            FileSystemPickerProtection = true,
            DisplayOverrideProtection = true,
            PictureInPictureProtection = true,
            DevicePostureProtection = true,
            WebAuthnProtection = true,
            FedCmProtection = true,
            LocalFontAccessProtection = true,
            AutoplayPolicyProtection = true,
            LaunchHandlerProtection = true,
            TopicsApiProtection = true,
            AttributionReportingProtection = true,
            FencedFrameProtection = true,
            SharedStorageProtection = true,
            PrivateAggregationProtection = true,
            WebOtpProtection = true,
            WebMidiProtection = true,
            WebCodecsProtection = true,
            NavigationApiProtection = true,
            ScreenCaptureProtection = true,
        };
    }
#pragma warning restore MA0051
}

/// <summary>
/// Настройки Client Hints для подмены <c>navigator.userAgentData</c> и HTTP-заголовков <c>Sec-CH-UA-*</c>.
/// </summary>
public sealed class ClientHintsSettings
{
    /// <summary>
    /// Список брендов (например, <c>Chromium/128</c>).
    /// </summary>
    public IReadOnlyList<ClientHintBrand>? Brands { get; init; }

    /// <summary>
    /// Полный список брендов с версиями для <c>getHighEntropyValues()</c>.
    /// Если не указан, используется <see cref="Brands"/>.
    /// </summary>
    public IReadOnlyList<ClientHintBrand>? FullVersionList { get; init; }

    /// <summary>
    /// Платформа (например, <c>"Windows"</c>, <c>"Linux"</c>, <c>"macOS"</c>).
    /// </summary>
    public string? Platform { get; init; }

    /// <summary>
    /// Версия платформы (например, <c>"15.0.0"</c>).
    /// </summary>
    public string? PlatformVersion { get; init; }

    /// <summary>
    /// Мобильное устройство.
    /// </summary>
    public bool? Mobile { get; init; }

    /// <summary>
    /// Архитектура CPU (например, <c>"x86"</c>).
    /// </summary>
    public string? Architecture { get; init; }

    /// <summary>
    /// Модель устройства (пустая строка для десктопов).
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Разрядность (например, <c>"64"</c>).
    /// </summary>
    public string? Bitness { get; init; }
}

/// <summary>
/// Бренд и версия для Client Hints.
/// </summary>
/// <param name="Brand">Название бренда (например, <c>"Chromium"</c>).</param>
/// <param name="Version">Версия (например, <c>"128"</c>).</param>
public sealed record ClientHintBrand(string Brand, string Version);

/// <summary>
/// Настройки Network Information API для подмены <c>navigator.connection</c>.
/// </summary>
public sealed class NetworkInfoSettings
{
    /// <summary>
    /// Эффективный тип соединения (<c>"4g"</c>, <c>"3g"</c>, <c>"2g"</c>, <c>"slow-2g"</c>).
    /// По умолчанию <c>"4g"</c>.
    /// </summary>
    public string EffectiveType { get; init; } = "4g";

    /// <summary>
    /// Round-trip time в миллисекундах.
    /// По умолчанию 50.
    /// </summary>
    public double Rtt { get; init; } = 50;

    /// <summary>
    /// Пропускная способность в Мбит/с.
    /// По умолчанию 10.0.
    /// </summary>
    public double Downlink { get; init; } = 10.0;

    /// <summary>
    /// Режим экономии трафика.
    /// </summary>
    public bool SaveData { get; init; }
}

/// <summary>
/// Настройки экрана для fingerprint-подмены.
/// </summary>
public sealed class ScreenSettings
{
    /// <summary>
    /// Ширина экрана в пикселях.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Высота экрана в пикселях.
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    /// Глубина цвета.
    /// </summary>
    public int? ColorDepth { get; init; }
}

/// <summary>
/// Настройки WebGL для fingerprint-подмены.
/// </summary>
public sealed class WebGLSettings
{
    /// <summary>
    /// Строка вендора (например, <c>"Google Inc. (NVIDIA)"</c>).
    /// </summary>
    public string? Vendor { get; init; }

    /// <summary>
    /// Строка рендерера (например, <c>"ANGLE (NVIDIA, NVIDIA GeForce GTX 1080 Direct3D11 vs_5_0 ps_5_0)"</c>).
    /// </summary>
    public string? Renderer { get; init; }
}

/// <summary>
/// Координаты геолокации для подмены <c>navigator.geolocation</c>.
/// </summary>
public sealed class GeolocationSettings
{
    /// <summary>
    /// Широта в диапазоне [−90, 90].
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// Долгота в диапазоне [−180, 180].
    /// </summary>
    public double Longitude { get; init; }

    /// <summary>
    /// Точность в метрах (по умолчанию 10).
    /// </summary>
    public double? Accuracy { get; init; }
}

/// <summary>
/// Голосовой синтезатор для подмены <c>speechSynthesis.getVoices()</c>.
/// </summary>
public sealed class SpeechVoiceSettings
{
    /// <summary>
    /// Имя голоса (например, <c>"Microsoft David"</c>).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Языковой тег BCP 47 (например, <c>"en-US"</c>).
    /// </summary>
    public required string Lang { get; init; }

    /// <summary>
    /// Является ли голос локальным (по умолчанию <see langword="true"/>).
    /// </summary>
    public bool LocalService { get; init; } = true;
}

/// <summary>
/// Настройки tab-local виртуальных медиа-устройств.
/// </summary>
public sealed class VirtualMediaDevicesSettings
{
    /// <summary>
    /// Включить виртуальный микрофон.
    /// </summary>
    public bool AudioInputEnabled { get; init; } = true;

    /// <summary>
    /// Label виртуального микрофона.
    /// </summary>
    public string AudioInputLabel { get; init; } = "Virtual Microphone";

    /// <summary>
    /// Явный browser-visible <c>MediaDeviceInfo.deviceId</c> для микрофона.
    /// Если задан, используется как основной ключ маршрутизации вместо label.
    /// </summary>
    public string? AudioInputBrowserDeviceId { get; init; }

    /// <summary>
    /// Включить виртуальную камеру.
    /// </summary>
    public bool VideoInputEnabled { get; init; } = true;

    /// <summary>
    /// Label виртуальной камеры.
    /// </summary>
    public string VideoInputLabel { get; init; } = "Virtual Camera";

    /// <summary>
    /// Явный browser-visible <c>MediaDeviceInfo.deviceId</c> для камеры.
    /// Если задан, используется как основной ключ маршрутизации вместо label.
    /// </summary>
    public string? VideoInputBrowserDeviceId { get; init; }

    /// <summary>
    /// Включить виртуальное audio output устройство в <c>enumerateDevices()</c>.
    /// </summary>
    public bool AudioOutputEnabled { get; init; }

    /// <summary>
    /// Label виртуального output устройства.
    /// </summary>
    public string AudioOutputLabel { get; init; } = "Virtual Speakers";

    /// <summary>
    /// Общий groupId для виртуальных устройств.
    /// Если не указан, будет использован детерминированный groupId контекста.
    /// </summary>
    public string? GroupId { get; init; }
}

/// <summary>
/// Расширенные параметры WebGL для anti-fingerprinting.
/// </summary>
public sealed class WebGLParamsSettings
{
    /// <summary>
    /// Максимальный размер текстуры (<c>MAX_TEXTURE_SIZE</c>).
    /// </summary>
    public int? MaxTextureSize { get; init; }

    /// <summary>
    /// Максимальный размер рендер-буфера (<c>MAX_RENDERBUFFER_SIZE</c>).
    /// </summary>
    public int? MaxRenderbufferSize { get; init; }

    /// <summary>
    /// Максимальные размеры viewport (<c>MAX_VIEWPORT_DIMS</c>).
    /// </summary>
    public IReadOnlyList<int>? MaxViewportDims { get; init; }

    /// <summary>
    /// Максимальное количество varying-векторов (<c>MAX_VARYING_VECTORS</c>).
    /// </summary>
    public int? MaxVaryingVectors { get; init; }

    /// <summary>
    /// Максимальное количество vertex-uniform-векторов (<c>MAX_VERTEX_UNIFORM_VECTORS</c>).
    /// </summary>
    public int? MaxVertexUniformVectors { get; init; }

    /// <summary>
    /// Максимальное количество fragment-uniform-векторов (<c>MAX_FRAGMENT_UNIFORM_VECTORS</c>).
    /// </summary>
    public int? MaxFragmentUniformVectors { get; init; }
}
