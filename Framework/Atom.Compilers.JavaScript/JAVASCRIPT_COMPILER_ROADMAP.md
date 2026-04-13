<!-- markdownlint-disable MD024 MD029 MD032 -->

# JavaScript Compiler Roadmap

## Mission

`Atom.Compilers.JavaScript` должен стать полностью managed JavaScript-компилятором и runtime-платформой нового поколения для .NET, с фокусом на:

- абсолютный приоритет производительности над универсальностью реализации;
- лидерство в speed, CPU efficiency и memory efficiency относительно ClearScript, Jint, Jurassic и прочих managed/native аналогов;
- полную совместимость с NativeAOT и триммингом;
- полную реализацию актуального ECMAScript-стандарта и browser-compatible поведения для compile/runtime surface;
- исполнение через C#-сущности без DLR, без Reflection.Emit, без runtime codegen, без обязательных heap allocations на hot path;
- production surface area из одного публичного `JavaScriptRuntime` и набора атрибутов для source generation.

## Product Vision

На выходе продукт состоит из двух слоёв.

1. `JavaScriptRuntime`

- единственная публичная runtime-точка входа;
- регистрирует пользовательские сущности;
- создаёт и держит state-сессию исполнения;
- реализует deterministic lifecycle через `IAsyncDisposable`;
- исполняет скрипты и модули, не раскрывая внутреннюю архитектуру наружу.

2. Attribute-driven generation model

- набор публичных атрибутов описывает JavaScript-compatible contract над CLR-типами;
- Roslyn source generator строит adapters, shape tables, fast binders, dictionaries, prototypes и metadata blobs на compile time;
- рантайм не занимается reflection-discovery в продакшне и не зависит от runtime metadata walking.

### Internal Runtime Reader Track

- generated metadata из source-generation слоя должны читаться internal reader-пайплайном в compact runtime descriptors;
- reader является bridge-слоем между compile-time constants и execution engine, а не публичной runtime abstraction;
- reader обязан быть NativeAOT-friendly, trimming-safe и без ad-hoc runtime discovery;
- reader должен валидировать `MetadataVersion`, generator identity, member ordering и flag compatibility до выхода в execution phase.

Детальный план reader-слоя вынесен в [RUNTIME_READER_BLUEPRINT.md](RUNTIME_READER_BLUEPRINT.md).

### Current Runtime Milestone Status

- public runtime surface по-прежнему остаётся минимальным и ограничен одним `JavaScriptRuntime` без дополнительных public execution/session/value abstractions;
- registration bootstrap уже переводится в frozen execution-state scaffold с session tables, binding tables, lookup cache, binding/marshalling plan caches и узким invocation/engine-entry prep layer;
- raw script execution уже пропускается через явный internal pipeline: operation-aware execution request, parser-stage scaffold с реальной feature matrix, lowering-stage scaffold с policy flags, execution-plan seed layer, engine-dispatch scaffold и explicit execution result со status/phase-aware diagnostics;
- текущий execution engine пока намеренно остаётся scaffold-only boundary, но non-empty source уже не всегда сводится к одному generic fallback: strict parser/lowering policy violations и capability-level unavailable paths materialize-ятся детерминированно ещё до появления реального engine;
- internal runtime value taxonomy уже отделена от public projection policy и на текущем этапе включает `null`, `undefined`, boolean, number, `bigint`, string, opaque `symbol`, `array`, `array-buffer`, `shared-array-buffer`, `data-view`, `typed-array`, `int8-array`, `uint8-array`, `uint8-clamped-array`, `uint16-array`, `int16-array`, `int32-array`, `uint32-array`, `float32-array`, `float64-array`, `bigint64-array`, `biguint64-array`, `atomics`, `proxy`, `reflect`, `math`, `json`, `object`, `function`, `promise`, `set`, `map`, `weakmap`, `weakset`, `weakref`, `finalization-registry`, `regexp`, `date`, `error`, `type-error`, `range-error`, `reference-error`, `syntax-error`, `uri-error`, `eval-error`, `aggregate-error`, `suppressed-error`, `internal-error`, `stack-overflow-error`, `timeout-error`, `memory-limit-error`, `cancellation-error`, `host-interop-error`, `resource-exhausted-error` и host-object contracts;
- `undefined` на текущем public boundary временно проецируется как `null`, пока отдельная public JS-value model сознательно не зафиксирована.

