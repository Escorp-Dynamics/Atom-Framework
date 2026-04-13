# Структура runtime-расширения

Документ фиксирует первую файловую структуру для browser extension runtime, который должен подключаться к уже существующим server-side контрактам BridgeServer.

Опорные документы:

- [README.md](README.md)
- [BRIDGE_SERVER_PHASE1_STATE_MODEL.md](BRIDGE_SERVER_PHASE1_STATE_MODEL.md)
- [BRIDGE_SERVER_HANDSHAKE_CONTRACT.md](BRIDGE_SERVER_HANDSHAKE_CONTRACT.md)
- [STAGED_RUNTIME_BOUNDARY.md](STAGED_RUNTIME_BOUNDARY.md)

Источником истины для live surface остаётся README текущего пакета. Legacy-дерево WebDriver.Reference внутри репозитория больше не используется.

## Инварианты интерфейсов

1. Внешний контракт background, content и page слоёв не зависит от браузера
2. Browser-specific различия уходят только в platform adapters, packaging и permissions
3. Shared protocol остаётся единым для Chromium- и Firefox-реализаций
4. Переход со staged runtime на live extension transport не должен менять public contract у WebBrowser, WebWindow и WebPage
5. Любая browser-specific реализация обязана реализовывать тот же внешний интерфейс, что и базовая абстракция

## Корневые каталоги

Предлагаемая структура первого прохода:

- ExtensionRuntime
  - Shared
    - Protocol
    - Config
    - Diagnostics
  - Background
    - Session
    - Transport
    - Tabs
    - Routing
    - Diagnostics
  - Content
    - Channel
    - Commands
    - Navigation
    - Diagnostics
  - Page
    - Hooks
    - Safety
  - Platform
    - Chromium
    - Firefox
  - Packaging
    - Chrome
    - Firefox

## Shared

### Shared/Protocol

Файлы первого прохода:

- BridgeMessage
- BridgeCommand
- BridgeStatus
- BridgeEventName
- BridgeMessageSerializer
- TransportEnvelopeValidator

Инвариант:

- Ни один browser-specific слой не меняет shape этих типов

### Shared/Config

Файлы первого прохода:

- RuntimeConfig
- RuntimeFeatureFlags
- RuntimeConfigLoader
- RuntimeConfigValidator

Инвариант:

- Config handoff одинаков для всех browser adapters

### Shared/Diagnostics

Файлы первого прохода:

- DiagnosticsSink
- RuntimeTraceBuffer
- HealthSnapshot

Инвариант:

- Формат логов и диагностических событий одинаков для всех адаптеров

## Background

### Background/Session

Файлы первого прохода:

- SessionCoordinator
- HandshakeClient
- SessionHealthReporter
- SessionLifecycleState

Ключевые интерфейсы:

- ISessionCoordinator
- IHandshakeClient
- ISessionHealthReporter

Инвариант:

- Session lifecycle и handshake правила одинаковы для всех браузеров

### Background/Transport

Файлы первого прохода:

- BridgeTransportClient
- OutboundSendQueue
- InboundReceiveLoop
- RequestCorrelationStore
- KeepAliveController

Ключевые интерфейсы:

- IBridgeTransportClient
- IRequestCorrelationStore
- IKeepAliveController

Инвариант:

- Browser-specific адаптер не влияет на transport contract между extension и BridgeServer

### Background/Tabs

Файлы первого прохода:

- TabRegistry
- TabDiscoveryService
- TabContextPublisher
- TabRuntimeEndpoint

Ключевые интерфейсы:

- ITabRegistry
- ITabDiscoveryService
- ITabContextPublisher

Инвариант:

- На выходе всегда одинаковые tabId, windowId и readiness semantics

### Background/Routing

Файлы первого прохода:

- CommandRouter
- EventRouter
- RouteFailurePolicy

Ключевые интерфейсы:

- ICommandRouter
- IEventRouter
- IRouteFailurePolicy

Инвариант:

- Routing не знает, Chromium это или Firefox; он работает только с tab endpoints

### Background/Diagnostics

Файлы первого прохода:

- BackgroundDiagnosticsSink
- DebugPortStatusProvider

Инвариант:

- Диагностика должна собираться одинаково независимо от browser adapter

## Content

### Content/Channel

Файлы первого прохода:

