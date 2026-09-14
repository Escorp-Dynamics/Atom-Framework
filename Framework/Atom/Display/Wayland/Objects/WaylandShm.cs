using System.Runtime.Versioning;

using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland.Objects;

/// <summary>
/// Объект <c lang="text">wl_shm</c> — буферы в разделяемой памяти.
/// </summary>
/// <remarks>
/// ★ Именно этот путь позволяет обойтись без GPU. Клиент передаёт дескриптор разделяемой памяти,
/// а композитор мог бы прочитать оттуда пиксели — но выводить их некуда, поэтому дескриптор сразу
/// закрывается. Важно только подтвердить поддерживаемые форматы: без них браузер считает, что
/// рисовать нечем, и не запускается вовсе.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandShm : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_shm";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestCreatePool:
                HandleCreatePool(message.ReadUInt(ref offset));
                break;

            case RequestRelease:
                Client.Unregister(Id);
                break;
        }
    }

    /// <summary>
    /// Объявляет форматы пикселей, которые композитор принимает.
    /// </summary>
    /// <remarks>
    /// Два обязательных формата протокола объявляет любой композитор; их и ждёт браузер.
    /// </remarks>
    public void SendFormats()
    {
        Emit(EventFormat, writer => writer.WriteUInt(FormatArgb8888));
        Emit(EventFormat, writer => writer.WriteUInt(FormatXrgb8888));
    }

    private void HandleCreatePool(uint poolId)
    {
        // Дескриптор приходит вспомогательными данными сокета вместе с этим сообщением.
        var descriptor = Client.TakeDescriptor();
        var pool = new WaylandShmPool { Id = poolId, Version = Version, Client = Client, Descriptor = descriptor };

        // Память отображается только при включённом выводе: в боевом режиме пиксели не нужны.
        if (Client.Compositor.Settings.EnablePresentation)
            pool.MapMemory();

        Client.Register(pool);
    }

    private const ushort RequestCreatePool = 0;
    private const ushort RequestRelease = 1;
    private const ushort EventFormat = 0;

    private const uint FormatArgb8888 = 0;
    private const uint FormatXrgb8888 = 1;
}

