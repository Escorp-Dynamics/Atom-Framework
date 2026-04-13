# Atom.Net.Browsing DOM/BOM Interface Roadmap

## Цель

- [ ] Создать в проекте отдельные папки DOM и BOM.
- [ ] Перенести модель браузерных контрактов от агрегированных файлов к структуре «одна сущность = один файл».
- [ ] Покрыть интерфейсами весь доступный Web IDL-контур спецификаций, относящийся к DOM и BOM, без реализаций на этом этапе.
- [ ] Сохранить именование максимально близким к оригинальным Web IDL сущностям, но в корректном и последовательном стиле C#.

## Нефункциональные правила

- [ ] Каждая сущность находится в собственном файле.
- [ ] В одном файле не смешиваются интерфейсы, перечисления, делегаты, record-тип и вспомогательные типы.
- [ ] Namespace-структура отражает каталог и предметную область.
- [ ] Публичные имена приводятся к стандарту C#: PascalCase для типов и членов, I-префикс только для интерфейсов.
- [ ] Имена Web IDL сохраняются максимально близко к оригиналу: Document -> IDocument, HTMLElement -> IHtmlElement, NodeList -> INodeList.
- [x] Acronym-правила фиксируются заранее и применяются консистентно: Html, Css, Svg, Url, Xml, MathMl; DOM и BOM остаются отдельными корневыми исключениями.
- [ ] Все интерфейсы на первом этапе остаются без реализаций, фабрик и concrete-классов.
- [ ] Любые спорные места между Web IDL и C# документируются до генерации файлов.

## Целевая структура каталогов

- [ ] Создать каталог DOM.
- [ ] Создать каталог BOM.
- [ ] Создать подкаталоги в DOM по крупным областям ответственности.
- [ ] Создать подкаталоги в BOM по крупным областям ответственности.

### Предлагаемая структура DOM

- [ ] DOM/Core
- [ ] DOM/Traversal
- [ ] DOM/Events
- [ ] DOM/Ranges
- [ ] DOM/Selection
- [ ] DOM/Geometry
- [ ] DOM/Observers
- [ ] DOM/Abort
- [ ] DOM/Html
- [ ] DOM/Html/Elements
- [ ] DOM/Svg
- [ ] DOM/MathMl
- [ ] DOM/Cssom
- [ ] DOM/Cssom/View
- [ ] DOM/Fullscreen
- [ ] DOM/Clipboard
- [ ] DOM/Editing
- [ ] DOM/Forms
- [ ] DOM/Media

### Предлагаемая структура BOM

- [ ] BOM/Windowing
- [ ] BOM/Navigation
- [ ] BOM/History
- [ ] BOM/Location
- [ ] BOM/Navigator
- [ ] BOM/Screen
- [ ] BOM/Storage
- [ ] BOM/Timing
- [ ] BOM/Performance
- [ ] BOM/Messaging
- [ ] BOM/Workers
- [ ] BOM/Console
- [ ] BOM/Permissions
- [ ] BOM/Networking
- [ ] BOM/Device
- [ ] BOM/Scheduling

## Правила маппинга Web IDL в C Sharp

### Типы

- [ ] interface X -> interface IX
- [x] mixin X -> отдельный interface IX без суффикса Mixin по умолчанию; source kind сохраняется в provenance и документации, а не в имени типа
- [x] namespace X -> не пытаться искусственно превращать в interface на first pass; такие API фиксируются отдельно как deferred non-interface surface
- [x] callback -> отдельный delegate в собственном файле
- [x] dictionary -> отдельный lightweight contract-type в собственном файле, если без него нельзя выразить типобезопасную сигнатуру интерфейса
- [x] enum -> отдельный enum в собственном файле, если он определяет закрытый набор значений для публичной сигнатуры
- [x] typedef -> C# alias не использовать; по умолчанию документировать и маппить на реальный CLR-тип

### Наследование и композиция

- [ ] Web IDL inheritance переносится в inheritance list интерфейсов C#.
- [x] partial interface объединяется в один конечный C#-интерфейс, а provenance фиксируется в inventory и file-level документации, а не через разнесение одного типа по каталогам.
- [x] includes/mixin relationships моделируются как явное наследование конечных интерфейсов от отдельных mixin-интерфейсов.
- [ ] iterable/maplike/setlike сначала документируются как семантика, затем отражаются через IReadOnlyList, IReadOnlyDictionary, IEnumerable или специализированные интерфейсы.

### Члены интерфейсов

