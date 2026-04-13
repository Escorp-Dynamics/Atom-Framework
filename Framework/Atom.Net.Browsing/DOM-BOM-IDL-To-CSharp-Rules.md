# DOM/BOM IDL To C Sharp Rules

## Назначение

Этот документ выносит в одно место правила маппинга Web IDL surface в C# contracts для DOM/BOM migration.

Он не заменяет inventory или roadmap, а фиксирует нормализованные conversion rules для дальнейшей генерации.

## Naming rules

- interface X -> interface IX
- PascalCase используется для типов и членов.
- I-префикс используется только для интерфейсов.
- Web IDL names сохраняются максимально близко к оригиналу, но в корректном C# casing.
- Root namespaces остаются Atom.Net.Browsing.DOM и Atom.Net.Browsing.BOM.
- Внутри типов, файлов и subnamespace используется Html, Css, Svg, Url, Xml, MathMl.

## Type kind mapping

- interface -> interface
- mixin -> standalone interface without a default Mixin suffix
- callback -> named delegate in its own file
- dictionary -> dedicated supporting contract type in its own file when required by public signatures
- enum -> dedicated enum in its own file when the source defines a closed vocabulary
- typedef -> mapped CLR type with provenance retained in documentation instead of alias declarations
- namespace -> deferred non-interface surface unless a later explicit modeling decision says otherwise

## Composition rules

- Web IDL inheritance maps to interface inheritance.
- includes relations map to explicit inheritance from standalone mixin interfaces.
- partial interfaces merge into one final public C# interface.
- One public type is not split across multiple folders to mirror provenance.
- Provenance remains in inventory, generation notes and XML documentation.

## Member mapping rules

- attribute -> property
- readonly attribute -> get-only property
- operation -> method
- constructor declarations are not modeled inside interfaces
- static members stay deferred until an explicit modeling strategy is approved
- stringifier members stay deferred until a dedicated ToString review is approved
- event handler attributes stay under the agreed event-model strategy and are not improvised ad hoc during early batches

## Primitive and collection mapping

- DOMString and USVString -> string
- boolean -> bool
- short, long, long long -> short, int, long
- unsigned variants -> ushort, uint, ulong unless there is a later unification decision
- double and unrestricted double -> double
- float -> float
- object -> object
- any -> object?
- sequence<T> -> IReadOnlyList<T>
- FrozenArray<T> -> IReadOnlyList<T>
- record<K, V> -> IReadOnlyDictionary<K, V>
- Promise<T> -> ValueTask<T>
- Promise<void> -> ValueTask
- nullable -> nullable reference or value type

## Deferred mapping areas

- union types require an explicit strategy before generation
- named and indexed property semantics require a separate decision
- legacy platform objects require review before direct modeling
- namespace-based APIs remain outside the first interface-generation pass
- full Fetch family stays deferred to a dedicated networking-focused pass
- worker-specific exposure does not enter the window-centric first pass automatically

## Supporting type boundary

- Supporting types are allowed when needed to preserve a type-safe spec-shaped signature.
- Supporting types do not override the interface-first principle, but they are allowed where enums, dictionaries and delegates are necessary.
- Deferred families do not pull their supporting types into the first pass transitively.

## Early batch application

- Url batch validates base naming, placement and one-entity-per-file rules.
- Scheduling batch validates delegates, dictionaries and partial Window merge.
- Permissions batch validates partial Navigator merge without worker-side spillover.
