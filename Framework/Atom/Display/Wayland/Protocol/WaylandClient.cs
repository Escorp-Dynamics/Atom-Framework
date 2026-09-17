using System.Runtime.Versioning;

// Композитор целиком linux-only: атрибуты платформы у каждого вызова только зашумляли бы код.
#pragma warning disable CA1416

namespace Atom.Display.Wayland.Protocol;

/// <summary>
/// Подключённый клиент композитора.
/// </summary>
/// <remarks>
/// Держит таблицу объектов клиента: протокол адресует сообщения по идентификаторам, которые
/// назначает сам клиент, и композитор обязан их различать.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandClient : IDisposable
{
    private readonly WaylandConnection connection;
    private readonly Dictionary<uint, WaylandObject> objects = [];
    private int isDisposed;

    internal WaylandClient(WaylandConnection connection, WaylandCompositor compositor)
    {
        this.connection = connection;
        Compositor = compositor;
        handleRequest = HandleRequest;
    }

    /// <summary>Композитор, обслуживающий клиента.</summary>
    internal WaylandCompositor Compositor { get; }


    /// <summary>Запрос клиента: имя интерфейса и код операции. Для отладки протокола.</summary>
    public event Action<string, ushort>? RequestObserved;

    /// <summary>Клиент отключился.</summary>
    public bool IsClosed => connection.IsClosed;

    /// <summary>Регистрирует объект в таблице клиента.</summary>
    public void Register(WaylandObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        objects[target.Id] = target;
    }

    /// <summary>
    /// Удаляет объект из таблицы.
    /// </summary>
    /// <remarks>
    /// ★ Клиент обязан получить <c lang="text">wl_display.delete_id</c>, иначе он не вправе переиспользовать
    /// идентификатор. Без подтверждения таблицы расходятся, и следующий запрос по этому номеру
    /// клиент считает нарушением протокола — связь рвётся.
    /// </remarks>
    public void Unregister(uint objectId)
    {
        if (!objects.Remove(objectId, out var removed))
            return;

        removed.OnDestroyed();
        SendDeleteId(objectId);
    }

    /// <summary>Подтверждает клиенту освобождение идентификатора.</summary>
    public void SendDeleteId(uint objectId)
    {
        if (IsClosed)
            return;

        if (objects.TryGetValue(Objects.WaylandDisplay.WellKnownId, out var display)
            && display is Objects.WaylandDisplay wlDisplay)
        {
            wlDisplay.SendDeleteId(objectId);
        }
    }

    /// <summary>Ищет объект по идентификатору.</summary>
    public T? Find<T>(uint objectId) where T : WaylandObject
        => objects.TryGetValue(objectId, out var target) ? target as T : null;

    /// <summary>Все объекты клиента указанного типа.</summary>
    public IEnumerable<T> OfType<T>() where T : WaylandObject => objects.Values.OfType<T>();

    /// <summary>Забирает дескриптор, пришедший с текущим сообщением.</summary>
    public int TakeDescriptor() => connection.TakeDescriptor();

    /// <summary>Общий сборщик сообщений этого клиента.</summary>
    public WaylandMessageWriter Writer { get; } = new();

    /// <summary>Отправляет клиенту готовое сообщение.</summary>
    public void Send(ReadOnlySpan<byte> message) => connection.Send(message);

    /// <summary>Отправляет сообщение вместе с дескриптором.</summary>
    public void SendWithDescriptor(ReadOnlySpan<byte> message, int descriptor) => connection.SendWithDescriptor(message, descriptor);

    /// <summary>
    /// Разбирает накопившиеся сообщения и раздаёт их объектам.
    /// </summary>
    public void ProcessPendingRequests() => connection.Receive(handleRequest);

    private void HandleRequest(WaylandMessage message)
    {
        if (!objects.TryGetValue(message.ObjectId, out var target))
        {
            // Запрос несуществующему объекту — нарушение протокола, но обрывать соединение
            // из-за него нельзя: клиент мог удалить объект, пока сообщение шло по сокету.
            return;
        }

        RequestObserved?.Invoke(target.InterfaceName, message.Opcode);

        try
        {
            target.HandleRequest(message);
        }
        catch (WaylandProtocolException)
        {
            // Ошибку одного запроса не переносим на всё соединение: остальные объекты живы.
        }
    }

    // Делегат создаётся один раз, а не на каждый оборот цикла событий.
    private readonly Action<WaylandMessage> handleRequest;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
            return;

        // Порядок обхода словаря произволен, а владелец ресурса может освобождать его по числу
        // ссылок: сперва уходят зависимые объекты, затем те, от кого они зависят.
        var ordered = objects.Values
            .OrderByDescending(target => target.DestructionPriority)
            .ToArray();

        foreach (var target in ordered)
            target.OnDestroyed();

        objects.Clear();
        connection.Dispose();
    }
}
