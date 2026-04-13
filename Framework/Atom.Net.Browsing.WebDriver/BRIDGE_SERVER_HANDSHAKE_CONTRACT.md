# BridgeServer Handshake Contract

Документ фиксирует handshake browser session для нового транспортного узла BridgeServer в текущей ветке.

Handshake не регистрирует вкладки и не заменяет их жизненный цикл. Он только:

- аутентифицирует клиент на стороне браузера
- регистрирует browser session
- согласует транспортную политику

## Уровень handshake

Handshake — это обмен на уровне browser session.

Одно WebSocket-соединение должно пройти один handshake. После успешного handshake каналы вкладок регистрируются отдельными сообщениями жизненного цикла, а не через повторный handshake.

## Тип сообщения

Для handshake используется уже существующий `BridgeMessageType.Handshake`.

И клиент, и сервер обмениваются обычными `BridgeMessage`, где:

- `Type = Handshake`
- `Id` обязателен и используется для correlation
- `Command` и `Event` не используются

## Client Payload

Первое сообщение клиента после websocket upgrade обязано содержать payload со следующими обязательными полями:

- `sessionId: string`
- `secret: string`
- `protocolVersion: int`
- `browserFamily: string`
- `extensionVersion: string`

Опциональные поля:

- `browserVersion: string`
- `capabilities: object`

Правила валидации:

- `sessionId` не пустой и не whitespace
- `secret` не пустой и не whitespace
- `protocolVersion > 0`
- `browserFamily` не пустой и не whitespace
- `extensionVersion` не пустой и не whitespace
- неизвестные дополнительные поля допускаются как forward-compatible расширение

## Server Accept Payload

При успешном handshake сервер отвечает `BridgeMessage` с:

- `Type = Handshake`
- `Id` равен `Id` клиентского handshake
- `Status = Ok`
- `Error = null`

Payload accept-ответа:

- `sessionId: string`
- `negotiatedProtocolVersion: int`
- `requestTimeoutMs: int`
- `pingIntervalMs: int`
- `maxMessageSize: int`
- `serverTimeUnixMs: long` опционально

Сервер не должен возвращать секрет назад клиенту.

## Server Reject Payload

При reject сервер отвечает `BridgeMessage` с:

- `Type = Handshake`
- `Id` равен `Id` клиентского handshake, если correlation возможен
- `Status = Error`
- `Error` содержит короткий machine-readable код

Рекомендуемые коды reject:

- `неверные-данные`
- `отсутствует-секрет`
- `секрет-не-совпадает`
- `отсутствует-идентификатор-сеанса`
- `идентификатор-сеанса-уже-занят`
- `версия-протокола-не-поддерживается`

Опциональный payload reject-ответа:

- `retryable: bool`
- `supportedProtocolVersion: int?`

## First Message Rule

Первое websocket сообщение клиента должно быть handshake.

Если первым приходит `Request`, `Response`, `Event`, `Ping` или `Pong`, соединение считается protocol-invalid и не должно регистрировать session.

Повторный handshake на уже живом connection тоже считается protocol error.

Дополнительные транспортные правила текущего этапа:

- close frame до успешного handshake не регистрирует session
- invalid JSON frame после accept считается protocol-invalid и закрывает connection
- binary frame после accept считается protocol-invalid и закрывает connection

## Правило регистрации session

Handshake регистрирует только browser session.

После успешного handshake:

- session появляется в owner state
- connection считается transport-ready
- обычные request/event сообщения больше не обязаны нести `secret`
- минимальный live post-handshake loop уже поддерживает tab lifecycle, pending request correlation и propagation ошибок

После reject:

- session не создаётся
- health snapshot не увеличивает session count
- connection либо закрывается, либо переводится в unusable state без обычного command flow

После accept:

- второй handshake на том же websocket получает reject и immediate teardown
- normal close клиента должен снимать session из owner state без зависания на close acknowledgement

## Связь с жизненным циклом вкладки

Handshake не заменяет tab registration.

Следующий слой transport после handshake:

