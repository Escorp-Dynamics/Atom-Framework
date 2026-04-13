# Atom.Net.Browsing.WebDriver

Драйвер браузера через WebSocket-мост и расширение-коннектор. В отличие от Selenium/Puppeteer не использует CDP и отладочный API браузера: связь идёт через расширение браузера, а DOM-команды выполняются через изолированный канал вкладки. Пользовательский ввод при этом больше не синтетический: действия по селектору и точке идут через доверенные `VirtualMouse` и `VirtualKeyboard` в изолированном контексте дисплея.

## Архитектура

```text
.NET (WebBrowser)
  │
    ├── BridgeServer (HTTP + WebSocket)
    │     ├── Discovery / health / debug-event endpoint
    │     ├── WebSocket каналы для обычного bridge-пути
    │     ├── WSS transport для extension-backed channel
    │     └── HTTPS managed delivery для Chromium system-policy path
  │
  └── TabChannel (по одному на вкладку)
        ↕ WebSocket
Extension (background.js)
  │
  └── content.js (по одному на вкладку)
        ↕ MutationObserver eval bridge
      Страница (MAIN world)
```

Каждая вкладка получает изолированный transport-канал. Для обычного bridge-пути это локальный WebSocket, а для Linux Chrome Stable с system-policy bootstrap текущий browser-side channel поднимается через отдельный WSS transport. Команды выполняются независимо, как если бы каждая вкладка была отдельным процессом.

### Текущий runtime-срез

В текущей ветке уже существует промежуточный runtime-слой, который подготавливает переход к полноценному BridgeServer, но пока не притворяется полной заменой эталонной реализации.

```text
PageNavigationState
    ├── local BridgeMessage request/response envelope
    ├── local BridgeEvent queue
    ├── local callback producer для ExecuteScript
    └── context propagation: windowId + tabId

WebPage
    ├── transport consumer
    ├── BridgeEventReceived
    ├── публичные события жизненного цикла
    └── page-local callback/interception dispatch

Frame
    └── публичные события жизненного цикла

WebWindow
    ├── bridge event queue
    ├── BridgeEventReceived
    └── публичная цепочка ретрансляции lifecycle и interception

WebBrowser
    ├── bridge event queue
    ├── BridgeEventReceived
    └── публичная цепочка ретрансляции lifecycle и interception
```

Сейчас staged runtime публикует локальный bridge-поток по цепочке transport -> page -> window -> browser.

Для одной навигации active runtime публикует упорядоченный конверт:

- `RequestIntercepted`
- `ResponseReceived`
- `DomContentLoaded`
- `NavigationCompleted`
- `PageLoaded`

`Callback` и `CallbackFinalized` тоже уже существуют в текущем runtime-срезе, но пока генерируются локальным transport-слоем только для распознанных вызовов подписанных callback-путей во время `ExecuteScript`.

### Threading policy

Текущий runtime-срез сейчас считается потокобезопасной базой, но не runtime без блокировок в строгом смысле:

- `PageNavigationState` сериализует mutable navigation state (`history`, `currentIndex`) под `System.Threading.Lock`, а delivery очереди bridge events держит отдельно в `ConcurrentQueue`
- `WebBrowser`, `WebWindow` и `WebPage` используют `ConcurrentQueue` для внутренних потоков bridge-событий, поэтому параллельный drain больше не зависит от обычных `Queue<T>`
- публикация `CurrentWindow` и `CurrentPage` упорядочена: новая сущность сначала попадает в snapshot-коллекцию, и только потом публикуется через `Volatile`
- `OpenWindowAsync`, `OpenPageAsync` и teardown-path координируются через `System.Threading.Lock`, чтобы `DisposeAsync` не гонялся с добавлением новых сущностей
- browser-level `ClearAllCookiesAsync` fan-out'ит очистку по всем еще открытым окнам браузера, а window-level `ClearAllCookiesAsync` делает то же самое по всем еще открытым страницам окна; если teardown уже начался, оба boundary должны fail-fast через `ObjectDisposedException`
- после входа `WebBrowser` или `WebWindow` в disposed state поздние `Open*`, `NavigateAsync`, `ReloadAsync`, lookup-методы, cookie/geometry inspection и inspection текущей страницы должны либо завершиться до teardown, либо упасть с `ObjectDisposedException`; тихое продолжение новой операции после dispose больше не считается допустимым поведением
- `IWebBrowser`, `IWebWindow`, `IDomContext` и `IElement` теперь публикуют `IsDisposed` как advisory snapshot текущего lifecycle state; это удобно для недорогих чтений и snapshot-логики, но не отменяет необходимость fail-fast guard'ов на boundary-методах, потому что между чтением флага и реальным действием остаётся обычное TOCTOU-окно
- `WebPage`, `MainFrame`, `Element` и `ShadowRoot` теперь тоже отражают disposal state через owner snapshot и сохраняют fail-fast поведение на boundary-вызовах после teardown
- пока browser/window остаются живыми, их ownership-коллекции скрывают уже disposed children; `CurrentWindow` промотируется только при dispose текущего окна, а `CurrentPage` промотируется только внутри текущего окна при наличии живой replacement-страницы; если текущая страница текущего окна удалена и внутри этого окна replacement больше нет, browser/window сохраняют последний опубликованный snapshot этой страницы до явного `OpenWindowAsync` или `OpenPageAsync`, даже если в других окнах еще есть живые страницы, и current-boundary методы в этот промежуток fail-fast'ят через `ObjectDisposedException`; после полного teardown верхнего уровня ownership-коллекции очищаются, но `CurrentWindow` и `CurrentPage` все равно сохраняют последний опубликованный snapshot; add/remove для lifecycle, callback и interception events остаются inert и non-throwing
- browser/window lookup больше не опираются на late-dispose try/catch: active runtime сканирует concrete live snapshots и пропускает кандидатов, которые уже успели перейти в disposed state к моменту проверки
- публичные события жизненного цикла и внутренний `BridgeEventReceived` рассчитаны на concurrent delivery и subscriber churn, но гарантия порядка относится к одному transport stream; при параллельных producers события разных навигаций могут естественно чередоваться

