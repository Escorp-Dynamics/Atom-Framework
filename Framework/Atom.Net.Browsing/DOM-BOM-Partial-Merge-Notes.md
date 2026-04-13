# DOM/BOM Partial Merge Notes

## Назначение

Этот документ фиксирует merge rules для ранних BOM batches, которые впервые затрагивают partial Window и partial Navigator.

Его задача: не принимать повторно архитектурные решения в момент генерации файлов.

## General merge rule

- Один публичный C# interface остается одним финальным типом.
- Partial members из разных normative sources не создают отдельные public types.
- Provenance partial members остается в inventory и generation notes, а не в размножении одного контракта по файлам.

## Window merge notes

### Window owning contract

- Final public type: IWindow.
- Primary ownership folder: BOM/Windowing.
- Scheduling batch не создает отдельный scheduling-specific window interface.

### Window allowed early partial sources

- HTML Window baseline, уже зафиксированный как canonical owner.
- Scheduling surface from requestIdleCallback.
- Другие partial sources не втягиваются автоматически только потому, что они тоже расширяют Window.

### Scheduling-specific merge rule

- requestIdleCallback(...) and cancelIdleCallback(...) merge into final IWindow.
- IdleRequestCallback delegate and IdleRequestOptions supporting contract не живут внутри IWindow file.
- IIdleDeadline остается самостоятельным interface contract.
- Scheduling batch не должен одновременно открывать unrelated Window partials из messaging, performance, clipboard or compatibility tails.

## Navigator merge notes

### Navigator owning contract

- Final public type: INavigator.
- Primary ownership folder: BOM/Navigator.
- Permissions batch не создает отдельный permissions-specific navigator interface как основное представление.

### Navigator allowed early partial sources

- HTML Navigator baseline and accepted navigator mixins.
- Permissions API window-facing partial surface.
- WebDriver automation disclosure surface stays independent in planning, но не конфликтует с base Navigator ownership.

### Permissions-specific merge rule

- Base permissions exposure merges into final INavigator only on the window-facing side.
- WorkerNavigator provenance stays deferred with the worker bucket.
- Descriptor specializations owned by later families do not expand the first permissions merge pass automatically.
- Navigator merge during Batch 3 should stay limited to members directly required by the generic Permissions API surface.

## Do not do during early batches

- Не открывать full Window cleanup batch во время Scheduling.
- Не открывать full Navigator cleanup batch во время Permissions.
- Не materialize WorkerNavigator or worker-global attachment points in the same pass.
- Не смешивать compatibility tails с modern baseline merge work.

## Exit condition

Early partial merge is considered validated when:

- IWindow accepts Scheduling partial members without namespace or ownership churn.
- INavigator accepts base Permissions partial members without pulling worker-side or compatibility-only surface.
- Supporting contracts remain one-entity-per-file and stay outside the owning interface files.
