# Specification Contract

Документ фиксирует текущее normative поведение для `JavaScriptRuntimeSpecification`.

## Modes

- `Extended` является default режимом создания runtime.
- `ECMAScript` представляет strict baseline без host registration surface.

## Lifecycle Invariants

- `ECMAScript` и `Extended` проходят через один и тот же lifecycle runtime.
- execution-state, execution-request, parsed-source и lowered-program обязаны сохранять выбранную specification без implicit fallback.
- async entry points и reset semantics не должны расходиться между режимами, кроме specification-aware policy checks.

## Lowering Policy

- lowering обязан materialize-ить явные policy flags, а не выводить strict и extended режим из неявного внешнего состояния.
- `ECMAScript` lowering path обязан выставлять `RequiresStrictRuntimeSurface`.
- `Extended` lowering path обязан выставлять `AllowsExtendedRuntimeSurface`.
- engine boundary должен опираться на lowered policy flags при выборе specification-aware behavior.
- если lowered policy требует `AllowsExtendedRuntimeSurface`, а runtime specification остаётся `ECMAScript`, это должно нормализоваться в lowering-phase `SpecificationViolation` до engine dispatch.
- lowering policy также должен materialize-ить capability flags из parser feature matrix: invocation, index access, mutation, literal materialization, operator lowering, template materialization, closure lowering, short-circuit lowering, aggregate literal materialization, spread lowering, conditional lowering, destructuring lowering и regular-expression materialization.
- перед engine dispatch lowered program должен materialize-ить отдельный execution-plan seed, чтобы дальнейшие runtime layers потребляли уже plan-oriented contract, а не читали lowering flags напрямую.
- execution-plan seed может материализовать более точные `EngineUnavailable` diagnostics для конкретных lowering capabilities, даже когда specification itself не нарушен.
- текущие capability-level diagnostics уже выделяют mutation lowering, index access lowering, invocation lowering, pure literal materialization, template materialization, closure lowering, short-circuit lowering, aggregate literal materialization, spread lowering, conditional lowering, destructuring lowering и regular-expression materialization как отдельные unsupported engine paths.
- binary и unary operator paths теперь тоже могут выходить через отдельный operator-lowering diagnostic, вместо прежнего generic engine-unavailable path.

## Parser Feature Flags

- parse boundary должен materialize-ить конкретные feature markers, а не только сохранять source length и specification.
- текущий минимальный набор markers включает identifier reference, member access, invocation, host-binding candidate, index access, assignment, string literal, numeric literal, unary operator, binary operator, comparison operator, logical operator, template literal, arrow-function candidate, nullish coalescing operator, optional-chaining candidate, array-literal candidate, object-literal candidate, spread/rest candidate, conditional-operator candidate, destructuring-pattern candidate и regular-expression-literal candidate.
- host-binding candidate пока фиксируется как descriptive marker без полноценного grammar-level host syntax validation.
- lowering может поднимать host-binding candidate в extended-surface policy даже до появления полноценного parser AST.
- когда `JavaScriptRuntimeSpecification.ECMAScript` сталкивается с `host.` candidate, runtime обязан нормализовать это в parser-phase `SpecificationViolation`, а не молча пропускать feature дальше до engine boundary.

## Strict-Incompatible Matrix

В strict `ECMAScript` режиме incompatible считаются следующие runtime value kinds:

- `HostObject`
- `InternalError`
- `StackOverflowError`
- `TimeoutError`
- `MemoryLimitError`
- `CancellationError`
- `HostInteropError`
- `ResourceExhaustedError`

Все эти kinds разрешены в `Extended`.

## Diagnostics Invariants

- host registration surface разрешён только в `Extended`.
- extended-only runtime values в strict `ECMAScript` не должны уходить в completed public projection path.
- strict violations должны переводиться в execution diagnostics с кодом `strict-value-kind-unsupported` до выхода на public execution boundary.
- engine-unavailable diagnostics обязаны оставаться specification-aware и различать `ECMAScript` и `Extended`.
- parser и lowering boundary failures должны materialize-иться через единый execution-result contract, а не через прямые исключения из scaffold layers.

## Change Discipline

- любые новые strict-incompatible kinds должны одновременно обновлять runtime policy, тесты и этот документ.
- любые новые lowering rules должны фиксироваться здесь до того, как станут нормативной частью execution contract.
