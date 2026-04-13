<!-- markdownlint-disable MD034 -->

# Atom.Net.Browsing DOM/BOM Web IDL Inventory

## Назначение

Этот документ фиксирует нормативные источники Web IDL, которые будут использоваться для поэтапного переноса DOM/BOM-контрактов в структуру с отдельными файлами и каталогами DOM и BOM.

## Правила использования inventory

- [x] Primary source для Living Standard API берется из WHATWG, если соответствующая поверхность живет там.
- [x] W3C Editor's Draft / Recommendation используется там, где API поддерживается профильной спецификацией W3C и не определен полноценно в WHATWG.
- [x] HTML-поверхности инвентаризируются по тематическим subsection URL, а не по корневому документу, потому что root-fetch отрабатывает нестабильно.
- [x] В inventory фиксируются не только interface, но и partial interface, interface mixin, callback, enum, typedef и dictionary, если они влияют на сигнатуры будущих интерфейсов.
- [x] Устаревшие и retired API не исключаются молча: они помечаются явно и требуют отдельного решения перед генерацией.

## Статусы

- [x] Collected: источник исследован, базовая IDL-поверхность извлечена.
- [ ] Pending: источник еще не снят или снят не полностью.
- [ ] Decision required: источник или часть поверхности требует архитектурного решения перед генерацией.
- [ ] Legacy review: спецификация или часть API устарела, но потенциально влияет на полноту покрытия.

## DOM Primary Sources

### DOM Core and Tree

- [x] DOM Standard
  - Source:
    - https://dom.spec.whatwg.org/
    - alternative extraction: WHATWG source file dom.bs from repository whatwg/dom
  - Area: DOM/Core, DOM/Traversal, DOM/Ranges, DOM/Events baseline, DOM/Abort
  - Key IDL entities:
    - Event
    - CustomEvent
    - EventTarget
    - EventListener callback interface
    - EventListenerOptions
    - AddEventListenerOptions
    - Node
    - Document
    - DocumentFragment
    - ShadowRoot
    - Element
    - Attr
    - CharacterData
    - Text
    - CDATASection
    - ProcessingInstruction
    - Comment
    - NodeList
    - NamedNodeMap
    - DOMTokenList
    - ParentNode mixin
    - NonElementParentNode mixin
    - DocumentOrShadowRoot mixin
    - AbstractRange
    - StaticRange
    - Range
    - NodeIterator
    - TreeWalker
    - MutationObserver
    - MutationRecord
    - MutationCallback
    - MutationObserverInit
    - AbortController
    - AbortSignal
  - Notes: page extractor для dom.spec.whatwg.org оказался ненадежным, но alternative extraction через исходный dom.bs подтвердил базовое ядро DOM, traversal, ranges, mutation observers, abort surface и event baseline.

### HTML DOM-facing Surface

- [x] HTML Standard, DOM-facing sections
  - Source:
    - https://html.spec.whatwg.org/multipage/dom.html
    - https://html.spec.whatwg.org/multipage/webappapis.html
  - Area: DOM/Html, DOM/Forms, DOM/Media, DOM/Events, BOM/Windowing shared surface
  - Key IDL entities:
    - partial Document
    - partial DocumentOrShadowRoot mixin
    - HTMLElement
    - HTMLUnknownElement
    - HTMLOrSvgElement mixin
    - DOMStringMap
    - DocumentReadyState
    - DocumentVisibilityState
    - HTMLOrSvgScriptElement typedef
    - GlobalEventHandlers mixin
    - WindowEventHandlers mixin
    - EventHandler callback/typedef layer
    - TimerHandler typedef
    - WindowOrWorkerGlobalScope mixin
    - SubmitEvent
    - BeforeUnloadEvent
    - PageTransitionEvent
    - ShowPopoverOptions
    - TogglePopoverOptions
  - Notes: HTML через subsection URL дает пригодную и широкую выборку для Document, HTMLElement, event handler contracts и shared global mixin surface.

- [x] HTML Standard, concrete element interface hierarchy
  - Source:
    - https://html.spec.whatwg.org/multipage/indices.html#elements-3
    - chapter-local ownership sections from multipage HTML for forms, embedded content, tables, text-level semantics and interactive elements
  - Area: DOM/Html/Elements
  - Key IDL entities:
    - HTMLAnchorElement
    - HTMLAreaElement
    - HTMLAudioElement
    - HTMLBaseElement
    - HTMLBodyElement
    - HTMLBRElement
    - HTMLButtonElement
    - HTMLCanvasElement
    - HTMLDataElement
    - HTMLDataListElement
    - HTMLDetailsElement
    - HTMLDialogElement
    - HTMLDivElement
    - HTMLDListElement
    - HTMLEmbedElement
    - HTMLFieldSetElement
    - HTMLFormElement
    - HTMLHeadElement
    - HTMLHeadingElement
    - HTMLHRElement
    - HTMLHtmlElement
    - HTMLIFrameElement
    - HTMLImageElement
    - HTMLInputElement
    - HTMLLabelElement
    - HTMLLegendElement
    - HTMLLIElement
    - HTMLLinkElement
    - HTMLMapElement
    - HTMLMediaElement
    - HTMLMenuElement
    - HTMLMetaElement
    - HTMLMeterElement
    - HTMLModElement
    - HTMLObjectElement
    - HTMLOListElement
    - HTMLOptGroupElement
    - HTMLOptionElement
    - HTMLOutputElement
    - HTMLParagraphElement
    - HTMLPictureElement
    - HTMLPreElement
    - HTMLProgressElement
    - HTMLQuoteElement
    - HTMLScriptElement
    - HTMLSelectElement
    - HTMLSlotElement
    - HTMLSourceElement
    - HTMLSpanElement
    - HTMLStyleElement
    - HTMLTableCaptionElement
    - HTMLTableCellElement
    - HTMLTableColElement
    - HTMLTableElement
    - HTMLTableRowElement
    - HTMLTableSectionElement
    - HTMLTemplateElement
    - HTMLTextAreaElement
    - HTMLTimeElement
    - HTMLTitleElement
    - HTMLTrackElement
    - HTMLUListElement
    - HTMLVideoElement
  - Notes: broad HTML ownership was already captured, but this separate block closes the concrete browser-facing element family explicitly. The HTML elements index is not itself the canonical normative owner for every member, yet it is the safest authoritative enumeration source for completeness audits and file-per-entity planning.