Текущий baseline проверяется concurrency suite в `Tests/Atom.Net.Browsing.WebDriver.Tests/WebDriverConcurrencyTests.cs`: concurrent drain, mixed producer/consumer, publication stress для `CurrentWindow` / `CurrentPage`, lookup stress, dispose-race, navigate-dispose, mixed dispose+lookup, `IsDisposed` visibility и subscriber churn.

## Поддерживаемые браузеры

| Браузер | Статус | Примечания |
| ------- | ------ | ---------- |
| Brave | ✅ | Полная поддержка |
| Opera | ✅ | Полная поддержка |
| Vivaldi | ✅ | Полная поддержка |
| Firefox | ⚠️ | На Linux Stable неподписанный profile-local bootstrap не гарантируется; stable-path требует подписанный XPI через ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH, для живой проверки без подписи используйте Developer Edition или Nightly |
| Chrome | ✅ | Полная поддержка |
| Edge | ✅ | Полная поддержка |
| Yandex | ✅ | Полная поддержка |

## API

### Интерфейсы (`Atom.Net.Browsing.WebDriver`)

| Интерфейс | Назначение |
| --------- | ---------- |
| `IDomContext` | DOM-операции: `EvaluateAsync`, `WaitForElementAsync`, `GetElementAsync`, `GetElementsAsync`, `GetUrlAsync`, `GetTitleAsync`, `GetContentAsync`, advisory `IsDisposed` |
| `IWebPage` | Страница: `NavigateAsync`, `ReloadAsync`, cookies, `MainFrame`, callback- и interception-события, публичные события жизненного цикла |
| `IFrame` | Фрейм: `Page`, `Host`, `GetNameAsync`, `GetParentFrameAsync`, наследует `IDomContext`, публичные события жизненного цикла |
| `IElement` | Элемент: `ClickAsync`, `TypeAsync`, `PressAsync`, `GetPropertyAsync`, `GetAttributeAsync`, advisory `IsDisposed` |
| `IWebWindow` | Окно: страницы, `ActivateAsync`, `CloseAsync`, навигация текущей вкладки, публичные события жизненного цикла, advisory `IsDisposed` |
| `IWebBrowser` | Браузер: окна, страницы, запуск, навигация, публичные события жизненного цикла, advisory `IsDisposed` |

### Реализации (`Atom.Net.Browsing.WebDriver`)

| Класс | Назначение |
| ----- | ---------- |
| `WebBrowser` | Точка входа: `LaunchAsync`, `OpenWindowAsync`, `CurrentWindow`, `CurrentPage`, browser-level lifecycle propagation |
| `WebWindow` | Окно браузера, владеет страницами, агрегирует lifecycle envelopes от своих вкладок |
| `WebPage` | Страница, делегирует DOM-операции к `MainFrame` и читает transport lifecycle envelopes |
| `Frame` | Runtime-реализация фрейма страницы |
| `Element` | Runtime-реализация DOM-элемента |
| `ShadowRoot` | Runtime-реализация ограниченного Shadow DOM-контекста |

### Семантика поиска