/// <summary>
/// Объект <c lang="text">wl_shm_pool</c> — область разделяемой памяти под буферы.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class WaylandShmPool : WaylandObject
{
    /// <summary>Дескриптор разделяемой памяти клиента.</summary>
    public required int Descriptor { get; init; }

    /// <inheritdoc/>
    public override string InterfaceName => "wl_shm_pool";

    /// <summary>Отображённая память пула; <see langword="null"/>, если вывод выключен.</summary>
    public nint MappedMemory { get; private set; }

    /// <summary>Размер отображённой области.</summary>
    public int MappedSize { get; private set; }

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestCreateBuffer:
                HandleCreateBuffer(ref offset, message);
                break;

            case RequestDestroy:
                Client.Unregister(Id);
                break;

            case RequestResize:
                HandleResize(message.ReadInt(ref offset));
                break;
        }
    }

    private void HandleCreateBuffer(ref int offset, in WaylandMessage message)
    {
        var bufferId = message.ReadUInt(ref offset);
        var bufferOffset = message.ReadInt(ref offset);
        var width = message.ReadInt(ref offset);
        var height = message.ReadInt(ref offset);
        var stride = message.ReadInt(ref offset);
        var format = message.ReadUInt(ref offset);

        ++liveBuffers;

        Client.Register(new WaylandBuffer
        {
            Id = bufferId,
            Version = Version,
            Client = Client,
            Pool = this,
            Offset = bufferOffset,
            Width = width,
            Height = height,
            Stride = stride,
            Format = format,
        });
    }

    /// <summary>
    /// Переотображает память после роста пула.
    /// </summary>
    /// <remarks>
    /// Пул только растёт, но старое отображение при этом не расширяется: кадры за его границей
    /// без повторного mmap оказались бы недоступны.
    /// </remarks>
    private void HandleResize(int size)
    {
        if (MappedMemory == nint.Zero || size <= MappedSize)
            return;

        _ = Munmap(MappedMemory, (nuint)MappedSize);
        MappedMemory = nint.Zero;
        MappedSize = 0;

        MapMemory();
    }

    /// <summary>
    /// Отображает память клиента, чтобы читать кадры.
    /// </summary>
    /// <remarks>
    /// Размер узнаётся у самого дескриптора: запрос создания пула несёт его отдельным аргументом,
    /// но к моменту вызова он уже разобран, а <c lang="text">lseek</c> даёт то же значение надёжнее.
    /// </remarks>
    public void MapMemory()
    {
        if (Descriptor < 0 || MappedMemory != nint.Zero)
            return;

        var length = (int)Lseek(Descriptor, 0, SeekEnd);
        if (length <= 0)
        {
            MapFailure = "lseek вернул " + length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ", errno " + System.Runtime.InteropServices.Marshal.GetLastPInvokeError().ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        var address = Mmap(nint.Zero, (nuint)length, ProtRead, MapShared, Descriptor, 0);
        if (address == MapFailedAddress)
        {
            MapFailure = "mmap отказал, errno " + System.Runtime.InteropServices.Marshal.GetLastPInvokeError().ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        MappedMemory = address;
        MappedSize = length;
    }

    /// <summary>Причина, по которой память не отобразилась.</summary>
    public string? MapFailure { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// ★ По протоколу уничтожение пула НЕ убивает созданные из него буферы — память
    /// освобождается лишь когда исчез последний из них. Chrome этим пользуется буквально: он
    /// уничтожает пул сразу после создания буферов и продолжает рисовать в них.
    /// </remarks>
    public override void OnDestroyed()
    {
        isDestroyRequested = true;
        TryReleaseResources();
    }

    /// <summary>Учитывает исчезновение буфера из этого пула.</summary>
    public void ReleaseBuffer()
    {
        if (liveBuffers > 0)
            --liveBuffers;

        TryReleaseResources();
    }

    private void TryReleaseResources()
    {
        if (!isDestroyRequested || liveBuffers > 0)
            return;

        if (MappedMemory != nint.Zero)
        {
            _ = Munmap(MappedMemory, (nuint)MappedSize);
            MappedMemory = nint.Zero;
        }

        if (Descriptor >= 0)
            _ = Close(Descriptor);
    }

    private int liveBuffers;
    private bool isDestroyRequested;

    private const ushort RequestCreateBuffer = 0;
    private const ushort RequestDestroy = 1;
    private const ushort RequestResize = 2;

    private const int ProtRead = 0x1;
    private const int MapShared = 0x01;
    private const int SeekEnd = 2;
    private static readonly nint MapFailedAddress = -1;

    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "mmap", SetLastError = true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.SafeDirectories)]
    private static partial nint Mmap(nint address, nuint length, int protection, int flags, int descriptor, long offset);

    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "munmap", SetLastError = true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.SafeDirectories)]
    private static partial int Munmap(nint address, nuint length);

    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "lseek", SetLastError = true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.SafeDirectories)]
    private static partial long Lseek(int descriptor, long offset, int whence);

    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.SafeDirectories)]
    private static partial int Close(int descriptor);
}

/// <summary>
/// Объект <c lang="text">wl_buffer</c> — кадр, который клиент отдаёт композитору.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class WaylandBuffer : WaylandObject
{
    /// <summary>Пул, в котором лежат пиксели.</summary>
    public WaylandShmPool? Pool { get; init; }

    /// <summary>Смещение кадра в пуле.</summary>
    public int Offset { get; init; }

    /// <summary>Ширина кадра.</summary>
    public int Width { get; init; }

    /// <summary>Высота кадра.</summary>
    public int Height { get; init; }

    /// <summary>Длина строки в байтах.</summary>
    public int Stride { get; init; }

    /// <summary>
    /// Формат пикселей: 0 — ARGB8888, 1 — XRGB8888.
    /// </summary>
    /// <remarks>
    /// ★ Браузер рисует окно с прозрачными скруглёнными углами и тенью. Потеря формата
    /// превращала прозрачные точки в чёрные — углы оказывались залиты.
    /// </remarks>
    public uint Format { get; init; }

    /// <inheritdoc/>
    public override string InterfaceName => "wl_buffer";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        if (message.Opcode == RequestDestroy)
            Client.Unregister(Id);
    }

    /// <inheritdoc/>
    public override void OnDestroyed() => Pool?.ReleaseBuffer();

    /// <inheritdoc/>
    /// <remarks>Буфер уходит раньше пула: пул держит память, пока жив хоть один буфер.</remarks>
    public override int DestructionPriority => 1;

    /// <summary>
    /// Возвращает буфер клиенту.
    /// </summary>
    /// <remarks>
    /// Пока буфер не отпущен, клиент не может в него рисовать и ждёт. Удержание буфера здесь
    /// означало бы остановку отрисовки страницы.
    /// </remarks>
    public void SendRelease() => Emit(EventRelease);

    private const ushort RequestDestroy = 0;
    private const ushort EventRelease = 0;
}
