# Staged Runtime Boundary

Документ фиксирует, что в текущем runtime-срезе уже считается стабильным контрактом перед redesign extension-архитектуры, а что пока остаётся временной локальной эмуляцией.

## Стабильный контракт

- Для одной навигации текущий runtime-срез публикует bridge-конверт в таком порядке:
  - `RequestIntercepted`
  - `ResponseReceived`
  - `DomContentLoaded`
  - `NavigationCompleted`
  - `PageLoaded`
- Один и тот же bridge-поток для одной transport-очереди доходит до `WebPage`, `WebWindow` и `WebBrowser` в одном порядке.
- `WebPage` уже поддерживает `SubscribeAsync` и `UnSubscribeAsync`, а для подписанного пути поднимает `Callback` и `CallbackFinalized`.
- Передача request, response и callback идёт асинхронно end-to-end; синхронная relay-обёртка больше не считается допустимой реализацией.
- Неявный Linux headless launch больше не поднимает auto-created virtual display: если внешний дисплей не задан, браузер идёт через native browser headless path.
- Явно переданный скрытый Linux-дисплей остаётся допустимым отдельным display-backed сценарием для trusted input и других hardware-bound проверок.
- Для Firefox Developer Edition headless live interception pipeline уже считается стабильным validated slice для fetch/subresource traffic: решения `Continue`, `Abort`, `Redirect` и `Fulfill` возвращаются обратно в браузерный transport и работают на page/window/browser scope.
- URL-pattern filtering, request header mutation, response header mutation и outer scope bubbling уже подтверждены real-browser тестами в Firefox Developer Edition.
- В validated request-header mutation slice теперь входят и Client Hints: active runtime покрывает low/high entropy `Sec-CH-UA-*`, JS-side `navigator.userAgentData` и живой Firefox Developer Edition loopback proof для исходящих request headers.

## Временная staged-реализация

- Producer для `Callback` и `CallbackFinalized` пока живёт в [Framework/Atom.Net.Browsing.WebDriver/Runtime/PageNavigationState.cs](Framework/Atom.Net.Browsing.WebDriver/Runtime/PageNavigationState.cs), а не в полноценном `BridgeServer` или background-pipeline расширения.
- Callback-producer сейчас распознаёт только ограниченный набор форм прямого вызова в `ExecuteScript`; это локальная эвристика, а не полноценное runtime-hooking поведение расширения.
- `RequestIntercepted` и `ResponseReceived` сейчас публикуются из локального transport-конверта навигации; это промежуточный паритет, а не доказательство полного network-level interception parity с reference-веткой.
- Общий live interception pipeline больше не является полностью локальной эмуляцией для Firefox Developer Edition: `BackgroundRuntimeHost` уже обслуживает direct `InterceptRequest`, а `BridgeServer` возвращает request/response decisions обратно в браузерный transport для validated fetch/subresource slice. Оставшийся gap относится к специальным navigation-level сценариям и к browser/channel parity за пределами Firefox Developer Edition.
- Chromium runtime уже поднимает discovery endpoint, materialize-ит `config.json`, стартует live `BridgeServer` и перевязывает текущие `WebPage` и `WebWindow` на реальный `tabId` и `windowId` после handshake.
- Полный bridge-bootstrap из reference-ветки всё ещё не завершён: page-side hook lifecycle, DOM command routing и общий browser-side event pipeline пока не являются источником этих событий для текущего runtime-среза.

## Правило для redesign

- Новая архитектура может переносить producer-слой из локального transport в extension/background/bridge server.
- Новая архитектура не должна менять публичную семантику уже существующего staged контракта без явного пересмотра тестов и документации.
- Если redesign заменяет локальную callback-эвристику на полноценный page-side hook, он должен сохранить минимум:
  - имена событий `Callback` и `CallbackFinalized`
  - модель подписки через `SubscribeAsync` и `UnSubscribeAsync`
  - порядок `Callback` перед `CallbackFinalized` для одного вызова
- Если redesign заменяет локальный navigation envelope на extension-originated transport, он должен сохранить минимум:
  - наличие `RequestIntercepted` и `ResponseReceived` до lifecycle-событий
  - порядок lifecycle `DomContentLoaded -> NavigationCompleted -> PageLoaded`

## Оставшиеся пробелы bridge-bootstrap

Текущее состояние уже покрывает server-side handshake, owner-state cleanup и health visibility, но до real-browser end-to-end bridge parity всё ещё остаются отдельные слои.

Что уже есть:

- server-side `BridgeServer` принимает websocket upgrade, валидирует first-message handshake, регистрирует session и публикует health snapshot
- `BridgeServerState` уже владеет session, tab и pending-request state через single-writer mailbox
- post-handshake loop уже маршрутизирует `TabConnected`, `TabDisconnected`, минимальную корреляцию outbound request, валидацию формы inbound response, settled late-response no-op semantics, первые command contracts для string payload (`GetTitle`, `GetUrl`, `GetContent`), rectangle payload (`GetWindowBounds`), point payload (`ResolveElementScreenPoint`) и richer object payload (`DebugPortStatus`, `DescribeElement` с дополнительными полями описания), а также propagation ошибок pending-request
- runnable bridge suites уже проверяют validator, factory, handshake transport и state cleanup semantics

Что ещё отсутствует в текущем runtime-срезе:

- bootstrap расширения на стороне браузера пока покрывает только initial discovery-tab и direct page/window command slice; это ещё не общий источник transport events для всего runtime
- post-handshake loop пока покрывает только минимальный routing slice; полноценный command surface и richer request/event matrix ещё не реализованы
- `WebBrowser`, `WebWindow` и `WebPage` пока не читают полный поток мостовых сообщений из browser-extension; staged navigation envelope остаётся локальной эмуляцией, хотя текущая discovery-вкладка уже получает page-bound bridge command adapter автоматически после handshake
- real-browser integration tests теперь подтверждают launch, profile и живой discovery-bootstrap текущей вкладки, но ещё не end-to-end DOM transport через extension
- `BridgeSettings.RequestTimeout` уже является частью протокольного контракта через handshake accept payload и server-side timeout semantics; после перехода default значения к 5 секундам это всё равно нельзя трактовать как обычное ужатие browser-smoke budget

Это и есть текущая практическая граница между уже стабильным contract slice и следующим implementation cut.

## Рекомендованные следующие проверки

- при продолжении bridge runtime расширять command surface поверх уже существующего minimal request routing
- держать отдельной проверкой handshake-related assertions и timeout-oriented skeleton tests после смены default `BridgeSettings.RequestTimeout`, пока живой extension-backed transport ещё не доведён до полного DOM end-to-end
- следующим cut переводить `EvaluateAsync` и базовые element-команды с локального transport на уже поднятый discovery-backed bridge path
- после этого расширять real-browser verification от discovery/page/window checks к настоящему extension-backed DOM path

## Что можно менять без ломки контракта

- Место, где рождаются bridge-сообщения
- Внутренние типы transport и bridge queue
- Способ bootstrap расширения и discovery
- Форму внутренних helper-логов и диагностической телеметрии

## Что нельзя менять молча

- Порядок уже опубликованного navigation bridge-конверта
- Семантику callback-подписки на странице
- Асинхронный характер relay-цепочки page -> window -> browser
- Разделение между implicit browser-native headless и explicit display-backed hidden launch
