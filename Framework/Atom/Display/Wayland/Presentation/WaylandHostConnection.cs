using System.Net.Sockets;
using System.Runtime.Versioning;

using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland.Presentation;

/// <summary>
/// Клиентская сторона протокола: подключение к композитору хоста.
/// </summary>
/// <remarks>
/// ★ Нужна только для отладки. В боевом режиме композитор безголовый — кадры браузера
/// отбрасываются. Когда вывод включён, композитор сам становится клиентом сессии разработчика и
/// показывает картинку окном, как это делают вложенные композиторы.
///
/// Транспорт тот же, что и на серверной стороне: разбор сообщений и передача дескрипторов уже
/// реализованы, здесь только обратное направление — мы шлём запросы и принимаем события.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandHostConnection : IDisposable
{
    private readonly Socket socket;
    private readonly WaylandMessageWriter writer = new();
    private readonly byte[] receiveBuffer = new byte[65536];
    private readonly Dictionary<uint, string> objectInterfaces = [];
    private int receiveLength;
    private uint nextId = 2;
    private int isDisposed;

    private WaylandHostConnection(Socket socket) => this.socket = socket;

    /// <summary>Событие от композитора хоста: объект, код операции и тело.</summary>
    public event Action<uint, ushort, ReadOnlyMemory<byte>>? EventReceived;

    /// <summary>
    /// Ошибка протокола, о которой сообщила сессия разработчика.
    /// </summary>
    /// <remarks>
    /// После неё оболочка рвёт соединение, и окно вывода просто исчезает с экрана.
    /// </remarks>
    public string? LastProtocolError { get; private set; }

    /// <summary>Соединение с сессией разорвано.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>
    /// Подключается к композитору хоста.
    /// </summary>
    /// <returns><see langword="null"/>, если сессии нет — тогда вывод невозможен.</returns>
    public static WaylandHostConnection? TryConnect()
    {
        var display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (string.IsNullOrEmpty(display))
            return null;

        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtimeDirectory))
            return null;

        var path = Path.IsPathRooted(display) ? display : Path.Combine(runtimeDirectory, display);

        var endpoint = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            endpoint.Connect(new UnixDomainSocketEndPoint(path));
            endpoint.Blocking = false;

            var connection = new WaylandHostConnection(endpoint);

            // Владение сокетом перешло соединению.
            endpoint = null;
            return connection;
        }
        catch (SocketException)
        {
            return null;
        }
        finally
        {
            endpoint?.Dispose();
        }
    }

    /// <summary>Выделяет идентификатор для нового объекта.</summary>
    public uint AllocateId(string interfaceName)
    {
        var id = nextId++;
        objectInterfaces[id] = interfaceName;
        return id;
    }

    /// <summary>Имя интерфейса объекта, если он известен.</summary>
    public string? ResolveInterface(uint objectId)
        => objectInterfaces.TryGetValue(objectId, out var name) ? name : null;

    /// <summary>Отправляет запрос композитору хоста.</summary>
    public void Send(uint objectId, ushort opcode, Action<WaylandMessageWriter>? writeArguments = null)
    {
        var message = writer.Begin(objectId, opcode);
        writeArguments?.Invoke(message);

        try
        {
            _ = socket.Send(message.Build(), SocketFlags.None);
        }
        catch (SocketException)
        {
            // Сессия разработчика могла закрыться: отладочный вывод не должен ронять композитор.
        }
    }

    /// <summary>Отправляет запрос вместе с файловым дескриптором.</summary>
    public void SendWithDescriptor(uint objectId, ushort opcode, int descriptor, Action<WaylandMessageWriter>? writeArguments = null)
    {
        var message = writer.Begin(objectId, opcode);
        writeArguments?.Invoke(message);

        WaylandAncillary.SendWithDescriptor(socket, message.Build(), descriptor);
    }

    /// <summary>Читает и разбирает события композитора хоста.</summary>
    public void Pump()
    {
        int received;

        try
        {
            if (!socket.Poll(0, SelectMode.SelectRead))
                return;

            received = socket.Receive(receiveBuffer, receiveLength, receiveBuffer.Length - receiveLength, SocketFlags.None);
        }
        catch (SocketException)
        {
            return;
        }

        if (received == 0)
        {
            IsClosed = true;
            return;
        }

        if (received < 0)
            return;

        receiveLength += received;

        var offset = 0;
        while (WaylandMessage.TryParse(receiveBuffer.AsMemory(offset, receiveLength - offset), out var message, out var consumed))
        {
            if (message.ObjectId == DisplayId && message.Opcode == DisplayError)
                CaptureProtocolError(message);
            else
                EventReceived?.Invoke(message.ObjectId, message.Opcode, message.Payload.ToArray());

            offset += consumed;
        }

        if (offset > 0)
        {
            Array.Copy(receiveBuffer, offset, receiveBuffer, 0, receiveLength - offset);
            receiveLength -= offset;
        }
    }

    private void CaptureProtocolError(in WaylandMessage message)
    {
        var offset = 0;
        var objectId = message.ReadUInt(ref offset);
        var code = message.ReadUInt(ref offset);
        var description = message.ReadString(ref offset);

        var culture = System.Globalization.CultureInfo.InvariantCulture;

        LastProtocolError = "объект " + objectId.ToString(culture)
            + ", код " + code.ToString(culture)
            + ": " + description
            + " (интерфейс " + (ResolveInterface(objectId) ?? "неизвестен") + ")";

        IsClosed = true;
    }

    private const uint DisplayId = 1;
    private const ushort DisplayError = 0;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
            return;

        socket.Dispose();
    }
}