- [ ] attribute -> свойство.
- [ ] readonly attribute -> get-only свойство.
- [ ] operation -> метод.
- [ ] stringifier -> ToString()-совместимое решение только после отдельного review.
- [ ] constructor declarations не моделируются внутри интерфейсов.
- [ ] static members не добавляются в интерфейсы до утверждения стратегии.
- [ ] event handler attributes (onclick и т.д.) выносятся в отдельную согласованную модель событий.

### Типовые преобразования

- [ ] DOMString / USVString -> string
- [ ] boolean -> bool
- [ ] short / long / long long -> short / int / long
- [ ] unsigned variants -> ushort / uint / ulong, если нет причин унифицировать
- [ ] double / unrestricted double -> double
- [ ] float -> float
- [ ] object -> object
- [ ] any -> object?
- [ ] sequence of T -> IReadOnlyList of T
- [ ] FrozenArray of T -> IReadOnlyList of T
- [ ] record<K, V> -> IReadOnlyDictionary<K, V>
- [ ] Promise of T -> ValueTask of T
- [ ] Promise of void -> ValueTask
- [ ] nullable -> nullable reference/value type
- [ ] union types требуют отдельной стратегии и не генерируются без принятого соглашения

## Этап 0. Инвентаризация исходных спецификаций

- [x] Зафиксировать список нормативных источников Web IDL.
- [x] Разделить источники на DOM и BOM.
- [x] Определить приоритет между W3C и WHATWG там, где спецификации расходятся.
- [x] Зафиксировать версию и дату снятия IDL для повторяемости.
- [x] Собрать перечень partial interface, mixin, callback, enum и dictionary, влияющих на DOM/BOM интерфейсы.

### Базовый список спецификаций DOM

- [x] DOM Standard
- [x] Abort API surface from DOM Standard
- [x] HTML Standard, DOM-facing sections
- [x] HTML Standard concrete element hierarchy
- [x] UI Events
- [x] Pointer Events
- [x] Input Events
- [x] Clipboard API and Events
- [x] Selection API
- [x] CSSOM
- [x] CSSOM View
- [x] CSS Animations
- [x] CSS Transitions
- [x] Geometry Interfaces
- [x] Fullscreen API
- [x] Resize Observer
- [x] Intersection Observer
- [x] Mutation Observer
- [x] DOM Parsing and Serialization
- [x] Shadow DOM
- [x] Touch Events
- [x] SVG DOM IDL
- [x] MathML DOM IDL
- [x] File API, только DOM-facing интерфейсы
- [ ] Encoding API, только если интерфейсы попадают в DOM-поверхность

### Базовый список спецификаций BOM

- [x] HTML Standard, Window/WindowProxy/Navigator/History/Location surface
- [x] Web Storage
- [x] Web Messaging
- [x] Channel Messaging
- [x] BroadcastChannel
- [x] Console API
- [x] Screen Orientation
- [x] High Resolution Time
- [x] User Timing
- [x] Performance Timeline
- [x] Navigation Timing
- [x] Page Visibility historical provenance; canonical generation owner remains HTML
- [x] Permissions API
- [x] Beacon API
- [x] Fetch API as a standalone BOM networking family; do not pull it into first pass implicitly through Beacon or other dependent surfaces
- [x] URL Standard, только интерфейсная поверхность BOM
- [x] Workers
- [x] Service Workers shared BOM contracts вынесены из first pass вместе с остальной worker-specific family
- [x] Scheduling API / requestIdleCallback surface
- [x] Web Locks
- [x] WebDriver automation disclosure surface

## Этап 1. Архитектурное проектирование C#-контрактов

- [x] Утвердить схему namespace для DOM.
- [x] Утвердить схему namespace для BOM.
- [x] Утвердить имя корневого пространства: Atom.Net.Browsing.DOM и Atom.Net.Browsing.BOM либо Atom.Net.Browsing.Dom и Atom.Net.Browsing.Bom.
- [x] Утвердить правило для HTML/SVG/CSS аббревиатур.
- [ ] Утвердить правило для async-операций, если в Web API встречаются Promise-returning members.
- [ ] Утвердить стратегию nullability для nullable IDL-типов.
- [ ] Утвердить стратегию для overloaded operations.
- [ ] Утвердить стратегию для indexers, named properties и legacy platform objects.
- [ ] Утвердить стратегию для event targets и event handler attributes.
- [ ] Утвердить стратегию для коллекций: NodeList, HTMLCollection, DOMTokenList, NamedNodeMap.
- [ ] Утвердить стратегию для union types и overloaded constructors.

### Принятое решение по casing root namespace