Всё остальное:

- parser;
- lexer;
- AST/IR/HIR/LIR;
- symbol tables;
- optimizers;
- caches;
- module loader internals;
- host binding machinery;
- diagnostics;
- browser compatibility tables;
- string tables;
- allocators;
- lock-free queues;
- generated metadata readers;

должно быть `internal`.

## Public API and Lifecycle Decision v1

На текущем этапе фиксируется следующий публичный контракт.

### Public Runtime Model

- один экземпляр `JavaScriptRuntime` равен одной изолированной stateful session исполнения;
- отдельный public `Session`, `Context`, `Realm`, `ModuleLoader`, `Parser`, `AstNode`, `CompiledScript` или `ExecutionFrame` наружу не выносится;
- runtime сам владеет внутренним пулом state-структур, caches и compiled artifacts.

### Runtime States

1. `Configuring`
2. `Running`
3. `Disposed`

### State Rules

- в `Configuring` разрешены registration calls;
- первый `Execute` или `Evaluate` переводит runtime в `Running`;
- после перехода в `Running` новые registrations запрещены;
- `ResetState` очищает пользовательский JavaScript-state, но не ломает compile-time registration model;
- после первого execution runtime должен жить поверх отдельного internal execution-state scaffold с frozen registration snapshot и session epoch;
- `DisposeAsync` завершает lifecycle и освобождает ownership над внутренними ресурсами.

### Threading Decision

- отдельный экземпляр `JavaScriptRuntime` намеренно не должен быть много-поточно reentrant;
- максимальная производительность достигается через runtime-per-session или runtime-per-shard модель;
- межпоточный шаринг mutable state запрещён;
- concurrency scale достигается multiplicity runtime instances и lock-free internal infrastructure, а не одним shared runtime.

### Minimal Public Surface v1

- `JavaScriptRuntime.Register<T>()`
- `JavaScriptRuntime.Register<T>(string alias)`
- `JavaScriptRuntime.Execute(ReadOnlySpan<char> source)`
- `JavaScriptRuntime.ExecuteAsync(ReadOnlyMemory<char> source, CancellationToken cancellationToken = default)`
- `JavaScriptRuntime.Evaluate<T>(ReadOnlySpan<char> source)`
- `JavaScriptRuntime.EvaluateAsync<T>(ReadOnlyMemory<char> source, CancellationToken cancellationToken = default)`
- `JavaScriptRuntime.ResetState()`
- `JavaScriptRuntime.DisposeAsync()`
- `JavaScriptRuntimeSpecification`
- public attribute set for source generation

Runtime creation также может принимать `JavaScriptRuntimeSpecification`, чтобы заранее отделить strict `ECMAScript` baseline от `Extended` runtime surface без разрастания дополнительных public session/config types. Первый behavioural split уже закреплён: `ECMAScript` не разрешает host registration surface, выбранная спецификация сохраняется в execution-state, parsed-source, lowered-program и execution-plan-seed graph для следующих parser/lowering/engine policy branches, parser stage уже materialize-ит concrete feature matrix, lowering stage переводит её в policy/capability flags, strict-only violations по extended runtime values и strict parser/lowering policies переводятся в execution diagnostics до public projection boundary, а engine-dispatch boundary уже умеет различать `ECMAScript`/`Extended` fallback и capability-level unavailable paths для mutation/index/invocation/literal/operator lowering.

Formal contract по specification behavior, current strict-incompatible matrix и execution invariants вынесен в [SPECIFICATION_CONTRACT.md](SPECIFICATION_CONTRACT.md), чтобы README и roadmap не дублировали один и тот же normative section.

