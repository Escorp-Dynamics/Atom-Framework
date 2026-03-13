# Atom.Net.Browsing.WebDriver

Драйвер браузера через WebSocket-мост и расширение-коннектор. В отличие от Selenium/Puppeteer не использует CDP — связь идёт через расширение браузера, что делает драйвер неотличимым от реального пользователя для систем антидетекта.

## Архитектура

```
.NET (WebDriverBrowser)
  │
  ├── BridgeServer (HTTP + WebSocket)
  │     ├── Discovery endpoint (HTML)
  │     └── WebSocket каналы
  │
  └── TabChannel (по одному на вкладку)
        ↕ WebSocket
Extension (background.js)
  │
  └── content.js (по одному на вкладку)
        ↕ MutationObserver eval bridge
      Страница (MAIN world)
```

Каждая вкладка получает изолированный WebSocket-канал — команды выполняются независимо, как если бы каждая вкладка была отдельным процессом.

## Поддерживаемые браузеры

| Браузер | Статус | Примечания |
|---------|--------|------------|
| Brave | ✅ | Полная поддержка |
| Opera | ✅ | Полная поддержка |
| Vivaldi | ✅ | Полная поддержка |
| Firefox | ✅ | Через `web-ext run` (MV2) |
| Chrome | ❌ | `--load-extension` + новый `--user-data-dir` деактивирует расширения |
| Edge | ❌ | FRE блокирует расширения при новом профиле |
| Yandex | ❌ | Аналогично Chrome |

## API

### Интерфейсы (`Atom.Net.Browsing`)

| Интерфейс | Назначение |
|-----------|------------|
| `IDomContext` | DOM-операции: `ExecuteAsync`, `FindElementAsync`, `GetTitleAsync`, `GetUrlAsync`, `GetContentAsync` |
| `IWebPage` | Страница: `NavigateAsync`, `CaptureScreenshotAsync`, cookies, `MainFrame` |
| `IFrame` | Фрейм: `Id`, `Name`, `Source`, наследует `IDomContext` |
| `IElement` | Элемент: `ClickAsync`, `TypeAsync`, `GetPropertyAsync`, `FindElementAsync` |

### Реализации (`Atom.Net.Browsing.WebDriver`)

| Класс | Назначение |
|-------|------------|
| `WebDriverBrowser` | Точка входа: `LaunchAsync`, `OpenTabAsync`, `CloseTabAsync` |
| `WebDriverPage` | Страница вкладки, делегирует DOM-операции к `MainFrame` |
| `WebDriverFrame` | Выполняет DOM-команды через `TabChannel` |
| `WebDriverElement` | Элемент DOM с действиями через расширение |
| `BridgeServer` | HTTP/WebSocket-сервер моста |
| `TabChannel` | Изолированный WebSocket-канал вкладки |

## Быстрый старт

```csharp
// Запуск браузера с расширением.
await using var browser = await WebDriverBrowser.LaunchAsync(
    "/usr/bin/brave",
    "./Extension",
    arguments: ["--headless=new", "--no-sandbox"]);

// Ожидание подключения первой вкладки.
var tcs = new TaskCompletionSource<TabConnectedEventArgs>();
browser.TabConnected += (_, e) => { tcs.TrySetResult(e); return ValueTask.CompletedTask; };
var tab = await tcs.Task;
var page = browser.GetPage(tab.TabId)!;

// DOM-операции.
var title = await page.GetTitleAsync();
var element = await page.FindElementAsync(new ElementSelector
{
    Strategy = ElementSelectorStrategy.Css,
    Value = "#login-button",
});
await element!.ClickAsync();

// Выполнение JS в MAIN world.
var result = await page.ExecuteAsync("return document.cookie");
```

## Механизм подключения

1. `LaunchAsync` копирует расширение во временную директорию и записывает `config.json` с параметрами моста (`host`, `port`, `secret`)
2. `background.js` при старте читает `config.json` через `fetch(browser.runtime.getURL("config.json"))`
3. Расширение открывает discovery-вкладку и регистрирует WebSocket-каналы для каждой вкладки
4. `content.js` устанавливает eval bridge через MutationObserver для выполнения JS в контексте страницы

## Расширение

Единая кодовая база для Chrome (MV3) и Firefox (MV2):