- `IWebWindow.GetUrlAsync` и `IWebWindow.GetTitleAsync` отражают только `CurrentPage` окна.
- `IWebWindow.ActivateAsync` публикует окно как `CurrentWindow` браузера и синхронизирует этот переход с bridge-backed runtime, если у текущей страницы уже привязаны мостовые команды.
- `IWebWindow.CloseAsync` закрывает окно через bridge-backed команду при наличии моста и затем завершает локальный teardown окна; после закрытия браузер публикует следующий живой snapshot окна, если он существует.
- `IWebBrowser.GetWindowAsync("current")` возвращает `CurrentWindow` напрямую; любой другой `string` ищет окно по заголовку его текущей страницы и пропускает живые окна, у которых текущий удержанный snapshot страницы уже перешёл в disposed state к моменту проверки.
- `IWebBrowser.GetWindowAsync(Uri)` ищет окно по любой открытой странице внутри окна, а не только по `CurrentPage`.
- `IWebBrowser.GetPageAsync(string/Uri)` и `IWebWindow.GetPageAsync(string/Uri)` сканируют concrete live snapshots и пропускают дочерние snapshots, которые успели перейти в disposed state к моменту проверки, без late-dispose exception loop.
- `IWebBrowser.GetPageAsync("current")` и `IWebWindow.GetPageAsync("current")` возвращают текущий snapshot напрямую; literal title `current` не имеет приоритета над этим специальным token.
- `IWebPage.GetFrameAsync("MainFrame")` возвращает `MainFrame` напрямую; literal frame name `MainFrame` не имеет приоритета над этим специальным token.
- Если несколько живых сущностей совпадают по одному и тому же обычному title или url, порядок возврата определяется текущим live snapshot и не считается публичной гарантией.
- Cookie-surface в текущем runtime-срезе materialize-ит page-local состояние: page-level `SetCookiesAsync` сохраняет cookies в живом runtime snapshot, `GetAllCookiesAsync` возвращает их обратно, `ClearAllCookiesAsync` очищает текущую страницу, а window/browser уровни fan-out'ят очистку по своим живым страницам и окнам.
- Screenshot-surface в текущем runtime-срезе тоже stub-овая: page, main frame и element сейчас возвращают пустой payload, поэтому отсутствие байтов не нужно интерпретировать как частичную ошибку захвата.

### Публичный жизненный цикл

Публичная lifecycle-surface теперь разделена на три отдельных события у `IFrame`, `IWebPage`, `IWebWindow` и `IWebBrowser`:

- `DomContentLoaded`
- `NavigationCompleted`
- `PageLoaded`

`WebLifecycleEventArgs` несёт только публичный navigation context: `Window`, `Page`, `Frame`, `Url` и `Title`.

Пример подписки на `NavigationCompleted` страницы:

```csharp
await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
{
    Profile = WebBrowserProfile.Brave,
});

var page = browser.CurrentPage;
page.NavigationCompleted += (_, args) =>
{
    Console.WriteLine($"{args.Title} -> {args.Url}");
    Console.WriteLine($"Frame ref: {ReferenceEquals(args.Frame, page.MainFrame)}");
};

await page.NavigateAsync(new Uri("https://example.com"));
```

Пример browser-level aggregation по трем lifecycle этапам:

```csharp
await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());

browser.DomContentLoaded += (_, args) => Console.WriteLine($"DOM ready: {args.Url}");
browser.NavigationCompleted += (_, args) => Console.WriteLine($"Navigation complete: {args.Url}");
browser.PageLoaded += (_, args) => Console.WriteLine($"Loaded: {args.Title}");
```

### Доверенный ввод

Для пользовательского ввода драйвер использует доверенный путь OS/X11 через `VirtualMouse` и `VirtualKeyboard`, а не синтетическую DOM-диспетчеризацию внутри страницы. Это важно для anti-detect и для параллельной работы нескольких вкладок: у каждой вкладки свой канал, свой контекст дисплея и ввода, и нет зависимости от скрытой отладочной сессии или контура управления DevTools.

Поддерживаются операции уровня элемента, включая `ClickAsync`, `HoverAsync`, `FocusAsync`, `TypeAsync`, `PressAsync`, а также их человекоподобные варианты `HumanityClickAsync` и `HumanityTypeAsync`.

Все эти операции выполняются через доверенный контур ввода и DOM-мост поиска. Это значит:

- нажатия мышью и клавиш не зависят от синтетических DOM-событий
- ввод остаётся безопасным для параллельного выполнения при изолированных контекстах вкладки и дисплея
- `isTrusted` и `userActivation` теперь определяются реальным путём ввода, а не диспетчеризацией на стороне страницы

DOM-мост по-прежнему отвечает только за поиск, ограничение области `shadow-root`, запросы геометрии и состояния и выполнение JS. Ввод больше не эмулируется через `element.click()`, синтетические `MouseEvent` и `PointerEvent` или прямое изменение `value`.

Пример:

```csharp
var element = await page.WaitForElementAsync("#login-button");
await element!.ClickAsync();

await element.HumanityTypeAsync("demo@example.com");
await element.PressAsync(ConsoleKey.Enter);
```

## Быстрый старт

```csharp
// LaunchAsync материализует profile defaults и запускает реальный browser process,
// если Profile указывает на запускаемый binary.
await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
{
    UseHeadlessMode = true,
    Args = ["--no-sandbox"],
    Profile = WebBrowserProfile.Brave,
});

// Работа с текущей вкладкой.
var page = browser.CurrentPage;

// DOM-операции.
var title = await page.GetTitleAsync();
var element = await page.GetElementAsync("#login-button");
await element!.ClickAsync();

// Выполнение JS в MAIN world.
var result = await page.EvaluateAsync<string>("document.cookie");
```