### CSSOM View

- [x] CSSOM View Module
  - Source: https://drafts.csswg.org/cssom-view/
  - Area: DOM/Cssom/View, BOM/Screen, BOM/Windowing
  - Key IDL entities:
    - MediaQueryList
    - MediaQueryListEvent
    - Screen
    - VisualViewport
    - partial Window
    - partial Document
    - partial Element
    - partial HTMLElement
    - partial HTMLImageElement
    - partial Range
    - partial MouseEvent
    - GeometryUtils mixin
    - GeometryNode typedef
    - ScrollBehavior and related scroll dictionaries/enums
  - Notes: источник уже дает значительную долю BOM/Windowing и DOM/View-расширений через partial interface.

### CSS Animations and Transitions

- [x] CSS Animations Level 1
  - Source: https://drafts.csswg.org/css-animations-1/
  - Area: DOM/Cssom, DOM/Events
  - Key IDL entities:
    - AnimationEvent
    - AnimationEventInit
    - CSSKeyframeRule
    - CSSKeyframesRule
    - partial CSSRule keyframe constants
    - partial GlobalEventHandlers mixin animation handler attributes
  - Notes: this is a separate browser-facing surface and should not be inferred transitively from generic CSSOM. It closes the gap for animation events and keyframes rule contracts already referenced by roadmap-level DOM coverage.

- [x] CSS Transitions
  - Source:
    - https://drafts.csswg.org/css-transitions-2/
    - transition event provenance via CSS Transitions Level 1 definitions referenced by the Level 2 draft
  - Area: DOM/Cssom, DOM/Events
  - Key IDL entities:
    - TransitionEvent
    - TransitionEventInit
    - CSSTransition
    - CSSStartingStyleRule
    - partial GlobalEventHandlers mixin transition handler attributes
  - Notes: TransitionEvent was a real omission in the explicit inventory. Level 2 additionally introduces CSSTransition and CSSStartingStyleRule, so transitions must be treated as a standalone DOM/CSSOM family rather than a side note under generic styling APIs.

### Selection

- [x] Selection API
  - Source: https://w3c.github.io/selection-api/
  - Area: DOM/Selection
  - Key IDL entities:
    - Selection
    - GetComposedRangesOptions
    - partial Document
    - partial Window
    - partial GlobalEventHandlers mixin
  - Notes: Selection не изолирован от Document/Window, поэтому потребует merge partial surface на этапе генерации.

### Pointer and Input-adjacent Events

- [x] Pointer Events
  - Source: https://w3c.github.io/pointerevents/
  - Area: DOM/Events
  - Key IDL entities:
    - PointerEvent
    - PointerEventInit
    - WheelEvent
    - WheelEventInit
    - MouseEvent
    - MouseEventInit
    - partial Element
    - partial GlobalEventHandlers mixin
    - partial Navigator
  - Notes: присутствуют и современные, и legacy-сигнатуры вроде initMouseEvent; нужен отдельный legacy review.

- [x] UI Events
  - Source:
    - https://w3c.github.io/uievents/
    - https://w3c.github.io/uievents/#idl-index
  - Area: DOM/Events
  - Key IDL entities:
    - UIEvent
    - UIEventInit
    - FocusEvent
    - FocusEventInit
    - InputEvent
    - InputEventInit
    - KeyboardEvent
    - KeyboardEventInit
    - EventModifierInit
    - CompositionEvent
    - CompositionEventInit
  - Notes: modern public event surface снят полностью. MouseEvent и WheelEvent в текущей редакции вынесены в Pointer Events, поэтому UI Events здесь нужен в основном для UI/focus/keyboard/composition/input ядра. Спецификация также содержит deprecated partials и legacy types вроде initUIEvent, initKeyboardEvent, initCompositionEvent, UIEvent.which, KeyboardEvent.charCode/keyCode, keypress, DOMActivate, DOMFocusIn/DOMFocusOut и TextEvent; их нужно держать в отдельном legacy review, а не смешивать с first-pass modern contracts.

- [x] Input Events
  - Source:
    - https://w3c.github.io/input-events/
    - https://w3c.github.io/input-events/#interface-InputEvent
  - Area: DOM/Events, DOM/Editing
  - Key IDL entities:
    - partial InputEvent
    - partial InputEventInit
    - DataTransfer-backed input payload surface
    - StaticRange-backed target range surface via getTargetRanges()
  - Notes: спецификация не переопределяет базовый InputEvent, а расширяет его partial members: nullable dataTransfer и getTargetRanges(), плюс partial dictionary member targetRanges. Отдельно фиксирует normative inputType taxonomy для beforeinput/input, composition-specific ordering и edit-host semantics. Для генерации это означает merge с базовым InputEvent из UI Events, а не создание альтернативного независимого интерфейса.

- [x] Touch Events
  - Source:
    - https://w3c.github.io/touch-events/
    - https://w3c.github.io/touch-events/#touch-interface
    - https://w3c.github.io/touch-events/#touchlist-interface
    - https://w3c.github.io/touch-events/#touchevent-interface
  - Area: DOM/Events
  - Key IDL entities:
    - TouchType
    - TouchInit
    - Touch
    - TouchList
    - TouchEventInit
    - TouchEvent
    - partial interface mixin GlobalEventHandlers with ontouchstart/ontouchend/ontouchmove/ontouchcancel
  - Notes: спецификация явно помечает Touch Events как legacy API и рекомендует Pointer Events как современную замену. Несмотря на это, public IDL surface остается значимым для полноты browser contract inventory. TouchEvent опирается на UI Events EventModifierInit, а раздел про conditional exposure legacy touch event APIs важен для policy-решения: включать ли touch handler attributes в first-pass baseline или выносить их в compatibility layer.

