#pragma warning disable S4487, CA1416, S2325, IDE0022, CA2216, S1994, IDE0004, S1905, IDE0060

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Atom.Media.Video.Backends.PipeWire.PipeWireNative;

namespace Atom.Media.Video.Backends.PipeWire;

[SupportedOSPlatform("linux")]
internal sealed unsafe class PipeWireExportedVideoDevice : IDisposable
{
    private readonly Lock syncRoot = new();

    private GCHandle selfHandle;
    private IntPtr interfaceTypeName;
    private SpaDevice* device;
    private SpaDeviceMethods* methods;
    private IntPtr exportProperties;
    private IntPtr infoPropsDict;
    private IntPtr exportProxy;
    private uint managedObjectId = PW_ID_ANY;
    private IntPtr managedObjectTypeName;
    private IntPtr managedObjectFactoryName;
    private IntPtr managedObjectPropsDict;

    private SpaDeviceEvents* deviceEvents;
    private void* deviceEventsData;
    private bool disposed;

    internal void Export(IntPtr core, IntPtr properties)
    {
        selfHandle = GCHandle.Alloc(this);
        interfaceTypeName = Marshal.StringToHGlobalAnsi(SPA_TYPE_INTERFACE_Device);

        device = (SpaDevice*)NativeMemory.AllocZeroed((nuint)sizeof(SpaDevice));
        methods = (SpaDeviceMethods*)NativeMemory.AllocZeroed((nuint)sizeof(SpaDeviceMethods));

        methods->Version = SPA_VERSION_DEVICE_METHODS;
        methods->AddListener = &OnAddListener;
        methods->Sync = &OnSync;
        methods->EnumParams = &OnEnumParams;
        methods->SetParam = &OnSetParam;

        device->Interface.Type = interfaceTypeName;
        device->Interface.Version = SPA_VERSION_DEVICE;
        device->Interface.Callbacks.Funcs = (IntPtr)methods;
        device->Interface.Callbacks.Data = (void*)GCHandle.ToIntPtr(selfHandle);

        exportProperties = properties;
        infoPropsDict = GetDictPointer(properties);

        exportProxy = pw_core_export(core, SPA_TYPE_INTERFACE_Device, properties, (IntPtr)device, 0);
        if (exportProxy == IntPtr.Zero)
        {
            throw new VirtualCameraException("Не удалось экспортировать SPA device в PipeWire.");
        }
    }

    internal uint TryGetBoundId() => exportProxy != IntPtr.Zero ? pw_proxy_get_bound_id(exportProxy) : PW_ID_ANY;

    internal void BindManagedObject(uint objectId, string typeName, IntPtr props, string? factoryName = null)
    {
        lock (syncRoot)
        {
            managedObjectId = objectId;
            managedObjectTypeName = ReplaceString(managedObjectTypeName, typeName);
            managedObjectFactoryName = ReplaceString(managedObjectFactoryName, factoryName);
            managedObjectPropsDict = props != IntPtr.Zero ? GetDictPointer(props) : IntPtr.Zero;

            EmitObjectInfo(full: true);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DestroyExportProxy();

        if (exportProperties != IntPtr.Zero)
        {
            pw_properties_free(exportProperties);
            exportProperties = IntPtr.Zero;
        }

        if (managedObjectTypeName != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(managedObjectTypeName);
            managedObjectTypeName = IntPtr.Zero;
        }

        if (managedObjectFactoryName != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(managedObjectFactoryName);
            managedObjectFactoryName = IntPtr.Zero;
        }

        infoPropsDict = IntPtr.Zero;
        managedObjectPropsDict = IntPtr.Zero;
        managedObjectId = PW_ID_ANY;

        if (methods is not null)
        {
            NativeMemory.Free(methods);
            methods = null;
        }

        if (device is not null)
        {
            NativeMemory.Free(device);
            device = null;
        }

        if (interfaceTypeName != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(interfaceTypeName);
            interfaceTypeName = IntPtr.Zero;
        }

        if (selfHandle.IsAllocated)
        {
            selfHandle.Free();
        }
    }

    internal void DestroyExportProxy()
    {
        if (exportProxy != IntPtr.Zero)
        {
            pw_proxy_destroy(exportProxy);
            exportProxy = IntPtr.Zero;
        }
    }

    private static PipeWireExportedVideoDevice GetSelf(void* objectPointer)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)objectPointer);
        return (PipeWireExportedVideoDevice)handle.Target!;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnAddListener(void* objectPointer, SpaHook* listener, SpaDeviceEvents* events, void* data)
    {
        var self = GetSelf(objectPointer);

        lock (self.syncRoot)
        {
            self.deviceEvents = events;
            self.deviceEventsData = data;
            self.EmitInfo(full: true);
            self.EmitObjectInfo(full: true);
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnSync(void* objectPointer, int seq)
    {
        var self = GetSelf(objectPointer);

        lock (self.syncRoot)
        {
            self.EmitResult(seq, res: 0, type: 0, result: null);
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnEnumParams(void* objectPointer, int seq, uint id, uint start, uint num, SpaPod* filter)
    {
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnSetParam(void* objectPointer, uint id, uint flags, SpaPod* param)
    {
        return -2;
    }

    private void EmitInfo(bool full)
    {
        if (deviceEvents is null || deviceEvents->Info is null)
        {
            return;
        }

        var info = new SpaDeviceInfo
        {
            Version = 0,
            ChangeMask = full
                ? SPA_DEVICE_CHANGE_MASK_FLAGS | SPA_DEVICE_CHANGE_MASK_PROPS
                : SPA_DEVICE_CHANGE_MASK_PROPS,
            Flags = 0,
            Props = infoPropsDict,
            Params = null,
            ParamCount = 0,
        };

        deviceEvents->Info(deviceEventsData, &info);
    }

    private void EmitObjectInfo(bool full)
    {
        if (managedObjectId == PW_ID_ANY
            || managedObjectTypeName == IntPtr.Zero
            || deviceEvents is null
            || deviceEvents->ObjectInfo is null)
        {
            return;
        }

        var info = new SpaDeviceObjectInfo
        {
            Version = SPA_VERSION_DEVICE_OBJECT_INFO,
            Type = managedObjectTypeName,
            FactoryName = managedObjectFactoryName,
            ChangeMask = full
                ? SPA_DEVICE_OBJECT_CHANGE_MASK_FLAGS | SPA_DEVICE_OBJECT_CHANGE_MASK_PROPS
                : SPA_DEVICE_OBJECT_CHANGE_MASK_PROPS,
            Flags = 0,
            Props = managedObjectPropsDict,
        };

        deviceEvents->ObjectInfo(deviceEventsData, managedObjectId, &info);
    }

    private void EmitResult(int seq, int res, uint type, void* result)
    {
        if (deviceEvents is null || deviceEvents->Result is null)
        {
            return;
        }

        deviceEvents->Result(deviceEventsData, seq, res, type, result);
    }

    private static IntPtr GetDictPointer(IntPtr properties)
    {
        return (IntPtr)(&((PwProperties*)properties)->Dict);
    }

    private static IntPtr ReplaceString(IntPtr current, string? value)
    {
        if (current != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(current);
        }

        return string.IsNullOrWhiteSpace(value)
            ? IntPtr.Zero
            : Marshal.StringToHGlobalAnsi(value);
    }
}