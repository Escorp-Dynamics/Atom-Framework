# DOM/BOM Spec Sync Reference

## Назначение

Этот документ фиксирует текущий synchronization state для DOM/BOM migration planning.

Его задача — быстро отвечать на три вопроса:

- какие normative families уже inventoried
- какие family входят в first-pass modern baseline
- какие family остаются deferred, compatibility-only или review-gated

## Snapshot Status

- Inventory collected and normalized
- First-pass scope frozen
- Supporting types policy extracted into a separate document
- Roadmap and inventory synchronized at checklist level
- First generation batch selected as Url surface
- Early batch execution order and partial merge notes documented

## DOM Families: Collected

- DOM Standard core and abort surface
- HTML DOM-facing surface
- HTML concrete element hierarchy
- UI Events modern baseline
- Pointer Events
- Input Events
- Touch Events, marked as compatibility-first
- Clipboard API and Events
- Selection API
- CSSOM base
- CSSOM View
- CSS Animations
- CSS Transitions
- Geometry Interfaces
- Fullscreen API
- Resize Observer
- Intersection Observer
- DOM Parsing and Serialization
- Shadow DOM
- File API DOM-facing surface
- SVG DOM surface including masking and filter effects ownership
- MathML DOM baseline

## BOM Families: Collected

- Window and WindowProxy baseline provenance
- Navigator baseline
- History and Location
- Navigation family
- Web Messaging and channels
- Workers provenance, but worker-specific generation deferred
- Web Storage surface and Storage Standard manager layer
- Permissions API
- Web Locks
- Console namespace surface, deferred from interface generation
- Screen Orientation
- High Resolution Time
- User Timing
- Performance Timeline
- Navigation Timing
- Page Visibility historical provenance with HTML as canonical owner
- Scheduling and requestIdleCallback
- URL surface
- Beacon provenance reviewed
- Fetch family reviewed and deferred as standalone networking pass
- WebDriver automation disclosure surface

## First-Pass Modern Baseline

### DOM

- Core, Traversal, Ranges, Selection, Abort, Geometry, Observers
- DOM Parsing baseline
- modern DOM event baseline
- CSSOM and CSSOM View baseline
- HTML baseline and concrete modern HTML element family
- Forms and Media modern contracts
- Fullscreen
- Clipboard modern surface
- SVG modern baseline
- MathML baseline

### BOM

- Windowing, Navigation, History, Location, Navigator
- Messaging
- Storage
- Permissions
- Screen
- Performance and Timing modern baseline
- Scheduling
- Url surface in Networking
- Web Locks
- navigator.webdriver disclosure surface

## Deferred Buckets

### Compatibility and legacy

- Touch family
- legacy DOM and UI event tails
- geometry aliases
- legacy SVG compatibility tails
- legacy navigator plugins and mime types
- obsolete HTML element interfaces
- obsolete timing objects

### Worker-specific

- Worker and SharedWorker families
- WorkerGlobalScope families
- WorkerNavigator and WorkerLocation
- service-worker-adjacent shared contracts

### Non-interface or large networking

- console namespace modeling
- full Fetch family
- Beacon signatures that require Fetch-owned types
- WindowProxy as standalone contract

### Review-gated

- Typed OM surface
- portal-related HTML surface
- union-heavy or unresolved signature areas that need separate mapping decisions

## Primary Planning Artifacts

- [DOM-BOM-Roadmap.md](DOM-BOM-Roadmap.md)
- [DOM-BOM-WebIDL-Inventory.md](DOM-BOM-WebIDL-Inventory.md)
- [DOM-BOM-Supporting-Types-Policy.md](DOM-BOM-Supporting-Types-Policy.md)
- [DOM-BOM-Spec-Sync-Checklist.md](DOM-BOM-Spec-Sync-Checklist.md)
- [DOM-BOM-First-Generation-Batch.md](DOM-BOM-First-Generation-Batch.md)
- [DOM-BOM-Partial-Merge-Notes.md](DOM-BOM-Partial-Merge-Notes.md)
- [DOM-BOM-Early-Batch-File-Map.md](DOM-BOM-Early-Batch-File-Map.md)
- [DOM-BOM-IDL-To-CSharp-Rules.md](DOM-BOM-IDL-To-CSharp-Rules.md)
- [DOM-BOM-Stage7-BOM-File-Map.md](DOM-BOM-Stage7-BOM-File-Map.md)
- [DOM-BOM-DOM-Stage-File-Map.md](DOM-BOM-DOM-Stage-File-Map.md)
- [DOM-BOM-DOM-Early-Batches.md](DOM-BOM-DOM-Early-Batches.md)
- [DOM-BOM-DOM-Early-File-Map.md](DOM-BOM-DOM-Early-File-Map.md)
- [DOM-BOM-Generation-Checklist.md](DOM-BOM-Generation-Checklist.md)
- [DOM-BOM-Layout-Task-List.md](DOM-BOM-Layout-Task-List.md)
- [DOM-BOM-Url-Then-Abort-Sequence.md](DOM-BOM-Url-Then-Abort-Sequence.md)
- [DOM-BOM-File-Templates.md](DOM-BOM-File-Templates.md)
- [DOM-BOM-Post-Abort-Next-Steps.md](DOM-BOM-Post-Abort-Next-Steps.md)

## Next Expected Transition

После этого reference state следующий практический шаг — либо физическая DOM/BOM layout preparation, либо прямой старт Url batch по уже зафиксированному early file map.