- `Extension/` — Chrome MV3 (service worker)
- `Extension.Firefox/` — Firefox MV2 (JS-файлы генерируются из `Extension/` при сборке через MSBuild)

Полифилл `const browser = globalThis.browser ?? globalThis.chrome;` обеспечивает кроссбраузерность API.

## Изоляция контекстов

Два уровня изоляции:

| Уровень | Метод | Изоляция | Когда использовать |
|---------|-------|----------|-------------------|
| **Процесс** | `CreateContextAsync` | Отдельный процесс + профиль (OS-level) | Полная изоляция: прокси, cookies, кэш, сетевой стек |
| **Расширение** | `OpenIsolatedTabAsync` | Одно окно, изоляция через расширение | Лёгкие параллельные сессии без накладных расходов на процесс |

### `CreateContextAsync` — изоляция на уровне процесса

Создаёт отдельный процесс браузера с уникальным временным профилем, подключённый к общему WebSocket-мосту.

```csharp
var ctx = await browser.CreateContextAsync(
    "/usr/bin/brave",
    "./Extension",
    settings: new TabContextSettings
    {
        Proxy = "socks5://proxy.example.com:1080",
        UserAgent = "CustomBot/1.0",
        Timezone = "America/New_York",
        Locale = "en-US",
    });

var page = ctx.Page;
await page.NavigateAsync(new Uri("https://example.com"));
```

### `OpenIsolatedTabAsync` — изоляция на уровне расширения

Открывает вкладку в текущем окне с MAIN world инъекцией для подмены navigator, storage, fingerprint.

```csharp
var page = await browser.OpenIsolatedTabAsync(
    url: new Uri("https://example.com"),
    settings: new TabContextSettings
    {
        UserAgent = "Mozilla/5.0 (Custom)",
        Platform = "Win32",
        Languages = ["en-US", "en"],
        Locale = "en-US",
        Timezone = "Europe/London",
        Screen = new ScreenSettings { Width = 1920, Height = 1080, ColorDepth = 24 },
        WebGL = new WebGLSettings
        {
            Vendor = "Google Inc. (NVIDIA)",
            Renderer = "ANGLE (NVIDIA GeForce GTX 1080)",
        },
        CanvasNoise = true,
        WebRtcPolicy = "disable",
        Proxy = "http://proxy:8080",
    });

var ua = await page.ExecuteAsync("navigator.userAgent");
```

### `TabContextSettings`