## Механизм подключения

Текущее состояние ветки:

- `LaunchAsync` уже материализует внутренние profile files и стартует реальный browser process из `Profile.BinaryPath`, если binary действительно запускаемый.
- `LaunchAsync` уже материализует внутренние profile files, поднимает live `BridgeServer`, раскладывает Chromium-расширение с `config.json` в materialized profile, стартует реальный browser process из `Profile.BinaryPath` и возвращает браузер только после initial discovery bridge-bootstrap текущей вкладки.
- Если в живой browser session уже есть bridge-bound страница, `OpenWindowAsync` и `OpenPageAsync` идут через direct bridge-команды `OpenWindow` и `OpenTab` и возвращают новый window/page snapshot только после регистрации новой вкладки и привязки page/window bridge-команд; без bridge-bound source page эти операции остаются локальным staged fallback.
- DOM/page model больше не является только пустым scaffold: поверх локального transport уже существуют protocol envelope, context propagation, lifecycle queues и публичные события жизненного цикла.
- Первый настоящий bridge-bootstrap уже подключён для Chromium-профилей: discovery endpoint живёт в `BridgeServer`, расширение получает явный `sessionId`, а текущие `WebPage` и `WebWindow` перевязываются на реальный `tabId` и `windowId` после handshake.
- Полной reference parity всё ещё нет: DOM `EvaluateAsync`, element-команды и общий browser-side lifecycle поток пока не переведены на живой extension-backed transport.

Текущий staged runtime в ветке:

1. `PageNavigationState` materialize-ит local `BridgeMessage` request/response envelope для `Navigate`, `Reload`, `GetUrl`, `GetTitle`, `GetContent` и `ExecuteScript`
2. Для `Navigate` и `Reload` transport ставит в очередь `RequestIntercepted` и `ResponseReceived`, а затем lifecycle-конверт `DomContentLoaded -> NavigationCompleted -> PageLoaded`
3. Для `ExecuteScript` transport может локально синтезировать `Callback` и `CallbackFinalized`, если выражение похоже на прямой вызов подписанного callback-пути
4. `WebPage` читает bridge stream из transport, поднимает page-level callback/interception events и прокидывает lifecycle в `MainFrame`, `WebWindow` и `WebBrowser`
5. `WebWindow` и `WebBrowser` получают тот же ordered bridge stream для одной transport-очереди; при параллельных producers interleave между разными навигациями допустим

Отдельная граница между стабильным контрактом и временной локальной эмуляцией зафиксирована в [Framework/Atom.Net.Browsing.WebDriver/STAGED_RUNTIME_BOUNDARY.md](Framework/Atom.Net.Browsing.WebDriver/STAGED_RUNTIME_BOUNDARY.md).

## Структура проверки bridge

Текущий bridge-срез проверяется несколькими слоями tests с разной зоной ответственности:

- `WebDriverBridgeHandshakeValidatorTests` держит field validation и reject-code mapping на уровне handshake validator
- `WebDriverBridgeHandshakeSkeletonTests` теперь является runnable contract-layer для validator и message-factory semantics, без дублирования transport-integration веток
- `WebDriverBridgeServerStateTests` и `WebDriverBridgeServerStateSkeletonTests` держат owner-state semantics, cleanup и health counters
- `WebDriverBridgeServerSkeletonTests` держит live transport integration для websocket handshake, tab lifecycle, `SendRequestAsync` guard contract, inbound response validation, late-response semantics, первые per-command payload contracts для `GetTitle`, `GetUrl`, `GetContent`, `GetWindowBounds`, `ResolveElementScreenPoint`, `DebugPortStatus` и richer `DescribeElement`, pending request completion and failure, timeout/cancel semantics, duplicate session, first-message policy, second handshake и close edge cases, а также secure transport coverage для WSS handshake, reject при неверном secret и health-среза secure transport
- `BridgeTestHelpers` централизует вспомогательные harness-методы для websocket и health в bridge-related test files

Внутри `BridgeServer` поверх raw `SendRequestAsync` уже существует минимальный command-aware helper layer для `GetTitle`, `GetUrl`, `GetContent`, `GetWindowBounds`, `ActivateWindow`, `CloseWindow`, `ResolveElementScreenPoint`, `DebugPortStatus` и `DescribeElement`. Это пока internal runtime-surface, а не публичный API верхнего уровня.

Поверх этого helper layer теперь существует отдельный internal `BridgeCommandClient`, а ещё выше появился page-bound internal `PageBridgeCommandClient`, который один раз связывает `session/tab` context и позволяет `WebPage` и `WebWindow` читать bridge-backed metadata и отправлять window-bound команды без утечки `sessionId` и `tabId` в публичный API.

Такое разбиение нужно сохранять и дальше:

- contract-level проверки должны оставаться рядом с validator, factory и owner state
- transport-specific handshake и teardown coverage не стоит возвращать в contract-level test files

## Путь проверки в реальном браузере

