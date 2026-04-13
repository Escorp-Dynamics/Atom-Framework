# JavaScript Generator Contracts

Этот документ фиксирует текущий compile-time контракт генераторов `Atom.Compilers.JavaScript.SourceGeneration`.

## Metadata Shapes

- все generated scaffolds публикуют `MetadataVersion = 1`
- `JavaScriptObject`: type entry + `IsGlobalExportEnabled`
- `JavaScriptDictionary`: type entry + `IsStringKeysOnly` + `IsPreserveEnumerationOrder`
- `JavaScriptProperty`: member entries + `ExportName` + `IsReadOnly` + `IsRequired`
- `JavaScriptFunction`: member entries + `ExportName` + `IsPure` + `IsInline`
- `JavaScriptIgnore`: ignored entries + explicit `Kind` for `Class/Struct/Interface/Property/Method/Field/Indexer/Event`

## Ordering Guarantees

- entries внутри одного declaration block идут в syntactic order
- partial aggregation идёт в том порядке, в котором Roslyn поставляет declarations в incremental pipeline
- multi-field и multi-event declarations раскрываются в порядке объявления variables

## Identity and Discovery Guarantees

- `record class` и `record struct` участвуют в entity discovery наравне с `class`, `struct` и `interface`
- fully-qualified spelling атрибутов поддерживается на syntax-level, включая формы вида `global::Atom.Compilers.JavaScript.JavaScriptFunction(...)`
- grouping по simple name больше не может молча склеить разные full type identities: такие случаи должны завершаться generator diagnostic `A1001`
- namespace collisions и nested collisions считаются contract-значимыми сценариями и покрываются отдельными tests

## Export Policy

- `event` сейчас поддерживается только как ignore-only scenario
- `indexer` сейчас explicit-unsupported для `JavaScriptProperty` export model
- unsupported scenarios должны давать analyzers, а не молча выпадать из generated metadata
- duplicate exported names внутри одного owning type запрещены и должны диагностироваться до runtime phase
- explicit interface implementations входят в duplicate-export validation для `JavaScriptFunction`, если они проецируются в то же exported name

## Flag Validation Policy

- `JavaScriptProperty(IsReadOnly = true)` требует getter
- `JavaScriptProperty(IsRequired = true)` требует getter
- `JavaScriptFunction(IsInline = true)` недопустим для abstract/interface methods
- `JavaScriptFunction(IsPure = true)` недопустим для async methods на текущем этапе scaffold
- `JavaScriptFunction(IsPure = true)` недопустим для iterator methods
- `JavaScriptFunction(IsPure = true)` недопустим для `ref/out/in` parameters
- `JavaScriptFunction(IsPure = true)` недопустим для abstract/interface methods без concrete body
- `JavaScriptFunction(IsPure = true)` недопустим для `void` return type
- `JavaScriptFunction(IsPure = true)` недопустим для body-less non-abstract methods (`extern`, partial definition without body)
- `JavaScriptFunction(IsPure = true)` недопустим для `Task`/`ValueTask` return type

## Purity Diagnostic Matrix

- `ATOMJS110`: async methods
- `ATOMJS111`: iterator methods
- `ATOMJS112`: `ref/out/in` parameters
- `ATOMJS113`: abstract/interface methods
- `ATOMJS114`: `void` return type
- `ATOMJS115`: body-less non-abstract methods
- `ATOMJS116`: `Task`/`ValueTask` return type

## Runtime Contract

- generated metadata целится в internal runtime-модель:
  - `JavaScriptGeneratedTypeMetadata`
  - `JavaScriptGeneratedMemberMetadata`
  - `JavaScriptGeneratedMemberKind`
- `JavaScriptGeneratedTypeMetadata.MetadataVersion` синхронизирован с generated constant `MetadataVersion`
- runtime reader пока не реализован, но shape контракта уже зарезервирован в runtime package

## Contract Lock Tests

- текущие generator tests с точным сравнением generated source выступают как reader-facing contract lock
- отдельные reader-contract tests дополнительно прогоняют generator output через lightweight test-reader и валидируют `MetadataVersion`, flags и member ordering как runtime-facing shape
- текущий reader-contract smoke покрывает object/function/dictionary/ignore/property metadata и partial ordering для property/function aggregation
- отдельные analyzer tests дополнительно фиксируют tail-cases вокруг fully-qualified attribute discovery, record hosts, namespace/nested collisions и explicit-interface duplicate exports
- изменение names, order, flags, kinds или policy diagnostics должно сопровождаться обновлением этих tests и документации
- topology rule: source-generation tests не должны использовать generator stack через `ProjectReference`, потому что analyzer dependency packaging в `Atom.SourceGeneration` и `Atom.Compilers.JavaScript.SourceGeneration` может продуцировать duplicate project keys в `GenerateDepsFile` для executable test host
- topology rule: generator/analyzer harness зависит от explicit assembly references к уже собранным generator DLL, а runtime registration/reader tests остаются в отдельном `Atom.Compilers.JavaScript.Tests`
