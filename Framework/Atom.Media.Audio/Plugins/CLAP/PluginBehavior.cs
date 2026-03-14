using System.Runtime.InteropServices;

namespace Atom.Media.Audio.Plugins.CLAP;

/// <summary>
/// Представляет базовый класс для реализации плагинов CLAP.
/// </summary>
public abstract class PluginBehavior
{
    private static PluginDescriptor descriptor;
    private static Host host;

    public PluginDescriptor Descriptor => descriptor;

    public Host Host => host;

    protected virtual void OnInitialized(PluginDescriptor descriptor) { }

    [UnmanagedCallersOnly(EntryPoint = "clap_init")]
    public static nint Init(nint hostPointer)
    {
        host = Marshal.PtrToStructure<Host>(hostPointer);
        return host.ToPointer();
    }
}