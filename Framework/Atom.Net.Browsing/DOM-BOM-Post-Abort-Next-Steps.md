# DOM/BOM Post-Abort Next Steps

## Назначение

Этот документ раскладывает маршрут после первого combined Url -> Abort pass.

Он нужен, чтобы следующие batches не выбирались заново после завершения Abort.

## Recommended order after Abort

1. DOM/Geometry
2. BOM/Scheduling
3. DOM/Observers
4. BOM/Permissions
5. DOM/Events base
6. DOM/Core base

## Why this order

- Geometry remains the lightest next DOM family after Abort.
- Scheduling is the first deliberate callback-plus-dictionary validation on BOM side.
- Observers should follow Geometry so their geometry-dependent signatures land on an existing baseline.
- Permissions should follow Scheduling because partial Navigator pressure is higher than partial Window pressure.
- Events base and Core base are intentionally delayed until isolated family mechanics are already proven.

## Gate after Geometry

- Confirm geometry types stay isolated from CSSOM View spillover.
- Confirm aliases and legacy geometry tails remain deferred.

## Gate after Scheduling

- Confirm delegate generation works cleanly.
- Confirm supporting dictionary files remain separate and minimal.
- Confirm only collected Window partial members were merged.

## Gate after Observers

- Confirm observer callbacks and init dictionaries follow one-entity-per-file.
- Confirm no HTML or CSSOM expansion occurred transitively.

## Gate after Permissions

- Confirm WorkerNavigator stayed deferred.
- Confirm only base permissions surface entered the merge path.
