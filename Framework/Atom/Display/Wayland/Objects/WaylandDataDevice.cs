using System.Runtime.Versioning;

using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland.Objects;

/// <summary>
/// Объект <c lang="text">wl_data_device_manager</c> — обмен данными между приложениями.
/// </summary>
/// <remarks>
/// ★ Отвечает за буфер обмена и перетаскивание между окнами. Нам ни то, ни другое не нужно, но
/// интерфейс обязателен: GTK не создаёт seat без него вовсе, а без seat браузер отбрасывает ВЕСЬ
/// ввод — мышь и клавиатуру. Внешне это выглядит как «события не доходят», хотя доходят они
/// исправно. Диагностируется по <c lang="text">GDK_IS_SEAT (seat)' failed</c> в выводе браузера.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandDataDeviceManager : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_data_device_manager";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestCreateDataSource:
                Client.Register(new WaylandDataSource { Id = message.ReadUInt(ref offset), Version = Version, Client = Client });
                break;

            case RequestGetDataDevice:
                Client.Register(new WaylandDataDevice { Id = message.ReadUInt(ref offset), Version = Version, Client = Client });
                break;
        }
    }

    private const ushort RequestCreateDataSource = 0;
    private const ushort RequestGetDataDevice = 1;
}

/// <summary>
/// Объект <c lang="text">wl_data_device</c> — приём данных для seat.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class WaylandDataDevice : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_data_device";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        // Ни перетаскивание между окнами, ни выделение мы не поддерживаем: единственное окно
        // обменивается данными само с собой, и композитору в этом участвовать не нужно.
        if (message.Opcode == RequestRelease)
            Client.Unregister(Id);
    }

    private const ushort RequestRelease = 2;
}

/// <summary>
/// Объект <c lang="text">wl_data_source</c> — предлагаемые клиентом данные.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class WaylandDataSource : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_data_source";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        if (message.Opcode == RequestDestroy)
            Client.Unregister(Id);
    }

    private const ushort RequestDestroy = 1;
}
