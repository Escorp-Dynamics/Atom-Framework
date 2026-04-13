# Atom.Compilers.JavaScript

Пакет `Atom.Compilers.JavaScript` закладывает базу для ультрапроизводительного JavaScript-компилятора и runtime-среды исполнения, полностью реализованных на C# и совместимых с NativeAOT.

Текущий каркас фиксирует:

- целевой публичный surface area: один `JavaScriptRuntime`;
- стартовый набор атрибутов для source generation над пользовательскими сущностями;
- internal metadata contract для generated type/member descriptors;
- направление для runtime reader, который будет читать generated metadata без runtime discovery;
- жёсткие нефункциональные требования к производительности, памяти, AOT и изоляции API;
- подробный поэтапный roadmap разработки компилятора, рантайма и генеративной модели.

Основной документ планирования:

- [JAVASCRIPT_COMPILER_ROADMAP.md](JAVASCRIPT_COMPILER_ROADMAP.md)
- [RUNTIME_READER_BLUEPRINT.md](RUNTIME_READER_BLUEPRINT.md)
- [SPECIFICATION_CONTRACT.md](SPECIFICATION_CONTRACT.md)

## Публичный API первого уровня

- `JavaScriptRuntime`
- `JavaScriptRuntimeSpecification`
- `JavaScriptObjectAttribute`
- `JavaScriptPropertyAttribute`
- `JavaScriptDictionaryAttribute`
- `JavaScriptIgnoreAttribute`
- `JavaScriptFunctionAttribute`

Создание runtime теперь также может явно фиксировать целевой surface через `JavaScriptRuntimeSpecification`: `ECMAScript` для strict standard baseline и `Extended` для расширенного runtime surface; default остаётся `Extended`, чтобы не ломать существующий call-site contract. Это различие уже влияет на поведение: host registration surface доступен только в `Extended`, выбранная спецификация протаскивается внутрь execution-state, execution-request, parsed-source и lowered-program pipeline, parser stage уже materialize-ит реальную feature matrix (identifier/member/invocation/host candidate/index/assignment/string/numeric/unary/binary), lowering stage переводит её в policy flags и execution-plan seeds, strict value-policy violations и strict parser/lowering policy violations оформляются через execution diagnostics, а engine boundary уже умеет возвращать capability-level diagnostics для mutation, index access, invocation, pure literal materialization и operator lowering.

Полный contract по strict и extended режимам, включая матрицу incompatible kinds и execution invariants, вынесен в [SPECIFICATION_CONTRACT.md](SPECIFICATION_CONTRACT.md).

Async execution остаётся частью `JavaScriptRuntime` и не требует вынесения новых public session/context типов. Публичный async surface при этом остаётся минимальным: наружу exposed только базовые `ExecuteAsync`/`EvaluateAsync` overloads, а выбор dispatch strategy остаётся internal runtime detail.

Public vs internal async contract фиксируется так:

- public async API даёт только cancellation-aware entry points и не закрепляет scheduling knobs в публичной модели;
- internal async API может выбирать dispatch mode для runtime bootstrap, тестов и будущего engine orchestration;
- это разделение не даёт преждевременно зацементировать `Task.Run`, dedicated worker или cooperative scheduling как permanent public contract;
- `ConfigureAwait(false)` рассматривается только как continuation detail и не участвует в выборе execution strategy.

Pooling policy для текущего runtime core фиксируется так:

- retained execution-state arrays, frozen registrations, lookup caches и member-indexed plan caches не должны уходить в `ArrayPool`, потому что это финальное удерживаемое состояние runtime instance;
- `ObjectPool` для bootstrap dictionaries и sets не вводится по умолчанию, пока exact sizing и current compaction не перестанут покрывать allocation pressure по benchmark-ам;
- `ArrayPool` и `ObjectPool` рассматриваются как кандидаты только для будущих transient engine buffers: parser/tokenizer scratch, lowering buffers, temporary invocation frames и прочих short-lived рабочих областей;
- любые pooled buffers обязаны иметь deterministic ownership и явную policy очистки ссылочных данных перед возвратом в pool.

Все остальные типы пакета должны оставаться internal.

## Предлагаемая lifecycle-модель

Зафиксирован базовый контракт v1:

- один экземпляр `JavaScriptRuntime` представляет одну stateful session исполнения;
- фаза `Configuring`: разрешена регистрация пользовательских сущностей;
- первый вызов `Execute` или `Evaluate` переводит runtime в фазу `Running` и блокирует дальнейшую регистрацию;
- `ExecuteAsync` и `EvaluateAsync` расширяют тот же lifecycle-контракт без выделения нового public execution context, а выбор dispatch strategy пока остаётся internal runtime detail до фиксации полноценной execution model;
- `ResetState` в v1 трактуется как сброс JavaScript-state без потери registration model;
- завершение lifecycle выполняется через `IAsyncDisposable`;
- масштабирование по concurrency предполагается через несколько runtime-экземпляров, а не через shared mutable global state.

