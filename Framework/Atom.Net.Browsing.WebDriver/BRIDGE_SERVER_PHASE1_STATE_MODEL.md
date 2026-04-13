# Модель состояния BridgeServer для этапа 1

Документ фиксирует первый этап реализации нового транспортного узла BridgeServer в текущей ветке.

Этап 1 не пытается сразу перенести bootstrap расширения, producer обратных вызовов или паритет перехвата. Цель этапа — ввести устойчивую серверную модель состояния, поверх которой затем можно безопасно подключать handshake и WebSocket-транспорт.

## Границы этапа

Входит в этап 1:

- серверная модель владения для browser session
- реестр каналов вкладок
- реестр ожидающих запросов
- семантика очистки при disconnect и явном teardown
- снимки состояния для health и debug

Не входит в этап 1:

- упаковка расширения
- discovery-bootstrap вкладки
- интеграция content/background
- page-side hooks для обратных вызовов
- паритет request/response с reference-расширением

## Центральный владелец

Главный изменяемый владелец — BridgeServerState.

Этот тип должен владеть:

- реестр session по session id
- индекс вкладок по tab id
- реестр ожидающих запросов по message id
- счётчики снимка состояния health

BridgeServerState не должен знать про HttpListener, WebSocket-upgrade или JSON wire format. Его зона ответственности — только владение, очистка и связанный lookup.

## Предлагаемый API

Минимальный API owner-слоя:

- CreateSession
- RemoveSession
- TryGetSession
- RegisterTab
- UnregisterTab
- TryGetTab
- GetTabsForSession
- AddPendingRequest
- TryCompletePendingRequest
- TryFailPendingRequest
- FailRequestsForSession
- FailRequestsForTab
- CreateHealthSnapshot
- CreateSessionSnapshot

Правило API:

- Descriptor — входные DTO
- Result — результат операции изменения
- Snapshot — DTO только для чтения в health и debug

## Result Types And DTO

Рекомендуемый словарь типов:

- BridgeSessionDescriptor
- BridgeTabChannelDescriptor
- BridgePendingRequestDescriptor
- SessionCreateResult
- SessionRemovalResult
- TabRegistrationResult
- TabRemovalResult
- PendingRequestAddResult
- PendingRequestCompletionResult
- BulkFailureResult
- BridgeServerHealthSnapshot
- BridgeBrowserSessionSnapshot
- BridgeTabChannelSnapshot
- BridgePendingRequestSnapshot

Ожидаемые result outcomes:

- session: Created, DuplicateSessionId, InvalidDescriptor, SessionNotFound, Removed
- tab: Registered, SessionNotFound, DuplicateTabId, AlreadyOwnedBySession, TabNotFound, TabOwnedByAnotherSession
- pending request: Added, DuplicateMessageId, SessionNotFound, TabNotFound, Completed, RequestNotFound, AlreadyCompleted

## Internal Runtime Types

### BridgeBrowserSession

BridgeBrowserSession — внутренний owner для одного browser-side transport подключения.

Минимальные поля:

- SessionId
- ProtocolVersion
- ConnectedAtUtc
- LastSeenAtUtc
- BrowserFamily
- IsConnected
- ChannelsByTabId

Правила:

- session владеет своими tab channels
- session не владеет pending requests напрямую; это owner BridgeServerState
- session не должна иметь собственный lock, чтобы не конкурировать с owner gate

### BridgeTabChannel

BridgeTabChannel — внутренний адресуемый endpoint для одной вкладки внутри session.

Минимальные поля:

- SessionId
- TabId
- WindowId
- RegisteredAtUtc
- LastSeenAtUtc
- IsRegistered

Опциональные поля для следующего transport slice:

- OutboundQueue
- PendingOutboundCount
- LastRequestId

Правила:

- tab channel никогда не живёт без owning session
- tab channel не является главным owner; tab index сервера хранит только shortcut на него
- remove session всегда удаляет все её channels без отдельного внешнего прохода

### BridgePendingRequest

BridgePendingRequest — внутреннее ожидание correlated response.

Минимальные поля:

- MessageId
- SessionId
- TabId
- CreatedAtUtc
- DeadlineUtc
- CompletionSource
- IsCompleted

Правила:

- pending request регистрируется только для существующего channel
- completion возможен только один раз
- cleanup session обязан завершать все её pending requests со статусом Disconnected

### Ownership Graph

Owner graph phase 1:

- BridgeServerState
  - sessions by session id
  - tab shortcut index by tab id
  - pending requests by message id
- BridgeBrowserSession
  - owned BridgeTabChannel items
- BridgeTabChannel
  - только identity и routing metadata, без владения другими runtime-типами

## Правила владения

- Один live session id соответствует только одной live browser session
- Один live tab id принадлежит только одной live session
- Один live message id принадлежит только одному pending request
- Session-владелец tab channel остаётся единственным источником истины; tab index служит только shortcut для lookup
- RemoveSession обязан атомарно очищать свой subtree: tabs и pending requests

## Правила конкурентности

- Изменяемое состояние координируется single-writer mailbox внутри BridgeServerState
- Внешний API owner-слоя асинхронный и сериализует операции изменения через loop на основе канала
- Типы session и tab channel не должны иметь собственную независимую синхронизацию
- Завершение pending request идёт только через методы owner-слоя
- Snapshots не возвращают live collections и не дают мутировать runtime state снаружи

## Текущие заметки по реализации

Этап 1 уже реализован как отдельный owner-layer поверх channel-based single writer.

- BridgeServerState обслуживает async operations через mailbox loop
- vocabulary Descriptor, Result и Snapshot вынесен в отдельные файлы под каталог [Framework/Atom.Net.Browsing.WebDriver/State](Framework/Atom.Net.Browsing.WebDriver/State)
- health snapshot уже используется live BridgeServer health endpoint и публикует sessions, tabs, pendingRequests, completedRequests и failedRequests
- cleanup session после disconnect и protocol-invalid teardown снимает session subtree и обновляет observable counters

## Минимальная test-matrix

Обязательные tests первого этапа:

- CreateSessionRejectsDuplicateSessionId
- RegisterTabRejectsDuplicateTabOwnedByAnotherSession
- AddPendingRequestRejectsDuplicateMessageId
- CompletePendingRequestSucceedsOnce
- RemoveSessionRemovesOwnedTabsAndRequests
- HealthSnapshotReflectsCounts

Следующий слой tests после базового этапа:

- RegisterTabFailsWhenSessionMissing
- UnregisterTabRejectsForeignOwner
- RemoveSessionReturnsNotFoundForUnknownId
- RemoveSessionFailsOwnedPendingRequestsAsDisconnected
- FailedPendingRequestsDoNotStayInHealthSnapshot
- SessionCleanupIsIdempotentAtStateLayer
- ReplayedUnregisterAfterSessionCleanupDoesNotCorruptState

## Критерии выхода из этапа

Модель состояния этапа 1 считается готовой, когда:

- BridgeServerState существует как отдельный тип owner-слоя
- session/tab/pending ownership больше не размазаны по самому BridgeServer
- cleanup session завершает tabs и pending requests без утечек
- health snapshot строится из owner state без знания transport деталей
- минимальная test matrix проходит без WebSocket и интеграции расширения

## Что идёт дальше

После фиксации state model следующий шаг — handshake contract и transport loops поверх уже существующего owner-layer.

То есть порядок такой:

1. State model
2. Handshake contract
3. WebSocket endpoint and keepalive
4. Tab channel transport routing
5. Event and command parity migration
