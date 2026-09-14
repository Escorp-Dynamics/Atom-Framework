using System.Runtime.Versioning;

using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland.Objects;

/// <summary>
/// Объект <c lang="text">wl_registry</c> — каталог возможностей композитора.
/// </summary>
/// <remarks>
/// ★ Состав и ПОРЯДОК объявляемых интерфейсов наблюдаемы клиентом. Браузер по ним судит о среде,
/// поэтому набор держится близким к тому, что объявляют настоящие композиторы: лишний интерфейс
/// заметен так же, как недостающий.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandRegistry : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_registry";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        if (message.Opcode != RequestBind)
            return;

        var offset = 0;
        var name = message.ReadUInt(ref offset);
        var interfaceName = message.ReadString(ref offset);
        var version = message.ReadUInt(ref offset);
        var newId = message.ReadUInt(ref offset);

        Client.Compositor.BindGlobal(Client, name, interfaceName, version, newId);
    }

    /// <summary>Объявляет клиенту все доступные интерфейсы.</summary>
    public void AnnounceGlobals()
    {
        foreach (var global in Client.Compositor.Globals)
        {
            Emit(EventGlobal, writer => writer
                .WriteUInt(global.Name)
                .WriteString(global.InterfaceName)
                .WriteUInt(global.Version));
        }
    }

    private const ushort RequestBind = 0;
    private const ushort EventGlobal = 0;
}
