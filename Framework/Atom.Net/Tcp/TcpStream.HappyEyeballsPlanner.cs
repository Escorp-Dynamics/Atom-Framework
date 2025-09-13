#pragma warning disable CA2000

using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tcp;

/// <summary>
/// Представляет поток чтения и записи по протоколу TCP.
/// </summary>
public sealed partial class TcpStream : NetworkStream
{
    /// <summary>
    /// Планировщик Happy Eyeballs v2 для текущего подключения.
    /// </summary>
    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    private struct HappyEyeballsPlanner(
        TcpStream owner,
        IPAddress[] addresses,
        int port,
        int[] idx6, int count6,
        int[] idx4, int count4,
        int maxConcurrency,
        TimeSpan stepDelay,
        CancellationTokenSource linkedCts,
        TaskCompletionSource<Socket> winner
        )
    {
        private readonly TcpStream owner = owner;
        private readonly IPAddress[] addresses = addresses;
        private readonly int port = port;

        private readonly int[] idx6 = idx6;
        private readonly int[] idx4 = idx4;
        private readonly int count6 = count6;
        private readonly int count4 = count4;

        private readonly int maxConc = maxConcurrency;
        private readonly TimeSpan stepDelay = stepDelay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(250) : stepDelay;
        private readonly CancellationTokenSource linkedCts = linkedCts;
        private readonly TaskCompletionSource<Socket> winner = winner;

        private int inFlight = 0;
        private int p6 = 0;
        private int p4 = 0;
        private bool turnV6 = true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchFirstIpv4IfAny()
        {
            if (count4 > 0) LaunchByIndex(idx4[p4++]);
        }

        /// <summary>
        /// Запускает первый IPv6 немедленно, если он есть.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchFirstIpv6IfAny()
        {
            if (count6 > 0) LaunchByIndex(idx6[p6++]);
        }

        /// <summary>
        /// Запускает первый IPv4 после initialDelay, если он есть.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task LaunchFirstIpv4AfterDelayAsync(TimeSpan initialDelay)
        {
            if (count4 is 0) return;

            if (initialDelay > TimeSpan.Zero)
            {
                try { await Task.Delay(initialDelay, linkedCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }

            LaunchByIndex(idx4[p4++]);
        }

        /// <summary>Запускает первый IPv6 после initialDelay, если он есть.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task LaunchFirstIpv6AfterDelayAsync(TimeSpan initialDelay)
        {
            if (count6 is 0) return;

            if (initialDelay > TimeSpan.Zero)
            {
                try { await Task.Delay(initialDelay, linkedCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }

            LaunchByIndex(idx6[p6++]);
        }

        /// <summary>
        /// Основной ступенчатый цикл: по одному новому адресу на «ступень» с чередованием семейств
        /// и ожиданием свободного слота по лимиту параллелизма.
        /// </summary>
        public async Task RunSchedulerAsync()
        {
            // Пропускаем уже запущенные первые попытки
            var hadV6 = p6 > 0;
            var hadV4 = p4 > 0;

            if (hadV6 && !hadV4)
                turnV6 = false;       // если стартовал только v6 — следующая «ступень» за v4
            else if (!hadV6 && hadV4)
                turnV6 = true;

            while (!linkedCts.IsCancellationRequested)
            {
                // Если обе очереди опустели — выходим
                if (p6 >= count6 && p4 >= count4) return;

                // Ждём свободный слот при превышении лимита параллелизма
                await WaitForConcurrencySlotAsync().ConfigureAwait(false);
                if (linkedCts.IsCancellationRequested) return;

                // Попытка запустить следующий адрес, сохраняя чередование
                if (!TryLaunchNext())
                {
                    // Если по текущему семейству адресов больше нет — попробуем другое
                    if (!TryLaunchNext(forceOtherFamily: true)) return; // адресов больше нет
                }

                // Пауза между «ступенями», как в браузерах
                try { await Task.Delay(stepDelay, linkedCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task WaitForConcurrencySlotAsync()
        {
            // Быстрый путь: есть место
            if (Volatile.Read(ref inFlight) < maxConc) return;

            // Лёгкий спин + «уступи квант», без блокировок/семафоров
            var sw = new SpinWait();

            while (Volatile.Read(ref inFlight) >= maxConc && !linkedCts.IsCancellationRequested)
            {
                sw.SpinOnce(sleep1Threshold: 20);

                if (sw.NextSpinWillYield)
                {
                    try { await Task.Yield(); } catch { /* страховка */ }
                }
            }
        }

        /// <summary>
        /// Запустить следующую попытку согласно текущему чередованию.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryLaunchNext(bool forceOtherFamily = false)
        {
            if (!forceOtherFamily && turnV6 && p6 < count6)
            {
                LaunchByIndex(idx6[p6++]);
                turnV6 = false;
                return true;
            }

            if ((!forceOtherFamily && !turnV6 && p4 < count4) || (forceOtherFamily && p4 < count4))
            {
                LaunchByIndex(idx4[p4++]);
                turnV6 = true;
                return true;
            }

            if (forceOtherFamily) return default;

            // Если «текущего» семейства не осталось — пробуем другое без переключения turn
            if (p6 < count6)
            {
                LaunchByIndex(idx6[p6++]);
                return true;
            }

            if (p4 < count4)
            {
                LaunchByIndex(idx4[p4++]);
                return true;
            }

            return default;
        }

        /// <summary>
        /// Фактический запуск одной попытки (fire-and-forget).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LaunchByIndex(int index)
        {
            Interlocked.Increment(ref inFlight);
            _ = LaunchAttemptAsync(addresses[index]);
        }

        /// <summary>
        /// Подключение к одному адресу с пер-попыточным таймаутом и сообщением победителя.
        /// </summary>
        private async Task LaunchAttemptAsync(IPAddress ip)
        {
            Socket? s = default;

            try
            {
                if (linkedCts.IsCancellationRequested) return;

                s = owner.CreateAttemptSocket(ip);
                using var attempt = owner.CreateAttemptCts(linkedCts.Token);

                await s.ConnectAsync(new IPEndPoint(ip, port), attempt.Token).ConfigureAwait(false);

                // Победитель
                if (winner.TrySetResult(s))
                {
                    s = null;               // владение передано победителю
                    await linkedCts.CancelAsync().ConfigureAwait(false);    // отменяем остальных
                }
            }
            catch (OperationCanceledException)
            {
                // Нормально при отмене после победителя
            }
            catch
            {
                // Молча — поведение браузеров: продолжаем другие попытки
            }
            finally
            {
                if (s is not null) { try { s.Dispose(); } catch { /* страховка */ } }
                Interlocked.Decrement(ref inFlight);
            }
        }
    }
}