Текущий test status для уже введённого runtime scaffolding подтверждён двумя зелёными ветками: runtime suite и source-generation suite проходят полностью, поэтому следующие шаги уже можно смещать из области стабилизации в область новых execution descriptors и real engine layers.

### Public Async Contract Boundary

- public async API должен оставаться cancellation-aware, но не strategy-aware;
- public contract не должен фиксировать dispatch enum, scheduler abstraction или worker ownership model до появления реального execution engine;
- любые dispatch/scheduling knobs до этого этапа должны жить только во internal runtime contract и тестовой инфраструктуре;
- `ConfigureAwait(false)` считается implementation detail continuation handling и не заменяет execution/disptach contract.

### Pooling Boundaries

- retained runtime state не должен зависеть от `ArrayPool` или `ObjectPool`, если данные переживают bootstrap и становятся частью execution-state graph;
- pooling допустим только для transient scratch structures, которые не переживают phase boundary и не публикуются как immutable retained state;
- первый приоритет на pooling получают parser, tokenizer, lowering и будущие engine-side temporary buffers;
- bootstrap dictionaries/sets переводить на `ObjectPool` только после benchmark-подтверждения, что exact sizing и compact topology больше не закрывают allocation pressure.

### Explicit Non-Goals for Public API v1

- public parser API;
- public AST API;
- public compiled-script handle API;
- public options bag with dozens of toggles;
- public diagnostics tree;
- public module loader abstraction;
- public host binder abstraction;
- public memory manager abstraction.

Причина простая: пока архитектура не прошла perf hardening, любое преждевременное расширение public surface закрепит неудачные абстракции и создаст permanent optimization tax.

## Non-Negotiable Requirements

### Performance

- Top-1 среди managed JavaScript runtimes по latency на short scripts.
- Top-1 по throughput на long-running benchmarks.
- Top-1 по CPU efficiency на identical workloads.
- Top-1 по peak RSS / managed heap / allocations per operation.
- Zero allocations на steady-state hot path там, где это технически возможно.
- GC-free steady-state execution для заранее подготовленного script/module graph.
- Минимум virtual dispatch на hot path.
- Минимум branch misprediction и pointer chasing.
- Обязательная отдельная стратегия для instruction cache locality.
- Обязательная SIMD/vector-friendly реализация там, где это даёт measurable gain.

### Concurrency

- Максимально lock-free архитектура.
- Запрет глобальных coarse-grained locks в parse / bind / execute hot paths.
- Single-writer/multi-reader или sharded lock-free structures вместо обычных concurrent collections, когда они дают меньший overhead.
- Deterministic ownership model для pooled buffers, arenas и tables.

### Memory

- Arena-based allocation для parse, bind, lowering и временных IR.
- Recyclable pools для frequently reused objects.
- String interning policy без unbounded growth.
- UTF-16 и UTF-8 стратегия хранения должна быть benchmark-driven.
- Zero-copy slices wherever safe.
- Prefer structs/ref structs/value layouts на hot path.
- Unsafe допустим как основной инструмент, а не как исключение.

Уточнение для текущего runtime milestone:

- execution-state factory и reader сейчас предпочитают exact-size arrays + immutable freeze вместо pooling retained arrays;
- pooling откладывается до появления реальных transient engine buffers, где ownership и lifetime будут короткими и benchmark-driven.

### AOT

- Полная совместимость с NativeAOT.
- Полная совместимость с trimming.
- Никакого Reflection.Emit.
- Никакого DLR.
- Никакого runtime IL generation.
- Никакого обязательного dynamic dispatch через `dynamic`.
- Никакой обязательной runtime-reflection модели для host binding.
- Все required metadata должны генерироваться compile time.

### Standards and Compatibility

- Поддержка актуального ECMAScript без сознательных функциональных урезаний.
- Корректная поддержка scripts, modules, strict mode, async/await, generators, iterators, proxies, symbols, private fields, decorators при их стандартизации, import attributes, temporal, typed arrays и последующих релизов.
- Максимально точная браузерная совместимость semantics, включая corner cases.
- Совместимость должна проверяться не на уровне синтаксиса, а на уровне observable behavior.

