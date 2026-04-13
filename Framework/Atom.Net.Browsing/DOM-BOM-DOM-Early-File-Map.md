# DOM/BOM DOM Early File Map

## Назначение

Этот документ раскладывает ранние DOM batches по конкретным target files.

Он дополняет DOM early batch order и служит прямым bridge к future file creation passes.

## DOM Batch 1: Abort

### DOM/Abort

- DOM/Abort/IAbortController.cs
- DOM/Abort/IAbortSignal.cs

## DOM Batch 2: Geometry

### DOM/Geometry

- DOM/Geometry/IDomRectReadOnly.cs
- DOM/Geometry/IDomRect.cs
- DOM/Geometry/IDomPointReadOnly.cs
- DOM/Geometry/IDomPoint.cs
- DOM/Geometry/IDomQuad.cs
- DOM/Geometry/IDomMatrixReadOnly.cs
- DOM/Geometry/IDomMatrix.cs

## DOM Batch 3: Observers

### DOM/Observers

- DOM/Observers/IMutationObserver.cs
- DOM/Observers/IResizeObserver.cs
- DOM/Observers/IIntersectionObserver.cs
- DOM/Observers/MutationCallback.cs
- DOM/Observers/ResizeObserverCallback.cs, if kept as the accepted callback name from collected provenance
- DOM/Observers/IntersectionObserverCallback.cs, if kept as the accepted callback name from collected provenance
- DOM/Observers/MutationObserverInit.cs
- DOM/Observers/ResizeObserverOptions.cs, if required by the accepted observer signatures
- DOM/Observers/IntersectionObserverInit.cs, if required by the accepted observer signatures

Notes:

- Observer callback and dictionary file names must follow the collected normative names during actual generation.
- Geometry dependencies should already exist before this batch starts.

## DOM Batch 4: Events base

### DOM/Events

- DOM/Events/IEvent.cs
- DOM/Events/IEventListener.cs, if generated as a callback interface contract from the collected DOM Standard surface
- DOM/Events/AddEventListenerOptions.cs
- DOM/Events/EventListenerOptions.cs

### DOM/Core ownership for event baseline

- DOM/Core/IEventTarget.cs, even when materialized during the early event-baseline pass because ownership stays with DOM/Core

Notes:

- Touch and other compatibility-first event families stay outside this early file map.
- Legacy event initialization methods do not affect file creation planning for the base event batch.

## DOM Batch 5: Core base

### DOM/Core base contracts

- DOM/Core/INode.cs
- DOM/Core/IElement.cs
- DOM/Core/IDocument.cs
- DOM/Core/IDocumentFragment.cs
- DOM/Core/IText.cs
- DOM/Core/IComment.cs
- DOM/Core/IAttr.cs
- DOM/Core/ICharacterData.cs

Notes:

- This batch should remain narrower than the full Stage 3 inventory and validate only the first core tree slice.
- Full DOM Core completion can continue after the early batch path is proven.

## Map constraints

- One entity per file applies to interfaces, callback interfaces, delegates, dictionaries and enums.
- Supporting contracts should be created in the same owning family folder.
- Deferred compatibility and legacy tails stay out of this map.
- This file map is intentionally narrower than the full DOM stage map.
