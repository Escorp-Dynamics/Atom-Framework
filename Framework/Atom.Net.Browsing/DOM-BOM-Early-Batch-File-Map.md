# DOM/BOM Early Batch File Map

## Назначение

Этот документ раскладывает ранние BOM batches по target folders и expected files.

Он не заменяет roadmap или inventory, а служит bridge between planning and physical file creation.

## Batch 1

### BOM/Networking

- BOM/Networking/IUrl.cs
- BOM/Networking/IUrlSearchParams.cs

## Batch 2

### BOM/Scheduling

- BOM/Scheduling/IIdleDeadline.cs
- BOM/Scheduling/IdleRequestCallback.cs
- BOM/Scheduling/IdleRequestOptions.cs

### BOM/Windowing

- BOM/Windowing/IWindow.cs

Notes:

- Scheduling-specific Window members merge into the final IWindow file.
- No separate IWindowScheduling or similar split contract should be introduced.

## Batch 3

### BOM/Permissions

- BOM/Permissions/IPermissions.cs
- BOM/Permissions/PermissionDescriptor.cs
- BOM/Permissions/IPermissionStatus.cs
- BOM/Permissions/PermissionState.cs
- BOM/Permissions/PermissionSetParameters.cs, only if required by the accepted window-side public surface

### BOM/Navigator

- BOM/Navigator/INavigator.cs

Notes:

- Permissions-specific Navigator members merge into the final INavigator file.
- WorkerNavigator is not created in this early batch map.
- Later permission-specific descriptor families do not enter this map automatically.

## Map constraints

- One entity per file applies to all interfaces, delegates, enums and supporting contract types.
- Owning family decides placement, not first mention site.
- Batch-local supporting contracts should be created before widening partial merge pressure.
- This map covers only Url, Scheduling and base Permissions batches; it is not the full Stage 7 file inventory.