| Свойство | Тип | Описание |
|----------|-----|----------|
| `Proxy` | `string?` | URL прокси: `http://`, `https://`, `socks4://`, `socks5://` |
| `UserAgent` | `string?` | Подмена `navigator.userAgent` + HTTP-заголовок `User-Agent` |
| `Locale` | `string?` | Подмена `navigator.language` |
| `Timezone` | `string?` | IANA timezone (`America/New_York`). Подменяет `Intl.DateTimeFormat`, `getTimezoneOffset()`, `toLocaleString` |
| `Platform` | `string?` | Подмена `navigator.platform` |
| `Languages` | `IReadOnlyList<string>?` | Подмена `navigator.languages` |
| `Screen` | `ScreenSettings?` | Подмена `screen.width`, `screen.height`, `screen.colorDepth` |
| `WebGL` | `WebGLSettings?` | Подмена `WEBGL_debug_renderer_info` vendor/renderer |
| `CanvasNoise` | `bool` | Добавляет случайный шум в `canvas.toDataURL()` / `getImageData()` |
| `WebRtcPolicy` | `string?` | `"disable"` — блокирует `RTCPeerConnection`; `"relay-only"` — только TURN |
| `Geolocation` | `GeolocationSettings?` | Подмена `navigator.geolocation` (latitude, longitude, accuracy) |
| `AllowedFonts` | `IReadOnlyList<string>?` | Список разрешённых шрифтов; `document.fonts.check()` блокирует остальные |
| `AudioNoise` | `bool` | Детерминированный шум AudioContext fingerprint (на основе contextId) |
| `HardwareConcurrency` | `int?` | Подмена `navigator.hardwareConcurrency` |
| `DeviceMemory` | `double?` | Подмена `navigator.deviceMemory` (ГБ) |
| `BatteryProtection` | `bool` | `navigator.getBattery()` → charging=true, level=1.0 |
| `PermissionsProtection` | `bool` | `navigator.permissions.query()` → state="prompt" для всех |
| `ClientHints` | `ClientHintsSettings?` | Подмена `navigator.userAgentData` + HTTP-заголовков `Sec-CH-UA-*` |
| `NetworkInfo` | `NetworkInfoSettings?` | Подмена `navigator.connection` (effectiveType, rtt, downlink, saveData) |
| `SpeechVoices` | `IReadOnlyList<SpeechVoiceSettings>?` | Подмена `speechSynthesis.getVoices()` фиксированным набором |
| `MediaDevicesProtection` | `bool` | `enumerateDevices()` → стандартный набор (1 audio in/out, 1 video) |
| `WebGLParams` | `WebGLParamsSettings?` | Расширенные параметры WebGL (`MAX_TEXTURE_SIZE` и др.) |
| `DoNotTrack` | `string?` | Значение `navigator.doNotTrack` (`"1"` / `"0"`) |
| `GlobalPrivacyControl` | `bool?` | Значение `navigator.globalPrivacyControl` |
| `IntlSpoofing` | `bool` | Подменяет locale в `Intl.NumberFormat`, `Intl.ListFormat` и др. на `Locale` |
| `ScreenOrientation` | `string?` | `screen.orientation.type` (`"portrait-primary"`, `"landscape-primary"`) |
| `ColorScheme` | `string?` | `matchMedia('prefers-color-scheme')` → `"light"` / `"dark"` |
| `ReducedMotion` | `bool?` | `matchMedia('prefers-reduced-motion')` → reduce / no-preference |
| `TimerPrecisionMs` | `double?` | Округление `performance.now()` и `Date.now()` до заданной точности (мс) |
| `WebSocketProtection` | `string?` | `"block"` — отключить WebSocket; `"same-origin"` — только same-origin |
| `WebGLNoise` | `bool` | Шум в `readPixels()` для рандомизации WebGL-хешей |
| `StorageQuota` | `long?` | Подмена `navigator.storage.estimate()` (байты) |
| `KeyboardLayout` | `string?` | Фиксированная раскладка `keyboard.getLayoutMap()` (QWERTY) |
| `WebRtcIcePolicy` | `string?` | `"sanitize"` — замена приватных IP на `0.0.0.0`; `"block"` — подавление всех ICE |
| `PluginSpoofing` | `bool` | Подмена `navigator.plugins` / `navigator.mimeTypes` (PDF Viewer) |
| `SpeechRecognitionProtection` | `bool` | Заглушка `SpeechRecognition` / `webkitSpeechRecognition` |
| `MaxTouchPoints` | `int?` | `navigator.maxTouchPoints` + удаление `TouchEvent` при 0 |
| `AudioSampleRate` | `int?` | `AudioContext.sampleRate` override (напр. 44100) |
| `AudioChannelCount` | `int?` | `AudioDestinationNode.maxChannelCount` override |
| `PdfViewerEnabled` | `bool?` | `navigator.pdfViewerEnabled` override |
| `NotificationPermission` | `string?` | `Notification.permission` + `requestPermission()` override |
| `GamepadProtection` | `bool?` | Блокирует Gamepad API — `getGamepads()` возвращает `[]` |
| `HardwareApiProtection` | `bool?` | Скрывает `navigator.bluetooth`, `.usb`, `.serial`, `.hid` |
| `PerformanceProtection` | `bool?` | Фильтрует `PerformanceObserver` + `getEntries()` → `[]` |
| `DocumentReferrer` | `string?` | `document.referrer` override |
| `HistoryLength` | `int?` | `history.length` override |
| `DeviceMotionProtection` | `bool` | Блокирует `DeviceOrientationEvent` / `DeviceMotionEvent` |
| `AmbientLightProtection` | `bool` | Блокирует `AmbientLightSensor` API |
| `ConnectionRtt` | `int?` | `navigator.connection.rtt` standalone override (мс) |
| `ConnectionDownlink` | `double?` | `navigator.connection.downlink` standalone override (Мбит/с) |
| `MediaCapabilitiesProtection` | `bool` | `mediaCapabilities.decodingInfo` → always supported |
| `ClipboardProtection` | `bool` | Блокирует `clipboard.read` / `readText` |
| `WebShareProtection` | `bool` | Блокирует `navigator.share` / `canShare` |
| `WakeLockProtection` | `bool` | Блокирует `navigator.wakeLock.request` |
| `IdleDetectionProtection` | `bool` | Блокирует `IdleDetector` API |
| `CredentialProtection` | `bool` | Блокирует `navigator.credentials.get` / `create` |
| `PaymentProtection` | `bool` | Блокирует `PaymentRequest` конструктор |
| `StorageEstimateUsage` | `long?` | `navigator.storage.estimate()` — подмена usage |
| `FileSystemAccessProtection` | `bool` | Блокирует `showOpenFilePicker`/`showSaveFilePicker`/`showDirectoryPicker` |
| `BeaconProtection` | `bool` | `navigator.sendBeacon` → тихий `true` |
| `VisibilityStateOverride` | `string?` | `document.visibilityState` + блокировка `visibilitychange` |
| `ColorDepth` | `int?` | `screen.colorDepth` / `pixelDepth` override |
| `InstalledAppsProtection` | `bool` | Блокирует `navigator.getInstalledRelatedApps` → пустой массив |
| `FontMetricsProtection` | `bool` | Нормализует метрики шрифтов через `getComputedStyle` |
| `CrossOriginIsolationOverride` | `bool?` | `window.crossOriginIsolated` override + блокировка `SharedArrayBuffer` |
| `PerformanceNowJitter` | `double?` | Jitter для `performance.now()` (мс) |
| `WindowControlsOverlayProtection` | `bool` | Скрывает `navigator.windowControlsOverlay` |
| `ScreenOrientationLockProtection` | `bool` | Блокирует `screen.orientation.lock()` |
| `KeyboardApiProtection` | `bool` | Блокирует `navigator.keyboard.getLayoutMap()` |
| `UsbHidSerialProtection` | `bool` | Скрывает `navigator.usb`, `navigator.hid`, `navigator.serial` |
| `PresentationApiProtection` | `bool` | Скрывает `navigator.presentation` |
| `ContactsApiProtection` | `bool` | Скрывает `navigator.contacts` |
| `BluetoothProtection` | `bool` | Скрывает `navigator.bluetooth` |
| `EyeDropperProtection` | `bool` | Блокирует `EyeDropper` API |
| `MultiScreenProtection` | `bool` | Блокирует `window.getScreenDetails()` |
| `InkApiProtection` | `bool` | Скрывает `navigator.ink` |
| `VirtualKeyboardProtection` | `bool` | Скрывает `navigator.virtualKeyboard` |
| `NfcProtection` | `bool` | Скрывает `NDEFReader` (Web NFC) |
| `FileHandlingProtection` | `bool` | Блокирует File Handling API (`launchQueue`) |
| `WebXrProtection` | `bool` | Скрывает `navigator.xr` (WebXR) |
| `WebNnProtection` | `bool` | Блокирует `navigator.ml` (Web Neural Network) |
| `SchedulingProtection` | `bool` | Скрывает `scheduler.postTask()` / `scheduler.yield()` |
| `StorageAccessProtection` | `bool` | Блокирует `document.requestStorageAccess()` / `hasStorageAccess()` |
| `ContentIndexProtection` | `bool` | Блокирует Content Indexing API (`registration.index`) |
| `BackgroundSyncProtection` | `bool` | Блокирует `registration.sync` / `periodicSync` |
| `CookieStoreProtection` | `bool` | Скрывает `window.cookieStore` |
| `WebLocksProtection` | `bool` | Скрывает `navigator.locks` (Web Locks API) |
| `ShapeDetectionProtection` | `bool` | Скрывает `BarcodeDetector` / `FaceDetector` / `TextDetector` |
| `WebTransportProtection` | `bool` | Блокирует `WebTransport` |
| `RelatedAppsProtection` | `bool` | Блокирует `navigator.getInstalledRelatedApps()` |
| `DigitalGoodsProtection` | `bool` | Скрывает `window.getDigitalGoodsService()` |
| `ComputePressureProtection` | `bool` | Скрывает `PressureObserver` (Compute Pressure API) |
| `FileSystemPickerProtection` | `bool` | Блокирует `showDirectoryPicker()` / `showOpenFilePicker()` / `showSaveFilePicker()` |
| `DisplayOverrideProtection` | `bool` | Скрывает `navigator.windowControlsOverlay` |
| `BatteryLevelOverride` | `double?` | Подменяет `BatteryManager.level` (0.0–1.0) |
| `PictureInPictureProtection` | `bool` | Блокирует `DocumentPictureInPicture` / `PictureInPictureWindow` |
| `DevicePostureProtection` | `bool` | Скрывает `navigator.devicePosture` |
| `WebAuthnProtection` | `bool` | Блокирует WebAuthn (FIDO) через `credentials.get/create` |
| `FedCmProtection` | `bool` | Блокирует FedCM через `credentials.get` (identity) |
| `LocalFontAccessProtection` | `bool` | Блокирует `window.queryLocalFonts()` |
| `AutoplayPolicyProtection` | `bool` | Блокирует `navigator.getAutoplayPolicy()` |
| `LaunchHandlerProtection` | `bool` | Скрывает `window.LaunchParams` |
| `TopicsApiProtection` | `bool` | Блокирует `document.browsingTopics()` (Privacy Sandbox) |
| `AttributionReportingProtection` | `bool` | Блокирует Attribution Reporting API |
| `FencedFrameProtection` | `bool` | Скрывает `HTMLFencedFrameElement` / `window.fence` |
| `SharedStorageProtection` | `bool` | Блокирует `window.sharedStorage` |
| `PrivateAggregationProtection` | `bool` | Блокирует `privateAggregation` |
| `WebOtpProtection` | `bool` | Блокирует Web OTP (`OTPCredential`) |
| `WebMidiProtection` | `bool` | Блокирует `navigator.requestMIDIAccess()` |
| `WebCodecsProtection` | `bool` | Блокирует WebCodecs API (`VideoEncoder`, etc.) |
| `NavigationApiProtection` | `bool` | Блокирует `window.navigation` (Navigation API) |
| `ScreenCaptureProtection` | `bool` | Блокирует `getDisplayMedia()` (Screen Capture) |
| `StartUrl` | `Uri?` | URL при запуске (`CreateContextAsync`) |
| `Arguments` | `IEnumerable<string>?` | Аргументы командной строки (`CreateContextAsync`) |