- `TabConnected` регистрирует `BridgeTabChannel`
- `TabDisconnected` снимает `BridgeTabChannel`

Таким образом transport ownership остаётся split-нутым:

- handshake → session ownership
- tab events → channel ownership

## Current Post-Handshake Runtime Slice

Текущий этап после accept уже маршрутизирует минимальный набор transport-semantics:

- outbound `Request` обязан содержать `message id`, `tab id` и `command`, и регистрируется как pending request только для живой tab ownership
- inbound `Response` обязан содержать `message id`, `tab id` и `status`, и завершает pending request только при корректной tab-correlation с зарегистрированным pending request
- late `Response` для уже settled request id не должен повторно завершать request и не должен сам по себе закрывать connection
- первый active per-command response contract уже включён: успешные `GetTitle`, `GetUrl` и `GetContent` responses обязаны нести строковый payload
- первый active non-string response contract уже включён: успешный `GetWindowBounds` response обязан нести объект с `left`, `top`, `width`, `height`
- следующий active non-string response contract уже включён: успешный `ResolveElementScreenPoint` response обязан нести объект с `viewportX` и `viewportY`
- следующий richer object contract уже включён: успешный `DebugPortStatus` response обязан нести минимум `tabId`, `hasPort`, `queueLength`, `hasSocket`, `interceptEnabled`
- следующий richer object contract уже включён: успешный `DescribeElement` response обязан нести минимум `tagName`, `isVisible`, `boundingBox`, а при наличии richer members дополнительно валидируются `checked`, `selectedIndex`, `isActive`, `associatedControlId`, `computedStyle` и `options`
- поверх raw request routing уже существует минимальный command-aware helper layer для `GetTitle`, `GetUrl`, `GetContent`, `GetWindowBounds`, `ResolveElementScreenPoint`, `DebugPortStatus` и `DescribeElement` на стороне `BridgeServer`, а поверх него уже есть internal `BridgeCommandClient` и page-bound internal `PageBridgeCommandClient`
- `TabDisconnected` снимает tab ownership и завершает связанные pending requests со статусом `Disconnected` и ошибкой `вкладка-отключена`
- close или teardown session завершает связанные pending requests со статусом `Disconnected` и ошибкой `сеанс-отключён`
- timeout outbound request завершает pending request со статусом `Timeout` и ошибкой `время-ожидания-истекло`
- cancellation outbound request завершает pending request как failure с ошибкой `запрос-отменён`
- late response после timeout или cancellation допускается как no-op поверх уже settled request id
- неизвестный, malformed или tab-mismatched `Response` после accept считается protocol-invalid и закрывает connection

## Поток accept

Успешный handshake-pipeline для первого этапа:

1. Transport loop принимает websocket upgrade и создаёт connection context без session binding.
1. Первый inbound frame читается как обязательное handshake message.
1. Если frame не может быть разобран в корректный `BridgeMessage`, сервер формирует reject с `неверные-данные` или закрывает connection, если correlation невозможен.
1. Проверяется, что `Type = Handshake`.
1. Проверяется наличие обязательного `Id` для correlation.
1. Payload десериализуется в handshake request DTO.
1. Выполняется field validation для `sessionId`, `secret`, `protocolVersion`, `browserFamily`, `extensionVersion`.
1. Проверяется transport policy.

   - поддерживается ли `protocolVersion`
   - совпадает ли `secret`
   - свободен ли `sessionId`

1. После успешной валидации вызывается owner-layer операция создания session.
1. Только после успешного create session connection помечается как transport-ready и связывается с этой session.
1. Сервер отправляет handshake accept с negotiated policy.
1. После accept разрешён минимальный request/event flow для tab lifecycle и pending request correlation.

Ключевое правило:

- session не должна появляться в owner state раньше, чем пройдены validation, secret check и protocol negotiation

## Поток reject

Reject pipeline для первого implementation cut:

