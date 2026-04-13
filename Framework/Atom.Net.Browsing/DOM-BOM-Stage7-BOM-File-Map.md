# DOM/BOM Stage 7 BOM File Map

## Назначение

Этот документ раскладывает весь Stage 7 BOM core checklist по target folders и expected files.

Он нужен как planning bridge между high-level roadmap и будущим physical file creation pass.

## Windowing, navigation and top-level browsing context

### BOM/Windowing

- BOM/Windowing/IWindow.cs
- BOM/Windowing/IBarProp.cs
- BOM/Windowing/IWindowPostMessageOptions.cs
- BOM/Windowing/IVisualViewport.cs

### BOM/Location

- BOM/Location/ILocation.cs

### BOM/History

- BOM/History/IHistory.cs

### BOM/Navigation

- BOM/Navigation/INavigation.cs
- BOM/Navigation/INavigationHistoryEntry.cs
- BOM/Navigation/INavigationActivation.cs
- BOM/Navigation/INavigationDestination.cs

### BOM/Networking

- BOM/Networking/IUrl.cs
- BOM/Networking/IUrlSearchParams.cs

### BOM/Navigator mixin and capability contracts

- BOM/Navigator/INavigator.cs

### BOM/Screen

- BOM/Screen/IScreen.cs
- BOM/Screen/IScreenOrientation.cs

## Storage, messaging and page state

### BOM/Storage

- BOM/Storage/IStorage.cs
- BOM/Storage/IStorageManager.cs, if included in the accepted first-pass storage surface
- BOM/Storage/IStorageEstimate.cs

### BOM/Messaging

- BOM/Messaging/IBroadcastChannel.cs
- BOM/Messaging/IMessagePort.cs
- BOM/Messaging/IMessageChannel.cs
- BOM/Messaging/IMessageEventTarget.cs
- BOM/Messaging/IStructuredSerializeOptions.cs

### BOM/Permissions

- BOM/Permissions/IPermissions.cs
- BOM/Permissions/PermissionDescriptor.cs
- BOM/Permissions/IPermissionStatus.cs
- BOM/Permissions/PermissionState.cs
- BOM/Permissions/PermissionSetParameters.cs, only if required by the accepted base permissions surface

### DOM/Clipboard

- DOM/Clipboard/IClipboard.cs
- DOM/Clipboard/IClipboardItem.cs
- DOM/Clipboard/IClipboardPermissionDescriptor.cs

### BOM/Scheduling for locks

- BOM/Scheduling/ILockManager.cs
- BOM/Scheduling/ILock.cs

## Performance, timing and scheduling

### BOM/Performance

- BOM/Performance/IPerformance.cs
- BOM/Performance/IPerformanceEntry.cs
- BOM/Performance/IPerformanceMark.cs
- BOM/Performance/IPerformanceMeasure.cs
- BOM/Performance/IPerformanceObserver.cs
- BOM/Performance/IPerformanceObserverCallbackOptions.cs

### BOM/Timing

- BOM/Timing/IPerformanceNavigationTiming.cs

### BOM/Scheduling for idle callbacks

- BOM/Scheduling/IIdleDeadline.cs
- BOM/Scheduling/IdleRequestCallback.cs
- BOM/Scheduling/IdleRequestOptions.cs

## Navigator capability and environment contracts

### BOM/Navigator

- BOM/Navigator/INavigatorAutomationInformation.cs
- BOM/Navigator/INavigatorLanguage.cs
- BOM/Navigator/INavigatorOnLine.cs
- BOM/Navigator/INavigatorConcurrentHardware.cs
- BOM/Navigator/INavigatorCookies.cs
- BOM/Navigator/INavigatorStorage.cs
- BOM/Navigator/INavigatorId.cs
- BOM/Navigator/INavigatorLocks.cs
- BOM/Navigator/INavigatorPermissions.cs, only if accepted in the window-only permissions merge strategy

Notes:

- INavigatorCookies and INavigatorStorage remain navigator-owned capability contracts even when their public members connect to storage-adjacent behavior.
- These navigator-specific files represent standalone mixin or capability contracts, not a rejection of the final INavigator merge strategy.
- Partial members for the final INavigator still merge into BOM/Navigator/INavigator.cs according to the accepted partial merge notes.

## Explicit non-goals of this map

- WindowProxy as standalone public contract
- WorkerNavigator and other worker-specific files
- console namespace modeling files
- Fetch-owned networking files
- compatibility-only tails and obsolete browser surface

## Map constraints

- One entity per file applies to interfaces, delegates, enums and supporting contracts.
- Owning family decides placement.
- Partial Window and partial Navigator members still merge into the final owning files instead of creating split contracts.
- This map is a target inventory for Stage 7 planning, not a commitment that all files will be generated in the first coding pass.