#### `ClientHintsSettings`

| Свойство | Тип | Описание |
|----------|-----|----------|
| `Brands` | `IReadOnlyList<ClientHintBrand>?` | Список брендов (бренд + версия) |
| `FullVersionList` | `IReadOnlyList<ClientHintBrand>?` | Полный список для `getHighEntropyValues()` |
| `Platform` | `string?` | Платформа (`"Windows"`, `"Linux"`, `"macOS"`) |
| `PlatformVersion` | `string?` | Версия платформы |
| `Mobile` | `bool?` | Мобильное устройство |
| `Architecture` | `string?` | Архитектура CPU (`"x86"`) |
| `Model` | `string?` | Модель устройства |
| `Bitness` | `string?` | Разрядность (`"64"`) |

#### `NetworkInfoSettings`

| Свойство | Тип | Описание |
|----------|-----|----------|
| `EffectiveType` | `string` | Тип соединения (`"4g"`, `"3g"`, `"2g"`) — по умолчанию `"4g"` |
| `Rtt` | `double` | Round-trip time, мс — по умолчанию `50` |
| `Downlink` | `double` | Пропускная способность, Мбит/с — по умолчанию `10.0` |
| `SaveData` | `bool` | Режим экономии трафика |

#### `CreateAntiDetectProfile`

