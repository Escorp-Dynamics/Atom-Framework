#pragma warning disable CA2000, VSTHRD003

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tcp;

/// <summary>
/// Представляет поток чтения и записи по протоколу TCP.
/// </summary>
/// <remarks>
/// Класс включает все низкоуровневые настройки в духе браузеров: NoDelay, буферы, DSCP/TTL, локальный bind,
/// per-socket MSS/TFO (где доступны), TCP keep-alive (политика <c>Always</c>), и «пауза перед TLS». Значения берутся из <see cref="Settings"/>.
/// Алгоритм выбора адреса реализован как последовательный «Happy Eyeballs-подобный» (чередование семейств с задержками).
/// </remarks>
public sealed partial class TcpStream : NetworkStream
{
    /// <inheritdoc/>
    protected override bool RequireAtomicSyncSend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => true;
    }

    /// <summary>
    /// Признак включённого Keep-Alive на уровне TCP.
    /// Для безопасности управляем только при политике <see cref="HttpKeepAlivePingPolicy.Always"/>.
    /// </summary>
    public bool UseKeepAlive
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (Settings.KeepAlivePingPolicy is not HttpKeepAlivePingPolicy.Always)
            {
                field = default;
                return;
            }

            field = value;

            if (Socket is null) return;

            if (field)
                TryEnableKeepAlive();
            else
                TryDisableKeepAlive();
        }
    }

    /// <summary>
    /// Параметры TCP.
    /// </summary>
    public TcpSettings Settings { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="TcpStream"/>.
    /// </summary>
    /// <param name="socket">Внешний сокет (может быть как не подключённым, так и уже подключённым).</param>
    /// <param name="settings">Параметры TCP.</param>
    /// <param name="ownsSocket">Закрывать ли сокет при Dispose().</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TcpStream([NotNull] Socket socket, in TcpSettings settings, bool ownsSocket) : base(socket, ownsSocket)
    {
        Settings = settings;
        PostAdoptConfigureSocket();
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="TcpStream"/>.
    /// </summary>
    /// <param name="socket">Внешний сокет (может быть как не подключённым, так и уже подключённым).</param>
    /// <param name="settings">Параметры TCP.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TcpStream(Socket socket, in TcpSettings settings) : this(socket, settings, ownsSocket: true) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="TcpStream"/>.
    /// </summary>
    /// <remarks>
    /// Используем IPv6-сокет с <c>DualMode=true</c>, чтобы уметь ходить как к IPv6, так и к IPv4 узлам одним инстансом.
    /// Все настройки, не зависящие от адресного семейства удалённого узла, применяются сразу.
    /// Адресоспецифичные (например DSCP для IPv4) — после успешного Connect.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TcpStream(in TcpSettings settings) : this(CreateInitialSocket(settings.IsNagleDisabled), settings, ownsSocket: true) => PreConfigureUnboundSocket();

    /// <summary>
    /// Преднастройка «неподключённого» сокета по общим (не адресоспецифичным) параметрам.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PreConfigureUnboundSocket()
    {
        ApplyBuffers(Socket, Settings.ReceiveBufferSize, Settings.SendBufferSize);
        ApplyTtlHopLimit(Socket, Settings.TimeToLive);
        TryBindLocalEndpoint(Socket, Settings.LocalEndPoint);

        if (Settings.KeepAlivePingPolicy is HttpKeepAlivePingPolicy.Always) TryEnableKeepAlive();

        SetTcpMaxSegmentSizeIfSupported(Socket, Settings.MaxSegmentSize);
        EnableTcpFastOpenIfSupported(Socket, Settings.UseFastOpen);
    }

    /// <summary>
    /// Донастройка внешнего сокета согласно Settings (учитывая, подключён он уже или нет).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PostAdoptConfigureSocket()
    {
        // Общие вещи можно и после Connect
        try { Socket.NoDelay = Settings.IsNagleDisabled; } catch { /* Страховка */ }

        if (Settings.ReceiveBufferSize > 0) Socket.ReceiveBufferSize = Settings.ReceiveBufferSize;
        if (Settings.SendBufferSize > 0) Socket.SendBufferSize = Settings.SendBufferSize;

        // TTL/HopLimit
        if (Settings.TimeToLive is not 0)
        {
            try { Socket.Ttl = Settings.TimeToLive; } catch { /* Страховка */ }
            try { Socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.HopLimit, Settings.TimeToLive); } catch { /* Страховка */ }
        }

        // Keep-alive: если политика позволяет — применяем сразу
        if (Settings.KeepAlivePingPolicy is HttpKeepAlivePingPolicy.Always) TryEnableKeepAlive();

        ApplyTrafficClass(Socket, Settings.Dscp, useEcn: false, SafeRemoteAddressFamily);

        // Параметры, которые корректны только ДО Connect
        if (!IsConnected)
        {
            SetTcpMaxSegmentSizeIfSupported(Socket, Settings.MaxSegmentSize);
            EnableTcpFastOpenIfSupported(Socket, Settings.UseFastOpen);
            TryBindLocalEndpoint(Socket, Settings.LocalEndPoint);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryEnableKeepAlive()
    {
        try { Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, optionValue: true); } catch { /* страховка */ }

        if (Settings.KeepAlivePingDelay == Timeout.InfiniteTimeSpan) return;

        var timeSec = Math.Max(1, (int)Settings.KeepAlivePingDelay.TotalSeconds);
        var intervalSec = Math.Max(1, (int)Settings.KeepAlivePingTimeout.TotalSeconds);

        ApplyCrossPlatformKeepAlive(timeSec, intervalSec);
        ApplyWindowsKeepAlive();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyCrossPlatformKeepAlive(int timeSec, int intervalSec)
    {
        try { Socket.SetSocketOption(SocketOptionLevel.Tcp, (SocketOptionName)0x10 /* TcpKeepAliveTime */, timeSec); } catch { /* страховка */ }
        try { Socket.SetSocketOption(SocketOptionLevel.Tcp, (SocketOptionName)0x12 /* TcpKeepAliveInterval */, intervalSec); } catch { /* страховка */ }
        try { Socket.SetSocketOption(SocketOptionLevel.Tcp, (SocketOptionName)0x11 /* TcpKeepAliveRetryCount */, 5); } catch { /* страховка */ }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyWindowsKeepAlive()
    {
        var inBuf = ArrayPool<byte>.Shared.Rent(12);

        try
        {
            BinaryPrimitives.WriteUInt32LittleEndian(inBuf.AsSpan(0, 4), 1u);
            BinaryPrimitives.WriteUInt32LittleEndian(inBuf.AsSpan(4, 4), (uint)Math.Max(1000, (int)Settings.KeepAlivePingDelay.TotalMilliseconds));
            BinaryPrimitives.WriteUInt32LittleEndian(inBuf.AsSpan(8, 4), (uint)Math.Max(1000, (int)Settings.KeepAlivePingTimeout.TotalMilliseconds));
            const int IoKeepAlive = unchecked((int)0x98000004);
            Socket.IOControl(IoKeepAlive, inBuf, default);
        }
        catch { /* *nix/старые Windows — допускаем */ }
        finally { ArrayPool<byte>.Shared.Return(inBuf); }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryDisableKeepAlive()
    {
        try { Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, optionValue: false); } catch { /* страховка */ }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnableTcpFastOpenIfSupported(Socket socket, bool enabled)
    {
        if (!enabled) return;

        try
        {
            if (OperatingSystem.IsLinux()) socket.SetSocketOption(SocketOptionLevel.Tcp, (SocketOptionName)30 /* TCP_FASTOPEN_CONNECT */, 1);
            // На Windows — мягкая деградация (браузеры часто выключают TFO по политике)
        }
        catch { /* безопасная деградация */ }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetTcpMaxSegmentSizeIfSupported(Socket socket, int mss)
    {
        if (mss <= 0) return;

        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                socket.SetSocketOption(SocketOptionLevel.Tcp, (SocketOptionName)2, mss);
        }
        catch { /* ОС может ограничить/проигнорировать */ }
    }

    /// <summary>
    /// Создаёт dual-mode IPv6 сокет под будущий Connect (универсальный вариант).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Socket CreateInitialSocket(bool noDelay)
    {
        var s = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = noDelay,
        };

        try { s.DualMode = true; } catch { /* страховка */ } // единый инстанс для v4/v6
        return s;
    }
}