- [x] Корневые namespace для новой структуры фиксируются как Atom.Net.Browsing.DOM и Atom.Net.Browsing.BOM.
- [x] В качестве casing используется uppercase acronym style, а не Dom/Bom.
- [x] Основание: репозиторий уже использует uppercase acronym segments в namespace, например Atom.IO и Atom.Net, поэтому DOM/BOM лучше соответствует существующей naming convention.
- [x] Имена корневых каталогов и namespace должны оставаться синхронизированными: DOM <-> Atom.Net.Browsing.DOM, BOM <-> Atom.Net.Browsing.BOM.

### Принятое решение по acronym casing внутри типов, файлов и subnamespace

- [x] Внутри типов, файлов и subnamespace используется обычный C# PascalCase для длинных акронимов: Html, Svg, Css, Url, Xml, MathMl.
- [x] Корневые DOM и BOM остаются отдельным исключением и сохраняются uppercase только на root namespace boundary.
- [x] HTMLCollection, HTMLElement и related types маппятся как IHtmlCollection, IHtmlElement и DOM/Html.
- [x] SVGElement и related types маппятся как ISvgElement и DOM/Svg.
- [x] CSSOM namespace/folder surface маппится как DOM/Cssom, а type prefixes как ICssStyleSheet, ICssRule и similar forms.
- [x] URL Standard contracts при генерации остаются ближе к Web API identity, но в C# casing: IUrl и IUrlSearchParams, а не uppercase URL inside type names.
- [x] MathML surface маппится как DOM/MathMl и IMathMlElement, несмотря на uppercase spelling в названии спецификации.

### Принятое решение по mixin-интерфейсам

- [x] Web IDL mixin генерируется как самостоятельный C# interface в собственном файле.
- [x] Конечные интерфейсы наследуют mixin явно, чтобы сохранить структуру includes из спецификаций.
- [x] Flatten-only подход не используется как основной, потому что он теряет provenance и усложняет повторную генерацию при изменении спецификаций.
- [x] Суффикс Mixin в имени C# типа по умолчанию не используется; I-префикса достаточно для различения kind на уровне языка.
- [x] Если конкретный mixin создает конфликт имен с обычным interface, это фиксируется как точечное исключение, а не как общее правило именования.

### Принятое решение по partial interface

- [x] Все partial-интерфейсы из разных спецификаций сливаются в один конечный C# interface по имени Web IDL сущности.
- [x] Папка и namespace выбираются по primary ownership итогового публичного типа, а не по каждому contributing source.
- [x] Provenance partial-членов хранится в inventory и в file-level generation notes/XML documentation.
- [x] Один и тот же публичный тип не разносится по нескольким файлам или каталогам ради отражения источников.
- [x] Такая стратегия обязательна для типов вроде Document, Window, Navigator, Element, SVGSVGElement и других spec-aggregated surface types.

### Принятое решение по supporting types

- [x] First pass остается interface-first, но допускает минимальные supporting types, если без них нельзя сохранить типобезопасную форму Web IDL API.
- [x] Enum генерируется как самостоятельный тип, когда спецификация задает закрытый vocabulary для публичной сигнатуры.
- [x] Dictionary генерируется как отдельный lightweight contract-type для options/init/payload surface, если он реально участвует в интерфейсных сигнатурах.
- [x] Typedef по умолчанию не становится отдельным alias-типом C#; вместо этого фиксируется provenance и используется конкретный mapped CLR type.
- [x] Необязательные supporting types, относящиеся только к deferred compatibility surface, можно отложить до следующего прохода.

### Принятое решение по namespace-based API

- [x] Namespace-based API, такие как console, не участвуют в первом interface-generation pass.
- [x] Их нельзя искусственно моделировать как интерфейсы только ради единообразия генератора.
- [x] Такие API остаются в inventory как нормативная часть BOM, но переходят в отдельный deferred non-interface modeling pass.
- [x] Исторические/manual файлы вроде ConsoleContracts.cs, если еще физически присутствуют в дереве, не считаются migration input и не определяют naming rules будущей генерации DOM/BOM интерфейсов.

### Принятое решение по callback -> delegate mapping

- [x] Web IDL callback маппится на именованный C# delegate в собственном файле.
- [x] Имя delegate сохраняет имя спецификации в C#-casing и не получает I-префикс, потому что это не interface.
- [x] callback interface не смешивается с callback typedef и продолжает следовать обычным правилам interface-generation.
- [x] Action/Func не используются как основной публичный mapping, потому что они теряют provenance и нарушают правило one-entity-per-file.
- [x] Supporting enums и dictionaries, используемые callback-сигнатурами, генерируются по уже принятой общей policy для supporting types.

