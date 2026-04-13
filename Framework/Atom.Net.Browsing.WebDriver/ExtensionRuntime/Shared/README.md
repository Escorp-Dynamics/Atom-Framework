# Shared

Shared слой задаёт инвариантную форму данных для background, content и page частей runtime.

Что уже закреплено:

- BridgeMessage, BridgeCommand, BridgeStatus и BridgeEventName валидируются единообразно
- RuntimeConfig и RuntimeFeatureFlags проходят проверку на границе загрузки config handoff
- TabContextEnvelope одинаков для background и content и не зависит от браузера
- внутренние ContentPortEnvelope и related command names описаны отдельно от host bridge protocol

Что намеренно не закреплено здесь:

- browser-specific permissions, manifest и packaging детали
