using System.Runtime.Versioning;

using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland.Objects;

/// <summary>
/// Объект <c lang="text">zxdg_decoration_manager_v1</c> — распределение декораций окна.
/// </summary>
/// <remarks>
/// ★ Без этого интерфейса клиент считает, что рисовать рамку некому, и берёт декорации на себя.
/// Своя рамка означает поверхность БОЛЬШЕ окна: по краям идёт тень, и <c lang="text">outerWidth</c>
/// оказывается шире заявленного профилем — расхождение видно странице одной строкой.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class XdgDecorationManager : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "zxdg_decoration_manager_v1";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestDestroy:
                Client.Unregister(Id);
                break;

            case RequestGetToplevelDecoration:
                HandleGetDecoration(ref offset, message);
                break;
        }
    }

    private void HandleGetDecoration(ref int offset, in WaylandMessage message)
    {
        var decorationId = message.ReadUInt(ref offset);
        _ = message.ReadUInt(ref offset);

        var decoration = new XdgToplevelDecoration { Id = decorationId, Version = Version, Client = Client };

        Client.Register(decoration);
        decoration.SendMode();
    }

    private const ushort RequestDestroy = 0;
    private const ushort RequestGetToplevelDecoration = 1;
}

/// <summary>
/// Объект <c lang="text">zxdg_toplevel_decoration_v1</c> — режим декораций конкретного окна.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class XdgToplevelDecoration : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "zxdg_toplevel_decoration_v1";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        switch (message.Opcode)
        {
            case RequestDestroy:
                Client.Unregister(Id);
                break;

            case RequestSetMode:
            case RequestUnsetMode:
                // Режим выбирает композитор; пожелание клиента не меняет решения.
                SendMode();
                break;
        }
    }

    /// <summary>
    /// Сообщает клиенту, что рамку рисует оболочка.
    /// </summary>
    /// <remarks>
    /// Рамка при этом не рисуется вовсе — окно и так занимает выход целиком. Важно лишь то, что
    /// клиент перестаёт резервировать поля под собственную тень.
    /// </remarks>
    public void SendMode() => Emit(EventConfigure, writer => writer.WriteUInt(ModeServerSide));

    private const ushort RequestDestroy = 0;
    private const ushort RequestSetMode = 1;
    private const ushort RequestUnsetMode = 2;

    private const ushort EventConfigure = 0;
    private const uint ModeServerSide = 2;
}