Фабричный метод для быстрого создания полного anti-detect профиля:

```csharp
var settings = TabContextSettings.CreateAntiDetectProfile(
    userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ...",
    proxy: "socks5://127.0.0.1:9050",
    locale: "en-US",
    timezone: "America/New_York");
```

Включает: CanvasNoise, AudioNoise, WebRTC disable, Battery/Permissions protection,
HardwareConcurrency=4, DeviceMemory=8, стандартный набор шрифтов, Client Hints, Network Info,
SpeechVoices, MediaDevices protection, DoNotTrack.

#### `SpeechVoiceSettings`

| Свойство | Тип | Описание |
|----------|-----|----------|
| `Name` | `string` | Имя голоса (`"Microsoft David"`) |
| `Lang` | `string` | Язык BCP 47 (`"en-US"`) |
| `LocalService` | `bool` | Локальный голос (по умолчанию `true`) |

#### `WebGLParamsSettings`

| Свойство | Тип | Описание |
|----------|-----|----------|
| `MaxTextureSize` | `int?` | `MAX_TEXTURE_SIZE` |
| `MaxRenderbufferSize` | `int?` | `MAX_RENDERBUFFER_SIZE` |
| `MaxViewportDims` | `IReadOnlyList<int>?` | `MAX_VIEWPORT_DIMS` (2 элемента) |
| `MaxVaryingVectors` | `int?` | `MAX_VARYING_VECTORS` |
| `MaxVertexUniformVectors` | `int?` | `MAX_VERTEX_UNIFORM_VECTORS` |
| `MaxFragmentUniformVectors` | `int?` | `MAX_FRAGMENT_UNIFORM_VECTORS` |

