# DOM/BOM Supporting Types Policy

## Назначение

Этот документ фиксирует правила для supporting types в DOM/BOM migration pass.

Под supporting types здесь понимаются публичные типы, которые не являются интерфейсами браузерной поверхности сами по себе, но нужны для типобезопасного переноса Web IDL сигнатур:

- enum
- dictionary-like contract types
- callback delegates
- typedef mappings
- supporting options, init и payload contracts

## Базовый принцип

First pass остается interface-first, но не interface-only в буквальном смысле.

Если без supporting type невозможно сохранить спецификационную форму сигнатуры без деградации в string, object или ad hoc overload explosion, supporting type разрешен.

## Callback policy

- Web IDL callback маппится на именованный delegate.
- Delegate живет в собственном файле.
- Delegate не получает I-префикс.
- Имя сохраняется максимально близким к Web IDL имени в C Sharp casing.
- Action и Func не используются как основной публичный mapping, потому что они скрывают provenance и ломают one-entity-per-file discipline.
- Callback interface не смешивается с callback typedef и продолжает жить как обычный interface contract.

## Enum policy

- Enum создается, если спецификация задает закрытый vocabulary для публичной сигнатуры.
- Один enum размещается в одном файле.
- Enum не заменяется string-based API, если это не продиктовано отдельным explicit exception.
- Legacy-only enums не должны попадать в modern first pass автоматически; они уходят в compatibility bucket вместе со своей family.

## Dictionary policy

- Dictionary-like contract type создается, если interface member напрямую зависит от options, init или payload shape.
- Dictionary type живет в собственном файле.
- Dictionary type не моделируется как interface.
- Dictionary type не добавляется в first pass, если он нужен только deferred family или compatibility-only surface.
- Если dictionary принадлежит modern baseline family, его следует считать частью обязательного supporting surface для этого family.

## Typedef policy

- C Sharp alias declarations не используются как primary strategy.
- Trivial typedef маппится на конкретный CLR type и сохраняется в provenance/documentation.
- Typedef не получает отдельный публичный type только ради номинального сохранения имени.
- Исключение возможно только при отдельном explicit decision, если semantic wrapper materially stabilizes signatures.

## Supporting contract placement

- One entity per file распространяется и на supporting contracts.
- File placement выбирается по primary ownership family, а не по месту первого упоминания.
- Supporting type должен находиться рядом с owning DOM or BOM family, если это не создает cycle or naming ambiguity.
- Partial provenance хранится в inventory и generation notes, а не в размножении одного supporting type по нескольким каталогам.

## Inclusion rules for first pass

- Supporting type входит в first pass только если его owning family уже входит в modern baseline.
- Deferred family не протаскивает свои supporting types в first pass транзитивно.
- Compatibility-only family не протаскивает supporting types в modern baseline.
- Fetch-owned types не входят в first pass через dependent APIs вроде Beacon.
- Worker-owned types не входят в first pass через worker-side exposure notes.

## What stays deferred

- Console namespace modeling
- Full Fetch family and Fetch-owned signature surface
- Worker-specific supporting types
- Legacy SVG compatibility payloads and alias types
- Legacy navigator plugin and mime-type tails
- Touch compatibility family supporting types
- Typed OM supporting surface до отдельного provenance review

## Practical mapping guidance

- options/init dictionaries: создавать как lightweight contract types
- event init dictionaries: создавать, если соответствующий event contract входит в baseline
- callback payload signatures: сохранять через named delegates plus supporting enums or dictionaries when needed
- primitive typedefs: маппить прямо на CLR types
- closed keyword sets: выражать через enums

## Review rule

Если для сигнатуры есть сомнение между string, object и supporting type, по умолчанию выбирать supporting type только тогда, когда это already-backed normative shape из collected inventory.

Если normative shape не закрыта или provenance спорная, surface остается в deferred review bucket до отдельного решения.