### Принятое решение по Fetch first-pass boundary

- [x] Полная Fetch family не включается в first-pass DOM/BOM generation автоматически.
- [x] Beacon, Navigator и Window partial surface не протаскивают Fetch-owned types в first pass по факту зависимости.
- [x] Сигнатуры, требующие Fetch-owned types или unions вроде BodyInit, остаются в documented provenance bucket до отдельного networking-focused прохода.
- [x] Shared dependencies, которые уже входят в scope по собственным нормативным основаниям, например AbortSignal, продолжают жить в своем primary ownership слое и не считаются частью Fetch phase.
- [x] Для Fetch следует планировать отдельный BOM/Networking/Fetch batch, а не размазывать его по ранним DOM/BOM стадиям.

### Принятое решение по WindowProxy

- [x] WindowProxy не генерируется как публичный C# interface в first pass.
- [x] Причина: HTML описывает WindowProxy как exotic proxy/reference surface, а не как обычный interface object с самостоятельным contract ownership.
- [x] Члены, типизированные как Window, продолжают генерироваться как обычные Window contracts; WindowProxy-specific semantics остаются в provenance/documentation bucket до отдельной reference-strategy.
- [x] IWindowProxy не резервируется как обязательный артефакт этапа BOM core.

### Принятое решение по Workers first-pass boundary

- [x] First pass остается window-centric BOM baseline и не включает полную worker-specific family автоматически.
- [x] Worker, SharedWorker, WorkerGlobalScope, DedicatedWorkerGlobalScope, SharedWorkerGlobalScope, WorkerNavigator, WorkerLocation и service-worker-adjacent shared contracts переносятся в отдельный Workers phase.
- [x] Shared neutral contracts, чей primary ownership уже закрыт в других спецификациях или которые напрямую нужны window-facing surface, остаются в first pass; это включает messaging channels/ports, URL, Performance core и связанные supporting types.
- [x] Exposures вида Window/Worker сохраняются в provenance inventory, но first-pass generation не обязан немедленно материализовать worker-side attachment points до начала отдельной worker phase.

## Этап 2. Подготовка файловой структуры

- [ ] Создать каталоги DOM и BOM.
- [ ] Создать все согласованные подкаталоги.
- [ ] Подготовить шаблон размещения «один файл на сущность».
- [ ] Подготовить правила имени файла: имя типа без дополнительных суффиксов, например IDocument.cs, IWindow.cs, IHtmlElement.cs.
- [ ] Подготовить правила имени файла для enum/delegate/supporting types.
- [x] Зафиксировать, что исторические aggregate-файлы не являются transitional migration layer; если они остались в дереве, это cleanup residue, а не целевая архитектура.
- [ ] Зафиксировать план удаления агрегированных контрактов после завершения переноса.

## Этап 3. Ядро DOM

### Узлы и дерево документа

- [ ] IEventTarget
- [ ] INode
- [ ] IDocument
- [ ] IDocumentType
- [ ] IDocumentFragment
- [ ] IShadowRoot
- [ ] IElement
- [ ] IAttr
- [ ] ICharacterData
- [ ] IText
- [ ] ICDataSection
- [ ] IComment
- [ ] IProcessingInstruction
- [ ] INamedNodeMap
- [ ] INodeList
- [ ] IHtmlCollection
- [ ] IDomTokenList
- [ ] IDomImplementation

### Поиск, traversal и range-механика

- [ ] INodeIterator
- [ ] ITreeWalker
- [ ] IRange
- [ ] IStaticRange
- [ ] IAbstractRange
- [ ] ISelection

### Событийная модель DOM

- [ ] IEvent
- [ ] ICustomEvent
- [ ] IUiEvent
- [ ] IMouseEvent
- [ ] IKeyboardEvent
- [ ] IInputEvent
- [ ] ICompositionEvent
- [ ] IFocusEvent
- [ ] IWheelEvent
- [ ] IPointerEvent
- [x] ITouchEvent и остальная touch family остаются в отдельном compatibility layer и не входят в modern first-pass DOM event baseline
- [ ] ISubmitEvent
- [ ] IBeforeUnloadEvent
- [ ] IPageTransitionEvent
- [ ] IProgressEvent
- [ ] IMessageEvent
- [ ] IErrorEvent
- [ ] ICloseEvent
- [ ] IHashChangeEvent
- [ ] IPopStateEvent
- [ ] IStorageEvent
- [ ] IClipboardEvent
- [ ] IClipboardChangeEvent
- [ ] IAnimationEvent
- [ ] ITransitionEvent

