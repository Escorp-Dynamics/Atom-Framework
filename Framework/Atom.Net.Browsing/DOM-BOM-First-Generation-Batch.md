# First Generation Batch Recommendation

## Primary Recommendation

Первый generation batch: BOM Networking Url surface.

Это означает старт с двух основных контрактов:

- IUrl
- IUrlSearchParams

## Почему выбран именно этот batch

### 1. Минимальный policy risk

Url surface уже явно отделен от полной Fetch family.

Это позволяет начать generation pass без риска случайно затянуть:

- Fetch-owned types
- worker-specific surface
- console namespace modeling
- touch compatibility family
- WindowProxy-specific contract modeling

### 2. Минимальный merge pressure

Url family не требует тяжелого partial merge уровня Window, Document или Navigator.

Это хороший старт для проверки:

- one-entity-per-file discipline
- naming conventions
- namespace and folder placement
- baseline interface generation pipeline

### 3. Высокая foundation value при маленьком объеме

Url surface компактный, но потом естественно переиспользуется следующими family:

- navigation-adjacent contracts
- File API adjacent signatures
- messaging and storage related workflows
- later networking-focused batches

## Why not other candidates first

### DOM Observers

Хороший ранний batch, но лучше уже после проверки базового pipeline на более маленькой family.

Observers сразу тянут callbacks, dictionaries и geometry-adjacent types, поэтому они лучше подходят как следующий шаг, а не самый первый.

### DOM Abort

Тоже безопасный кандидат, но Url дает чуть более чистый старт на уровне isolated family planning и сразу открывает BOM-side generation path без конфликтов с deferred buckets.

### Scheduling Batch

Высокая value family, но она уже требует partial Window plus callback and dictionary handling. Это хороший batch номер два после Url.

## Recommended early order

1. Url
2. Scheduling
3. Permissions
4. Web Locks
5. Storage

## Batch 1 Execution Checklist

### Url

- Create BOM/Networking folder.
- Generate IUrl in its own file.
- Generate IUrlSearchParams in its own file.
- Keep Batch 1 strictly limited to URL Standard interface surface.
- Do not pull Fetch-owned types or Beacon spillover into the same pass.
- Do not couple Url generation to Window, Navigator or Document partial work.

## Batch 2 Preparation

### Scheduling

Scheduling is the first planned follow-up batch after Url.

Its purpose is to validate the first controlled step beyond plain standalone interfaces:

- callback-to-delegate mapping
- dictionary support type generation
- partial Window merge discipline

### Scheduling expected public surface

- IIdleDeadline
- IdleRequestOptions supporting contract
- IdleRequestCallback delegate
- Window partial members for requestIdleCallback(...)
- Window partial members for cancelIdleCallback(...)

### Batch 2 rules

- Keep Scheduling scoped only to requestIdleCallback surface from the collected normative source.
- Generate IdleRequestCallback as a named delegate in its own file.
- Generate IdleRequestOptions as a dedicated supporting contract in its own file.
- Keep IIdleDeadline as its own interface contract in its own file.
- Merge scheduling members into the final IWindow contract only after Url batch conventions are validated.
- Do not pull worker-side scheduling assumptions into this batch; requestIdleCallback remains Window-only.

### Batch 2 exit criteria

- delegate generation works under one-entity-per-file rules
- supporting dictionary generation works without collapsing into object or string
- partial Window members can be merged without changing the already accepted ownership model for IWindow
- no additional deferred family is imported transitively

## Batch 3 Preparation

### Permissions Batch

Permissions is planned only after Scheduling because it adds partial Navigator pressure.

### Permissions expected public surface

- IPermissions
- PermissionDescriptor supporting contract
- IPermissionStatus
- PermissionState enum
- PermissionSetParameters supporting contract, if the final generated window-side surface includes it
- Navigator partial members that expose permissions

### Permissions preparation notes

- Base permissions batch stays centered on the generic Permissions API surface rather than permission-specific descriptor families.
- WorkerNavigator provenance remains documented, but worker-side exposure is not materialized in this batch.
- Any descriptor specialization that belongs to Clipboard, MIDI, Push or other later families should not expand Batch 3 automatically.
- If a permission-related contract is needed only because of a later family, it stays deferred with that owning family instead of entering the base permissions slice.
- The goal of Batch 3 is partial Navigator validation, not full permission taxonomy completeness.

### Batch 3 rules

- Keep worker-side WorkerNavigator exposure deferred together with the rest of the worker family.
- Generate only the window-facing Permissions surface required by the first-pass boundary.
- Respect supporting-type policy for dictionaries and enums instead of flattening permission descriptors into weakly typed shapes.
- Treat any permission-name taxonomy or descriptor specialization pressure as a follow-up policy check, not as a blocker for the base batch plan.

## Batch-plan rationale

The sequence Url -> Scheduling -> Permissions is intentional.

- Url validates the smallest isolated family.
- Scheduling validates delegate, dictionary and partial Window mechanics.
- Permissions validates partial Navigator only after the generator path is already proven on simpler batches.

## Explicitly Out of Scope for Batch 1

- full Fetch family
- Beacon signatures that depend on Fetch-owned types
- console namespace modeling
- worker-specific families
- touch family
- legacy HTML, SVG, navigator and timing compatibility tails
- WindowProxy as standalone public contract

## Practical Goal of Batch 1

Цель первого batch не максимальный coverage volume, а validation of generation mechanics on the safest possible modern-baseline family.

Если Url batch проходит cleanly, следующий step — Scheduling, потому что он уже проверяет delegate, dictionary и partial Window integration поверх того же pipeline.