### Clipboard and Editing

- [x] Clipboard API and Events
  - Source: https://w3c.github.io/clipboard-apis/
  - Area: DOM/Clipboard, BOM/Navigator shared surface
  - Key IDL entities:
    - Clipboard
    - ClipboardItem
    - ClipboardEvent
    - ClipboardEventInit
    - ClipboardChangeEvent
    - ClipboardChangeEventInit
    - ClipboardUnsanitizedFormats
    - ClipboardPermissionDescriptor
    - partial Navigator
  - Notes: часть поверхности относится к permission-модели и navigator extensions, а не только к DOM event contracts.

### File and URL-linked DOM Surface

- [x] File API
  - Source: https://w3c.github.io/FileAPI/
  - Area: DOM/Core shared, DOM/Forms, BOM/Networking shared
  - Key IDL entities:
    - Blob
    - File
    - FileList
    - FileReader
    - FileReaderSync
    - BlobPropertyBag
    - FilePropertyBag
    - BlobPart typedef/union-adjacent surface
    - partial URL
  - Notes: FileList historically считается рискованной поверхностью для долгосрочной эволюции стандарта, но для полноты DOM-контракта ее нельзя игнорировать.

### Fullscreen and View State

- [x] Fullscreen API
  - Source: https://fullscreen.spec.whatwg.org/
  - Area: DOM/Fullscreen, BOM/Windowing shared surface
  - Key IDL entities:
    - FullscreenOptions
    - FullscreenNavigationUI
    - partial Element
    - partial Document
    - partial DocumentOrShadowRoot mixin
  - Notes: значим именно как источник partial extension для Element, Document и ShadowRoot-related surfaces.

### Geometry and Observers

- [x] Geometry Interfaces Module Level 1
  - Source: https://drafts.csswg.org/geometry/
  - Area: DOM/Geometry
  - Key IDL entities:
    - DOMPointReadOnly
    - DOMPoint
    - DOMPointInit
    - DOMRectReadOnly
    - DOMRect
    - DOMRectInit
    - DOMRectList
    - DOMQuad
    - DOMQuadInit
    - DOMMatrixReadOnly
    - DOMMatrix
    - DOMMatrix2DInit
    - DOMMatrixInit
  - Notes: актуальный canonical draft расположен на CSSWG; IDL Index подтверждает полное geometry baseline покрытие, включая legacy aliases SVGPoint, SVGRect, SVGMatrix и WebKitCSSMatrix, а DOMRectList требует отдельного legacy review как compatibility-only surface.

- [x] Resize Observer
  - Source: https://drafts.csswg.org/resize-observer-1/
  - Area: DOM/Observers
  - Key IDL entities:
    - ResizeObserverBoxOptions
    - ResizeObserverOptions
    - ResizeObserver
    - ResizeObserverCallback
    - ResizeObserverEntry
    - ResizeObserverSize
  - Notes: актуальный draft находится на CSSWG; публичный IDL surface закрыт полностью. В processing model фигурирует ResizeObservation, но это non-exposed example struct и не должен попадать в публичный контрактный слой как обычная Web IDL сущность.

- [x] Intersection Observer
  - Source: https://w3c.github.io/IntersectionObserver/
  - Area: DOM/Observers
  - Key IDL entities:
    - IntersectionObserverCallback
    - IntersectionObserver
    - IntersectionObserverEntry
    - IntersectionObserverEntryInit
    - IntersectionObserverInit
  - Notes: source дал не только public IDL, но и важные behavioral constraints для rootMargin, scrollMargin, threshold sorting, delay, trackVisibility и cross-origin suppression of rootBounds. Эти правила стоит сохранить как provenance notes для будущей XML-документации интерфейсов.

- [x] Mutation Observer
  - Area: DOM/Observers
  - Key IDL entities:
    - MutationObserver
    - MutationRecord
    - MutationCallback
    - MutationObserverInit
  - Notes: полностью закрыт через DOM Standard alternative extraction из dom.bs; отдельный observer-specific source больше не требуется.

### Parsing, Shadow DOM, SVG, MathML

- [x] DOM Parsing and Serialization
  - Source:
    - https://w3c.github.io/DOM-Parsing/
    - https://w3c.github.io/DOM-Parsing/#idl-index
    - https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html
  - Area: DOM/Core, DOM/Html
  - Key IDL entities:
    - DOMParser
    - XMLSerializer
    - TrustedHTML or DOMString parsing entrypoints via DOMParser.parseFromString(...)
    - InnerHTML mixin
    - Element outerHTML/insertAdjacentHTML
    - Range.createContextualFragment(...)
  - Notes: современная public IDL-поверхность парсинга и markup insertion живет в HTML Standard, а W3C DOM Parsing остается важным как provenance source для XML serialization algorithms. Для генерации это означает dual-source mapping, а не изолированный отдельный parsing-spec bucket.

- [x] Shadow DOM
  - Source:
    - https://dom.spec.whatwg.org/
    - https://html.spec.whatwg.org/multipage/dom.html
    - https://html.spec.whatwg.org/multipage/scripting.html
    - https://html.spec.whatwg.org/multipage/interaction.html
  - Area: DOM/Core, DOM/Html
  - Key IDL entities:
    - ShadowRoot
    - ShadowRootMode
    - SlotAssignmentMode
    - Element.attachShadow(...)
    - ShadowRootInit
    - HTMLSlotElement
    - AssignedNodesOptions
    - HTMLTemplateElement declarative shadow root attributes
    - partial DocumentOrShadowRoot mixin activeElement-related integration
  - Notes: canonical surface split confirmed. DOM Standard owns core shadow tree model, ShadowRoot, slot-assignment primitives and attachShadow contracts. HTML Standard owns HTMLSlotElement, declarative shadow root template attributes and focus/activeElement integration details. Эту область нельзя корректно сгенерировать из одного source document.

