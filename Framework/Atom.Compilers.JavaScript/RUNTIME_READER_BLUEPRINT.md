# Internal Runtime Reader Blueprint

## Purpose

Internal runtime reader нужен как узкий bridge между generated metadata contract и будущими runtime descriptors.

Reader не должен:

- открывать новый public API;
- выполнять reflection-discovery в steady-state runtime;
- разбирать произвольные runtime shapes без compile-time guarantees;
- добавлять heap-heavy или lock-heavy слой поверх registration.

Reader должен:

- читать generated type/member metadata, полученные из source-generated scaffolds;
- валидировать `MetadataVersion` и базовую совместимость shape до execution phase;
- переводить metadata в compact internal descriptors, пригодные для fast-path registration и дальнейшего binder/runtime planning;
- оставаться полностью совместимым с NativeAOT и trimming.

## Input Contract v1

Reader работает только с internal runtime contract:

- `JavaScriptGeneratedTypeMetadata`
- `JavaScriptGeneratedMemberMetadata`
- `JavaScriptGeneratedMemberKind`

Ожидаемые invariants v1:

- `MetadataVersion = 1`
- `EntityName` уже стабилизирован generator-слоем
- порядок `Members` уже является contract-значимым
- unsupported combinations должны быть отсечены analyzer-слоем до runtime
- explicit-interface collisions, record discovery и fully-qualified attribute spellings уже нормализованы compile-time pipeline

## Output Contract v1

Reader должен строить internal-only runtime descriptors следующего уровня:

1. Type descriptor

- stable registration name
- generator kind or source kind
- object or dictionary flags
- offsets or indexes на member table

1. Member descriptor

- source name
- exported JavaScript name
- normalized member kind
- compact flags bitmap for read-only, required, pure and inline semantics

1. Registration descriptor

- alias-normalized registration key
- frozen descriptor references ready for runtime state bootstrap

1. Execution state scaffold

- frozen registration snapshot for the active runtime instance
- current session epoch for reset-driven state invalidation
- narrow base contract for future execution tables, caches and host binding state

Ни один из этих descriptor-типов не должен быть public.

## Reader Pipeline

### Phase 1. Intake

- принять массив generated type metadata от registration path
- проверить null-free and duplicate-free assumptions на internal boundary
- быстро отвергнуть пустые или version-incompatible shapes

### Phase 2. Validation

- проверить `MetadataVersion`
- проверить допустимость generator identity
- проверить consistency member names, kinds and flags
- подтвердить отсутствие impossible runtime combinations, даже если analyzer layer уже должен был их исключить

### Phase 3. Normalization

- нормализовать export names
- свернуть bool-флаги в compact bit layout
- преобразовать member kinds в execution-oriented enum layout
- вычислить deterministic ordering tokens для последующих caches и binder tables

### Phase 4. Materialization

- собрать compact descriptor arrays
- сохранить индексы вместо runtime dictionary lookups там, где это возможно
- подготовить tables, пригодные для fast registration and binding
- для multi-type и multi-member shapes prefer direct array materialization с точным размером вместо builder-accumulation, если размер известен заранее

### Phase 5. Freeze

- перевести descriptors в immutable registration snapshot
- запретить дальнейшую mutation после первого execution transition

### Phase 6. Execution-State Scaffold

- собрать отдельный internal execution-state объект поверх frozen registration snapshot
- перенести session epoch в execution-state contract, а не держать его как разрозненное lifecycle поле
- использовать этот scaffold как единственную точку расширения для будущих runtime tables и caches
- в качестве первого шага materialize registration-level session tables с aggregate type/member counts и offsets
- следующий шаг materialize-ить flat binding tables для type/member lookup поверх session tables
- поверх binding tables materialize-ить immutable lookup cache по registration, type и exported member keys
- поверх lookup cache добавить narrow internal host binding resolution API для deterministic name-based binding lookup
- binding plan cache и marshalling plan cache держать в member-indexed compact form, а не в отдельных string-keyed maps поверх уже существующего lookup graph
- dispatch-ready targets materialize-ить напрямую поверх binding plan + session epoch без отдельного dispatcher wrapper layer
- invocation и engine-entry preparation строить поверх общей execution-member resolution пары (`binding plan + marshalling plan`) и member-indexed caches, минимизируя промежуточные struct hops
- поверх dispatch и marshalling layers собирать единый invocation plan для следующего invoke orchestration stage
- поверх invocation plan подготовить узкий engine entry scaffold для следующего execution engine stage
- raw script execution после lifecycle/bootstrap boundary пропускать через отдельный internal execution contract: operation-aware request, explicit result contract со status/phase-aware diagnostic boundary и internal runtime value shape, parser-stage scaffold с минимальным parsed artifact contract для первого non-empty source шага, lowering-stage scaffold с lowered artifact contract для следующего pipeline boundary и execution scaffold как текущий engine-dispatch boundary, чтобы будущий engine подключался к явному contract вместо inline runtime stub, а runtime facade отдельно решал public projection failures; current runtime value layer уже покрывает `null`, `undefined`, boolean, number, `bigint`, string, opaque `symbol` contract, opaque `array` contract, opaque `array-buffer` contract, opaque `shared-array-buffer` contract, opaque `data-view` contract, opaque `typed-array` umbrella contract, opaque `int8-array` contract, opaque `uint8-array` contract, opaque `uint8-clamped-array` contract, opaque `uint16-array` contract, opaque `int16-array` contract, opaque `int32-array` contract, opaque `uint32-array` contract, opaque `float32-array` contract, opaque `float64-array` contract, opaque `bigint64-array` contract, opaque `biguint64-array` contract, opaque `atomics` contract, opaque `proxy` contract, opaque `reflect` contract, opaque `math` contract, opaque `json` contract, opaque `object` contract, opaque `function` contract, opaque `promise` contract, opaque `set` contract, opaque `map` contract, opaque `weakmap` contract, opaque `weakset` contract, opaque `weakref` contract, opaque `finalization-registry` contract, opaque `regexp` contract, opaque `date` contract, opaque `error` contract, opaque `type-error` contract, opaque `range-error` contract, opaque `reference-error` contract, opaque `syntax-error` contract, opaque `uri-error` contract, opaque `eval-error` contract, opaque `aggregate-error` contract, opaque `suppressed-error` contract, opaque `internal-error` contract, opaque `stack-overflow-error` contract, opaque `timeout-error` contract, opaque `memory-limit-error` contract, opaque `cancellation-error` contract, opaque `host-interop-error` contract, opaque `resource-exhausted-error` contract и host-object projection paths как baseline для будущих engine return values, при этом `undefined` на текущем public boundary временно сводится к `null`
- legacy альтернативные Create-path materializers в session/binding/cache types не поддерживать: execution-state factory должен оставаться единственной актуальной topology

