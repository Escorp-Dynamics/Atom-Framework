# DOM/BOM DOM Early Batches

## Назначение

Этот документ фиксирует безопасный ранний DOM sequence после стартового BOM Url batch.

Его задача — не начинать DOM generation с самых тяжелых family и поэтапно проверять generator pressure.

## Recommended order

1. DOM/Abort
2. DOM/Geometry
3. DOM/Observers
4. DOM/Events base
5. DOM/Core base

## Batch 1: DOM/Abort

### Contracts included in DOM Abort

- IAbortController
- IAbortSignal

### Why DOM Abort is first

- минимальная isolated family
- нет heavy partial merge pressure
- нет worker or Fetch spillover inside the DOM-owned slice itself
- хороший первый DOM pass после BOM Url

## Batch 2: DOM/Geometry

### Contracts included in DOM Geometry

- IDomRectReadOnly
- IDomRect
- IDomPointReadOnly
- IDomPoint
- IDomQuad
- IDomMatrixReadOnly
- IDomMatrix

### Why DOM Geometry is second

- geometry types are foundational for later observer and measurement surface
- family остается относительно lightweight
- не требует callback or dictionary support

## Batch 3: DOM/Observers

### Families included in DOM Observers

- IMutationObserver
- IResizeObserver
- IIntersectionObserver
- related callback delegates
- related observer init/options contracts

### Why DOM Observers are third

- validates callback-to-delegate and dictionary-support generation on DOM side
- benefits from geometry baseline already existing
- still avoids full HTML and large CSSOM pressure

## Batch 4: DOM/Events base

### Contracts included in DOM Events base

- IEvent
- IEventTarget
- event-listener baseline contracts needed for later family integration

### Why DOM Events base is fourth

- builds the minimal event foundation before heavy HTML element generation
- avoids touching compatibility tails like touch or legacy UI event methods

## Batch 5: DOM/Core base

### Contracts included in DOM Core base

- INode
- IElement
- IDocument
- IDocumentFragment
- IText
- IComment
- related core tree contracts from the accepted baseline

### Why DOM Core base is fifth

- core DOM is foundational but has higher merge pressure than Abort, Geometry and Observers
- delaying it keeps early validation focused on smaller families first

## Families to avoid as first DOM batch

- DOM/Html/Elements, because concrete HTML hierarchy is too wide for the first DOM pass
- DOM/Svg, because modern SVG baseline is large and has its own compatibility tails
- DOM/MathMl, because it should follow the broader DOM foundation rather than lead it
- DOM/Cssom and DOM/Cssom/View, because stylesheet and rule families add too much early complexity
- Forms and Media, because they depend on broader HTML element stabilization
