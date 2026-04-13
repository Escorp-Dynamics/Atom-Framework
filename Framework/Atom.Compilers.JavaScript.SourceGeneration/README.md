# Atom.Compilers.JavaScript.SourceGeneration

Модуль `Atom.Compilers.JavaScript.SourceGeneration` предоставляет Roslyn-analyzer/source-generator инфраструктуру для JavaScript runtime-пакета. На текущем этапе пакет содержит каркас генераторов и анализаторов для публичных JavaScript-атрибутов и фиксирует точки расширения для дальнейшей генерации shape-table, host binding metadata и fast-path marshalling.

## Что уже есть

- `JavaScriptObject` generator scaffold для type-level export surface
- `JavaScriptDictionary` generator scaffold для dictionary-shape metadata
- `JavaScriptProperty` generator scaffold для property-level binding metadata
- `JavaScriptFunction` generator scaffold для function export metadata
- `JavaScriptIgnore` generator scaffold для ignore metadata и suppression surface
- Hidden analyzers для всех JavaScript marker-атрибутов, чтобы инфраструктура сразу регистрировала их в Roslyn pipeline

Object/dictionary/property/function scaffolds уже генерируют richer metadata с declaration name, kind и export name; object/dictionary также публикуют type-level flag constants, property/function публикуют member-level flags, а ignore scaffold делает то же для ignored declarations.

## Семантика JavaScriptIgnore

- `JavaScriptIgnore` участвует в generator pipeline наравне с остальными marker-атрибутами
- ignore-аннотированные type/member declarations исключаются из validation/export checks текущего scaffold-слоя
- текущий scaffold генерирует агрегированное metadata-представление ignored declarations по owning type с `Member{N}Name` и `Member{N}Kind` константами; multi-field declaration раскрывается в несколько field entries, а indexer/event тоже имеют явные kinds
- в базовом `Atom.SourceGeneration` для этого добавлен `MemberSyntaxProvider`, который покрывает type/property/field/method declarations единым reusable слоем
- `MemberSyntaxProvider` теперь имеет overridable extension points для declaration support и symbol resolution, чтобы later можно было безопасно добавить `event/indexer`-сценарии без дублирования базовой логики
- type-level metadata для `JavaScriptObject` и `JavaScriptDictionary` дополнительно содержит generator flags (`IsGlobalExportEnabled`, `IsStringKeysOnly`, `IsPreserveEnumerationOrder`)
- member-level metadata для `JavaScriptProperty` и `JavaScriptFunction` дополнительно содержит flags (`IsReadOnly`, `IsRequired`, `IsPure`, `IsInline`)

## Internal Metadata Contract

- в runtime-пакете добавлены internal contract-типы для будущего чтения generated metadata без ad-hoc reflection:
  - `JavaScriptGeneratedTypeMetadata`
  - `JavaScriptGeneratedMemberMetadata`
  - `JavaScriptGeneratedMemberKind`

## Цели текущего слоя

- сохранить NativeAOT-friendly модель без runtime reflection
- отделить compile-time анализ JavaScript-атрибутов от runtime-пакета
- подготовить стабильные точки входа для последующей генерации host adapters
- не раздувать публичный API: весь генераторный слой остаётся internal

## Ordering Contract

- generated entries идут в declaration order внутри одного syntax tree
- partial aggregation сохраняет порядок между source files в том виде, как Roslyn подаёт declarations в incremental pipeline
- multi-field и multi-event declarations раскрываются в порядке объявления variables
- unsupported declaration kinds не должны молча теряться: для известных неподдержанных сценариев добавляются explicit analyzers

## Export Policy

- `event` на текущем этапе поддерживается только как `JavaScriptIgnore` scenario; export-surface для event не формируется
- `indexer` на текущем этапе не входит в `JavaScriptProperty` export model и должен давать explicit diagnostic вместо молчаливого пропуска
- property/function/object/dictionary flag combinations должны валидироваться до runtime phase, если флаги создают недостижимую export-модель
- `JavaScriptFunction(IsPure = true)` сейчас compile-time ограничен concrete non-async non-iterator non-byref non-void methods с доступным body; abstract/interface и body-less pure surface должна отбрасываться analyzer-слоем до runtime phase

## Future Model Notes

- future `indexer` model потребует отдельный metadata contract: parameter list, getter/setter presence, key conversion policy и named surface strategy вместо обычного property export
- future `event` model потребует отдельный binding contract: subscribe/unsubscribe surface, delegate marshalling и lifecycle ownership; до этого момента `event` остаётся ignore-only
- inline/pure/property flags должны оставаться compile-time validated, чтобы runtime reader не получал заведомо невозможные metadata combinations

## Reader Contract Tests

- кроме exact generated source snapshot tests, пакет теперь поддерживает отдельный reader-contract слой, который прогоняет generator через Roslyn `GeneratorDriver`, извлекает emitted constants и валидирует runtime-facing metadata shape без привязки ко всему тексту файла
- этот слой нужен для безопасной эволюции будущего runtime reader: snapshots ловят текстовые регрессии, reader-contract tests ловят структурные регрессии в versioning, flags и member ordering
- текущий smoke-набор уже покрывает object/function/dictionary/ignore/property cases и partial ordering для property/function metadata
- source-generation test project намеренно ссылается на уже собранные generator assemblies через explicit assembly references, а не через `ProjectReference`: analyzer-style multi-assembly graph из `Atom.SourceGeneration` дублировал project entries в `GenerateDepsFile` для test host
- compile-time reference wiring в source-generation tests теперь делается target-time `ReferencePath` injection вместо formatter-чувствительных статических `HintPath`, а generator/runtime support assemblies копируются в output каталоги test host для parallel-safe запуска вместе с `Atom.Compilers.JavaScript.Tests`
- runtime reader и registration tests живут отдельно в `Atom.Compilers.JavaScript.Tests`, чтобы main package проверялся своим test project без смешения с generator/analyzer harness

## Current Tail Guarantees

- syntax discovery поддерживает `record class` и `record struct` hosts
- fully-qualified attribute spellings поддерживаются как для reader-contract scenarios, так и для analyzer export/flag extraction
- simple-name collisions между разными full type identities не объединяются молча и переводятся в generator diagnostic `A1001`
- duplicate export validation теперь охватывает и explicit interface implementations, если они публикуют одно и то же JavaScript-имя
- эти случаи считаются частью стабильного compile-time contract и защищены тестами

## Current Purity Diagnostics

- `ATOMJS110`: pure + async method
- `ATOMJS111`: pure + iterator/yield method
- `ATOMJS112`: pure + `ref/out/in` parameters
- `ATOMJS113`: pure + abstract/interface method
- `ATOMJS114`: pure + `void` return type
- `ATOMJS115`: pure + body-less non-abstract method
- `ATOMJS116`: pure + `Task`/`ValueTask` return type

Сводный контракт генераторов и текущих policy guarantees вынесен в [GENERATOR_CONTRACTS.md](GENERATOR_CONTRACTS.md).

Отдельный roadmap по будущей поддержке `indexer/event` вынесен в [INDEXER_EVENT_SUPPORT_ROADMAP.md](INDEXER_EVENT_SUPPORT_ROADMAP.md).

Отдельный blueprint по будущему internal runtime reader вынесен в [../Atom.Compilers.JavaScript/RUNTIME_READER_BLUEPRINT.md](../Atom.Compilers.JavaScript/RUNTIME_READER_BLUEPRINT.md).

## Подключение

Пакет задуман как analyzer dependency для `Atom.Compilers.JavaScript` и не требует отдельного runtime API.