- [x] SVG DOM IDL
  - Source:
    - https://svgwg.org/svg2-draft/types.html
    - https://svgwg.org/svg2-draft/struct.html
    - https://svgwg.org/svg2-draft/interact.html
    - https://svgwg.org/svg2-draft/paths.html
    - https://svgwg.org/svg2-draft/shapes.html
    - https://svgwg.org/svg2-draft/text.html
    - https://svgwg.org/svg2-draft/embedded.html
    - https://svgwg.org/svg2-draft/pservers.html
    - https://svgwg.org/svg2-draft/linking.html
    - https://svgwg.org/svg2-draft/painting.html
    - https://svgwg.org/specs/animations/
    - https://drafts.csswg.org/css-masking-1/
    - https://drafts.csswg.org/filter-effects-1/
  - Area: DOM/Svg
  - Confirmed coverage from collected chapters:
    - SVGElement
    - SVGGraphicsElement
    - SVGGeometryElement
    - SVGNumber, SVGLength, SVGAngle
    - SVGNumberList, SVGLengthList, SVGStringList
    - SVGAnimatedBoolean, SVGAnimatedEnumeration, SVGAnimatedInteger, SVGAnimatedNumber, SVGAnimatedLength, SVGAnimatedAngle, SVGAnimatedString, SVGAnimatedRect, SVGAnimatedNumberList, SVGAnimatedLengthList
    - SVGUnitTypes, SVGTests, SVGFitToViewBox, SVGURIReference
    - SVGSVGElement, SVGGElement, SVGDefsElement, SVGDescElement, SVGMetadataElement, SVGTitleElement, SVGSymbolElement, SVGUseElement, SVGSwitchElement
    - SVGUseElementShadowRoot, SVGElementInstance, ShadowAnimation, GetSVGDocument
    - SVGScriptElement
    - SVGPathElement
    - SVGRectElement, SVGCircleElement, SVGEllipseElement, SVGLineElement, SVGPolylineElement, SVGPolygonElement
    - SVGAnimatedPoints mixin, SVGPointList
    - SVGTextContentElement, SVGTextPositioningElement, SVGTextElement, SVGTSpanElement, SVGTextPathElement
    - SVGImageElement, SVGForeignObjectElement
    - SVGGradientElement, SVGLinearGradientElement, SVGRadialGradientElement, SVGStopElement, SVGPatternElement
    - SVGAElement, SVGViewElement
    - SVGMarkerElement
    - TimeEvent
    - SVGAnimationElement, SVGAnimateElement, SVGSetElement, SVGAnimateMotionElement, SVGMPathElement, SVGAnimateTransformElement
    - partial SVGSVGElement animation timeline control surface
    - SVGClipPathElement, SVGMaskElement
    - SVGFilterElement
    - SVGFilterPrimitiveStandardAttributes mixin
    - SVGFEBlendElement
    - SVGFEColorMatrixElement
    - SVGFEComponentTransferElement
    - SVGComponentTransferFunctionElement
    - SVGFEFuncRElement, SVGFEFuncGElement, SVGFEFuncBElement, SVGFEFuncAElement
    - SVGFECompositeElement
    - SVGFEConvolveMatrixElement
    - SVGFEDiffuseLightingElement
    - SVGFEDistantLightElement, SVGFEPointLightElement, SVGFESpotLightElement
    - SVGFEDisplacementMapElement
    - SVGFEDropShadowElement
    - SVGFEFloodElement
    - SVGFEGaussianBlurElement
    - SVGFEImageElement
    - SVGFEMergeElement, SVGFEMergeNodeElement
    - SVGFEMorphologyElement
    - SVGFEOffsetElement
    - SVGFESpecularLightingElement
    - SVGFETileElement
    - SVGFETurbulenceElement
    - partial Document SVG extensions captured in struct.html
  - Notes:
    - SVG scope now includes not only core SVG2 chapters, but also animation DOM from SVG Animations Level 2 and cross-spec SVG-owned DOM from CSS Masking and Filter Effects.
    - Filter Effects adds a large secondary SVG interface family, including filter primitive element interfaces, light source elements, primitive-standard-attributes mixin and several enum-backed constant groups; this must be treated as first-class SVG DOM, not as CSS-only behavior notes.
    - CSS Masking is the normative owner for SVGClipPathElement and SVGMaskElement; these should not be sourced from SVG2 chapters by approximation.
    - SVG Animations extends both standalone animation interfaces and partial SVGSVGElement timeline control members, so generation must preserve provenance for partial merge.
    - shapes and text chapters also add important behavior-only provenance, including SVGPointList reflection semantics, text layout/query APIs, textPath geometry coupling and geometry-element target rules for URL references.
    - Remaining work for SVG is no longer source collection, but generation policy: handling mixins, partial interfaces, constant groups and legacy compatibility tails like SVGElementInstance and ShadowAnimation.

- [x] MathML DOM IDL
  - Source:
    - https://w3c.github.io/mathml-core/#dom-and-javascript
    - https://w3c.github.io/mathml-core/#mathml-elements-and-attributes
  - Area: DOM/MathMl
  - Key IDL entities:
    - MathMLElement
    - GlobalEventHandlers mixin inclusion
    - HTMLOrForeignElement mixin inclusion
  - Notes: MathML Core intentionally exposes a minimal DOM surface. В подтвержденных source sections нет большой отдельной interface hierarchy по tag names; вместо этого почти вся DOM-экспозиция сводится к единому MathMLElement и platform integration с HTML/global attributes/event handlers. Для first-pass interface generation это low-volume area compared to SVG.

## BOM Primary Sources