Это позволяет держать один публичный runtime, не вынося наружу отдельные public session/context типы.

## Internal Metadata Reader

Следующий runtime-этап закреплён как internal-only reader pipeline поверх generated metadata contract.

- reader не должен заниматься reflection-discovery на hot path;
- reader должен принимать только compile-time сформированные metadata shapes с `MetadataVersion = 1`;
- reader должен нормализовать object/dictionary/property/function/ignore entries в cache-friendly internal descriptors;
- reader должен сводить generator identity к компактному internal kind один раз на type boundary и не держать string-based branching внутри member loops;
- reader должен нормализовать registration keys до frozen registration snapshot до входа в execution phase;
- все ошибки несовместимости metadata должны обнаруживаться на этапе registration/bootstrap, а не во время steady-state execution.

Детальный blueprint reader-слоя вынесен в [RUNTIME_READER_BLUEPRINT.md](RUNTIME_READER_BLUEPRINT.md).

## Current Bootstrap Status

- registration metadata уже нормализуется в internal descriptors на registration boundary;
- config-time registration staging теперь идёт через mutable builder и ordinal registration-name set, чтобы не делать `ImmutableArray.Add` reallocations и repeated duplicate scans на каждом `Register`;
- первый `Execute` теперь фиксирует frozen registration snapshot и переводит runtime в `Running` даже для empty-source вызова;
- sync fast path и async entry points теперь сходятся в один execution core, чтобы bootstrap semantics и future engine hand-off оставались идентичными независимо от выбранного dispatch mode;
- execution bootstrap materialization выполняется только один раз на экземпляр runtime и не повторяется на последующих `Execute` вызовах;
- после materialization runtime больше не удерживает исходные generated metadata blobs, потому что execution bootstrap уже опирается на frozen internal descriptors;
- async entry points внутри runtime уже поддерживают узкую dispatch-модель: synchronous completion для cheap local path и worker-thread dispatch как честный non-blocking entry point для будущих heavy script executions;
- public async API не закрепляет dispatch enum раньше времени; dispatch mode выбирает место старта runtime work как internal detail, тогда как `ConfigureAwait(false)` влияет только на continuation после `await` и не заменяет scheduling/execution strategy;
- duplicate export tracking внутри reader теперь выделяется только для generator kinds, которым действительно нужна uniqueness validation, и только когда type содержит больше одного member entry;
- для small host shapes duplicate export validation идёт линейно по уже materialized member descriptors без `HashSet` allocation, а set-based path включается только для более широких type surfaces;
- single-type registrations и single-member type descriptors проходят через singleton fast paths без builder/materialization overhead;
- multi-type registration paths и multi-member type paths в reader теперь materialize-ятся через точные arrays вместо builder-based accumulation, чтобы registration boundary не платила лишний mutable-builder overhead;
- bootstrap materialization теперь сводится в единый execution-state factory, чтобы pre-engine tables, caches и plans собирались без лишней orchestration cost между слоями;
- после первого `Execute` runtime собирает отдельный internal execution-state scaffold, который держит frozen registration snapshot и current session epoch как базу для следующего execution-layer этапа;
- `ResetState` теперь обновляет именно execution-state scaffold, а не только отдельный счётчик epoch, чтобы последующий runtime layer строился поверх одного internal state contract;
- execution-state scaffold уже строит первый session-table слой с registration-level offsets и aggregate type/member counts для будущих runtime caches и binding tables;
- поверх session tables runtime теперь materialize-ит flat binding tables по types и members, чтобы следующий lookup/binding слой работал по плотным read-only arrays вместо вложенного descriptor graph;
- поверх binding tables runtime теперь materialize-ит immutable lookup cache по registration names, type identities и exported member names для будущего fast-path binding resolution;
- поверх lookup cache runtime теперь уже умеет internal-only resolution registration, type и member binding targets по именам, не раскрывая наружу новый public API;
- binding plan cache и marshalling plan cache теперь хранятся как arrays, индексируемые по `memberIndex`, чтобы не дублировать второй и третий string-keyed graph поверх уже существующего `LookupCache.MemberIndexes`;
- dispatch target теперь materialize-ится напрямую из `bindingPlan + session epoch`, без отдельного dispatcher wrapper layer;
- invocation plan и engine entry теперь строятся поверх общей execution-member resolution пары (`bindingPlan + marshallingPlan`) и member-indexed caches, а не через лишние промежуточные keyed lookups между слоями;
- raw script execution теперь тоже проходит через отдельный internal engine-facing seam: operation-aware `JavaScriptRuntimeExecutionRequest`, explicit `JavaScriptRuntimeExecutionResult` со status/phase-aware diagnostic boundary и internal runtime value contract, `JavaScriptRuntimeParserStageScaffold` как первый non-empty source stage с уже materialized feature matrix, `JavaScriptRuntimeLoweringStageScaffold` как следующий lowering boundary с policy-bearing lowered artifact contract, `JavaScriptRuntimeExecutionPlanSeed` как plan-oriented bridge между lowering и engine dispatch и `JavaScriptRuntimeExecutionEngineScaffold` как текущий engine-dispatch boundary; public exception/value projection при этом остаётся отдельной ответственностью runtime facade, а engine boundary уже различает generic specification fallback, strict parser/lowering policy violations и capability-level unavailable paths для mutation/index/invocation/literal/operator lowering, пока реальный execution engine ещё не введён; runtime value layer при этом уже поддерживает `null`, `undefined`, boolean, number, `bigint`, string, opaque `symbol` contract, opaque `array` contract, opaque `array-buffer` contract, opaque `shared-array-buffer` contract, opaque `data-view` contract, opaque `typed-array` umbrella contract, opaque `int8-array` contract, opaque `uint8-array` contract, opaque `uint8-clamped-array` contract, opaque `uint16-array` contract, opaque `int16-array` contract, opaque `int32-array` contract, opaque `uint32-array` contract, opaque `float32-array` contract, opaque `float64-array` contract, opaque `bigint64-array` contract, opaque `biguint64-array` contract, opaque `atomics` contract, opaque `proxy` contract, opaque `reflect` contract, opaque `math` contract, opaque `json` contract, opaque `object` contract, opaque `function` contract, opaque `promise` contract, opaque `set` contract, opaque `map` contract, opaque `weakmap` contract, opaque `weakset` contract, opaque `weakref` contract, opaque `finalization-registry` contract, opaque `regexp` contract, opaque `date` contract, opaque `error` contract, opaque `type-error` contract, opaque `range-error` contract, opaque `reference-error` contract, opaque `syntax-error` contract, opaque `uri-error` contract, opaque `eval-error` contract, opaque `aggregate-error` contract, opaque `suppressed-error` contract, opaque `internal-error` contract, opaque `stack-overflow-error` contract, opaque `timeout-error` contract, opaque `memory-limit-error` contract, opaque `cancellation-error` contract, opaque `host-interop-error` contract, opaque `resource-exhausted-error` contract и non-null host-object paths, причём `undefined` пока намеренно проецируется наружу как `null` до фиксации отдельной public JS-value модели;
- empty export graphs теперь не аллоцируют лишние member-level mutable dictionaries на bootstrap path, а используют shared empty frozen caches только там, где keyed lookup действительно нужен;
- поверх dispatch и marshalling layers runtime теперь уже умеет собирать invocation plan как единый внутренний contract для следующего invoke/dispatcher этапа;
- поверх invocation plan runtime теперь уже умеет готовить узкий engine entry scaffold как следующую internal точку входа для будущего execution engine;
- legacy per-type `Create(...)` materialization paths в session/binding/cache types удалены, чтобы у runtime оставалась только одна актуальная execution-state factory topology;
- после execution transition новые registrations детерминированно запрещены;
- `ResetState` на текущем этапе является cheap session reset без потери frozen registration snapshot и двигает только internal session epoch, чтобы lifecycle-контракт уже был закреплён до появления execution engine.

## Test Topology

- runtime-facing reader и registration tests живут в `Atom.Compilers.JavaScript.Tests`, чтобы основной пакет проверялся собственным isolated test project;
- внутри `Atom.Compilers.JavaScript.Tests` reader/metadata coverage и execution-pipeline coverage теперь физически разделены по отдельным test files, чтобы runtime contract и pre-engine flow оставались independently maintainable;
- engine-facing execution seam тоже покрыт отдельными targeted tests внутри runtime test project, чтобы boundary между lifecycle/bootstrap и будущим execution engine оставался стабилен при дальнейших шагах;
- generator snapshots, analyzer tests и lightweight reader-contract tests остаются в `Atom.Compilers.JavaScript.SourceGeneration.Tests`;
- source-generation harness использует explicit assembly references к уже собранному generator stack вместо `ProjectReference`, потому что analyzer-style multi-assembly graph ломал `GenerateDepsFile` для executable test host;
- compile-time generator references в source-generation tests подаются через target-time `ReferencePath` injection, а нужные generator assemblies копируются в output test host, чтобы XML autoformatting больше не мог ломать critical `HintPath` resolution и оба JavaScript test project запускались параллельно без topology race.
