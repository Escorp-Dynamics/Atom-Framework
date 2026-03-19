#pragma warning disable CA2000

using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Atom.Net.Udp;

/// <summary>
/// Представляет UDP-поток.
/// </summary>
public sealed class UdpStream : NetworkStream
{
    /// <summary>
    /// Кэш шаблона удалённой точки, требуемый API ReceiveMessageFrom(… ref EndPoint …).
    /// </summary>
    private EndPoint RemoteTemplate
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            field ??= new IPEndPoint(IPAddress.IPv6Any, 0);
            return field;
        }
    }

    /// <inheritdoc/>
    protected override bool RequireAtomicSyncSend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => true;     // UDP-дейтаграмма должна уйти целиком или не уйти (иначе считаем ошибкой)
    }

    /// <summary>
    /// Параметры UDP.
    /// </summary>
    public UdpSettings Settings { get; }

    /// <summary>
    /// Последние полученные pktinfo (обновляются при каждом Read/ReadAsync).
    /// </summary>
    public UdpPacketInfo LastPacketInfo
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private set;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UdpStream"/>.
    /// </summary>
    /// <param name="socket">Сокет UDP (может быть не «connected»).</param>
    /// <param name="settings">Параметры UDP.</param>
    /// <param name="ownsSocket">Закрывать ли сокет при Dispose().</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UdpStream(Socket socket, in UdpSettings settings, bool ownsSocket) : base(socket, ownsSocket)
    {
        Settings = settings;
        PostConfigureSocket();
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UdpStream"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UdpStream(Socket socket, in UdpSettings settings) : this(socket, settings, ownsSocket: true) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UdpStream"/>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UdpStream(in UdpSettings settings) : this(CreateSocket(), settings, ownsSocket: true) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PostConfigureSocket()
    {
        ApplyBuffers(Socket, Settings.ReceiveBufferSize, Settings.SendBufferSize);
        ApplyTtlHopLimit(Socket, Settings.TimeToLive);

        if (Settings.DontFragment)
        {
            try { Socket.DontFragment = true; } catch { /* страховка */ }
        }

        if (Settings.UsePacketInfo) EnablePacketInfo(Socket);

        TryBindLocalEndpoint(Socket, Settings.LocalEndPoint);
        SuppressConnectionResetIfSupported();
        ApplyTrafficClass(Socket, Settings.Dscp, Settings.UseEcn, SafeRemoteAddressFamily);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ConnectSeqAsync(IPAddress[] addresses, int port, CancellationToken cancellationToken)
    {
        SocketException? last = default;

        for (var i = 0; i < addresses.Length; i++)
        {
            try
            {
                await Socket.ConnectAsync(new IPEndPoint(addresses[i], port), cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SocketException ex)
            {
                if (!IsExpectedConnectFailure(ex)) throw;
                last = ex;
            }
        }

        if (last is not null) throw last;
        throw new SocketException((int)SocketError.HostUnreachable);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ConnectAlternatingAsync(IPAddress[] addresses, int port, CancellationToken cancellationToken)
    {
        CountFamilies(addresses, out var n6, out var n4);

        // Use heap-allocated arrays here because Span<T> (stackalloc) cannot be
        // stored across await boundaries. We copy indices into arrays and pass
        // their spans to the FillFamilyIndices helper.
        var idx6Arr = n6 > 0 ? new int[n6] : [];
        var idx4Arr = n4 > 0 ? new int[n4] : [];

        FillFamilyIndices(addresses, idx6Arr.AsSpan(), idx4Arr.AsSpan(), out var i6, out var i4);

        var p6 = 0;
        var p4 = 0;
        var turnV6 = true;
        var initialDelay = NormalizeDelay(Settings.HappyEyeballsDelay, 200);
        var stepDelay = NormalizeDelay(Settings.HappyEyeballsStepDelay, 250);
        var firstV4Delayed = false;

        while (p6 < i6 || p4 < i4)
        {
            if (await TryHandleIpv6TurnAsync(addresses, idx6Arr, p6, i6, port, turnV6, firstV4Delayed, initialDelay, stepDelay, cancellationToken).ConfigureAwait(false) is { handled: true, completed: true })
            {
                return;
            }

            if (turnV6 && p6 < i6)
            {
                p6++;
                turnV6 = false;
                firstV4Delayed = true;
                continue;
            }

            if (await TryHandleIpv4TurnAsync(addresses, idx4Arr, p4, i4, port, turnV6, stepDelay, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (!turnV6 && p4 < i4)
            {
                p4++;
                turnV6 = true;
                continue;
            }

            (p6, p4) = await RunFinalFamilyConnectAsync(addresses, idx6Arr, idx4Arr, p6, i6, p4, i4, port, cancellationToken).ConfigureAwait(false);
        }

        throw new SocketException((int)SocketError.HostUnreachable);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TimeSpan NormalizeDelay(TimeSpan value, int defaultMilliseconds)
        => value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(defaultMilliseconds) : value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async ValueTask<bool> DelayNextFamilyAsync(bool firstV4Delayed, TimeSpan initialDelay, TimeSpan stepDelay, CancellationToken cancellationToken)
    {
        if (!firstV4Delayed)
        {
            await Task.Delay(initialDelay, cancellationToken).ConfigureAwait(false);
            return true;
        }

        await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<(bool handled, bool completed)> TryHandleIpv6TurnAsync(IPAddress[] addresses, int[] indices, int position, int count, int port, bool turnV6, bool firstV4Delayed, TimeSpan initialDelay, TimeSpan stepDelay, CancellationToken cancellationToken)
    {
        if (!turnV6 || position >= count) return default;
        if (await TryConnectOneAsync(addresses[indices[position]], port, cancellationToken).ConfigureAwait(false)) return (true, true);
        await DelayNextFamilyAsync(firstV4Delayed, initialDelay, stepDelay, cancellationToken).ConfigureAwait(false);
        return (true, false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<bool> TryHandleIpv4TurnAsync(IPAddress[] addresses, int[] indices, int position, int count, int port, bool turnV6, TimeSpan stepDelay, CancellationToken cancellationToken)
    {
        if (turnV6 || position >= count) return false;
        if (await TryConnectOneAsync(addresses[indices[position]], port, cancellationToken).ConfigureAwait(false)) return true;
        await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false);
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<(int p6, int p4)> RunFinalFamilyConnectAsync(IPAddress[] addresses, int[] idx6, int[] idx4, int p6, int i6, int p4, int i4, int port, CancellationToken cancellationToken)
    {
        if (p6 < i6)
        {
            await TryConnectOneAsync(addresses[idx6[p6]], port, cancellationToken).ConfigureAwait(false);
            return (p6 + 1, p4);
        }

        if (p4 < i4)
        {
            await TryConnectOneAsync(addresses[idx4[p4]], port, cancellationToken).ConfigureAwait(false);
            return (p6, p4 + 1);
        }

        return (p6, p4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<bool> TryConnectOneAsync(IPAddress ip, int port, CancellationToken cancellationToken)
    {
        try
        {
            await Socket.ConnectAsync(new IPEndPoint(ip, port), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SocketException ex)
        {
            if (!IsExpectedConnectFailure(ex)) throw;
            // Ожидаемый отказ конкретного адреса: продолжаем перебор других адресов.
            return default;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsExpectedConnectFailure(SocketException ex) => ex.SocketErrorCode switch
    {
        SocketError.AddressAlreadyInUse => true,
        SocketError.AddressNotAvailable => true,
        SocketError.ConnectionRefused => true,
        SocketError.ConnectionReset => true,
        SocketError.HostNotFound => true,
        SocketError.HostUnreachable => true,
        SocketError.NetworkDown => true,
        SocketError.NetworkUnreachable => true,
        SocketError.NoData => true,
        SocketError.Shutdown => true,
        SocketError.TimedOut => true,
        SocketError.TryAgain => true,
        _ => false,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SuppressConnectionResetIfSupported()
    {
        if (!Settings.UseConnectionResetWorkaround) return;
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            Socket.IOControl((IOControlCode)(-1744830452) /* 0x9800000C */, [0, 0, 0, 0], optionOutValue: null);
        }
        catch { /* мягкая деградация */ }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int Read(Span<byte> buffer)
    {
        if (!Settings.UsePacketInfo) return base.Read(buffer);

        var ep = RemoteTemplate;
        var flags = ReadFlags;

        // IPv4/IPv6: используем перегрузки с out pktinfo.
        // Для DualMode сокета возможны оба пути.
        try
        {
            if (Socket.AddressFamily is AddressFamily.InterNetworkV6)
            {
                var n6 = Socket.ReceiveMessageFrom(buffer, ref flags, ref ep, out var pi6);
                if (n6 >= 0) LastPacketInfo = new UdpPacketInfo(pi6.Address, pi6.Interface);
                return n6;
            }

            var n4 = Socket.ReceiveMessageFrom(buffer, ref flags, ref ep, out var pi4);
            if (n4 >= 0) LastPacketInfo = new UdpPacketInfo(pi4.Address, pi4.Interface);
            return n4;
        }
        catch (SocketException se) when (TreatWouldBlockAsZero && se.SocketErrorCode is SocketError.WouldBlock)
        {
            return 0;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!Settings.UsePacketInfo) return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

        var res = await Socket.ReceiveMessageFromAsync(buffer, RemoteTemplate, cancellationToken).ConfigureAwait(false);

        LastPacketInfo = CreatePacketInfo(res.PacketInformation);

        return res.ReceivedBytes;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static UdpPacketInfo CreatePacketInfo(IPPacketInformation packetInformation)
    {
        if (packetInformation.Address is null) return default;
        if (packetInformation.Address.Equals(IPAddress.None)) return default;
        return new UdpPacketInfo(packetInformation.Address, packetInformation.Interface);
    }

    /// <summary>
    /// Подключается к <paramref name="host"/>:<paramref name="port"/> (connected-режим).
    /// Для нескольких адресов — «чередование семейств» (v6/v4) без параллелизма.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var cts = Settings.AttemptTimeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : default;

        var ct = cancellationToken;
        if (cts is not null)
        {
            cts.CancelAfter(Settings.AttemptTimeout);
            ct = cts.Token;
        }

        var addresses = IPAddress.TryParse(host, out var parsed)
            ? [parsed]
            : await ResolveHostAsync(host, ct).ConfigureAwait(false);

        if (addresses.Length is 0) throw new SocketException((int)SocketError.HostNotFound);

        if (!Settings.UseHappyEyeballsAlternating || !HasBothFamilies(addresses))
            await ConnectSeqAsync(addresses, port, ct).ConfigureAwait(false);
        else
            await ConnectAlternatingAsync(addresses, port, ct).ConfigureAwait(false);

        ApplyTrafficClass(Socket, Settings.Dscp, Settings.UseEcn, SafeRemoteAddressFamily);
    }

    /// <summary>Подключается без токена отмены.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask ConnectAsync(string host, int port) => ConnectAsync(host, port, CancellationToken.None);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Socket CreateSocket()
    {
        var s = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        try { s.DualMode = true; } catch { /* страховка */ }
        return s;
    }

    /// <summary>
    /// Включает получение packet information (pktinfo) для IPv4 и IPv6.
    /// Это необходимо для точной мимикрии браузеров: позволяет считывать адрес назначения,
    /// интерфейс и корректно реализовывать Path Validation/Connection Migration в QUIC.
    /// </summary>
    /// <param name="s">UDP-сокет.</param>
    /// <remarks>
    /// Без P/Invoke: используем стандартный <see cref="Socket.SetSocketOption(SocketOptionLevel, SocketOptionName, bool)"/>.
    /// Отрабатываем мягкой деградацией — если ОС/стек не поддерживает опцию, просто продолжаем без pktinfo.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnablePacketInfo(Socket s)
    {
        // IPv4: IP_PKTINFO → SocketOptionLevel.IP / PacketInformation
        try { s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, optionValue: true); }
        catch { /* не поддерживается/не требуется — мягкая деградация */ }

        // IPv6: IPV6_RECVPKTINFO → SocketOptionLevel.IPv6 / PacketInformation
        try { s.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.PacketInformation, optionValue: true); }
        catch { /* не поддерживается/не требуется — мягкая деградация */ }
    }
}