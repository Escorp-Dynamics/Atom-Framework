using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tcp;

/// <summary>
/// Содержит connect-оркестрацию TCP, включая Happy Eyeballs и пер-попыточные таймауты.
/// </summary>
public sealed partial class TcpStream
{
    /// <summary>
    /// Последовательный перебор адресов с пер-попыточным таймаутом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ConnectSeqAsync(IPAddress[] addresses, int port, CancellationToken cancellationToken)
    {
        SocketException? last = default;

        for (var i = 0; i < addresses.Length; i++)
        {
            try
            {
                using var attempt = CreateAttemptCts(cancellationToken);
                await Socket.ConnectAsync(new IPEndPoint(addresses[i], port), attempt.Token).ConfigureAwait(false);
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

    /// <summary>
    /// Чередование семейств в духе Happy Eyeballs (без параллелизма): v6,v4,v6,v4… с паузами между «ступенями».
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ConnectAlternatingAsync(IPAddress[] addresses, int port, CancellationToken cancellationToken)
    {
        CountFamilies(addresses, out var n6, out var n4);
        var idx6 = ArrayPool<int>.Shared.Rent(n6);
        var idx4 = ArrayPool<int>.Shared.Rent(n4);

        try
        {
            FillFamilyIndices(addresses, idx6, idx4, out var i6, out var i4);
            var initialDelay = Settings.HappyEyeballsDelay <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(200)
                : Settings.HappyEyeballsDelay;
            var stepDelay = Settings.HappyEyeballsStepDelay <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(250)
                : Settings.HappyEyeballsStepDelay;

            await RunAlternatingConnectLoop(addresses, idx6, idx4, i6, i4, port, initialDelay, stepDelay, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(idx6);
            ArrayPool<int>.Shared.Return(idx4);
        }
    }

    private async ValueTask RunAlternatingConnectLoop(IPAddress[] addresses, int[] idx6, int[] idx4, int i6, int i4, int port, TimeSpan initialDelay, TimeSpan stepDelay, CancellationToken cancellationToken)
    {
        var p6 = 0;
        var p4 = 0;
        var turnV6 = true;
        var firstV4Delayed = false;

        while (p6 < i6 || p4 < i4)
        {
            if (await TryHandleIpv6TurnAsync(addresses, idx6, p6, i6, port, turnV6, firstV4Delayed, initialDelay, stepDelay, cancellationToken).ConfigureAwait(false) is { handled: true, completed: true })
                return;

            if (turnV6 && p6 < i6)
            {
                p6++;
                turnV6 = false;
                firstV4Delayed = true;
                continue;
            }

            if (await TryHandleIpv4TurnAsync(addresses, idx4, p4, i4, port, turnV6, stepDelay, cancellationToken).ConfigureAwait(false))
                return;

            if (!turnV6 && p4 < i4)
            {
                p4++;
                turnV6 = true;
                continue;
            }

            (p6, p4) = await RunFinalFamilyConnect(addresses, idx6, idx4, p6, i6, p4, i4, port, cancellationToken).ConfigureAwait(false);
        }

        throw new SocketException((int)SocketError.HostUnreachable);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<(bool handled, bool completed)> TryHandleIpv6TurnAsync(IPAddress[] addresses, int[] indices, int position, int count, int port, bool turnV6, bool firstV4Delayed, TimeSpan initialDelay, TimeSpan stepDelay, CancellationToken cancellationToken)
    {
        if (!turnV6 || position >= count) return default;
        if (await TryConnectOneAsync(addresses[indices[position]], port, cancellationToken).ConfigureAwait(false)) return (true, true);

        if (!firstV4Delayed)
        {
            await Task.Delay(initialDelay, cancellationToken).ConfigureAwait(false);
            return (true, false);
        }

        await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false);
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

    private async ValueTask<(int, int)> RunFinalFamilyConnect(IPAddress[] addresses, int[] idx6, int[] idx4, int p6, int i6, int p4, int i4, int port, CancellationToken cancellationToken)
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
    private async ValueTask<bool> TryConnectOneAsync(IPAddress ip, int port, CancellationToken ct)
    {
        try
        {
            using var attempt = CreateAttemptCts(ct);
            await Socket.ConnectAsync(new IPEndPoint(ip, port), attempt.Token).ConfigureAwait(false);
            return true;
        }
        catch (SocketException ex)
        {
            if (!IsExpectedConnectFailure(ex)) throw;
            // Ожидаемый сетевой отказ одной попытки: даём Happy Eyeballs продолжить другие адреса.
            return default;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask DelayBeforeTlsAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsLinux() && Settings.UseFastOpen) return;

        var delay = Settings.Delay;
        if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Socket CreateAttemptSocket(IPAddress remoteIp)
    {
        var s = new Socket(remoteIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = Settings.IsNagleDisabled,
        };

        if (Settings.ReceiveBufferSize > 0) s.ReceiveBufferSize = Settings.ReceiveBufferSize;
        if (Settings.SendBufferSize > 0) s.SendBufferSize = Settings.SendBufferSize;

        if (Settings.TimeToLive is not 0)
        {
            try { s.Ttl = Settings.TimeToLive; } catch { /* Платформа может не принять TTL до Connect. */ }
            try { s.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.HopLimit, Settings.TimeToLive); } catch { /* Платформа может не поддерживать HopLimit на этом сокете. */ }
        }

        SetTcpMaxSegmentSizeIfSupported(s, Settings.MaxSegmentSize);
        EnableTcpFastOpenIfSupported(s, Settings.UseFastOpen);
        TryBindLocalEndpoint(s, Settings.LocalEndPoint);

        return s;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private CancellationTokenSource CreateAttemptCts(in CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timeout = Settings.AttemptTimeout;

        if (timeout > TimeSpan.Zero) cts.CancelAfter(timeout);
        return cts;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ConnectHappyEyeballsAsync(IPAddress[] addresses, int port, byte preferredFamily, CancellationToken cancellationToken)
    {
        CountFamilies(addresses, out var n6, out var n4);

        if (n6 is 0 || n4 is 0 || !Settings.UseHappyEyeballsAlternating)
        {
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

            var initialDelay = NormalizeHappyEyeballsDelay(Settings.HappyEyeballsDelay, 200);
            var stepDelay = NormalizeHappyEyeballsDelay(Settings.HappyEyeballsStepDelay, 250);
            var maxConc = NormalizeMaxConcurrency(Settings.HappyEyeballsMaxConcurrency);

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
                winner: winnerTcs);

            var firstOtherTask = LaunchInitialHappyEyeballsTasks(plan, preferredFamily, initialDelay);
            var schedulerTask = RunHappyEyeballsSchedulerAsync(plan, firstOtherTask);
            var all = Task.WhenAll(firstOtherTask, schedulerTask);

            await EnsureHappyEyeballsWinnerAsync(winnerTcs.Task, all).ConfigureAwait(false);

            var winner = await winnerTcs.Task.ConfigureAwait(false);
            AdoptWinnerSocket(winner);
            PostAdoptConfigureSocket();
            await all.ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(idx6);
            ArrayPool<int>.Shared.Return(idx4);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TimeSpan NormalizeHappyEyeballsDelay(TimeSpan value, int defaultMilliseconds)
        => value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(defaultMilliseconds) : value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NormalizeMaxConcurrency(int value) => value <= 0 ? 2 : value;

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
    private static async Task LaunchInitialHappyEyeballsTasks(HappyEyeballsPlanner plan, byte preferredFamily, TimeSpan initialDelay)
    {
        if (preferredFamily is 4)
        {
            plan.LaunchFirstIpv4IfAny();
            await plan.LaunchFirstIpv6AfterDelayAsync(initialDelay).ConfigureAwait(false);
            return;
        }

        plan.LaunchFirstIpv6IfAny();
        await plan.LaunchFirstIpv4AfterDelayAsync(initialDelay).ConfigureAwait(false);
    }

    [SuppressMessage("Reliability", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "Happy Eyeballs scheduler intentionally coordinates the externally-created initial launch task.")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async Task RunHappyEyeballsSchedulerAsync(HappyEyeballsPlanner plan, Task initialLaunchTask)
    {
        await initialLaunchTask.ConfigureAwait(false);
        await plan.RunSchedulerAsync().ConfigureAwait(false);
    }

    [SuppressMessage("Reliability", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "Happy Eyeballs intentionally coordinates externally-created tasks and does not resume on a captured context.")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async ValueTask EnsureHappyEyeballsWinnerAsync(Task winnerTask, Task allTasks)
    {
        if (winnerTask.IsCompletedSuccessfully) return;

        var completed = await Task.WhenAny(winnerTask, allTasks).ConfigureAwait(false);
        if (winnerTask.IsCompletedSuccessfully) return;
        if (!ReferenceEquals(completed, winnerTask)) throw new SocketException((int)SocketError.HostUnreachable);

        await winnerTask.ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdoptWinnerSocket([NotNull] Socket winner)
    {
        ArgumentNullException.ThrowIfNull(winner);

        var old = Socket;
        Socket = winner;

        if (!ReferenceEquals(old, winner))
        {
            try { old?.Dispose(); } catch { /* Старый сокет уже мог быть закрыт конкурентным путём очистки. */ }
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

        if (HasBothFamilies(addresses))
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

        ApplyTrafficClass(Socket, Settings.Dscp, useEcn: false, SafeRemoteAddressFamily);
        HappyEyeballsMemory.Remember(host, SafeRemoteAddressFamily);
        await DelayBeforeTlsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Подключается к <paramref name="host"/>:<paramref name="port"/> без токена отмены.
    /// </summary>
    /// <param name="host">Имя хоста.</param>
    /// <param name="port">Номер порта.</param>
    /// <exception cref="SocketException">Если ни один адрес не достигнут.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask ConnectAsync(string host, int port)
    {
        if (Settings.ConnectTimeout <= TimeSpan.Zero || Settings.ConnectTimeout == Timeout.InfiniteTimeSpan)
        {
            await ConnectAsync(host, port, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        using var timeoutCts = new CancellationTokenSource(Settings.ConnectTimeout);
        await ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
    }
}