1. Сервер останавливается на первой ошибке handshake и не пытается частично продолжать pipeline.
2. Если у ошибки уже есть client message id, сервер возвращает reject `BridgeMessage` с тем же `Id`.
3. Если message id отсутствует или frame не удалось разобрать, сервер может закрыть connection без полноценного reject response.
4. Reject никогда не создаёт session и не меняет tab registry.
5. Reject никогда не добавляет pending request.
6. После reject connection закрывается либо помечается unusable до немедленного teardown.

Правило mapping ошибок:

- некорректный envelope или payload shape → `неверные-данные`
- отсутствует `secret` → `отсутствует-секрет`
- `secret` не совпал → `секрет-не-совпадает`
- отсутствует `sessionId` → `отсутствует-идентификатор-сеанса`
- `sessionId` уже занят live session → `идентификатор-сеанса-уже-занят`
- `protocolVersion` не поддерживается → `версия-протокола-не-поддерживается`

## Responsibility Split

Handshake implementation первого cut должна оставаться split по слоям:

- BridgeServer transport layer:
  - websocket upgrade
  - first-frame read
  - raw message read and write
  - connection teardown
- handshake parsing and validation layer:
  - envelope checks
  - DTO parsing
  - validation and reject-code mapping
  - accept payload construction
- BridgeServerState owner layer:
  - create session
  - reject duplicate session id
  - health snapshot visibility

Текущее файловое разбиение active implementation:

- handshake DTO, validator и message factory лежат под [Framework/Atom.Net.Browsing.WebDriver/Protocol/Handshake](Framework/Atom.Net.Browsing.WebDriver/Protocol/Handshake)
- owner vocabulary и runtime state types лежат под [Framework/Atom.Net.Browsing.WebDriver/State](Framework/Atom.Net.Browsing.WebDriver/State)

Owner layer не должен знать про websocket close, raw JSON или reject transport semantics.

## Minimal Implementation Ordering

Чтобы избежать повторного проектирования во время кода, handshake flow лучше собирать в таком порядке:

1. Ввести DTO для handshake request и accept payload.
2. Ввести validation result и reject-code mapping helper.
3. Реализовать owner-side create session path.
4. Подключить первый websocket message path в BridgeServer.
5. Добавить accept and reject writes.
6. Повесить teardown policy после reject.
7. Закрыть минимальную handshake test matrix.

## Minimal Handshake Test Matrix

Обязательные tests первого implementation cut:

- `HandshakeAcceptsValidClientPayload`
- `HandshakeResponseEchoesCorrelationId`
- `HandshakeReturnsNegotiatedTransportPolicy`
- `HandshakeRejectsNonHandshakeFirstMessage`
- `HandshakeRejectsSecretMismatch`
- `HandshakeRejectsDuplicateSessionId`
- `HandshakeRejectsUnsupportedProtocolVersion`
- `HandshakeRejectDoesNotRegisterSession`

Следующий слой tests после базового cut:

- `HandshakeRequiresFirstWebSocketMessage`
- `HandshakeRejectsSecondHandshakeOnSameConnection`
- `HandshakeRejectsMissingSessionId`
- `HandshakeRejectsMissingBrowserFamily`
- `HandshakeRejectsMissingExtensionVersion`
- `HandshakeAllowsUnknownOptionalFields`
- `HandshakeDoesNotLeakSecretInAcceptPayload`
- `HandshakeHealthSnapshotDoesNotCountRejectedSession`

Этот follow-up слой уже частично активен в runnable coverage:

- second handshake on same connection
- close before handshake
- missing correlation id close without reject payload
- invalid JSON after accept
- binary frame after accept
- request-response correlation после accept
- timeout и cancellation для pending request
- pending request failure на `TabDisconnected` и session teardown
- unknown response id protocol close

## Follow-up After Handshake

Следующий meaningful cut после текущего active slice — расширять command surface поверх уже существующего minimal request routing, а затем подключать browser-side extension bootstrap к live bridge pipeline.

Связанный документ по owner state: [Framework/Atom.Net.Browsing.WebDriver/BRIDGE_SERVER_PHASE1_STATE_MODEL.md](Framework/Atom.Net.Browsing.WebDriver/BRIDGE_SERVER_PHASE1_STATE_MODEL.md).
