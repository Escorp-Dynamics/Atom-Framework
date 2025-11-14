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
        get => field ??= new IPEndPoint(IPAddress.IPv6Any, 0);
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
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ConnectSeqAsync(IPAddress[] addresses, int port, CancellationToken cancellationToken)
    {
        Exception? last = default;

        for (var i = 0; i < addresses.Length; i++)
        {
            try
            {
                await Socket.ConnectAsync(new IPEndPoint(addresses[i], port), cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { last = ex; }
        }

        throw last is SocketException se ? se : new SocketException((int)SocketError.HostUnreachable);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ConnectAlternatingAsync(IPAddress[] addresses, int port, CancellationToken cancellationToken)
    {
        CountFamilies(addresses, out var n6, out var n4);

        // Use heap-allocated arrays here because Span<T> (stackalloc) cannot be
        // stored across await boundaries. We copy indices into arrays and pass
        // their spans to the FillFamilyIndices helper.
        var idx6Arr = n6 > 0 ? new int[n6] : Array.Empty<int>();
        var idx4Arr = n4 > 0 ? new int[n4] : Array.Empty<int>();

        FillFamilyIndices(addresses, idx6Arr.AsSpan(), idx4Arr.AsSpan(), out var i6, out var i4);

        var p6 = 0;
        var p4 = 0;
        var turnV6 = true;
        var initialDelay = Settings.HappyEyeballsDelay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(200) : Settings.HappyEyeballsDelay;
        var stepDelay = Settings.HappyEyeballsStepDelay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(250) : Settings.HappyEyeballsStepDelay;
        var firstV4Delayed = false;

        while (p6 < i6 || p4 < i4)
        {
            if (turnV6 && p6 < i6)
            {
                if (await TryConnectOneAsync(addresses[idx6Arr[p6++]], port, cancellationToken).ConfigureAwait(false)) return;
                turnV6 = false;

                if (!firstV4Delayed) { firstV4Delayed = true; await Task.Delay(initialDelay, cancellationToken).ConfigureAwait(false); }
                else { await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false); }
            }
            else if (!turnV6 && p4 < i4)
            {
                if (await TryConnectOneAsync(addresses[idx4Arr[p4++]], port, cancellationToken).ConfigureAwait(false)) return;
                turnV6 = true;
                await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (p6 < i6) { if (await TryConnectOneAsync(addresses[idx6Arr[p6++]], port, cancellationToken).ConfigureAwait(false)) return; }
                else if (p4 < i4) { if (await TryConnectOneAsync(addresses[idx4Arr[p4++]], port, cancellationToken).ConfigureAwait(false)) return; }
            }
        }

        throw new SocketException((int)SocketError.HostUnreachable);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<bool> TryConnectOneAsync(IPAddress ip, int port, CancellationToken cancellationToken)
    {
        try
        {
            await Socket.ConnectAsync(new IPEndPoint(ip, port), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return default; }
    }

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
            else // IPv4
            {
                var n4 = Socket.ReceiveMessageFrom(buffer, ref flags, ref ep, out var pi4);
                if (n4 >= 0) LastPacketInfo = new UdpPacketInfo(pi4.Address, pi4.Interface);
                return n4;
            }
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

        // IPv4 pktinfo:
        if (res.PacketInformation.Address is not null && !res.PacketInformation.Address.Equals(IPAddress.None))
        {
            LastPacketInfo = new UdpPacketInfo(res.PacketInformation.Address, res.PacketInformation.Interface);
        }
        // IPv6 pktinfo доступен начиная с свежих версий (если свойство присутствует).
        else if (res.PacketInformation.Address is not null)
        {
            LastPacketInfo = new UdpPacketInfo(res.PacketInformation.Address, res.PacketInformation.Interface);
        }
        else
        {
            LastPacketInfo = default; // мягкая деградация (pktinfo недоступен)
        }

        return res.ReceivedBytes;
    }

    /// <summary>
    /// Подключается к <paramref name="host"/>:<paramref name="port"/> (connected-режим).
    /// Для нескольких адресов — «чередование семейств» (v6/v4) без параллелизма.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var cts = (Settings.AttemptTimeout > TimeSpan.Zero)
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : default;

        cts?.CancelAfter(Settings.AttemptTimeout);
        var ct = cts?.Token ?? cancellationToken;

        var addresses = IPAddress.TryParse(host, out var parsed)
            ? [parsed]
            : await ResolveHostAsync(host, ct).ConfigureAwait(false);

        if (addresses.Length is 0) throw new SocketException((int)SocketError.HostNotFound);

        if (!Settings.UseHappyEyeballsAlternating || !HasBothFamilies(addresses))
            await ConnectSeqAsync(addresses, port, ct).ConfigureAwait(false);
        else
            await ConnectAlternatingAsync(addresses, port, ct).ConfigureAwait(false);

        TrySetIpv4Dscp(Socket, Settings.Dscp, SafeRemoteAddressFamily);
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