Тесты реального браузера в `Tests/Atom.Net.Browsing.WebDriver.Tests` не включаются по умолчанию. Для них требуется явное переопределение окружения:

- `ATOM_TEST_WEBDRIVER_BROWSER` — имя браузера: `chrome`, `edge`, `brave`, `opera`, `vivaldi`, `yandex` или `firefox`
- `ATOM_TEST_WEBDRIVER_BROWSER_PATH` — опциональный путь к исполняемому файлу, если нужен не автоматически найденный браузер
- `ATOM_TEST_WEBDRIVER_HEADLESS` — опциональное переопределение headless-режима: `true` или `false`

Для branded Google Chrome Stable на Linux discovery-bootstrap теперь дополнительно материализует системный managed-policy target по пути `/etc/opt/chrome/policies/managed/atom-webdriver-extension.json`.

- локальный `chromium.managed-policy.json` внутри temp-профиля сохраняется как диагностический артефакт и источник JSON для публикации
- если процесс уже имеет права записи в системный каталог, policy публикуется напрямую
- если прямой записи нет, runtime пробует best-effort публикацию через `sudo`, используя `ATOM_WEBDRIVER_ROOT_PASSWORD` либо legacy `ESCORP_ROOT_PASSWORD`
- если системная публикация не удалась, это видно в `profile.json` через `bridge.managedPolicyPublishPath` и `bridge.managedPolicyDiagnostics`

Важно разделять два независимых условия:

- system-policy publish и trust bootstrap для branded Chrome Stable на Linux всё ещё могут требовать системные права, если у процесса нет прямой записи в `/etc/opt/chrome/policies/managed`
- после того как policy уже опубликована и certificate trust уже устроен, сам browser-side transport больше не зависит от нового root-вмешательства: текущий post-delivery channel для этого профиля идёт через отдельный WSS transport

Именно этот transport blocker и был финальным live-блокером в текущей ветке: старый plain ws путь закрывался со стороны клиента до server-side upgrade, а переход на WSS снял этот сбой, не отменяя саму необходимость managed-policy bootstrap для branded Chrome Stable

Рекомендованный безопасный путь проверки для текущей ветки:

1. Сначала прогнать обычный `Atom.Net.Browsing.WebDriver.Tests` без переопределений и убедиться, что bridge- и runtime-слой unit/integration тестов зелёный.
2. Затем включить только переопределения для реального браузера и прогнать `WebDriverRealBrowserIntegrationTests` либо полный проект тестов.
3. Интерпретировать эти tests как проверку запуска браузера, materialization профиля, времени жизни процесса, discovery-bootstrap текущей вкладки и базовой page/window-surface, а не как уже полную DOM-паритетную замену локального transport.

Актуально подтверждённый live-срез для этого пути сейчас такой:

- `RealBrowserLaunchBootstrapsExtensionBackedDiscoverySurface` подтверждает discovery-bootstrap и первичную привязку extension-backed surface
- `LaunchAsyncMaterializesRequestedRealBrowserProfileAndHeadlessMode` подтверждает materialization профиля и запуск branded Chrome Stable в ожидаемом режиме
- `RealBrowserLaunchKeepsWindowAndPageSurfaceOperational` подтверждает рабочие page/window команды поверх уже поднятого extension-backed канала
- `RealBrowserWindowActivateAndCloseSurfaceStaysOperational` подтверждает, что window activate/close surface остаётся рабочим поверх того же transport-контура

Отдельно на серверном контрактном слое теперь зафиксировано, что secure transport:

- принимает WSS handshake только с корректным secret из `transportUrl`
- отвергает upgrade с неверным secret ещё до websocket acceptance
- публикуется в health payload отдельным `secureTransport` блоком

Текущий живой smoke для реального браузера уже держится в коротком бюджете: ожидание завершения browser process после `DisposeAsync` в `WebDriverRealBrowserIntegrationTests` ужато до 5 секунд и остаётся зелёным на Chrome в headless-режиме.

Теперь тот же 5-секундный default выставлен и для `BridgeSettings.RequestTimeout`, но его всё равно нужно трактовать как часть мостового контракта, а не как обычный test-tuning:

- default живёт в `BridgeSettings.RequestTimeout` и теперь равен 5 секундам
- значение сериализуется в handshake accept payload как `RequestTimeoutMs`
- server-side `BridgeServer` использует тот же budget для pending request completion и timeout-маркировки
- contract и handshake tests уже явно проверяют текущее значение по умолчанию

После перехода default `BridgeSettings.RequestTimeout` к 5 секундам безопасная проверка выглядит так:

1. Держать отдельным контуром real-browser smoke для запуска браузера и базовой page/window-surface.
2. Держать отдельным контуром handshake и contract assertions в `WebDriverProtocolSurfaceTests`, `WebDriverBridgeHandshakeSkeletonTests`, `WebDriverBridgeHandshakeValidatorTests` и `BridgeTestHelpers`, потому что они подтверждают уже именно протокольное значение default timeout.
3. Отдельно перепроверять `WebDriverBridgeServerSkeletonTests`, где timeout-oriented сценарии уже используют локальные override-значения `200ms` и потому проверяют server-side timeout semantics независимо от нового default.