## Strategic Architecture

## Layer 0. Source Model

Входом является текст JavaScript, но система не должна жить как классический динамический interpreter-first runtime.

Базовая стратегия:

- текст парсится в internal representation;
- representation маппится в C#-сущности и компактные runtime descriptors;
- выполнение идёт через hand-tuned execution engine над этими descriptor-структурами;
- все public host contracts генерируются заранее.

Ключевая мысль: парсинг текста допускается, но в рантайме не должно быть “свободной динамики”, если её можно стабилизировать заранее через compile-time generation и deterministic metadata layout.

## Layer 1. Lexer

### Goals

- fastest lexer among .NET JavaScript engines;
- zero-alloc tokenization;
- UTF-aware scanning без промежуточных строк;
- поддержка incremental lexing для IDE и tooling.

### Design

- `ref struct` lexer over `ReadOnlySpan<char>` и отдельный UTF-8 fast path over `ReadOnlySpan<byte>`;
- table-driven classification для ASCII fast path;
- vectorized classification для identifier/whitespace/digit runs;
- keyword detection через perfect hash или branchless trie/table;
- token payload хранится как offsets + lengths + normalized flags;
- string/regexp/template segments materialize только при реальной необходимости.

### Milestones

1. ASCII fast path.
2. Full Unicode identifiers.
3. Template literals and escape decoding.
4. RegExp literal scanner with browser-correct ambiguity resolution.
5. Error recovery mode for tooling.

### Exit Criteria

- zero managed allocations on token stream generation for valid source;
- lexer throughput выше Jint parser-front benchmarks минимум на 2x;
- stable branch profile on mixed real-world scripts.

## Layer 2. Parser

### Goals

- full ECMAScript grammar coverage;
- browser-grade correctness;
- parse without heap churn;
- dual modes: validating parser and recovery parser.

### Design

- hand-written recursive descent с predictive tables там, где это выгодно;
- pooled node arenas;
- compact node headers;
- optional green/red tree model only if IDE tooling будет в scope;
- parser options encoded as flags struct, no polymorphic config objects on hot path.

### Required Capabilities

- scripts;
- modules;
- top-level await;
- async/generator combinations;
- import/export all current forms;
- class fields/private fields/static blocks;
- decorators pipeline readiness;
- temporal/proposal gates через feature flags;
- tolerant parsing mode only in tooling layer, never in execution fast path.

### Exit Criteria

- Test262 parser subset target: near-complete pass;
- zero allocations outside arenas/pools;
- parser memory per MB input materially ниже аналогов.

## Layer 3. Semantic Binding

### Goals

- отделить синтаксис от исполняемой semantics;
- заранее вычислить scopes, captures, declarations, hoisting, closures, temporal dead zone, private names и module linkage.

### Design

- symbol tables в compact arena-backed maps;
- lexical environments lowered to indexed slots;
- closures described через capture descriptors вместо reflection/dictionary-based environments;
- import/export graph frozen ahead of execution;
- browser semantic quirks encoded явно, без scattered checks по runtime.

### Deliverables

- scope graph;
- slot assignment;
- capture map;
- declaration order maps;
- module dependency plan;
- side-effect summary hooks for optimizer.

## Layer 4. Internal IR Stack

Нужны минимум три уровня представления.

1. AST
- максимально точное отражение спецификации.

2. HIR
- нормализованная semantic form;
- explicit control-flow;
- explicit environments and slots;
- desugared language constructs.

3. LIR
- execution-oriented layout;
- compact op descriptors;
- dense jump tables;
- cache-friendly instruction format.

### Why multiple IR levels

- без HIR тяжело доказуемо делать корректные lowering passes;
- без LIR невозможно выжать latency и CPU efficiency;
- AST не должен попадать в execution hot path.

### Exit Criteria

- AST полностью исключён из steady-state runtime execution;
- LIR serializable в AOT-friendly metadata blobs;
- HIR поддерживает property shape analysis, escape analysis, closure lowering и constant propagation.