### Observer и служебные DOM API

- [ ] IMutationObserver
- [ ] IResizeObserver
- [ ] IIntersectionObserver
- [ ] IAbortController
- [ ] IAbortSignal
- [ ] IDomParser
- [ ] IXmlSerializer

## Этап 4. HTML DOM

### Базовые HTML-контракты

- [ ] IHtmlDocument
- [ ] IHtmlElement
- [ ] IHtmlUnknownElement
- [ ] IHtmlTemplateElement
- [ ] IHtmlSlotElement
- [ ] IHtmlScriptElement
- [ ] IHtmlStyleElement
- [ ] IHtmlLinkElement
- [ ] IHtmlBaseElement
- [ ] IHtmlMetaElement
- [ ] IHtmlTitleElement
- [ ] IHtmlHeadElement
- [ ] IHtmlBodyElement

### Формы и пользовательский ввод

- [ ] IHtmlFormElement
- [ ] IHtmlInputElement
- [ ] IHtmlButtonElement
- [ ] IHtmlLabelElement
- [ ] IHtmlSelectElement
- [ ] IHtmlOptionElement
- [ ] IHtmlOptGroupElement
- [ ] IHtmlTextAreaElement
- [ ] IHtmlOutputElement
- [ ] IHtmlFieldSetElement
- [ ] IHtmlLegendElement
- [ ] IHtmlDataListElement
- [ ] IHtmlMeterElement
- [ ] IHtmlProgressElement
- [ ] IHtmlDetailsElement
- [ ] IHtmlDialogElement

### Текст, секции, списки и табличные элементы

- [ ] IHtmlParagraphElement
- [ ] IHtmlHeadingElement
- [ ] IHtmlQuoteElement
- [ ] IHtmlPreElement
- [ ] IHtmlHrElement
- [ ] IHtmlBrElement
- [ ] IHtmlDivElement
- [ ] IHtmlSpanElement
- [ ] IHtmlDListElement
- [ ] IHtmlOListElement
- [ ] IHtmlUListElement
- [ ] IHtmlDirectoryElement, если включается legacy-контур
- [ ] IHtmlMenuElement
- [ ] IHtmlLIElement
- [ ] IHtmlTableElement
- [ ] IHtmlTableCaptionElement
- [ ] IHtmlTableColElement
- [ ] IHtmlTableSectionElement
- [ ] IHtmlTableRowElement
- [ ] IHtmlTableCellElement

### Медиа и внедрение контента

- [ ] IBlob
- [ ] IFile
- [ ] IFileList
- [ ] IFileReader
- [ ] IHtmlMediaElement
- [ ] IHtmlAudioElement
- [ ] IHtmlVideoElement
- [ ] IHtmlSourceElement
- [ ] IHtmlTrackElement
- [ ] IHtmlImageElement
- [ ] IHtmlPictureElement
- [ ] IHtmlEmbedElement
- [ ] IHtmlObjectElement
- [ ] IHtmlParamElement, только если legacy compatibility bucket отдельно подтвержден для генерации
- [ ] IHtmlIFrameElement
- [ ] IHtmlFrameElement, если включается legacy-контур
- [ ] IHtmlFrameSetElement, если включается legacy-контур
- [ ] IHtmlCanvasElement
- [ ] IHtmlMapElement
- [ ] IHtmlAreaElement

### Навигация и интерактивность

- [ ] IHtmlAnchorElement
- [ ] IHtmlAreaElement
- [ ] HTMLPortalElement family требует отдельного provenance review перед включением в roadmap как spec-shaped contract

## Этап 5. CSSOM и геометрия DOM-поверхности

- [ ] IMediaQueryList
- [ ] IMediaQueryListEvent
- [ ] ICssStyleSheet
- [ ] ICssRule
- [ ] ICssRuleList
- [ ] ICssStyleDeclaration
- [ ] ICssStyleRule
- [ ] ICssImportRule
- [ ] ICssGroupingRule
- [ ] ICssPageRule
- [ ] ICssMarginRule
- [ ] ICssNamespaceRule
- [ ] ICssKeyframeRule
- [ ] ICssKeyframesRule
- [ ] ICssTransition
- [ ] ICssStartingStyleRule
- [ ] Typed OM / ICssStyleValue surface требует отдельного source/provenance review перед включением
- [ ] IStyleSheet
- [ ] IStyleSheetList
- [ ] IMediaList
- [ ] IDomRectReadOnly
- [ ] IDomRect
- [ ] IDomPointReadOnly
- [ ] IDomPoint
- [ ] IDomQuad
- [ ] IDomMatrixReadOnly
- [ ] IDomMatrix