### URL, Messaging, Workers

- [x] URL Standard
  - Source: https://url.spec.whatwg.org/
  - Area: BOM/Networking, BOM/Navigation shared surface
  - Key IDL entities:
    - URL
    - URLSearchParams
  - Notes: хотя часть URL surface используется в DOM/File API, canonical source для интерфейсов URL и URLSearchParams именно здесь.

- [x] HTML Standard, Web Messaging
  - Source: https://html.spec.whatwg.org/multipage/web-messaging.html
  - Area: BOM/Messaging
  - Key IDL entities:
    - partial Window.postMessage(...)
    - MessageChannel
    - MessagePort
    - BroadcastChannel
    - MessageEventTarget mixin
    - StructuredSerializeOptions
  - Notes: messaging surface распределен между Window partials и отдельными каналами/портами.

- [x] HTML Standard, Workers
  - Source: https://html.spec.whatwg.org/multipage/workers.html
  - Area: BOM/Workers
  - Key IDL entities:
    - Worker
    - WorkerOptions
    - SharedWorker
    - SharedWorkerOptions
    - WorkerGlobalScope
    - DedicatedWorkerGlobalScope
    - SharedWorkerGlobalScope
    - WorkerNavigator
    - WorkerLocation
    - AbstractWorker mixin
    - NavigatorConcurrentHardware mixin
  - Notes: worker surface частично пересекается с Permissions и High Resolution Time через shared mixin/partial members. Provenance is closed, but first-pass generation should stay window-centric: the dedicated worker constructor/global-scope family moves to a later Workers phase, while cross-cutting neutral contracts remain owned by their primary specs.

### Storage and Permissions

- [x] Storage Standard
  - Source: https://storage.spec.whatwg.org/
  - Area: BOM/Storage
  - Key IDL entities:
    - StorageManager
    - StorageEstimate
    - NavigatorStorage mixin
  - Notes: storage.spec.whatwg.org covers the quota/storage-manager layer only. Browser-facing localStorage/sessionStorage and StorageEvent ownership is explicitly closed separately by the HTML Web Storage section below.

- [x] Permissions API
  - Source: https://w3c.github.io/permissions/
  - Area: BOM/Permissions
  - Key IDL entities:
    - Permissions
    - PermissionDescriptor
    - PermissionStatus
    - PermissionState
    - PermissionSetParameters
    - partial Navigator
    - partial WorkerNavigator
  - Notes: важен как cross-cutting источник для navigator и worker navigator partials.

- [x] Web Locks API
  - Source: https://w3c.github.io/web-locks/
  - Area: BOM/Navigator, BOM/Scheduling
  - Key IDL entities:
    - NavigatorLocks mixin
    - LockManager
    - Lock
    - LockMode
    - LockOptions
    - LockManagerSnapshot
    - LockInfo
    - LockGrantedCallback
    - Navigator includes NavigatorLocks
    - WorkerNavigator includes NavigatorLocks
  - Notes: navigator.locks is real browser-facing surface and belongs in completeness inventory even though worker-side exposure remains deferred by first-pass policy. This closes the previously missing NavigatorLocks family and also reinforces the callback-to-delegate rule through LockGrantedCallback.

### Console and Screen

- [x] Console Standard
  - Source: https://console.spec.whatwg.org/
  - Area: BOM/Console
  - Key IDL entities:
    - namespace console
  - Decision:
    - console is deferred from first-pass interface generation because the source spec defines a namespace, not an interface.
    - it remains in inventory as a normative BOM surface, but is treated as a non-interface API family that requires a later dedicated modeling pass.
    - any leftover ConsoleContracts.cs-style file, if still present in the tree, is historical/manual residue only and must not drive the generated naming rules for the modern DOM/BOM interface baseline.

- [x] Screen Orientation API
  - Source: https://w3c.github.io/screen-orientation/
  - Area: BOM/Screen
  - Key IDL entities:
    - ScreenOrientation
    - partial Screen
    - OrientationLockType
    - OrientationType
  - Notes: BOM/Screen уже частично покрыт CSSOM View через Screen, здесь добавляется orientation-specific partial surface.

### Performance and Timing

- [x] High Resolution Time
  - Source: https://w3c.github.io/hr-time/
  - Area: BOM/Performance
  - Key IDL entities:
    - Performance
    - DOMHighResTimeStamp typedef
    - EpochTimeStamp typedef
    - partial WindowOrWorkerGlobalScope mixin
  - Notes: базовая точка для performance clock surface.

- [x] Performance Timeline
  - Source: https://www.w3.org/TR/performance-timeline/
  - Area: BOM/Performance, BOM/Timing
  - Key IDL entities:
    - PerformanceEntry
    - PerformanceObserver
    - PerformanceObserverEntryList
    - PerformanceObserverCallback
    - PerformanceObserverInit
    - PerformanceObserverCallbackOptions
    - PerformanceEntryList typedef
    - partial Performance
  - Notes: это отдельный слой над базовым Performance API.

- [x] Navigation Timing Level 2
  - Source: https://www.w3.org/TR/navigation-timing-2/
  - Area: BOM/Timing, BOM/Navigation
  - Key IDL entities:
    - PerformanceNavigationTiming
    - NavigationTimingType
    - PerformanceTimingConfidence
    - PerformanceTimingConfidenceValue
    - obsolete PerformanceTiming
    - obsolete PerformanceNavigation
    - partial Performance
  - Legacy review:
    - старые PerformanceTiming и PerformanceNavigation устарели, но их наличие влияет на полноту BOM-контуров и политику совместимости.

### Visibility and Page Lifecycle

- [x] Page Visibility
  - Source: https://w3c.github.io/page-visibility/
  - Area: BOM/Windowing, DOM/Events shared
  - Key IDL entities:
    - VisibilityState
    - partial Document
  - Legacy review:
    - спецификация помечена как retired/discontinued.
    - canonical owner for generation is HTML Living Standard; this retired spec is kept only as historical corroboration for visibility provenance.

