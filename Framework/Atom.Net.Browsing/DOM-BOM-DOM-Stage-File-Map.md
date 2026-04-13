# DOM/BOM DOM Stage File Map

## Назначение

Этот документ раскладывает DOM stages из roadmap по target folders и expected files.

Он служит planning bridge для стадий DOM/Core, HTML DOM, CSSOM/Geometry, SVG и MathML.

## Stage 3: DOM Core

### DOM/Core

- DOM/Core/IEventTarget.cs
- DOM/Core/INode.cs
- DOM/Core/IDocument.cs
- DOM/Core/IDocumentType.cs
- DOM/Core/IDocumentFragment.cs
- DOM/Core/IShadowRoot.cs
- DOM/Core/IElement.cs
- DOM/Core/IAttr.cs
- DOM/Core/ICharacterData.cs
- DOM/Core/IText.cs
- DOM/Core/ICDataSection.cs
- DOM/Core/IComment.cs
- DOM/Core/IProcessingInstruction.cs
- DOM/Core/INamedNodeMap.cs
- DOM/Core/INodeList.cs
- DOM/Core/IHtmlCollection.cs
- DOM/Core/IDomTokenList.cs
- DOM/Core/IDomImplementation.cs

### DOM/Traversal

- DOM/Traversal/INodeIterator.cs
- DOM/Traversal/ITreeWalker.cs

### DOM/Ranges

- DOM/Ranges/IRange.cs
- DOM/Ranges/IStaticRange.cs
- DOM/Ranges/IAbstractRange.cs

### DOM/Selection

- DOM/Selection/ISelection.cs

### DOM/Events

- DOM/Events/IEvent.cs
- DOM/Events/ICustomEvent.cs
- DOM/Events/IUiEvent.cs
- DOM/Events/IMouseEvent.cs
- DOM/Events/IKeyboardEvent.cs
- DOM/Events/IInputEvent.cs
- DOM/Events/ICompositionEvent.cs
- DOM/Events/IFocusEvent.cs
- DOM/Events/IWheelEvent.cs
- DOM/Events/IPointerEvent.cs
- DOM/Events/ISubmitEvent.cs
- DOM/Events/IBeforeUnloadEvent.cs
- DOM/Events/IPageTransitionEvent.cs
- DOM/Events/IProgressEvent.cs
- DOM/Events/IMessageEvent.cs
- DOM/Events/IErrorEvent.cs
- DOM/Events/ICloseEvent.cs
- DOM/Events/IHashChangeEvent.cs
- DOM/Events/IPopStateEvent.cs
- DOM/Events/IStorageEvent.cs
- DOM/Events/IClipboardEvent.cs
- DOM/Events/IClipboardChangeEvent.cs
- DOM/Events/IAnimationEvent.cs
- DOM/Events/ITransitionEvent.cs

### DOM/Observers

- DOM/Observers/IMutationObserver.cs
- DOM/Observers/IResizeObserver.cs
- DOM/Observers/IIntersectionObserver.cs

### DOM/Abort

- DOM/Abort/IAbortController.cs
- DOM/Abort/IAbortSignal.cs

### DOM/Core or DOM/Parsing

- DOM/Core/IDomParser.cs
- DOM/Core/IXmlSerializer.cs

Notes:

- Touch family stays outside this map in the compatibility bucket.
- Parser placement may later be split into a dedicated DOM/Parsing folder if the physical layout chooses that shape.

## Stage 4: HTML DOM

### DOM/Html

- DOM/Html/IHtmlDocument.cs
- DOM/Html/IHtmlElement.cs
- DOM/Html/IHtmlUnknownElement.cs
- DOM/Html/IHtmlTemplateElement.cs
- DOM/Html/IHtmlSlotElement.cs
- DOM/Html/IHtmlScriptElement.cs
- DOM/Html/IHtmlStyleElement.cs
- DOM/Html/IHtmlLinkElement.cs
- DOM/Html/IHtmlBaseElement.cs
- DOM/Html/IHtmlMetaElement.cs
- DOM/Html/IHtmlTitleElement.cs
- DOM/Html/IHtmlHeadElement.cs
- DOM/Html/IHtmlBodyElement.cs

### DOM/Forms

- DOM/Forms/IHtmlFormElement.cs
- DOM/Forms/IHtmlInputElement.cs
- DOM/Forms/IHtmlButtonElement.cs
- DOM/Forms/IHtmlLabelElement.cs
- DOM/Forms/IHtmlSelectElement.cs
- DOM/Forms/IHtmlOptionElement.cs
- DOM/Forms/IHtmlOptGroupElement.cs
- DOM/Forms/IHtmlTextAreaElement.cs
- DOM/Forms/IHtmlOutputElement.cs
- DOM/Forms/IHtmlFieldSetElement.cs
- DOM/Forms/IHtmlLegendElement.cs
- DOM/Forms/IHtmlDataListElement.cs
- DOM/Forms/IHtmlMeterElement.cs
- DOM/Forms/IHtmlProgressElement.cs
- DOM/Forms/IHtmlDetailsElement.cs
- DOM/Forms/IHtmlDialogElement.cs