## Этап 6. SVG и MathML

- [x] Собрать отдельный полный список SVG DOM интерфейсов.
- [x] Определить границу включения legacy SVG типов.
- [x] Собрать отдельный полный список MathML DOM интерфейсов.
- [ ] Вынести каждую SVG и MathML сущность в отдельный файл.
- [ ] Проверить единообразие Html/Svg/MathMl именования по всему проекту.

## Этап 7. Ядро BOM

### Окно, документ и навигация верхнего уровня

- [ ] IWindow
- [x] WindowProxy остается documented reference surface; отдельный IWindowProxy в first pass не генерируется
- [x] IDocumentOrShadowRoot остается DOM-owned mixin contract и не рассматривается как BOM core entity
- [ ] IBarProp
- [ ] ILocation
- [ ] IHistory
- [ ] INavigation
- [ ] INavigationHistoryEntry
- [ ] INavigationActivation
- [ ] INavigationDestination
- [ ] IUrl, Batch 1
- [ ] IUrlSearchParams, Batch 1
- [ ] INavigator
- [ ] IWindowPostMessageOptions
- [ ] IScreen
- [ ] IScreenOrientation
- [ ] IVisualViewport

### Хранилище, коммуникации и состояние страницы

- [ ] IStorage
- [ ] IStorageManager, если включается
- [ ] IStorageEstimate
- [ ] IBroadcastChannel
- [ ] IMessagePort
- [ ] IMessageChannel
- [ ] IMessageEventTarget
- [ ] IStructuredSerializeOptions
- [ ] IPermissions, Batch 3
- [ ] PermissionDescriptor, Batch 3 base supporting contract
- [ ] IPermissionStatus, Batch 3
- [ ] IClipboard
- [ ] IClipboardItem
- [ ] IClipboardPermissionDescriptor
- [ ] ILockManager
- [ ] ILock
- [x] Worker и SharedWorker families вынесены в отдельный Workers phase, а не в first-pass BOM core
- [x] WorkerGlobalScope family вынесена в отдельный Workers phase

### Производительность, время и планирование

- [ ] IPerformance
- [ ] IPerformanceEntry
- [ ] IPerformanceMark
- [ ] IPerformanceMeasure
- [ ] IPerformanceNavigationTiming
- [ ] IPerformanceObserver
- [ ] IPerformanceObserverCallbackOptions
- [ ] IIdleDeadline, Batch 2

### Навигатор и возможности среды

- [ ] INavigatorAutomationInformation
- [ ] INavigatorLanguage
- [ ] INavigatorOnLine
- [ ] INavigatorConcurrentHardware
- [ ] INavigatorCookies
- [ ] INavigatorStorage
- [ ] INavigatorId
- [ ] INavigatorLocks
- [ ] INavigatorPermissions, если включается в window-only Batch 3 merge

### Stage 7 Early Batch Mapping

- [x] Batch 1 maps only to BOM/Networking Url surface: IUrl and IUrlSearchParams.
- [x] Batch 2 maps to BOM/Scheduling plus the minimal Window partial required for requestIdleCallback and cancelIdleCallback.
- [x] Batch 3 maps to BOM/Permissions plus the minimal Navigator partial required for the base permissions surface.
- [x] Permissions Batch 3 explicitly excludes worker-side WorkerNavigator materialization.
- [x] Permission-specific descriptor families owned by later APIs do not expand the base Stage 7 permissions batch automatically.

### Консоль и диагностическая поверхность

- [x] Зафиксировать deferred non-interface modeling pass для console и связанных diagnostic contracts.
- [ ] Уточнить, нужно ли сохранять какие-либо исторические manual console-supporting types вне DOM/BOM или их следует считать cleanup residue без migration статуса.

## Этап 8. Спорные и сложные зоны

- [ ] Legacy HTML interfaces.
- [ ] Vendor-prefixed или obsolete Web IDL.
- [ ] Partial interfaces из HTML Standard, распределенные по разным разделам.
- [ ] Mixin contracts: GlobalEventHandlers, DocumentAndElementEventHandlers, NonElementParentNode, ParentNode, ChildNode и др.
- [ ] Named/indexed property semantics.
- [ ] Event handler properties.
- [ ] Generic collections против точного моделирования DOM-коллекций.
- [ ] Promise/async API и ValueTask-совместимость.
- [ ] Union types, nullable unions, overloaded signatures.