## Layer 5. Execution Engine

### Strategic Choice

Основной runtime не должен генерировать IL/JIT-код. Основа исполнения:

- hand-written interpreter over compact LIR;
- superinstructions;
- specialized dispatch loops;
- optional ahead-of-time prepared artifacts;
- no runtime codegen.

### Why this path

- NativeAOT compatibility;
- controllable memory layout;
- predictable startup;
- отсутствие DLR/JIT artifacts;
- возможность делать GC-free steady-state.

### Core Requirements

- threaded dispatch or computed-goto equivalent strategy if measurable within C# constraints;
- minimized switch overhead;
- specialized op families by operand kind;
- inlined numeric/string/object fast paths;
- devirtualized builtin dispatch;
- separate hot loops for arithmetic, property access, calls and iteration.

### Mandatory Optimization Tracks

1. Slot-based locals, arguments and closures.
2. Monomorphic/polymorphic inline caches for property access.
3. Shape-based object layout.
4. Fast arrays and typed arrays with dedicated representations.
5. Intrinsic lowering for common builtins.
6. String builder elimination and concat specialization.
7. Exception path isolation from fast path.
8. Branchless boolean/null/undefined tests where profitable.

## Layer 6. Object Model

### Goals

- browser-compatible semantics;
- cache-friendly layouts;
- no dictionary-first design on hot path.

### Design

- shape/tree-based hidden class system;
- separate representations for:
  - plain objects;
  - arrays;
  - typed arrays;
  - function objects;
  - module namespace objects;
  - proxies;
  - host-backed objects.
- dictionary mode only as deoptimized fallback.

### Required Fast Paths

- monomorphic property read/write;
- array index read/write;
- element-kind specialization;
- sparse fallback separation;
- enumeration plans cached by shape.

### Exit Criteria

- object property access benchmarks materially ahead of Jint and competitive with native-hosted engines in managed scope;
- no per-access allocations;
- hidden class transitions pooled and deduplicated.

## Layer 7. Functions, Closures and Call Pipeline

### Requirements

- full support for normal, async, generator and async generator functions;
- exact semantics for `this`, `super`, `new.target`, rest/spread, default parameters, `arguments` object;
- cheap closure capture representation;
- tail-sensitive architecture even if spec tail-call mode remains gated.

### Design

- call frames as stack-like structs or pooled frame blocks;
- split frame descriptors from runtime mutable state;
- closure environments represented as compact slot bags;
- no boxing for common call paths;
- separate fast call paths by arity.

### Optimization Targets

- direct call for known internal functions;
- inline builtin trampolines;
- host call adapters generated at compile time;
- async state machine interop with minimal allocations.

## Layer 8. Modules

### Requirements

- full ES module semantics;
- cyclic dependency correctness;
- live bindings;
- dynamic import;
- import attributes;
- JSON/module-type extensibility.

### Design

- module graph built once;
- linker-style resolution stage;
- frozen import/export tables;
- optional precompiled module bundles;
- AOT-stable module descriptors.

### Browser Compatibility

- module resolution policy should support browser-like URL semantics in dedicated host layer;
- runtime core should not hardcode filesystem assumptions.

## Layer 9. Browser Compatibility Surface

Фраза “полная совместимость с браузерным компилятором” здесь трактуется как две задачи.

1. Browser-equivalent language semantics.
2. Browser-compatible parsing and module semantics surface.

Нельзя смешивать это с DOM/BOM runtime внутри базового компилятора. Поэтому roadmap делит scope:

- `Atom.Compilers.JavaScript`: язык, модули, рантайм, host interop core;
- browser APIs: отдельные верхние слои, использующие runtime как ядро.

### Required Compatibility Programs

- Test262 full track;
- custom browser parity suite;
- differential testing against Chromium, SpiderMonkey and WebKit on observable language behavior;
- module and syntax fuzzing.

## Layer 10. NativeAOT and Trimming Program

### Hard Rules