Это важно, потому что live-срез `BridgeServer` уже проверяет server-side handshake и state model, а Chromium bootstrap теперь доведён до discovery-tab и direct page/window commands, но полный bridge-транспорт на стороне браузера всё ещё не доведён до полной reference parity.

Актуальный список оставшихся пробелов bridge-bootstrap и рекомендованный порядок следующих этапов реализации зафиксирован в [Framework/Atom.Net.Browsing.WebDriver/STAGED_RUNTIME_BOUNDARY.md](Framework/Atom.Net.Browsing.WebDriver/STAGED_RUNTIME_BOUNDARY.md).

Ниже описан уже не только целевой, но и частично активный bootstrap path. Сейчас live runtime гарантированно покрывает discovery и initial page/window binding для Chromium, а DOM и richer command surface ещё остаются следующим этапом:

- `LaunchAsync` копирует расширение во временную директорию и записывает внутренний `config.json` для transport bridge между драйвером и extension
- `background.runtime.js` при старте читает `config.json` через `fetch(runtime.getURL("config.json"))`
- для Linux Chrome Stable с system-policy bootstrap `config.json` может нести отдельный `transportUrl`, и background runtime использует его как приоритетный browser-side channel
- Расширение открывает discovery-вкладку и регистрирует WebSocket-каналы для каждой вкладки
- `content.js` устанавливает eval bridge через MutationObserver для выполнения JS в контексте страницы

Для текущего WSS-пути это означает следующее разделение ролей:

- HTTP остаётся точкой discovery, health и debug-event
- HTTPS managed delivery остаётся путём доставки manifest и CRX для system-policy сценария
- WSS используется как выделенный browser-side transport channel после доставки и bootstrap
- secret из `transportUrl` валидируется сервером ещё на этапе upgrade, до принятия websocket-сессии

## Расширение

Единая кодовая база для Chrome (MV3) и Firefox (MV2):

- `ExtensionRuntime/` — исходники runtime и packaging templates
- `Extension/` и `Extension.Firefox/` в `bin` — build output каталоги расширений для Chrome MV3 и Firefox MV2

Новый scaffold ExtensionRuntime уже привязан к обычной сборке проекта: typecheck, сборка background runtime и синхронизация рабочих каталогов выполняются на уровне csproj, а build-owned артефакты живут в промежуточном каталоге и попадают в `bin`, а не в исходники

Полифилл `const browser = globalThis.browser ?? globalThis.chrome;` обеспечивает кроссбраузерность API.

## Перехват запросов

В active runtime события `Request` и `Response` уже работают как live extension-backed interception surface на уровнях page/window/browser для fetch и других subresource-запросов. В headless Firefox Developer Edition этот срез подтверждён реальными тестами: `Continue`, `Abort`, `Redirect`, `Fulfill`, URL-pattern filtering, request header mutation и response header mutation проходят end-to-end через `BridgeServer` и `ExtensionRuntime`, включая bubbling между page/window/browser.

Это и есть текущий production-ready contract slice пакета для заявленной цели Firefox Dev headless. Он покрывает active request-side и response-side decision semantics, outer scope inheritance и per-tab/page-context locality. Неподтверждённые зоны теперь уже относятся не к самому fetch/subresource interception pipeline, а к отдельным сценариям вроде main-frame navigation fulfill и к кроссбраузерной parity вне Firefox Developer Edition.

В этот validated slice теперь входят и Client Hints: active payload contract сериализует high-entropy поля, `ExtensionRuntime` покрывает low/high entropy HTTP mutation и JS-side `navigator.userAgentData`, а live Firefox Developer Edition headless loopback test подтверждает исходящие `Sec-CH-UA`, `Sec-CH-UA-Full-Version-List`, `Sec-CH-UA-Platform`, `Sec-CH-UA-Platform-Version`, `Sec-CH-UA-Mobile`, `Sec-CH-UA-Arch`, `Sec-CH-UA-Model` и `Sec-CH-UA-Bitness`.

### Подмена ответа для main_frame

- Описание ниже относится к отдельному special-case сценарию и не отменяет того факта, что fetch/subresource interception уже живой и подтверждённый.
- В active extension runtime request-side `FulfillAsync` для `main_frame` не считается production-ready и не должен использоваться как “честный” synthetic navigation response.
- Firefox WebExtensions `webRequest.onBeforeRequest` умеет только `cancel` или `redirectUrl`, а `filterResponseData` работает уже на стадии response stream. Поэтому current runtime не заявляет main-frame fulfill parity без сетевого шага к origin.
- Local bridge `/fulfill/{id}` — это внутренний buffering contract между C# bridge и extension runtime для хранения prepared body, а не доказательство прямой browser-side main_frame delivery. Для request-side `main_frame` активный runtime этот URL не consumes как fallback и fail-closed завершает запрос на browser webRequest boundary.
- Из этого следует жёсткое правило: если для `main_frame` критично отсутствие сетевого запроса к узлу назначения, текущий extension-backed runtime такой fulfill не поддерживает. Fetch/subresource fulfill остаётся поддержанным, а main-frame navigation fulfill следует считать неподдерживаемым сценарием, а не fallback-фичей.