## Этап 9. Контроль полноты

- [ ] Сопоставить каждый IDL interface из источников с C#-файлом.
- [ ] Сопоставить каждый mixin с принятой стратегией переноса.
- [ ] Проверить отсутствие смешивания сущностей по файлам.
- [ ] Проверить отсутствие пропусков в partial interfaces.
- [ ] Проверить корректность namespace для каждого файла.
- [ ] Проверить единообразие имён файлов и типов.
- [ ] Проверить отсутствие реализаций и логики.
- [ ] Проверить отсутствие случайных отклонений от C# naming conventions.

## Этап 10. Cleanup исторических aggregate leftovers

- [x] Зафиксировать, что DomContracts.cs, WebContracts.cs и ConsoleContracts.cs не считаются authoritative migration source для новой DOM/BOM структуры.
- [ ] Проверить, какие исторические aggregate-файлы еще физически остаются в дереве и реально участвуют ли они в публичной сборке.
- [ ] Удалить или архивировать оставшиеся aggregate leftovers после переноса нормативных интерфейсов в новую структуру.
- [ ] Если из historical leftovers еще нужен какой-то публичный surface, возвращать его только из нормативного DOM/BOM места, а не через compatibility-wrapper слой.

## Этап 11. Документация и сопровождение

- [x] Обновить README проекта под новую структуру DOM/BOM на уровне navigation and planning docs.
- [x] Добавить документ с правилами маппинга Web IDL -> C#.
- [x] Добавить отдельный документ по supporting types policy.
- [x] Добавить документ с перечнем покрытых спецификаций и current synchronization state.
- [x] Добавить checklist для последующих синхронизаций со спецификациями.
- [x] Добавить execution note для ранних generation batches.
- [x] Добавить merge notes для partial Window и partial Navigator.
- [x] Добавить early batch file map для bridge between planning and file creation.
- [x] Добавить полный Stage 7 BOM file map.
- [x] Добавить документ с ранним DOM batch order.
- [x] Добавить подробный DOM early file map.
- [x] Добавить pre-code generation checklist.
- [x] Добавить layout task list для physical structure creation.
- [x] Добавить combined Url then Abort execution sequence.
- [x] Добавить file templates для первого code pass.
- [x] Добавить post-Abort next-steps sequence.

## Definition of Done

- [ ] В проекте существуют папки DOM и BOM с согласованной внутренней структурой.
- [ ] Каждая интерфейсная сущность вынесена в отдельный файл.
- [ ] Все включенные Web IDL interfaces покрыты C#-контрактами.
- [ ] Именование соответствует C#-стандарту и остается узнаваемо близким к Web IDL.
- [ ] Исторические aggregate leftovers удалены из целевой структуры или явно оставлены только как временный cleanup residue без архитектурного статуса.
- [ ] README и сопровождающая документация обновлены.

## Решения, которые нужно принять до начала массовой генерации

- [x] Какой канонический casing выбрать для namespace: DOM/BOM или Dom/Bom.
- [x] Зафиксировать точную границу first-pass scope для Workers.
- [x] Зафиксировать границу first-pass для полной Fetch family surface; dependent APIs не включают Fetch автоматически.
- [x] Утвердить стратегию для callback -> delegate mapping, чтобы она не расходилась с уже зафиксированной policy для enum/dictionary/typedef.
- [x] Утвердить, что WindowProxy остается provenance/documentation bucket, а не публичным контрактным типом first pass.
- [x] Зафиксировать, что специальный compatibility layer для исторических IWebPage, IWebWindow, IWebBrowser, IDomContext и IElement не закладывается по умолчанию; их наличие в старых файлах не считается текущей архитектурной опорой.

## Зафиксированный First-Pass Scope

### Modern baseline DOM

- [x] В first pass входят DOM/Core, DOM/Traversal, DOM/Ranges, DOM/Selection, DOM/Abort, DOM/Geometry, DOM/Observers и DOM Parsing/Serialization baseline.
- [x] В first pass входит modern DOM event baseline из DOM, UI Events, Pointer Events и Input Events, включая AnimationEvent и TransitionEvent surface.
- [x] В first pass входят DOM/Cssom и DOM/Cssom/View как modern stylesheet, rule, declaration, media-query, viewport и scroll surface.
- [x] В first pass входят DOM/Html, DOM/Html/Elements, DOM/Forms и DOM/Media для современного browser-facing HTML element family.
- [x] В first pass входят DOM/Fullscreen, DOM/Clipboard, DOM/Svg и DOM/MathMl, но только в рамках modern baseline без compatibility tails.