- каждый новый public API должен проектироваться так, будто reflection недоступна;
- никаких runtime-сканирований сборок;
- никаких string-based member lookups в обязательном fast path;
- никаких late-bound delegates, если их можно сгенерировать заранее.

### AOT Plan

1. Все host metadata генерируются source generator-ом.
2. Все binder tables хранятся в static readonly generated blobs.
3. Все user entity adapters создаются на compile time.
4. Все fallback paths, требующие reflection, либо отсутствуют, либо вынесены в opt-in tooling/debug surface вне production runtime.
5. Постоянно проверять publish с NativeAOT на CI.

### Exit Criteria

- clean NativeAOT publish on sample apps;
- no trim warnings in supported path;
- no runtime MissingMetadata failures in supported path.

## Layer 11. Source Generation and Attributes

### Public Attribute Surface v1

- `JavaScriptObjectAttribute`
- `JavaScriptPropertyAttribute`
- `JavaScriptDictionaryAttribute`
- `JavaScriptIgnoreAttribute`
- `JavaScriptFunctionAttribute`

### Attribute Surface Under Review

Нужно согласовать и, возможно, добавить:

- `JavaScriptConstructorAttribute`
- `JavaScriptMethodAttribute`
- `JavaScriptIndexerAttribute`
- `JavaScriptPrototypeAttribute`
- `JavaScriptModuleAttribute`
- `JavaScriptGlobalAttribute`
- `JavaScriptConstantAttribute`
- `JavaScriptReadonlyAttribute`
- `JavaScriptEnumerableAttribute`
- `JavaScriptAliasAttribute`
- `JavaScriptSymbolAttribute`
- `JavaScriptGenericInstantiationAttribute`
- `JavaScriptArrayLikeAttribute`
- `JavaScriptPromiseLikeAttribute`
- `JavaScriptTypedArrayAttribute`
- `JavaScriptInteropPolicyAttribute`

### Generation Goals

- compile-time property tables;
- compile-time function tables;
- generated shape descriptors;
- generated marshaling adapters;
- generated numeric/string conversion fast paths;
- generated dictionary adapters;
- generated symbol maps;
- generated error messages for unsupported CLR projections.

### Compatibility Problem to Solve Explicitly

Возможности C# и JavaScript не совпадают:

- overloads;
- optional/default parameters;
- indexers;
- ref/out/in;
- generic methods;
- nullable annotations;
- readonly members;
- properties vs fields;
- extension methods;
- struct semantics;
- async methods/tasks/value tasks;
- explicit interface implementations;
- inheritance + prototype chain mismatches.

Нужен отдельный design paper на тему “CLR to JavaScript projection model”.

## Layer 12. Host Interop

### Principles

- интероп должен быть generated-first;
- рантайм не должен заниматься expensive reflection-based matching;
- все conversions должны быть explicit and measurable.

### Required Capabilities

- registration of user entities;
- global object registration;
- module export registration;
- function callbacks;
- promise/task bridge;
- array/span/memory/typed-array bridge;
- dictionary/map bridge;
- exception mapping.

### Performance Rules

- generated binders only on supported path;
- no LINQ in interop hot path;
- no boxing for common numeric conversions;
- ref-return and span-friendly paths where safe;
- separate unsafe fast paths for blittable arrays and typed buffers.

## Layer 13. Memory Model and GC Strategy

### Objective

Steady-state execution must be close to GC-free for precompiled scripts.

### Required Components

- parser arenas;
- binder arenas;
- IR arenas;
- object pools;
- frame pools;
- string and identifier intern pools;
- reusable exception-less diagnostics buffers;
- dedicated slab allocator for short-lived runtime data.

### Rules

- every allocation site on hot path must be benchmarked and justified;
- every new class introduced in runtime core must answer why it is not a struct or pooled object;
- every captured lambda in hot path is presumed a bug until proven otherwise.

## Layer 14. Lock-Free Infrastructure

### Potential Internal Components

- lock-free symbol table snapshots;
- sharded caches for module and shape descriptors;
- MPSC queues for diagnostic/event pipelines;
- lock-free object pool heads;
- versioned immutable snapshots for builtin metadata.