### DOM/Html/Elements

- DOM/Html/Elements/IHtmlParagraphElement.cs
- DOM/Html/Elements/IHtmlHeadingElement.cs
- DOM/Html/Elements/IHtmlQuoteElement.cs
- DOM/Html/Elements/IHtmlPreElement.cs
- DOM/Html/Elements/IHtmlHrElement.cs
- DOM/Html/Elements/IHtmlBrElement.cs
- DOM/Html/Elements/IHtmlDivElement.cs
- DOM/Html/Elements/IHtmlSpanElement.cs
- DOM/Html/Elements/IHtmlDListElement.cs
- DOM/Html/Elements/IHtmlOListElement.cs
- DOM/Html/Elements/IHtmlUListElement.cs
- DOM/Html/Elements/IHtmlDirectoryElement.cs, only if the legacy HTML bucket is explicitly admitted
- DOM/Html/Elements/IHtmlMenuElement.cs
- DOM/Html/Elements/IHtmlLIElement.cs
- DOM/Html/Elements/IHtmlTableElement.cs
- DOM/Html/Elements/IHtmlTableCaptionElement.cs
- DOM/Html/Elements/IHtmlTableColElement.cs
- DOM/Html/Elements/IHtmlTableSectionElement.cs
- DOM/Html/Elements/IHtmlTableRowElement.cs
- DOM/Html/Elements/IHtmlTableCellElement.cs
- DOM/Html/Elements/IHtmlAnchorElement.cs
- DOM/Html/Elements/IHtmlAreaElement.cs

### DOM/Media

- DOM/Media/IBlob.cs
- DOM/Media/IFile.cs
- DOM/Media/IFileList.cs
- DOM/Media/IFileReader.cs
- DOM/Media/IHtmlMediaElement.cs
- DOM/Media/IHtmlAudioElement.cs
- DOM/Media/IHtmlVideoElement.cs
- DOM/Media/IHtmlSourceElement.cs
- DOM/Media/IHtmlTrackElement.cs
- DOM/Media/IHtmlImageElement.cs
- DOM/Media/IHtmlPictureElement.cs
- DOM/Media/IHtmlEmbedElement.cs
- DOM/Media/IHtmlObjectElement.cs
- DOM/Media/IHtmlParamElement.cs, only if the legacy compatibility bucket is admitted
- DOM/Media/IHtmlIFrameElement.cs
- DOM/Media/IHtmlFrameElement.cs, only if the legacy compatibility bucket is admitted
- DOM/Media/IHtmlFrameSetElement.cs, only if the legacy compatibility bucket is admitted
- DOM/Media/IHtmlCanvasElement.cs
- DOM/Media/IHtmlMapElement.cs

Notes:

- HTMLPortalElement family stays outside this map until provenance review is accepted.

## Stage 5: CSSOM and Geometry

### DOM/Cssom

- DOM/Cssom/IMediaQueryList.cs
- DOM/Cssom/IMediaQueryListEvent.cs
- DOM/Cssom/ICssStyleSheet.cs
- DOM/Cssom/ICssRule.cs
- DOM/Cssom/ICssRuleList.cs
- DOM/Cssom/ICssStyleDeclaration.cs
- DOM/Cssom/ICssStyleRule.cs
- DOM/Cssom/ICssImportRule.cs
- DOM/Cssom/ICssGroupingRule.cs
- DOM/Cssom/ICssPageRule.cs
- DOM/Cssom/ICssMarginRule.cs
- DOM/Cssom/ICssNamespaceRule.cs
- DOM/Cssom/ICssKeyframeRule.cs
- DOM/Cssom/ICssKeyframesRule.cs
- DOM/Cssom/ICssTransition.cs
- DOM/Cssom/ICssStartingStyleRule.cs
- DOM/Cssom/IStyleSheet.cs
- DOM/Cssom/IStyleSheetList.cs
- DOM/Cssom/IMediaList.cs

### DOM/Geometry

- DOM/Geometry/IDomRectReadOnly.cs
- DOM/Geometry/IDomRect.cs
- DOM/Geometry/IDomPointReadOnly.cs
- DOM/Geometry/IDomPoint.cs
- DOM/Geometry/IDomQuad.cs
- DOM/Geometry/IDomMatrixReadOnly.cs
- DOM/Geometry/IDomMatrix.cs

Notes:

- Typed OM stays outside this map until review-gated source work is accepted.

## Stage 6: SVG and MathML

### DOM/Svg

- DOM/Svg, one file per accepted modern SVG entity from the inventoried SVG family

### DOM/MathMl

- DOM/MathMl, one file per accepted MathML DOM entity from the inventoried MathML baseline

Notes:

- This stage intentionally remains family-shaped because the SVG and MathML inventories are broader than the current roadmap checklist lines.
- Legacy SVG compatibility tails stay outside the default file map.

## Map constraints

- One entity per file applies everywhere.
- Owning family decides placement.
- Deferred compatibility and review-gated surface do not enter the default DOM stage map automatically.
- This map is a planning target, not a commitment that all DOM files will be created in one pass.