### Матрица возможностей: Chromium vs Firefox

| Возможность | Chromium (MV3) | Firefox (MV2) |
|-------------|:--------------:|:-------------:|
| User-Agent (HTTP) | `declarativeNetRequest` | `webRequest.onBeforeSendHeaders` |
| User-Agent (JS) | MAIN world injection | MAIN world injection |
| Cookies (Set-Cookie capture) | `webRequest.onHeadersReceived` (non-blocking, extraHeaders) | `webRequest.onHeadersReceived` (blocking) |
| Cookies (Set-Cookie strip) | `declarativeNetRequest` | `webRequest.onHeadersReceived` |
| Cookies (HTTP injection) | `declarativeNetRequest` Cookie header | `webRequest.onBeforeSendHeaders` Cookie header |
| Cookies (JS) | In-memory shim | In-memory shim |
| localStorage / sessionStorage | Namespace shim | Namespace shim |
| IndexedDB | Prefix shim | Prefix shim |
| Cache API | Prefix shim | Prefix shim |
| Screen | MAIN world override | MAIN world override |
| Canvas fingerprint | Noise injection | Noise injection |
| WebGL vendor/renderer | `getParameter` patch | `getParameter` patch |
| WebRTC | Block / relay-only | Block / relay-only |
| Timezone | `Intl` + `Date` override | `Intl` + `Date` override |
| Geolocation | `navigator.geolocation` override | `navigator.geolocation` override |
| Font fingerprint | `document.fonts.check()` filter | `document.fonts.check()` filter |
| AudioContext fingerprint | `OfflineAudioContext.startRendering` noise | `OfflineAudioContext.startRendering` noise |
| Hardware concurrency | `navigator.hardwareConcurrency` override | `navigator.hardwareConcurrency` override |
| Device memory | `navigator.deviceMemory` override | `navigator.deviceMemory` override |
| Battery API | `navigator.getBattery()` fake (charging, level=1) | `navigator.getBattery()` fake (charging, level=1) |
| Permissions API | `permissions.query()` → "prompt" | `permissions.query()` → "prompt" |
| Client Hints (HTTP) | `declarativeNetRequest` Sec-CH-UA-* | `webRequest.onBeforeSendHeaders` (TODO) |
| Client Hints (JS) | `navigator.userAgentData` override | `navigator.userAgentData` override |
| Network Information | `navigator.connection` override | `navigator.connection` override |
| Speech Synthesis | `speechSynthesis.getVoices()` override | `speechSynthesis.getVoices()` override |
| Media Devices | `enumerateDevices()` fake set | `enumerateDevices()` fake set |
| WebGL params | `getParameter()` patch (MAX_TEXTURE_SIZE и др.) | `getParameter()` patch |
| Do Not Track | `navigator.doNotTrack` override | `navigator.doNotTrack` override |
| Global Privacy Control | `navigator.globalPrivacyControl` override | `navigator.globalPrivacyControl` override |
| Intl locale spoofing | `Intl.*` конструкторы → spoofed locale | `Intl.*` конструкторы → spoofed locale |
| Screen orientation | `screen.orientation` override | `screen.orientation` override |
| matchMedia | `prefers-color-scheme` / `prefers-reduced-motion` | `prefers-color-scheme` / `prefers-reduced-motion` |
| Timer precision | `performance.now()` / `Date.now()` rounding | `performance.now()` / `Date.now()` rounding |
| WebSocket protection | block / same-origin | block / same-origin |
| WebGL readPixels noise | `readPixels()` шум (WebGL + WebGL2) | `readPixels()` шум (WebGL + WebGL2) |
| Storage quota | `navigator.storage.estimate()` fake | `navigator.storage.estimate()` fake |
| Keyboard layout | `keyboard.getLayoutMap()` QWERTY override | `keyboard.getLayoutMap()` QWERTY override |
| WebRTC ICE rewrite | sanitize private IPs / block candidates | sanitize private IPs / block candidates |
| Plugin/MimeType | `navigator.plugins` / `mimeTypes` fake | `navigator.plugins` / `mimeTypes` fake |
| Speech Recognition | `SpeechRecognition` заглушка | `SpeechRecognition` заглушка |
| Touch/maxTouchPoints | `navigator.maxTouchPoints` override | `navigator.maxTouchPoints` override |
| AudioContext params | sampleRate + maxChannelCount override | sampleRate + maxChannelCount override |
| PDF Viewer | `navigator.pdfViewerEnabled` override | `navigator.pdfViewerEnabled` override |
| Notification | `Notification.permission` override | `Notification.permission` override |
| Gamepad | `getGamepads()` → `[]`, блокировка событий | `getGamepads()` → `[]`, блокировка событий |
| Hardware API | Скрытие bluetooth/usb/serial/hid | Скрытие bluetooth/usb/serial/hid |
| Performance | `getEntries()` → `[]`, `PerformanceObserver` фильтр | `getEntries()` → `[]`, `PerformanceObserver` фильтр |
| Referrer | `document.referrer` override | `document.referrer` override |
| History | `history.length` override | `history.length` override |
| DeviceMotion | Блокировка DeviceOrientation/DeviceMotion | Блокировка DeviceOrientation/DeviceMotion |
| AmbientLight | Блокировка `AmbientLightSensor` | Блокировка `AmbientLightSensor` |
| Connection RTT/downlink | Standalone `rtt`/`downlink` override | Standalone `rtt`/`downlink` override |
| MediaCapabilities | `decodingInfo` → always supported | `decodingInfo` → always supported |
| Clipboard | Блокировка `read`/`readText` | Блокировка `read`/`readText` |
| Web Share | Блокировка `share`/`canShare` | Блокировка `share`/`canShare` |
| Wake Lock | Блокировка `wakeLock.request` | Блокировка `wakeLock.request` |
| Idle Detection | Блокировка `IdleDetector` | Блокировка `IdleDetector` |
| Credential Mgmt | Блокировка `credentials.get`/`create` | Блокировка `credentials.get`/`create` |
| Payment Request | Блокировка `PaymentRequest` | Блокировка `PaymentRequest` |
| Storage Estimate | `storage.estimate()` usage override | `storage.estimate()` usage override |
| File System Access | Блокировка File Picker API | Блокировка File Picker API |
| Beacon | `sendBeacon` → silent true | `sendBeacon` → silent true |
| Visibility State | `visibilityState` override + event block | `visibilityState` override + event block |
| Color Depth | `colorDepth`/`pixelDepth` override | `colorDepth`/`pixelDepth` override |
| Installed Apps | Блокировка `getInstalledRelatedApps` | Блокировка `getInstalledRelatedApps` |
| Font Metrics | Нормализация метрик шрифтов | Нормализация метрик шрифтов |
| Cross-Origin Isolation | `crossOriginIsolated` override + `SharedArrayBuffer` блокировка | `crossOriginIsolated` override + `SharedArrayBuffer` блокировка |
| Performance.now Jitter | Рандомный jitter `performance.now()` | Рандомный jitter `performance.now()` |
| Window Controls Overlay | Скрытие `windowControlsOverlay` API | Скрытие `windowControlsOverlay` API |
| Screen Orientation Lock | Блокировка `screen.orientation.lock()` | Блокировка `screen.orientation.lock()` |
| Keyboard API | Блокировка `keyboard.getLayoutMap()` | Блокировка `keyboard.getLayoutMap()` |
| USB/HID/Serial | Скрытие `navigator.usb`/`hid`/`serial` | Скрытие `navigator.usb`/`hid`/`serial` |
| Presentation API | Скрытие `navigator.presentation` | Скрытие `navigator.presentation` |
| Contacts API | Скрытие `navigator.contacts` | Скрытие `navigator.contacts` |
| Bluetooth | Скрытие `navigator.bluetooth` | Скрытие `navigator.bluetooth` |
| Eye Dropper | Блокировка `EyeDropper` API | Блокировка `EyeDropper` API |
| Multi-Screen | Блокировка `getScreenDetails()` | Блокировка `getScreenDetails()` |
| Ink API | Скрытие `navigator.ink` | Скрытие `navigator.ink` |
| Virtual Keyboard | Скрытие `navigator.virtualKeyboard` | Скрытие `navigator.virtualKeyboard` |
| Web NFC | Скрытие `NDEFReader` | Скрытие `NDEFReader` |
| File Handling | Блокировка `launchQueue` | Блокировка `launchQueue` |
| WebXR | Скрытие `navigator.xr` | Скрытие `navigator.xr` |
| Web Neural Network | Блокировка `navigator.ml` | Блокировка `navigator.ml` |
| Scheduling | Скрытие `scheduler.postTask()` / `yield()` | Скрытие `scheduler.postTask()` / `yield()` |
| Storage Access | Блокировка `requestStorageAccess()` | Блокировка `requestStorageAccess()` |
| Content Index | Блокировка `registration.index` | Блокировка `registration.index` |
| Background Sync | Блокировка `sync` / `periodicSync` | Блокировка `sync` / `periodicSync` |
| Cookie Store | Скрытие `window.cookieStore` | Скрытие `window.cookieStore` |
| Web Locks | Скрытие `navigator.locks` | Скрытие `navigator.locks` |
| Shape Detection | Скрытие `BarcodeDetector` / `FaceDetector` / `TextDetector` | Скрытие `BarcodeDetector` / `FaceDetector` / `TextDetector` |
| Web Transport | Блокировка `WebTransport` | Блокировка `WebTransport` |
| Related Apps | Блокировка `getInstalledRelatedApps()` | Блокировка `getInstalledRelatedApps()` |
| Digital Goods | Скрытие `getDigitalGoodsService()` | Скрытие `getDigitalGoodsService()` |
| Compute Pressure | Скрытие `PressureObserver` | Скрытие `PressureObserver` |
| File System Picker | Блокировка `showDirectoryPicker()` | Блокировка `showDirectoryPicker()` |
| Display Override | Скрытие `windowControlsOverlay` | Скрытие `windowControlsOverlay` |
| Battery Level Override | Подмена `BatteryManager.level` | Подмена `BatteryManager.level` |
| Picture-in-Picture | Блокировка PiP API | Блокировка PiP API |
| Device Posture | Скрытие `navigator.devicePosture` | Скрытие `navigator.devicePosture` |
| WebAuthn | Блокировка FIDO credentials | Блокировка FIDO credentials |
| FedCM | Блокировка identity credentials | Блокировка identity credentials |
| Local Font Access | Блокировка `queryLocalFonts()` | Блокировка `queryLocalFonts()` |
| Autoplay Policy | Блокировка `getAutoplayPolicy()` | Блокировка `getAutoplayPolicy()` |
| Launch Handler | Скрытие `LaunchParams` | Скрытие `LaunchParams` |
| Topics API | Блокировка `browsingTopics()` | Блокировка `browsingTopics()` |
| Attribution Reporting | Блокировка `attributionSrc` | Блокировка `attributionSrc` |
| Fenced Frames | Скрытие `HTMLFencedFrameElement` | Скрытие `HTMLFencedFrameElement` |
| Shared Storage | Блокировка `sharedStorage` | Блокировка `sharedStorage` |
| Private Aggregation | Блокировка `privateAggregation` | Блокировка `privateAggregation` |
| Web OTP | Блокировка `OTPCredential` | Блокировка `OTPCredential` |
| Web MIDI | Блокировка `requestMIDIAccess()` | Блокировка `requestMIDIAccess()` |
| WebCodecs | Блокировка `VideoEncoder`/`AudioEncoder` | Блокировка `VideoEncoder`/`AudioEncoder` |
| Navigation API | Скрытие `window.navigation` | Скрытие `window.navigation` |
| Screen Capture | Блокировка `getDisplayMedia()` | Блокировка `getDisplayMedia()` |
| Proxy | `chrome.proxy.settings` (глобальный, переключается по активной вкладке) | `proxy.onRequest` (per-tab routing) |
| navigator.webdriver | `false` | `false` |