## Performance Rules

- reader работает только в registration/bootstrap phase, никогда не попадает в script execution hot path
- steady-state runtime не должен повторно перечитывать generated metadata
- prefer contiguous arrays and indexes over layered object graphs
- prefer one keyed lookup graph and array-indexed secondary caches over multiple parallel keyed maps для одного и того же member surface
- исключить LINQ, reflection-walking и allocator-heavy normalization loops
- при необходимости использовать pooled temporary buffers с deterministic ownership

### Pooling Boundaries

- reader и bootstrap factory не должны отдавать в `ArrayPool` arrays, которые затем становятся частью immutable retained runtime state;
- pooled scratch buffers допустимы только для временных структур, которые гарантированно копируются или уничтожаются до freeze boundary;
- `ObjectPool` для bootstrap dictionaries/sets не вводится до benchmark-подтверждения, что current compact topology уже недостаточно снижает allocation pressure;
- будущие parser/tokenizer/lowering stages считаются основными кандидатами под pooling, а не текущие retained runtime tables.

## Error Policy

Reader должен падать рано и узко.

- version mismatch должен приводить к deterministic registration failure
- invalid generator identity должен приводить к deterministic registration failure
- impossible flag combinations должны приводить к deterministic registration failure
- ошибки должны формироваться до перехода runtime в phase `Running`

Reader не должен пытаться silently recover от contract violations.

## Integration Plan

1. Ввести internal descriptor types рядом с runtime metadata contract.
2. Ввести synchronous reader entry point без async boundaries.
3. Подключить reader к `JavaScriptRuntime.Register<T>()` как pre-execution normalization step.
4. Зафиксировать contract tests для reader отдельно от generator snapshot tests.
5. Ввести internal execution-state scaffold поверх frozen registrations.
6. Только после этого переходить к binder/prototype/object-shape planning.

## Test Strategy

### Reader Contract Tests

- happy-path cases для object, dictionary, property, function and ignore metadata
- version rejection
- duplicate export rejection на runtime boundary как defensive check
- ordering preservation
- alias normalization
- эти structural reader-contract tests остаются в source-generation harness, потому что они валидируют emitted metadata shape до runtime execution

### Integration Tests

- registration of generated host types
- freeze after first execute
- reset behavior without metadata re-discovery
- execution-state creation and epoch refresh over the same frozen registrations
- runtime-facing registration and metadata-reader tests должны жить в `Atom.Compilers.JavaScript.Tests`, чтобы основной пакет имел собственный изолированный test project
- внутри runtime test project structural reader coverage и execution-pipeline coverage должны оставаться разделёнными, чтобы metadata-boundary regressions и pre-engine lifecycle regressions сопровождались независимо

### Non-Goals for This Stage

- execution engine integration
- JavaScript parser implementation
- marshalling layer
- module system
- proxies, private fields or prototype semantics

## Exit Criteria

- runtime может превратить generated metadata в frozen internal descriptors без reflection-discovery
- registration phase детерминированно валидирует incompatible metadata
- повторный execution не делает metadata walking повторно
- первый execution создаёт отдельный internal execution-state scaffold поверх frozen registrations
- execution-state materialization опирается на одну factory topology без дублирующих legacy Create-paths
- public API остаётся неизменным: только `JavaScriptRuntime` и атрибуты