- ContentRuntimeChannel
- ContentDispatchLoop
- ContentReadySignal

Ключевые интерфейсы:

- ITabRuntimeChannel
- IContentDispatchLoop
- IContentReadySignal

Инвариант:

- Background всегда видит одинаковый контракт tab-local канала

### Content/Commands

Файлы первого прохода:

- CommandDispatchTable
- PageInfoCommandExecutor
- WindowGeometryCommandExecutor
- ElementGeometryCommandExecutor
- ElementDescriptionCommandExecutor
- DebugStatusCommandExecutor

Ключевые интерфейсы:

- ICommandDispatchTable
- IContentCommandExecutor

Инвариант:

- Командные executors обязаны возвращать payload shape, совместимый с уже существующим BridgeServer contract slice

### Content/Navigation

Файлы первого прохода:

- NavigationEventSource
- NavigationOrderingPolicy
- PageLifecycleSnapshotStore

Ключевые интерфейсы:

- INavigationEventSource
- IEventOrderingPolicy
- IPageLifecycleSnapshotStore

Инвариант:

- Порядок RequestIntercepted -> ResponseReceived -> DomContentLoaded -> NavigationCompleted -> PageLoaded сохраняется одинаково во всех браузерах

### Content/Diagnostics

Файлы первого прохода:

- ContentDiagnosticsSink
- ContentRuntimeStatus

Инвариант:

- Background и host должны получать одинаково сформулированные runtime события от content слоя

## Page

### Page/Hooks

Файлы второй фазы:

- PageHookInstaller
- CallbackProxyRegistry
- PageBridgeEmitter

Ключевые интерфейсы:

- IPageHookInstaller
- ICallbackProxyRegistry
- IPageBridgeEmitter

Инвариант:

- SubscribeAsync и UnSubscribeAsync работают одинаково, даже если реализация hook слоя browser-specific

### Page/Safety

Файлы второй фазы:

- InjectionGuard
- HookCleanupCoordinator
- PageScriptVersionMarker

Инвариант:

- Повторная инъекция, reload и tab close не меняют внешний callback contract

## Platform

### Platform/Chromium

Файлы первого прохода:

- ChromiumManifestComposer
- ChromiumPermissionProfile
- ChromiumTabDiscoveryAdapter

### Platform/Firefox

Файлы первого прохода:

- FirefoxManifestComposer
- FirefoxPermissionProfile
- FirefoxTabDiscoveryAdapter

Инвариант platform слоя:

- Platform слой может менять внутреннюю механику manifest, permissions и tab-discovery
- Platform слой не меняет внешние интерфейсы Session, Transport, Tabs, Routing, Commands и Navigation

## Packaging

### Packaging/Chrome

Файлы первого прохода:

- ChromePackageProfile
- ChromeBundleManifest

### Packaging/Firefox

Файлы первого прохода:

- FirefoxPackageProfile
- FirefoxBundleManifest

Инвариант:

- Packaging различается по браузерам, но не протекает в runtime contracts

## Минимальный набор файлов первого прохода

Если сокращать до самого первого рабочего набора, создавать стоит только это:

- RuntimeConfig
- RuntimeConfigLoader
- BridgeMessage
- BridgeCommand
- BridgeStatus
- BridgeMessageSerializer
- SessionCoordinator
- HandshakeClient
- BridgeTransportClient
- RequestCorrelationStore
- TabRegistry
- CommandRouter
- ContentRuntimeChannel
- ContentDispatchLoop
- CommandDispatchTable
- PageInfoCommandExecutor
- WindowGeometryCommandExecutor
- ElementGeometryCommandExecutor
- ElementDescriptionCommandExecutor
- DebugStatusCommandExecutor
- NavigationEventSource
- DiagnosticsSink

## Что не надо создавать в первом проходе

1. Полный Page/Hooks слой
2. Browser-specific page instrumentation глубже минимальных adapter файлов
3. Сложный interception rewrite engine
4. Глубокую browser-specific recovery state machine

## Ключевое правило проектирования

Снаружи всё выглядит как один и тот же extension runtime с единым protocol contract. Внутри Chromium и Firefox могут иметь разные manifest, permissions, tab-discovery и hook mechanics, но любые различия должны замыкаться в Platform и Packaging слоях, не ломая интерфейс Session, Transport, Tabs, Routing, Commands и Navigation
