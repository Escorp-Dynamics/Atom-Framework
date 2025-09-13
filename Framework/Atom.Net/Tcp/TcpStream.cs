#pragma warning disable CA2000

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
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
    public TcpStream(Socket socket, in TcpSettings settings) : this(socket, settings, true) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="TcpStream"/>.
    /// </summary>
    /// <remarks>
    /// Используем IPv6-сокет с <c>DualMode=true</c>, чтобы уметь ходить как к IPv6, так и к IPv4 узлам одним инстансом.
    /// Все настройки, не зависящие от адресного семейства удалённого узла, применяются сразу.
    /// Адресоспецифичные (например DSCP для IPv4) — после успешного Connect.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TcpStream(in TcpSettings settings) : this(CreateInitialSocket(settings.IsNagleDisabled), settings, true) => PreConfigureUnboundSocket();

    /// <summary>
    /// Последовательный перебор адресов с пер-попыточным таймаутом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ConnectSeqAsync(IPAddress[] addresses, int port, CancellationToken cancellationToken)
    {
        Exception? last = default;

        for (var i = 0; i < addresses.Length; i++)
        {
            try
            {
                using var attempt = CreateAttemptCts(cancellationToken);
                await Socket.ConnectAsync(new IPEndPoint(addresses[i], port), attempt.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { last = ex; }
        }

        throw last is SocketException se ? se : new SocketException((int)SocketError.HostUnreachable);
    }

    /// <summary>
    /// Чередование семейств в духе Happy Eyeballs (без параллелизма): v6,v4,v6,v4… с паузами между «ступенями».
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ConnectAlternatingAsync(IPAddress[] addresses, int port, CancellationToken cancellationToken)
    {
        CountFamilies(addresses, out var n6, out var n4); // разбивка по семействам
        var idx6 = ArrayPool<int>.Shared.Rent(n6);
        var idx4 = ArrayPool<int>.Shared.Rent(n4);

        try
        {
            FillFamilyIndices(addresses, idx6, idx4, out var i6, out var i4); // индексы в порядке DNS

            var initialDelay = Settings.HappyEyeballsDelay;
            if (initialDelay <= TimeSpan.Zero) initialDelay = TimeSpan.FromMilliseconds(200);

            var stepDelay = Settings.HappyEyeballsStepDelay;
            if (stepDelay <= TimeSpan.Zero) stepDelay = TimeSpan.FromMilliseconds(250);

            var p6 = 0;
            var p4 = 0;
            var turnV6 = true;
            var firstV4Delayed = false;

            while (p6 < i6 || p4 < i4)
            {
                if (turnV6 && p6 < i6)
                {
                    if (await TryConnectOneAsync(addresses[idx6[p6++]], port, cancellationToken).ConfigureAwait(false)) return;

                    turnV6 = false;

                    if (!firstV4Delayed)
                    {
                        firstV4Delayed = true;
                        await Task.Delay(initialDelay, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (!turnV6 && p4 < i4)
                {
                    if (await TryConnectOneAsync(addresses[idx4[p4++]], port, cancellationToken).ConfigureAwait(false)) return;
                    turnV6 = true;
                    await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Если одна очередь опустела — идём по оставшейся без задержек
                    if (p6 < i6)
                    {
                        if (await TryConnectOneAsync(addresses[idx6[p6++]], port, cancellationToken).ConfigureAwait(false)) return;
                    }
                    else if (p4 < i4)
                    {
                        if (await TryConnectOneAsync(addresses[idx4[p4++]], port, cancellationToken).ConfigureAwait(false)) return;
                    }
                }
            }

            throw new SocketException((int)SocketError.HostUnreachable);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(idx6);
            ArrayPool<int>.Shared.Return(idx4);
        }
    }

    /// <summary>
    /// Одна попытка Connect с учётом пер-попыточного таймаута.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<bool> TryConnectOneAsync(IPAddress ip, int port, CancellationToken ct)
    {
        try
        {
            using var attempt = CreateAttemptCts(ct);
            await Socket.ConnectAsync(new IPEndPoint(ip, port), attempt.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return default; }
    }

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

        // Параметры, которые корректны только ДО Connect
        if (!IsConnected)
        {
            SetTcpMaxSegmentSizeIfSupported(Socket, Settings.MaxSegmentSize);
            EnableTcpFastOpenIfSupported(Socket, Settings.UseFastOpen);
            TryBindLocalEndpoint(Socket, Settings.LocalEndPoint);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask DelayBeforeTlsAsync(CancellationToken cancellationToken)
    {
        // На Linux с TFO_CONNECT задержку не делаем — повторяем прежнюю политику
        if (OperatingSystem.IsLinux() && Settings.UseFastOpen) return;
        var delay = Settings.Delay;
        if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Создать новый «пер-попыточный» сокет под конкретное адресное семейство с общими настройками.
    /// Важно: keep-alive настраиваем в <see cref="PostAdoptConfigureSocket"/> после «усыновления».
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Socket CreateAttemptSocket(IPAddress remoteIp)
    {
        var s = new Socket(remoteIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = Settings.IsNagleDisabled
        };

        // Буферы
        if (Settings.ReceiveBufferSize > 0) s.ReceiveBufferSize = Settings.ReceiveBufferSize;
        if (Settings.SendBufferSize > 0) s.SendBufferSize = Settings.SendBufferSize;

        // TTL/HopLimit (оба пути, ядро применит корректный)
        if (Settings.TimeToLive is not 0)
        {
            try { s.Ttl = Settings.TimeToLive; } catch { /* страховка */ }
            try { s.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.HopLimit, Settings.TimeToLive); } catch { /* страховка */ }
        }

        // MSS/TFO — строго до Connect
        SetTcpMaxSegmentSizeIfSupported(s, Settings.MaxSegmentSize);
        EnableTcpFastOpenIfSupported(s, Settings.UseFastOpen);

        // Локальный bind (мягкая деградация как в браузерах)
        TryBindLocalEndpoint(s, Settings.LocalEndPoint);

        return s;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private CancellationTokenSource CreateAttemptCts(in CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var t = Settings.AttemptTimeout;
        if (t > TimeSpan.Zero) cts.CancelAfter(t);
        return cts;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryEnableKeepAlive()
    {
        try { Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch { /* страховка */ }

        if (Settings.KeepAlivePingDelay == Timeout.InfiniteTimeSpan) return;

        var timeSec = Math.Max(1, (int)Settings.KeepAlivePingDelay.TotalSeconds);
        var intervalSec = Math.Max(1, (int)Settings.KeepAlivePingTimeout.TotalSeconds);

        // Кросс-платформенные попытки (Linux/macOS)
        try { Socket.SetSocketOption(SocketOptionLevel.Tcp, (SocketOptionName)0x10 /* TcpKeepAliveTime */, timeSec); } catch { /* страховка */ }
        try { Socket.SetSocketOption(SocketOptionLevel.Tcp, (SocketOptionName)0x12 /* TcpKeepAliveInterval */, intervalSec); } catch { /* страховка */ }
        try { Socket.SetSocketOption(SocketOptionLevel.Tcp, (SocketOptionName)0x11 /* TcpKeepAliveRetryCount */, 5); } catch { /* страховка */ }

        // Windows fallback
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
        try { Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, false); } catch { /* страховка */ }
    }

    /// <summary>
    /// HEv2: конкурентные попытки с чередованием семейств, ступенями и лимитом параллелизма.
    /// Победивший сокет «усыновляется» текущим TcpStream (замена <see cref="Socket"/>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ConnectHappyEyeballsAsync(IPAddress[] addresses, int port, byte preferredFamily, CancellationToken cancellationToken)
    {
        // Если одно семейство или HEv2 отключён — используем уже реализованные стратегии
        CountFamilies(addresses, out var n6, out var n4);

        if (n6 is 0 || n4 is 0 || !Settings.UseHappyEyeballsAlternating)
        {
            // последовательно/чередованием (существующая логика)
            if (n6 is 0 || n4 is 0)
                await ConnectSeqAsync(addresses, port, cancellationToken).ConfigureAwait(false);
            else
                await ConnectAlternatingAsync(addresses, port, cancellationToken).ConfigureAwait(false);

            return;
        }

        var idx6 = ArrayPool<int>.Shared.Rent(n6);
        var idx4 = ArrayPool<int>.Shared.Rent(n4);

        try
        {
            FillFamilyIndices(addresses, idx6, idx4, out var i6, out var i4);

            var initialDelay = Settings.HappyEyeballsDelay;
            if (initialDelay <= TimeSpan.Zero) initialDelay = TimeSpan.FromMilliseconds(200);

            var stepDelay = Settings.HappyEyeballsStepDelay;
            if (stepDelay <= TimeSpan.Zero) stepDelay = TimeSpan.FromMilliseconds(250);

            var maxConc = Settings.HappyEyeballsMaxConcurrency;
            if (maxConc <= 0) maxConc = 2;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var winnerTcs = new TaskCompletionSource<Socket>(TaskCreationOptions.RunContinuationsAsynchronously);

            var plan = new HappyEyeballsPlanner(
                owner: this,
                addresses: addresses,
                port: port,
                idx6: idx6, count6: i6,
                idx4: idx4, count4: i4,
                maxConcurrency: maxConc,
                stepDelay: stepDelay,
                linkedCts: linkedCts,
                winner: winnerTcs
            );

            // Учитываем память: если последний победитель v4 — стартуем v4 сразу, v6 с задержкой.
            Task firstOtherTask;

            if (preferredFamily is 4)
            {
                plan.LaunchFirstIpv4IfAny();
                firstOtherTask = plan.LaunchFirstIpv6AfterDelayAsync(initialDelay);
            }
            else
            {
                plan.LaunchFirstIpv6IfAny();
                firstOtherTask = plan.LaunchFirstIpv4AfterDelayAsync(initialDelay);
            }

            // Основной ступенчатый цикл
            var schedulerTask = plan.RunSchedulerAsync();

            var all = Task.WhenAll(firstOtherTask, schedulerTask);
            var completed = await Task.WhenAny(winnerTcs.Task, all).ConfigureAwait(false);
            if (!ReferenceEquals(completed, winnerTcs.Task)) throw new SocketException((int)SocketError.HostUnreachable);

            var winner = await winnerTcs.Task.ConfigureAwait(false);
            AdoptWinnerSocket(winner);
            PostAdoptConfigureSocket();

            // дождаться остановки планировщика и задержек
            await all.ConfigureAwait(false);

        }
        finally
        {
            ArrayPool<int>.Shared.Return(idx6);
            ArrayPool<int>.Shared.Return(idx4);
        }
    }

    /// <summary>
    /// «Усыновляет» победивший сокет HEv2: все последующие I/O пойдут через него.
    /// Старый сокет безопасно закрывается (если он отличен от нового).
    /// </summary>
    /// <param name="winner">Подключённый сокет-победитель.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdoptWinnerSocket([NotNull] Socket winner)
    {
        ArgumentNullException.ThrowIfNull(winner);

        // Публикуем победителя: последующие Read/Write в NetworkStream пойдут через новый сокет.
        var old = Socket;
        Socket = winner;

        // Закрываем старый сокет, если он не совпадает с победителем.
        if (!ReferenceEquals(old, winner))
        {
            try { old?.Dispose(); } catch { /* страховка */ }
        }
    }

    /// <summary>
    /// Подключается к <paramref name="host"/>:<paramref name="port"/> через уже инициализированный внутренний сокет.
    /// Алгоритм выбора адресов — последовательный с чередованием семейств (HE-подобный), пер-попыточный таймаут учитывается.
    /// После успешного Connect применяются адресоспецифичные опции (например, DSCP для IPv4) и «пауза перед TLS».
    /// </summary>
    /// <param name="host">Имя хоста.</param>
    /// <param name="port">Номер порта.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <exception cref="SocketException">Если ни один адрес не достигнут.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(host, out var ip)
            ? [ip]
            : await ResolveHostAsync(host, cancellationToken).ConfigureAwait(false);

        if (addresses.Length is 0) throw new SocketException((int)SocketError.HostNotFound);

        var hasBothFamilies = HasBothFamilies(addresses);

        if (hasBothFamilies)
        {
            if (Settings.UseHappyEyeballsAlternating)
            {
                var pref = HappyEyeballsMemory.TryGet(host, out var p) ? p : (byte)0;
                await ConnectHappyEyeballsAsync(addresses, port, pref, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ConnectAlternatingAsync(addresses, port, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await ConnectSeqAsync(addresses, port, cancellationToken).ConfigureAwait(false);
        }

        TrySetIpv4Dscp(Socket, Settings.Dscp, SafeRemoteAddressFamily);

        // запоминаем победителя (семейство удалённой точки)
        try
        {
            HappyEyeballsMemory.Remember(host, SafeRemoteAddressFamily);
        }
        catch { /* страховка */ }

        await DelayBeforeTlsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Подключается к <paramref name="host"/>:<paramref name="port"/> через уже инициализированный внутренний сокет.
    /// Алгоритм выбора адресов — последовательный с чередованием семейств (HE-подобный), пер-попыточный таймаут учитывается.
    /// После успешного Connect применяются адресоспецифичные опции (например, DSCP для IPv4) и «пауза перед TLS».
    /// </summary>
    /// <param name="host">Имя хоста.</param>
    /// <param name="port">Номер порта.</param>
    /// <exception cref="SocketException">Если ни один адрес не достигнут.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask ConnectAsync(string host, int port) => ConnectAsync(host, port, CancellationToken.None);

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