## Shared / Cross-Cutting Sources

- [x] Web Storage surface from HTML
  - Source: https://html.spec.whatwg.org/multipage/webstorage.html
  - Area: BOM/Storage
  - Key IDL entities:
    - Storage
    - StorageEvent
    - StorageEventInit
    - WindowSessionStorage mixin
    - WindowLocalStorage mixin
    - Window includes WindowSessionStorage
    - Window includes WindowLocalStorage
  - Notes: HTML добавляет именно browser-facing storage surface поверх storage.spec.whatwg.org, включая localStorage, sessionStorage и StorageEvent.

- [x] History and Location from HTML
  - Source: https://html.spec.whatwg.org/multipage/nav-history-apis.html
  - Area: BOM/History, BOM/Location, BOM/Navigation
  - Key IDL entities:
    - Location
    - History
    - ScrollRestoration
    - Navigation
    - NavigationHistoryEntry
    - NavigationActivation
    - NavigationDestination
    - HashChangeEvent
    - HashChangeEventInit
    - PopStateEvent
    - PopStateEventInit
    - navigation-related event interfaces and dictionaries in the same chapter
  - Notes: section одновременно покрывает security-sensitive Window/Location cross-origin model и основные browser navigation/history contracts.

- [x] Navigator baseline from HTML
  - Source: https://html.spec.whatwg.org/multipage/system-state.html#navigator
  - Area: BOM/Navigator
  - Key IDL entities:
    - Navigator
    - NavigatorId mixin
    - NavigatorLanguage mixin
    - NavigatorOnLine mixin
    - NavigatorContentUtils mixin
    - NavigatorCookies mixin
    - NavigatorPlugins mixin
    - PluginArray
    - MimeTypeArray
    - Plugin
    - MimeType
    - Gecko-specific partial NavigatorId surface
  - Notes: HTML navigator layer содержит и современную, и значительную legacy surface; это один из главных источников compatibility policy decisions.

- [x] WebDriver automation disclosure surface
  - Source: https://w3c.github.io/webdriver/#interface
  - Area: BOM/Navigator
  - Key IDL entities:
    - NavigatorAutomationInformation mixin
    - Navigator includes NavigatorAutomationInformation
    - navigator.webdriver readonly boolean surface
  - Notes: browser-facing WebDriver surface is intentionally minimal. The spec explicitly says NavigatorAutomationInformation must not be exposed on WorkerNavigator, so this belongs in the window-side Navigator inventory and should not be generalized into the deferred worker family.

- [x] Window and WindowProxy baseline from HTML
  - Source:
    - https://html.spec.whatwg.org/multipage/nav-history-apis.html
    - https://html.spec.whatwg.org/multipage/webappapis.html
  - Area: BOM/Windowing
  - Key IDL entities:
    - Window
    - WindowPostMessageOptions
    - BarProp
    - WindowProxy exotic surface reference
    - partial/defaultView link between Document and Window
    - WindowOrWorkerGlobalScope mixin
    - GlobalEventHandlers mixin inclusion on Window
    - WindowEventHandlers mixin inclusion on Window
  - Notes: у Window есть полноценный IDL contract, а у WindowProxy нет interface object. For this migration WindowProxy should stay as documented provenance/reference surface rather than a generated public interface; WindowProxy-dependent semantics need a later dedicated reference strategy instead of an invented first-pass contract.

- [x] CSSOM base surface
  - Source: https://drafts.csswg.org/cssom/
  - Area: DOM/Cssom
  - Key IDL entities:
    - MediaList
    - StyleSheet
    - StyleSheetList
    - CSSStyleSheet
    - CSSStyleSheetInit
    - CSSRuleList
    - CSSRule
    - CSSStyleRule
    - CSSImportRule
    - CSSGroupingRule
    - CSSPageDescriptors
    - CSSPageRule
    - CSSMarginRule
    - CSSNamespaceRule
    - CSSStyleDeclaration
    - CSSStyleProperties
    - LinkStyle mixin
    - ElementCSSInlineStyle mixin
    - partial DocumentOrShadowRoot stylesheet surface
    - partial Window.getComputedStyle(...)
    - CSS namespace with escape(...)
  - Notes: CSSOM base surface выходит далеко за CSSOM View и задает core stylesheet/rule/declaration contracts. В first pass это обосновывает отдельный DOM/Cssom слой, при этом legacy members вроде rules/addRule/removeRule должны идти под ту же compatibility policy, что и другие obsolete tails.

- [x] User Timing
  - Source: https://w3c.github.io/user-timing/
  - Area: BOM/Performance, BOM/Timing
  - Key IDL entities:
    - PerformanceMarkOptions
    - PerformanceMeasureOptions
    - partial Performance with mark(...), clearMarks(...), measure(...), clearMeasures(...)
    - PerformanceMark
    - PerformanceMeasure
  - Notes: User Timing не является неявной частью Performance Timeline, а расширяет Performance через отдельную normative partial surface. Mark/measure contracts therefore belong in the explicit BOM/Performance inventory rather than being inferred transitively.

- [x] Scheduling surface
  - Source: https://w3c.github.io/requestidlecallback/
  - Area: BOM/Scheduling
  - Key IDL entities:
    - IdleRequestOptions
    - IdleDeadline
    - IdleRequestCallback callback
    - partial Window.requestIdleCallback(...)
    - partial Window.cancelIdleCallback(...)
  - Notes: requestIdleCallback is a Window-only cooperative scheduling surface. It is small enough to keep in first-pass BOM coverage, but it also becomes the clearest normative driver for the callback-to-delegate mapping rule.

- [x] Beacon API
  - Source: https://w3c.github.io/beacon/
  - Area: BOM/Networking
  - Key IDL entities:
    - partial Navigator.sendBeacon(USVString url, optional BodyInit? data = null)
  - Notes: Beacon is a minimal Navigator extension, but its public signature depends on Fetch-owned BodyInit. Provenance is closed here; actual generation should follow the Fetch boundary policy and avoid inventing ad-hoc placeholders just to force Beacon into first pass.