### Caution

Lock-free only when it wins. False sharing, CAS storms and cache-line bouncing must be measured.

### Mandatory Benchmarks

- no-contention vs high-contention;
- NUMA-aware runs where possible;
- false-sharing detection benchmarks;
- tail latency under concurrent script sessions.

## Layer 15. Builtins and Standard Library

### Scope

- full ECMAScript builtins;
- full typed arrays / array buffer family;
- proxies;
- Intl strategy;
- Temporal strategy;
- RegExp engine strategy.

### Separate Design Decisions Needed

1. RegExp
- own engine;
- wrapper over .NET regex impossible as full-semantics solution;
- likely dedicated ECMAScript-compatible engine required.

2. Intl
- minimal correctness layer first or full CLDR-driven implementation;
- if external data needed, it must remain AOT-friendly.

3. Temporal
- likely dedicated domain model with generated calendar/time-zone adapters.

## Layer 16. Diagnostics and Tooling

### Production Rule

Debugging must not poison hot path.

### Design

- compile-time switchable diagnostics;
- internal event probes;
- optional tracing buffers;
- debugger/tooling adapters outside core fast path;
- source maps support for diagnostics only.

### Tooling Scope

- parser diagnostics;
- binding diagnostics;
- runtime exception formatting;
- benchmark trace exporters;
- internal counters for IC hit rate, deopt rate, allocations, pool misses.

## Layer 17. Benchmarking Program

### This is a first-class product pillar

Без benchmark discipline проект провалится.

### Benchmark Categories

1. Microbenchmarks
- lexer;
- parser;
- property read/write;
- function calls;
- array ops;
- numeric ops;
- string ops;
- module resolution;
- host interop.

2. Macrobenchmarks
- real-world bundles;
- transpiled application fragments;
- template engines;
- validation rules;
- SSR-like workloads;
- scripting/automation scenarios.

3. Comparative Benchmarks
- ClearScript;
- Jint;
- Jurassic;
- any additional maintained managed engine.

4. Resource Benchmarks
- CPU time;
- instructions retired;
- branch misses;
- cache misses;
- allocations;
- GC count;
- peak RSS.

### Success Gate

Нельзя переходить к “feature complete” без регулярного превосходства в measurable scenarios.

## Layer 18. Correctness Program

### Mandatory Validation Sources

- Test262;
- custom conformance packs;
- browser differential testing;
- fuzzers for parser and runtime;
- property-based testing for host interop and conversions.

### Differential Testing

Каждая major semantic area должна сравниваться минимум с:

- Chromium/V8;
- Firefox/SpiderMonkey;
- WebKit/JavaScriptCore.

Если поведение расходится, расхождение должно быть документировано как deliberate incompatibility либо исправлено.

## Layer 19. Security and Isolation

### Requirements

- sandbox boundaries на уровне runtime configuration;
- контролируемый host exposure;
- quotas for memory/time/instructions if needed;
- no accidental escape via reflection surface;
- deterministic exception boundaries.

### Deliverables

- execution quotas;
- module import policy;
- host registration policy;
- resource budget hooks;
- safe defaults in public runtime.

## Layer 20. API Surface Governance

### Public API Freeze Rule

До стабилизации архитектуры публикуется только минимальный public API:

- `JavaScriptRuntime`;
- атрибуты.

Никаких public AST, public parser nodes, public IR, public host binders, public caches, public diagnostics internals.

### Why

- сохранить свободу внутренней оптимизации;
- не зафиксировать случайно неудачные abstractions;
- избежать future perf tax из-за ранних публичных контрактов.

## Phased Delivery Plan

## Phase 0. Design Freeze

### Deliverables

- product charter;
- perf charter;
- memory charter;
- NativeAOT charter;
- public API charter;
- attribute matrix draft;
- benchmarking protocol.

### Exit Criteria

- согласован минимальный public surface;
- согласован список обязательных JS features;
- согласован target benchmark matrix.

## Phase 1. Skeleton and Contracts

