# Indexer And Event Support Roadmap

Документ фиксирует будущую модель поддержки `indexer` и `event` в `Atom.Compilers.JavaScript`.

## Текущее состояние

- `indexer`:
  - поддерживается только как `JavaScriptIgnore`
  - `JavaScriptProperty` на indexer даёт explicit diagnostic (`ATOMJS106`)
- `event`:
  - поддерживается только как `JavaScriptIgnore`
  - export/binding model для event ещё не определена

## Почему это не включено сейчас

- `indexer` требует не только `ExportName`, но и модель ключа:
  - тип ключа
  - конверсию ключа из JavaScript
  - отдельные getter/setter call-sites
  - стратегию именования surface
- `event` требует полноценный lifecycle contract:
  - subscribe/unsubscribe projection
  - delegate marshalling
  - ownership и cleanup semantics
  - policy для weak/strong subscriptions

## Предлагаемые этапы

1. Metadata stage
   - добавить dedicated internal metadata shapes для indexer/event
   - не пытаться встраивать их в текущую property/function shape без явной модели

2. Validation stage
   - ввести compile-time analyzers для unsupported signatures
   - закрепить policy rules отдельными diagnostics

3. Runtime stage
   - реализовать reader для новых metadata shapes
   - добавить binding adapters в `JavaScriptRuntime`

4. Compatibility stage
   - покрыть host/runtime tests
   - проверить browser-compatible API expectations

## Contract Lock

- пока runtime reader не реализован, contract стабильности обеспечивается exact generated source tests
- любые изменения names/kinds/order/flags должны сопровождаться обновлением tests и contract docs