### Прокси на уровнях браузера, окна и страницы

```csharp
var proxy = new WebProxy("http://proxy.example.com:8080");

await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
{
    Proxy = proxy,
});

var proxiedPage = await browser.CurrentWindow.OpenPageAsync(
    new WebPageSettings { Proxy = proxy });
await proxiedPage.NavigateAsync(new Uri("https://example.com"));

var proxiedWindow = await browser.OpenWindowAsync(
    new WebWindowSettings { Proxy = proxy });
await proxiedWindow.NavigateAsync(new Uri("https://example.com"));
```

Proxy с логином и паролем тоже поддерживается:

```csharp
var authenticatedProxy = new WebProxy("http://proxy.example.com:8080")
{
    Credentials = new NetworkCredential("user", "pass"),
};
```

## Browser profiles and devices

Внешний orchestration-слой теперь можно строить целиком внутри пакета WebDriver, без вынесения profile/device surface в родительский контракт.

### Запуск по профилю браузера

```csharp
var profile = WebBrowserProfile.Brave;

await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
{
    Profile = profile,
    Args = ["--no-sandbox"],
    Device = Device.DesktopFullHd,
});
```

`WebBrowserProfile` здесь отвечает только за выбор бинарника, канала и runtime profile path. Временные файлы профиля materialize-ятся внутри `LaunchAsync` уже после того, как собраны все `WebBrowserSettings` и `Device`-данные.

При materialization драйвер теперь не ограничивается одним `profile.json`: под каждый browser family заранее раскладываются automation-oriented profile files. Для Chromium-профилей создаются `Default/Preferences`, `Local State` и `First Run` с отключёнными welcome/FRE, background networking, sync, autofill, translate, Safe Browsing и password-manager фичами. Для Firefox создаётся `user.js` с отключёнными telemetry/new tab/discovery/pocket/GPU-heavy флагами и с базовой automation-конфигурацией. Browser-specific ветки тоже учитываются: например, Edge получает anti-FRE disable-features, а Vivaldi — pre-seeded startup/welcome prefs.

Для Firefox на Linux это ограничение принципиальное для release Stable: неподписанное profile-local расширение обычно не активируется. Драйвер намеренно не переводит этот bootstrap на Marionette, потому что такой обход меняет JS-видимое automation state страницы, включая `navigator.webdriver`.

Для Firefox Stable runtime теперь поддерживает отдельный install overlay: если задать путь к подписанному XPI через `ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH`, драйвер поднимает локальную shim-installation внутри materialized profile, кладёт туда `distribution/policies.json`, force-install'ит signed XPI через `ExtensionSettings` и передаёт session config через `3rdparty -> Extensions -> <addon-id>`, который background runtime читает из `storage.managed`. Сам подписанный XPI runtime не модифицирует, поэтому пакет должен быть заранее собран и подписан из той же версии extension source.

Если на машине уже существует глобальный `/etc/firefox/policies/policies.json` c тем же addon id, Firefox Stable может предпочесть системную policy и проигнорировать overlay policy из install shim. Runtime теперь сохраняет такое пересечение в `bridge.managedPolicyDiagnostics.detail`, но сам конфликт нужно устранять на уровне host policy: удалить или синхронизировать системную запись.

Если signed XPI не задан, Linux Firefox Stable остаётся в диагностическом profile-local режиме, а для живой проверки extension-backed bootstrap по-прежнему рекомендуются Firefox Developer Edition или Nightly.

### Автоматическая упаковка и подпись Firefox XPI

Сборка `Framework/Atom.Net.Browsing.WebDriver` теперь умеет автоматически материализовать unsigned Firefox package в `obj/<Configuration>/<TargetFramework>/extension-packages/firefox/atom-webdriver-firefox-<version>-unsigned.zip`. Для локального прогона можно использовать задачу VS Code `package webdriver firefox xpi` или прямой `dotnet build -c Debug -p:CreateFirefoxExtensionPackageOnBuild=true`. Для `Release` в этом репо по-прежнему нужны доступные приватные NuGet-источники WebDriver-зависимостей.

Для автоматической подписи добавлен ручной workflow `.github/workflows/webdriver-firefox-sign.yml`. По умолчанию он собирает `Debug`, чтобы не зависеть от внешнего feed, ожидает Mozilla AMO API credentials в секретах `MOZILLA_AMO_JWT_ISSUER` и `MOZILLA_AMO_JWT_SECRET`, подписывает Firefox build output через `web-ext sign --channel unlisted`, выгружает готовый signed XPI как artifact и при желании может сразу запустить live smoke на Linux Firefox Stable через `ATOM_WEBDRIVER_FIREFOX_SIGNED_XPI_PATH`.