### Deliverables

- package skeleton;
- `JavaScriptRuntime` placeholder;
- initial attributes;
- roadmap;
- CI skeleton for AOT and trimming;
- versioned generated metadata contract (`MetadataVersion`) для runtime-facing scaffolds;
- compile-time purity validation baseline для async/iterator/by-ref/abstract/void scenarios;
- reader-contract tests поверх generator output для будущего metadata reader.

### Exit Criteria

- package builds;
- solution integration completed;
- roadmap approved as baseline;
- generated metadata shape зафиксирован snapshot и reader-contract tests;
- purity policy не оставляет недетерминированных fast-path semantics на текущем scaffold-этапе.

## Phase 2. Lexer MVP

### Deliverables

- zero-alloc lexer;
- token stream tests;
- keyword tables;
- UTF handling.

### Exit Criteria

- lexer faster than baselines;
- no hidden allocations.

## Phase 3. Parser MVP

### Deliverables

- scripts/modules parser;
- AST arena model;
- error diagnostics.

### Exit Criteria

- broad syntax coverage;
- parser benchmark lead in managed class.

## Phase 4. Binder and Semantic Core

### Deliverables

- scopes;
- slots;
- captures;
- module binding;
- HIR.

### Exit Criteria

- semantic correctness for core language.

## Phase 5. Execution Core MVP

### Deliverables

- LIR;
- interpreter loop;
- frames;
- functions;
- closures;
- objects;
- arrays.

### Exit Criteria

- execute real scripts;
- stable session model.

## Phase 6. Host Generation MVP

### Deliverables

- source generator;
- generated metadata;
- generated binders;
- generated property/function tables.

### Exit Criteria

- no required runtime reflection on supported path.

## Phase 7. Standards Expansion

### Deliverables

- async/generators;
- typed arrays;
- proxies;
- modules full;
- builtin coverage expansion.

### Exit Criteria

- major Test262 blocks passing.

## Phase 8. Perf Domination Program

### Deliverables

- IC system;
- superinstructions;
- shape specialization;
- string and numeric specialization;
- pool tuning;
- branch-locality tuning.

### Exit Criteria

- beats all target analogs on agreed benchmark suite.

## Phase 9. Browser Parity and Fuzzing

### Deliverables

- differential test farm;
- browser parity reports;
- fuzz infrastructure.

### Exit Criteria

- no untriaged semantic deltas in priority areas.

## Phase 10. Production Hardening

### Deliverables

- API review;
- compatibility docs;
- NativeAOT sample apps;
- memory and perf dashboards;
- security review.

### Exit Criteria

- release candidate for internal adoption.

## Open Design Questions Requiring Agreement

1. Должен ли `JavaScriptRuntime` представлять одну session-state единицу или factory для короткоживущих session contexts?
2. Нужен ли отдельный public compile/prepared-script API или он должен остаться internal и управляться самим runtime?
3. Нужен ли explicit module loader contract, или только registration-based model?
4. Как именно проецировать C# overloads в JavaScript surface?
5. Поддерживаем ли `ref`, `out`, `Span<T>`, `ReadOnlySpan<T>` в user-facing projection model?
6. Должны ли generated adapters быть частично дебажимыми человеком или только machine-optimized?
7. Где проходит граница между compiler package и будущими browser API packages?
8. Нужен ли отдельный IR serializer для ahead-of-time prepared bundles?
9. Какой набор атрибутов обязателен в v1, а какой откладывается до v2?
10. Нужна ли поддержка plugin-like builtin packs или всё должно быть монолитно скомпоновано под AOT?

После фиксации lifecycle v1 открытыми остаются вопросы тонкой настройки, но не базовая модель public runtime.

## Immediate Next Steps

1. Зафиксировать public lifecycle model `JavaScriptRuntime`.
2. Согласовать attribute matrix v1/v2.
3. Зафиксировать benchmark corpus и правила сравнения с аналогами.
4. Определить execution core architecture decision record.
5. Начать с lexer MVP и measurement harness, не с runtime-интеграции.