- [x] Fetch surface review
  - Source: https://fetch.spec.whatwg.org/
  - Area: BOM/Networking, DOM/Abort shared
  - Reviewed IDL entities:
    - Headers
    - Body mixin
    - Request
    - RequestInit
    - RequestDestination
    - RequestMode
    - RequestCredentials
    - RequestCache
    - RequestRedirect
    - RequestDuplex
    - RequestPriority
    - Response
    - ResponseInit
    - ResponseType
    - partial WindowOrWorkerGlobalScope.fetch(...)
    - DeferredRequestInit
    - FetchLaterResult
    - partial Window.fetchLater(...)
  - Notes: review complete. Fetch is not a small BOM-adjacent tail but a large standalone browser API family with its own enums, dictionaries, mixins and cross-spec dependencies, including AbortSignal and BodyInit. For this migration full Fetch generation is deferred to a dedicated networking-focused pass; dependent APIs do not automatically admit Fetch-owned signature types into first pass.

## Legacy / Compatibility Review

### Рекомендуемая policy для first pass

- [x] First pass generation should target modern baseline contracts first.
  - Include in first pass:
    - canonical modern interfaces and mixins from DOM, HTML, CSSOM View, Geometry, Observers, URL, Storage, Permissions, modern SVG chapters, CSS Masking and Filter Effects
    - partial interfaces and mixins that are still normative and actively used by current platform surface
  - Move to compatibility layer or explicit deferred bucket:
    - deprecated, obsolete, retired or at-risk API surface
    - legacy aliases that duplicate newer platform types
    - browser-compat tails whose main purpose is historical content support rather than modern authoring

### Compatibility-only candidates already identified

- [x] DOM and Events compatibility tails
  - Candidates:
    - UI Events legacy members and methods: initUIEvent, initKeyboardEvent, initCompositionEvent, UIEvent.which, KeyboardEvent.charCode, KeyboardEvent.keyCode
    - legacy event families and names: keypress, DOMActivate, DOMFocusIn, DOMFocusOut, TextEvent
    - Pointer/Mouse initialization-era methods such as initMouseEvent where still exposed for compatibility
  - Recommendation: keep out of first-pass modern DOM contracts and group under an explicit events compatibility layer.

- [x] Touch Events family
  - Candidates:
    - Touch, TouchList, TouchEvent, TouchEventInit
    - ontouch* handler attributes when treated as legacy mobile compatibility surface
  - Recommendation: inventory stays complete, but first pass should treat the entire touch family as compatibility-first API, separate from the default modern input baseline centered on Pointer Events.

- [x] Geometry compatibility aliases
  - Candidates:
    - DOMRectList
    - SVGPoint, SVGRect, SVGMatrix legacy aliases
    - WebKitCSSMatrix
  - Recommendation: model DOMPoint, DOMRect and DOMMatrix as primary contracts; aliases should be emitted only in a later compatibility pass or in a dedicated legacy namespace/folder.

- [x] SVG compatibility tails
  - Candidates:
    - partial Document.rootElement deprecated SVG extension
    - GetSVGDocument mixin and getSVGDocument()-style embedding access
    - SVGSVGElement deprecated no-op methods: suspendRedraw, unsuspendRedraw, unsuspendRedrawAll, forceRedraw
    - SVGSVGElement deprecated helpers duplicating newer APIs: deselectAll, createSVGPoint, createSVGMatrix, createSVGRect
    - SVGElementInstance mixin compatibility bridge for use-element shadow trees
    - ShadowAnimation read-only mirroring layer for use-element shadow trees
    - legacy use-element accessors and concepts tied to historical instance model, including instanceRoot and animatedInstanceRoot semantics
    - deprecated xlink-based attribute surface where parallel href-based modern contracts exist
  - Recommendation:
    - keep core SVG, masking and filter interfaces in modern baseline
    - move deprecated factories, no-op methods, GetSVGDocument and instance-compat bridge APIs into SVG compatibility layer
    - keep SVGElementInstance and ShadowAnimation under decision-required compatibility review if the goal is browser-surface completeness, but do not let them shape the base DOM/BOM folder layout.

- [x] Navigator legacy browser surface
  - Candidates:
    - NavigatorPlugins mixin
    - PluginArray
    - MimeTypeArray
    - Plugin
    - MimeType
    - Gecko-specific partial NavigatorId surface
  - Recommendation: separate these from modern Navigator baseline. They are important for completeness, but they should not dominate the first-pass navigator contract model.

- [x] Legacy HTML compatibility surface
  - Candidates:
    - HTMLDirectoryElement
    - HTMLParamElement
    - HTMLFrameElement
    - HTMLFrameSetElement
    - other obsolete or retired HTML element interfaces that remain browser-exposed mainly for legacy content compatibility
  - Recommendation: keep these contracts tracked explicitly for completeness, but do not let them shape the modern first-pass HTML baseline. They belong in the same deferred compatibility bucket as other legacy platform tails unless a later scope freeze decides otherwise.

- [x] Timing and visibility legacy surface
  - Candidates:
    - obsolete PerformanceTiming
    - obsolete PerformanceNavigation
    - retired Page Visibility provenance that should now defer to HTML as canonical owner
  - Recommendation: first pass should favor Performance, PerformanceEntry and PerformanceNavigationTiming; obsolete timing objects and retired-spec provenance belong in compatibility review.

### Practical generation consequence

- [x] Folder strategy should distinguish modern baseline from compatibility tails even if both remain in scope for eventual completeness.
  - Suggested rule:
    - DOM and BOM top-level generation starts from modern contracts only
    - compatibility-only contracts are either deferred entirely or emitted into clearly marked compatibility buckets after the baseline stabilizes
  - Reason:
    - this avoids polluting core signatures with obsolete overloads and deprecated factories
    - this keeps one-entity-per-file generation manageable while preserving a path to full coverage later
