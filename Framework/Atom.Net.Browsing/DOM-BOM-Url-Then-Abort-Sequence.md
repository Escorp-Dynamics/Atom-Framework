# DOM/BOM Url Then Abort Sequence

## Назначение

Этот документ фиксирует первый combined code path: старт с BOM Url batch, затем переход к DOM Abort batch.

Он нужен, чтобы не смешивать следующий DOM pass с более тяжелыми family вроде Geometry, Observers или Core.

## Step 1: Materialize layout prerequisites

- Create DOM and BOM root folders.
- Create BOM/Networking.
- Create DOM/Abort.

## Step 2: Execute BOM Url batch

### Url batch files

- BOM/Networking/IUrl.cs
- BOM/Networking/IUrlSearchParams.cs

### Url batch validation goal

- validate one-entity-per-file discipline
- validate naming and namespace placement
- validate isolated interface generation without partial merge pressure

## Step 3: Confirm post-Url gate

- No extra networking families were pulled in.
- No Fetch-owned types were introduced.
- File placement matches the early batch file map.

## Step 4: Execute DOM Abort batch

### Abort batch files

- DOM/Abort/IAbortController.cs
- DOM/Abort/IAbortSignal.cs

### Abort batch validation goal

- validate first DOM-side batch on an isolated family
- confirm DOM root namespace and folder conventions work the same as BOM
- keep support-type pressure near zero before moving to Geometry and Observers

## Step 5: Confirm post-Abort gate

- No worker-side or Fetch-owned spillover entered the DOM-owned slice.
- No unrelated DOM Core or Event surface was pulled in.
- Naming and file placement still align with the accepted C Sharp rules.

## Next recommended step after Abort

1. DOM/Geometry
2. BOM/Scheduling
3. DOM/Observers

This order keeps each next pass focused on a single new source of generator complexity.
