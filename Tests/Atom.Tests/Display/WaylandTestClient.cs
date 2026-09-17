using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;

namespace Atom.Tests;

/// <summary>
/// Минимальный клиент композитора для тестов: заводит окна и разбирает ответы.
/// </summary>
/// <remarks>
/// ★ Композитор принимает только настоящие протокольные соединения — подать ему объект напрямую
/// нельзя. Клиент повторяет ровно тот обмен, который ведёт браузер: каталог интерфейсов, привязка
/// оболочки, создание поверхности и назначение ей роли окна.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandTestClient : IDisposable
{
    private readonly Socket socket;
    private readonly Dictionary<string, (uint Name, uint Version)> globals = [];
    private readonly byte[] receiveBuffer = new byte[64 * 1024];
    private int received;
    private uint nextId = 2;

    private WaylandTestClient(Socket socket) => this.socket = socket;

    /// <summary>Подключается к композитору по пути его сокета.</summary>
    public static WaylandTestClient Connect(string socketPath)
    {
        var endpoint = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        endpoint.Connect(new UnixDomainSocketEndPoint(socketPath));
        endpoint.Blocking = false;

        var client = new WaylandTestClient(endpoint);
        client.BindGlobals();
        return client;
    }

    /// <summary>Интерфейс оболочки окон, привязанный к соединению.</summary>
    public uint XdgWmBase { get; private set; }

    /// <summary>Интерфейс поверхностей, привязанный к соединению.</summary>
    public uint Compositor { get; private set; }

    /// <summary>
    /// Заводит окно: поверхность, роль оболочки и заголовок.
    /// </summary>
    /// <param name="title">Заголовок окна.</param>
    /// <returns>Номера созданных объектов.</returns>
    public (uint Surface, uint XdgSurface, uint Toplevel) CreateWindow(string title)
    {
        var surface = AllocateId();
        Send(Compositor, CompositorCreateSurface, writer => WriteUInt(writer, surface));

        var xdgSurface = AllocateId();
        Send(XdgWmBase, XdgWmBaseGetXdgSurface, writer =>
        {
            WriteUInt(writer, xdgSurface);
            WriteUInt(writer, surface);
        });

        var toplevel = AllocateId();
        Send(xdgSurface, XdgSurfaceGetToplevel, writer => WriteUInt(writer, toplevel));
        Send(toplevel, XdgToplevelSetTitle, writer => WriteString(writer, title));

        // Пустой коммит завершает заявку роли: композитор отвечает на неё размером окна.
        Send(surface, SurfaceCommit, static _ => { });

        return (surface, xdgSurface, toplevel);
    }

    /// <summary>Уничтожает объект оболочки.</summary>
    public void DestroyToplevel(uint toplevel) => Send(toplevel, XdgToplevelDestroy, static _ => { });

    /// <summary>Просит свернуть окно.</summary>
    public void MinimizeToplevel(uint toplevel) => Send(toplevel, XdgToplevelSetMinimized, static _ => { });

    /// <summary>Задаёт границы окна внутри поверхности.</summary>
    public void SetWindowGeometry(uint xdgSurface, int x, int y, int width, int height)
        => Send(xdgSurface, XdgSurfaceSetWindowGeometry, writer =>
        {
            WriteInt(writer, x);
            WriteInt(writer, y);
            WriteInt(writer, width);
            WriteInt(writer, height);
        });

    /// <summary>Указатель соединения, если он заведён.</summary>
    public uint Pointer { get; private set; }

    /// <summary>Заводит указатель на сиденье: без него ввод адресовать некому.</summary>
    public uint CreatePointer()
    {
        var seat = BindGlobal("wl_seat");
        Pointer = AllocateId();
        Send(seat, SeatGetPointer, writer => WriteUInt(writer, Pointer));
        return Pointer;
    }

    /// <summary>Клавиатура соединения, если она заведена.</summary>
    public uint Keyboard { get; private set; }

    /// <summary>Заводит клавиатуру на сиденье.</summary>
    public uint CreateKeyboard()
    {
        var seat = BindGlobal("wl_seat");
        Keyboard = AllocateId();
        Send(seat, SeatGetKeyboard, writer => WriteUInt(writer, Keyboard));
        return Keyboard;
    }

    /// <summary>Разбирает накопившиеся события вместе с их телом.</summary>
    public List<(uint ObjectId, ushort Opcode, byte[] Body)> DrainEvents()
    {
        var events = new List<(uint, ushort, byte[])>();

        try
        {
            var count = socket.Receive(receiveBuffer.AsSpan(received), SocketFlags.None);
            received += count;
        }
        catch (SocketException)
        {
            // Событий пока нет — неблокирующий сокет отвечает отказом.
        }

        var offset = 0;

        while (received - offset >= HeaderLength)
        {
            var objectId = BinaryPrimitives.ReadUInt32LittleEndian(receiveBuffer.AsSpan(offset));
            var header = BinaryPrimitives.ReadUInt32LittleEndian(receiveBuffer.AsSpan(offset + 4));
            var opcode = (ushort)(header & 0xFFFF);
            var length = (int)(header >> 16);

            if (length < HeaderLength || received - offset < length)
                break;

            events.Add((objectId, opcode, receiveBuffer[(offset + HeaderLength)..(offset + length)]));
            offset += length;
        }

        if (offset > 0)
        {
            Array.Copy(receiveBuffer, offset, receiveBuffer, 0, received - offset);
            received -= offset;
        }

        return events;
    }

    /// <summary>Читает номер поверхности из тела события входа.</summary>
    /// <remarks>За номером события идёт поверхность — у входа и ухода начало тела одинаково.</remarks>
    public static uint ReadEnterSurface(byte[] body)
        => BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4));

    /// <summary>Читает номер поверхности из тела события ухода.</summary>
    public static uint ReadLeaveSurface(byte[] body)
        => BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4));

    /// <inheritdoc/>
    public void Dispose() => socket.Dispose();

    private void BindGlobals()
    {
        var registry = AllocateId();
        Send(DisplayId, DisplayGetRegistry, writer => WriteUInt(writer, registry));

        // Каталог приходит одним потоком сразу после запроса: ждём, пока появятся нужные записи.
        for (var attempt = 0; attempt < ReadAttempts && globals.Count == 0; ++attempt)
        {
            ReadGlobals(registry);
            if (globals.Count == 0)
                Thread.Sleep(PollDelay);
        }

        Compositor = BindGlobal("wl_compositor");
        XdgWmBase = BindGlobal("xdg_wm_base");
    }

    private void ReadGlobals(uint registry)
    {
        try
        {
            received += socket.Receive(receiveBuffer.AsSpan(received), SocketFlags.None);
        }
        catch (SocketException)
        {
            return;
        }

        var offset = 0;

        while (received - offset >= HeaderLength)
        {
            var objectId = BinaryPrimitives.ReadUInt32LittleEndian(receiveBuffer.AsSpan(offset));
            var header = BinaryPrimitives.ReadUInt32LittleEndian(receiveBuffer.AsSpan(offset + 4));
            var opcode = (ushort)(header & 0xFFFF);
            var length = (int)(header >> 16);

            if (length < HeaderLength || received - offset < length)
                break;

            if (objectId == registry && opcode == RegistryGlobal)
                ReadGlobalEntry(receiveBuffer.AsSpan(offset + HeaderLength, length - HeaderLength));

            offset += length;
        }

        if (offset > 0)
        {
            Array.Copy(receiveBuffer, offset, receiveBuffer, 0, received - offset);
            received -= offset;
        }

        registryId = registry;
    }

    private uint registryId;

    private void ReadGlobalEntry(ReadOnlySpan<byte> body)
    {
        var name = BinaryPrimitives.ReadUInt32LittleEndian(body);
        var textLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);

        // Длина включает завершающий ноль, а само поле выровнено до четырёх байтов.
        var interfaceName = Encoding.UTF8.GetString(body.Slice(8, textLength - 1));
        var padded = (textLength + 3) & ~3;
        var version = BinaryPrimitives.ReadUInt32LittleEndian(body[(8 + padded)..]);

        globals[interfaceName] = (name, version);
    }

    private uint BindGlobal(string interfaceName)
    {
        if (!globals.TryGetValue(interfaceName, out var entry))
            throw new InvalidOperationException("Композитор не объявил интерфейс " + interfaceName + ".");

        var id = AllocateId();

        Send(registryId, RegistryBind, writer =>
        {
            WriteUInt(writer, entry.Name);
            WriteString(writer, interfaceName);
            WriteUInt(writer, entry.Version);
            WriteUInt(writer, id);
        });

        return id;
    }

    private uint AllocateId() => nextId++;

    private void Send(uint objectId, ushort opcode, Action<List<byte>> writeBody)
    {
        var body = new List<byte>();
        writeBody(body);

        var message = new byte[HeaderLength + body.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(message, objectId);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), ((uint)message.Length << 16) | opcode);
        body.CopyTo(message, HeaderLength);

        _ = socket.Send(message, SocketFlags.None);
    }

    private static void WriteUInt(List<byte> body, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        body.AddRange(buffer);
    }

    private static void WriteInt(List<byte> body, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        body.AddRange(buffer);
    }

    private static void WriteString(List<byte> body, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt(body, (uint)(bytes.Length + 1));
        body.AddRange(bytes);
        body.Add(0);

        // Строка выравнивается до четырёх байтов: без добивки следующий аргумент читается со сдвигом.
        while (body.Count % 4 != 0)
            body.Add(0);
    }

    private const int HeaderLength = 8;
    private const int ReadAttempts = 50;
    private const int PollDelay = 20;

    private const uint DisplayId = 1;
    private const ushort DisplayGetRegistry = 1;
    private const ushort RegistryBind = 0;
    private const ushort RegistryGlobal = 0;
    private const ushort CompositorCreateSurface = 0;
    private const ushort SurfaceCommit = 6;
    private const ushort SeatGetPointer = 0;
    private const ushort SeatGetKeyboard = 1;
    private const ushort XdgWmBaseGetXdgSurface = 2;
    private const ushort XdgSurfaceGetToplevel = 1;
    private const ushort XdgSurfaceSetWindowGeometry = 3;
    private const ushort XdgToplevelDestroy = 0;
    private const ushort XdgToplevelSetTitle = 2;
    private const ushort XdgToplevelSetMinimized = 13;
}