- [x] First concrete generation batch is now selected.
  - Batch 1:
    - BOM/Networking Url surface only
    - IUrl
    - IUrlSearchParams
  - Immediate follow-up order:
    - BOM/Scheduling
    - BOM/Permissions
    - BOM/Web Locks
    - BOM/Storage
  - Reason:
    - Url is the smallest modern-baseline family with low merge pressure and no forced Fetch spillover
    - Scheduling becomes the next policy-validation step because it exercises delegates, dictionaries and partial Window after the baseline generator path is proven

## Решения, которые нужны до генерации

- [x] Legacy/obsolete surfaces should not be mixed into the first modern pass; they should be tracked in a separate compatibility layer and generated only after the baseline DOM/BOM contracts stabilize.
- [x] Interface mixin should be modeled as a standalone C# interface and then composed into final interfaces through inheritance, not flattened away as the primary representation.
  - Policy:
    - each Web IDL mixin becomes its own generated interface in a separate file
    - consuming interfaces inherit that generated mixin interface explicitly
    - provenance of includes stays visible in generated type structure and remains easy to audit against specs
    - flatten-only generation is rejected for first pass because it destroys source structure and complicates re-generation when spec ownership changes
  - Naming rule:
    - use the normal interface prefix and keep the original mixin name as close as possible to Web IDL, for example ParentNode -> IParentNode, DocumentOrShadowRoot -> IDocumentOrShadowRoot, WindowEventHandlers -> IWindowEventHandlers
    - do not append Mixin suffix by default unless a concrete collision is found later
  - Consequence:
    - final interfaces such as IDocument, IElement, IShadowRoot, IWindow and INavigator will often have multiple inherited mixin contracts, and that is expected rather than a design smell
- [x] Partial interfaces should merge into a single final C# interface per public type name; provenance is retained in inventory and file-level documentation, not by splitting one type across folders.
  - Policy:
    - if multiple specs define partial members for the same Web IDL interface, generate one final C# interface file for that type
    - merge partial members into the same type in deterministic source order during generation
    - choose file placement by primary conceptual ownership of the final public type, not by every contributing spec
    - keep per-member or per-batch provenance notes in generation metadata or XML documentation, while the inventory remains the authoritative full provenance map
  - Placement examples:
    - IDocument stays in DOM/Core even when members come from DOM, HTML, Selection, Fullscreen and SVG partials
    - IWindow stays in BOM/Windowing even when members come from HTML webappapis, messaging, history/navigation and timing-related partials
    - ISvgSvgElement stays in DOM/Svg even when animation control members come from SVG Animations Level 2
  - Rejected approaches:
    - generating separate partial C# interface files for the same public type as the main representation
    - reflecting provenance in folder structure by scattering one public type across multiple directories
  - Consequence:
    - folder layout remains stable and human-readable
    - one-entity-per-file rule is preserved
    - regeneration stays possible when new specs add partial members without forcing namespace churn
- [x] First pass may introduce minimal supporting enum, dictionary and typedef mappings when they are required to keep interface signatures type-safe and spec-shaped.
  - Enum policy:
    - generate enums when they are referenced by public interface members, options dictionaries or callback/event payload contracts
    - keep one enum per file
    - prefer real enums over stringly-typed substitutes when the source spec defines a closed keyword set
  - Dictionary policy:
    - allow generation of dictionary-shaped support types when an interface member directly depends on them for parameters, options objects or event/init payloads
    - keep them as lightweight contract types in separate files, not as interfaces
    - treat them as signature-supporting contracts, not as runtime implementations
    - postpone non-essential dictionaries that are only internal or only relevant to deferred compatibility surface
  - Typedef policy:
    - do not generate C# alias declarations as the main strategy
    - inline mapped CLR types for trivial typedefs like DOMString, USVString, DOMHighResTimeStamp and similar primitive aliases
    - if a typedef names a reused semantic shape that materially improves readability or overload stability, keep the typedef name documented in provenance but still map to the concrete C# type unless a later generation pass introduces a dedicated wrapper by explicit decision
  - Consequence:
    - first pass remains interface-first, but not interface-only in the literal sense when support types are necessary for correct API shape
    - this avoids collapsing option bags and closed-value vocabularies into object?, string or ad-hoc overload explosions
- [x] Namespace-based APIs like console are deferred from the first interface-generation pass and handled as explicit non-interface modeling work later.
  - Policy:
    - do not force namespace APIs into artificial interfaces just to satisfy generator uniformity
    - keep such APIs in scope at the inventory level, but outside the first-pass generated DOM/BOM interface set
    - manual or transitional contracts may remain temporarily where the project already exposes them, but they are not canonical templates for generated interface naming

## Следующие шаги

- [x] Доснять DOM Standard через альтернативный source path и закрыть базовое ядро DOM/Core.
- [x] Доснять HTML Standard по разделам Window, Document, Navigator, History, Location, Web Storage и HTML element hierarchy.
- [x] Найти актуальный источник для Resize Observer и снять Geometry Interfaces с корректного URL.
- [x] Закрыть observer block для Geometry, Resize Observer, Intersection Observer и подтвердить MutationObserver provenance через DOM Standard.
- [x] Закрыть UI Events, Input Events Level 2 и Touch Events с разделением modern и legacy surface.
- [x] Закрыть явные gaps повторного audit pass: HTML concrete element family, CSS Animations/Transitions, Web Locks и navigator.webdriver provenance.
- [x] После завершения inventory утвердить first-pass scope для генерации каталогов DOM и BOM.
- [x] Выбрать безопасный первый generation batch и зафиксировать ранний execution order.
- [ ] Только после этого переходить к созданию файловой структуры и генерации по принципу "одна сущность = один файл".