Для локальной подписи добавлен helper `Framework/Atom.Net.Browsing.WebDriver/ExtensionRuntime/scripts/sign-firefox-package.sh`. Он повторяет тот же pipeline, но читает credentials из переменных окружения `WEB_EXT_API_KEY` и `WEB_EXT_API_SECRET`, сам вызывает `dotnet build -p:CreateFirefoxExtensionPackageOnBuild=true`, затем отправляет текущую Firefox-версию в AMO через `web-ext sign` без встроенного скачивания и сам доводит процесс до конца через AMO API: ждёт нужную unlisted-версию по текущему `manifest.version`, скачивает signed XPI в `obj/<Configuration>/<TargetFramework>/extension-packages/firefox/signed` и умеет восстановиться, если тот же upload уже был отправлен раньше. Если нужно, окно ожидания можно переопределить через `--approval-timeout <ms>`. Его можно запускать напрямую или через `npm --prefix Framework/Atom.Net.Browsing.WebDriver/ExtensionRuntime run sign:firefox -- --configuration Debug --target-framework net10.0`. Для удобства в VS Code добавлена задача `sign webdriver firefox xpi local`, а workflow подписи теперь использует тот же helper, чтобы локальный и CI-пути не расходились.

Это не локальная CA-подпись: Firefox Stable доверяет только подписи, выданной Mozilla signing service через AMO API. Поэтому automation pipeline может быть полностью нашим, но шаг trust/signing всё равно проходит через Mozilla credentials.

Поверх browser-family defaults в materialized profile теперь также подмешиваются runtime overrides из `WebBrowserSettings` и `Device`: Chromium effective arguments получают `--proxy-server`, `--lang` и `--user-agent`, а Firefox `user.js` получает proxy prefs, `general.useragent.override`, `intl.accept_languages` и `browser.privatebrowsing.autostart`, если это было запрошено настройками запуска.

### Применение device preset ко всем новым вкладкам

```csharp
await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
{
    Profile = WebBrowserProfile.Brave,
    Device = Device.Pixel2,
});
```

### Применение device preset только к новой вкладке

```csharp
var page = await browser.CurrentWindow.OpenPageAsync(new WebPageSettings
{
    Device = Device.MacBookPro14,
});
await page.NavigateAsync(new Uri("https://example.com"));
```

### Тонкая настройка полного fingerprint-профиля

```csharp
var device = Device.DesktopFullHd;
device.Locale = "de-DE";
device.Timezone = "Europe/Berlin";
device.Languages = ["de-DE", "de", "en-US"];
device.Geolocation = new GeolocationSettings
{
    Latitude = 52.5200,
    Longitude = 13.4050,
    Accuracy = 20,
};
device.NetworkInfo = new NetworkInfoSettings
{
    EffectiveType = "wifi",
    Type = "wifi",
    Downlink = 80,
    Rtt = 12,
};

var page = await browser.CurrentWindow.OpenPageAsync(new WebPageSettings
{
    Device = device,
});
```

`Device` теперь описывает не только viewport/mobile/touch, но и основные fingerprint-группы: `UserAgent`, `Platform`, `Locale`, `Timezone`, `Languages`, `Screen`, `ClientHints`, `Geolocation`, `NetworkInfo`, `WebGL`, `WebGLParams`, `SpeechVoices`, `VirtualMediaDevices`, а также privacy/noise-переключатели вроде `CanvasNoise`, `AudioNoise`, `FontFiltering`, `DoNotTrack` и `GlobalPrivacyControl`.

### Навигация с локальной HTML-подстановкой

```csharp
await page.NavigateAsync(
    new Uri("https://example.com"),
    new NavigationSettings
    {
        Html = "<html><body>Hello from local fulfill</body></html>",
    });
```

### Матрица возможностей: Chromium vs Firefox

| Возможность | Chromium (MV3) | Firefox (MV2) |
| ----------- | :------------: | :-----------: |
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
| Client Hints (HTTP) | `declarativeNetRequest` Sec-CH-UA-* | `webRequest.onBeforeSendHeaders` Sec-CH-UA-* |
| Client Hints (JS) | `navigator.userAgentData` override | `navigator.userAgentData` override |
| Network Information | `navigator.connection` override | `navigator.connection` override |
| Speech Synthesis | `speechSynthesis.getVoices()` override | `speechSynthesis.getVoices()` override |
| Media Devices | `enumerateDevices()` fake set | `enumerateDevices()` fake set |
| Virtual Media Devices | Tab-local alias `enumerateDevices()` + routed `getUserMedia()` к browser-visible audio/video устройствам; audio output alias-only | Tab-local alias `enumerateDevices()` + routed `getUserMedia()` к browser-visible audio/video устройствам; audio output alias-only |

Для Client Hints active readiness now means end-to-end, а не только наличие API surface: .NET payload contract, background request-header mutation, content/main-world `navigator.userAgentData` override и live Firefox Developer Edition wire proof закрыты одновременно.
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