### Modern baseline BOM

- [x] В first pass входят BOM/Windowing, BOM/Navigation, BOM/History, BOM/Location и BOM/Navigator без отдельного WindowProxy contract.
- [x] В first pass входят BOM/Messaging, BOM/Storage, BOM/Permissions, BOM/Screen, BOM/Performance, BOM/Timing и BOM/Scheduling.
- [x] В BOM/Networking first pass ограничен Url surface, то есть IUrl и IUrlSearchParams, без полной Fetch family.
- [x] Navigator webdriver disclosure, Web Locks, modern storage surface и modern navigation family входят в first pass как нормативно закрытые browser-facing contracts.

### Deferred Buckets

- [x] Compatibility bucket: Touch family, legacy DOM/UI event tails, geometry aliases, legacy SVG compatibility surface, legacy navigator plugins and mime types, obsolete HTML element interfaces, obsolete timing tails.
- [x] Worker bucket: Worker, SharedWorker, WorkerGlobalScope families, WorkerNavigator and other worker-specific globals, service-worker-adjacent shared contracts.
- [x] Non-interface or large networking bucket: console namespace modeling, full Fetch family and Fetch-owned signature spillover such as Beacon BodyInit dependencies.
- [x] Provenance review bucket: Typed OM and portal-related HTML surface до отдельного source/provenance review.

### Freeze Rules

- [x] First pass остается modern-baseline only и не смешивает compatibility tails с основной генерацией.
- [x] One-entity-per-file правило распространяется и на supporting contracts, которые нужны для типобезопасной формы public surface.
- [x] Если family находится в deferred bucket, она не протаскивается в first pass транзитивно через зависимые сигнатуры.
- [x] После scope freeze зафиксирован первый generation batch; следующий практический шаг — физическая DOM/BOM layout preparation или прямой старт Url batch, а не новый research pass по уже закрытым normative family.

### Зафиксированный Early Generation Order

- [x] Batch 1: BOM/Networking Url surface, ограниченный IUrl и IUrlSearchParams.
- [x] Batch 1 intentionally validates the smallest safe generator slice: folder placement, one-entity-per-file discipline, naming and baseline public surface shape.
- [x] Batch 2 planned as BOM/Scheduling, чтобы проверить callback-to-delegate mapping, supporting dictionaries и partial Window integration на уже обкатанном pipeline.
- [x] Batch 3 planned as BOM/Permissions, чтобы добавить partial Navigator pressure только после Url and Scheduling validation.
- [x] Web Locks, Storage и другие modern baseline family остаются ранними follow-up batches, но не опережают Url, Scheduling и Permissions.
- [x] Decision rationale and fallback order are tracked in DOM-BOM-First-Generation-Batch.md as the dedicated execution note.

### Execution Checklist For Early Batches

- [x] Url batch stays isolated in BOM/Networking and не включает Fetch-owned types, Beacon spillover или partial Window/Navigator work.
- [x] Scheduling batch начинается только после фиксации naming and placement conventions на IUrl и IUrlSearchParams.
- [x] Scheduling batch обязан покрыть три механики одним проходом: IIdleDeadline, IdleRequestCallback delegate и IdleRequestOptions supporting contract.
- [x] Scheduling batch merges only the collected requestIdleCallback and cancelIdleCallback members into final IWindow ownership.
- [x] Permissions batch начинается только после успешной проверки partial Window merge discipline на Scheduling.
- [x] Permissions batch остается window-centric: worker-side WorkerNavigator exposure остается в deferred worker bucket.
- [x] Permissions batch планируется как IPermissions, IPermissionStatus, PermissionState, PermissionDescriptor и supporting options, но без неутвержденного расширения в compatibility or worker surface.

### Execution Checklist For Early DOM Batches

- [x] DOM/Abort starts the DOM path because it validates an isolated family without heavy partial merge pressure.
- [x] DOM/Geometry runs before DOM/Observers, чтобы geometry-dependent observer signatures не открывали file creation and dependency questions в одном проходе.
- [x] DOM/Observers batch обязан валидировать callback delegates and supporting dictionaries на DOM side без втягивания HTML or CSSOM family pressure.
- [x] DOM/Events base остается modern-only и не втягивает touch or legacy event tails.
- [x] DOM/Core base начинается только после early DOM supporting-type mechanics уже проверены на Abort, Geometry and Observers.
- [x] Full HTML, CSSOM, Svg and MathMl family generation не смешиваются с early DOM batches.
