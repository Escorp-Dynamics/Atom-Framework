#pragma warning disable CA2215

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

using Stream = Atom.IO.Stream;

namespace Atom.Net;

/// <summary>
/// Представляет базовую реализацию сетевого потока.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="NetworkStream"/>.
/// Сокет должен быть создан и (для TCP) подключён к удалённой стороне.
/// </remarks>
/// <param name="socket">Экземпляр сокета, которым будет управлять поток.</param>
/// <param name="ownsSocket">Если <c>true</c>, сокет будет закрыт при <see cref="Dispose(bool)"/></param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public abstract class NetworkStream([NotNull] Socket socket, bool ownsSocket) : Stream
{
    /// <summary>
    /// Владелец сокета. Если true, сокет будет закрыт при Dispose().
    /// </summary>
    private readonly bool ownsSocket = ownsSocket;

    /// <summary>
    /// Флажки завершения работы: бит0=Rx закрыт, бит1=Tx закрыт.
    /// </summary>
    private int shutdownMask; // 0..3

    /// <summary>
    /// Флаг утилизации потока (0 — жив, 1 — Dispose() вызван).
    /// </summary>
    private bool isDisposed;

    /// <summary>
    /// Требовать ли «атомарной» синхронной записи (всё или исключение).
    /// </summary>
    protected virtual bool RequireAtomicSyncSend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => default;
    }

    /// <summary>
    /// Обрабатывать ли <see cref="SocketError.WouldBlock"/> как «временную недоступность данных» и возвращать 0.
    /// </summary>
    protected virtual bool TreatWouldBlockAsZero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => true;
    }

    /// <summary>
    /// Флаги чтения по умолчанию.
    /// </summary>
    protected virtual SocketFlags ReadFlags
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => SocketFlags.None;
    }

    /// <summary>
    /// Флаги записи по умолчанию.
    /// </summary>
    protected virtual SocketFlags WriteFlags
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => SocketFlags.None;
    }

    /// <summary>
    /// Безопасно определяет семейство удалённой конечной точки сокета.
    /// </summary>
    /// <returns>
    /// <see cref="AddressFamily"/> удалённой конечной точки, либо <see cref="Socket.AddressFamily"/> как запасной вариант,
    /// если удалённая точка недоступна (например, сокет ещё не подключён).
    /// </returns>
    /// <remarks>
    /// Метод не генерирует исключений и пригоден для горячих путей (используется при применении DSCP/ToS).
    /// </remarks>
    protected AddressFamily SafeRemoteAddressFamily
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            try { return (Socket.RemoteEndPoint as IPEndPoint)?.AddressFamily ?? Socket.AddressFamily; }
            catch { return Socket.AddressFamily; }
        }
    }

    /// <inheritdoc/>
    public sealed override bool CanRead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !Volatile.Read(ref isDisposed);
    }

    /// <inheritdoc/>
    public sealed override bool CanWrite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !Volatile.Read(ref isDisposed);
    }

    /// <summary>
    /// Локальная конечная точка (проксируется из сокета).
    /// </summary>
    public virtual EndPoint? LocalEndPoint
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            try
            {
                return Socket.LocalEndPoint;
            }
            catch
            {
                return default;
            }
        }
    }

    /// <summary>
    /// Удалённая конечная точка (проксируется из сокета).
    /// </summary>
    public virtual EndPoint? RemoteEndPoint
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            try
            {
                return Socket.RemoteEndPoint;
            }
            catch
            {
                return default;
            }
        }
    }

    /// <summary>
    /// Признак полузакрытия чтения.
    /// </summary>
    public bool IsReceiveShutdown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Volatile.Read(ref shutdownMask) & 0b01) is not 0;
    }

    /// <summary>
    /// Признак полузакрытия записи.
    /// </summary>
    public bool IsSendShutdown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Volatile.Read(ref shutdownMask) & 0b10) is not 0;
    }

    /// <summary>
    /// Связанный с потоком экземпляр <see cref="System.Net.Sockets.Socket"/>.
    /// </summary>
    public Socket Socket
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref field);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected set => Volatile.Write(ref field, value);
    } = socket;

    /// <summary>
    /// Определяет, установлено ли соединение с сервером.
    /// </summary>
    public virtual bool IsConnected
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            try { return Socket.Connected && Socket.RemoteEndPoint is not null; }
            catch { return default; }
        }
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="NetworkStream"/>.
    /// Сокет должен быть создан и (для TCP) подключён к удалённой стороне.
    /// </summary>
    /// <param name="socket">Экземпляр сокета, которым будет управлять поток.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected NetworkStream([NotNull] Socket socket) : this(socket, true) { }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        try
        {
            Socket.Shutdown(SocketShutdown.Both);
        }
        catch { /* ignore */ }
        finally
        {
            if (ownsSocket)
            {
                try { Socket.Dispose(); } catch { /* ignore */ }
            }
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length is 0) return default;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        // Если чтение полузакрыто — как в NetworkStream, возвращаем 0 (EOF).
        if (IsReceiveShutdown) return default;

        var n = Socket.Receive(buffer, ReadFlags, out var err);

        if (err is SocketError.Success) return n; // 0 для закрытия соединения
        if (TreatWouldBlockAsZero && err is SocketError.WouldBlock) return default;

        throw new SocketException((int)err);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length is 0) return default;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (IsReceiveShutdown) return default;

        int n;

        try
        {
            n = await Socket.ReceiveAsync(buffer, ReadFlags, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException se) when (TreatWouldBlockAsZero && se.SocketErrorCode is SocketError.WouldBlock)
        {
            return default;
        }

        return n; // 0 — закрыто
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length is 0) return;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (IsSendShutdown) throw new IOException("Не удается выполнить запись: отправляющая сторона была отключена");

        var n = Socket.Send(buffer, WriteFlags, out var err);

        if (err is not SocketError.Success) throw new SocketException((int)err);
        if (RequireAtomicSyncSend && n != buffer.Length) throw new InvalidOperationException("Частичная отправка запрещена в соответствии с транспортной политикой");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length is 0) return;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (IsSendShutdown) throw new IOException("Не удается выполнить запись: отправляющая сторона была отключена");

        var n = await Socket.SendAsync(buffer, WriteFlags, cancellationToken).ConfigureAwait(false);

        if (RequireAtomicSyncSend && n != buffer.Length) throw new InvalidOperationException("Частичная отправка запрещена в соответствии с транспортной политикой");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override void Flush() { /* без пользовательских буферов */ }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask DisposeAsync()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Полузакрытие канала на чтение/запись.
    /// </summary>
    /// <param name="kind">Способ закрытия.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Shutdown(SocketShutdown kind)
    {
        if (Volatile.Read(ref isDisposed)) return;

        try
        {
            switch (kind)
            {
                case SocketShutdown.Receive:
                    if ((Interlocked.Or(ref shutdownMask, 0b01) & 0b01) is 0)
                        Socket.Shutdown(SocketShutdown.Receive);
                    break;
                case SocketShutdown.Send:
                    if ((Interlocked.Or(ref shutdownMask, 0b10) & 0b10) is 0)
                        Socket.Shutdown(SocketShutdown.Send);
                    break;
                case SocketShutdown.Both:
                    if (Interlocked.Exchange(ref shutdownMask, 0b11) is not 0b11)
                        Socket.Shutdown(SocketShutdown.Both);
                    break;
            }
        }
        catch
        {
            // Игнорируем ошибки при полузакрытии (соединение могло уже быть закрыто).
        }
    }

    /// <summary>
    /// Полузакрытие канала на чтение/запись.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Shutdown() => Shutdown(SocketShutdown.Both);

    /// <summary>
    /// Асинхронно разрешает DNS-имя хоста в набор IP-адресов.
    /// </summary>
    /// <param name="host">Хост для разрешения. Допускается как доменное имя, так и строковое представление IP-адреса.</param>
    /// <param name="cancellationToken">Токен отмены операции. При отмене будет выброшено исключение <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// Массив IP-адресов, возвращённый системным резолвером.
    /// </returns>
    /// <exception cref="OperationCanceledException">Операция была отменена через <paramref name="cancellationToken"/>.</exception>
    /// <exception cref="SocketException">Не удалось разрешить <paramref name="host"/> (например, <see cref="SocketError.HostNotFound"/>).</exception>
    /// <remarks>
    /// Метод работает без дополнительных аллокаций, кроме возвращаемого массива адресов,
    /// и мягко деградирует в случае внутренних ошибок резолвера.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static async ValueTask<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        try { return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { throw new SocketException((int)SocketError.HostNotFound); }
    }

    /// <summary>
    /// Проверяет, присутствуют ли одновременно адреса IPv4 и IPv6 в заданном наборе.
    /// </summary>
    /// <param name="addresses">Набор адресов для анализа.</param>
    /// <returns>
    /// <see langword="true"/>, если в <paramref name="addresses"/> есть и IPv4, и IPv6; иначе <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Используется при выборе стратегии подключения (alternating/HE-подобная логика) для мимикрии браузеров.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool HasBothFamilies(ReadOnlySpan<IPAddress> addresses)
    {
        var v4 = false;
        var v6 = false;

        for (var i = 0; i < addresses.Length; i++)
        {
            var af = addresses[i].AddressFamily;

            if (af is AddressFamily.InterNetworkV6)
                v6 = true;
            else if (af is AddressFamily.InterNetwork)
                v4 = true;

            if (v4 && v6) return true;
        }

        return default;
    }

    /// <summary>
    /// Подсчитывает количество адресов каждого семейства (IPv4/IPv6).
    /// </summary>
    /// <param name="addresses">Набор адресов для анализа.</param>
    /// <param name="v6">Число адресов IPv6 на выходе.</param>
    /// <param name="v4">Число адресов IPv4 на выходе.</param>
    /// <remarks>
    /// Функция выполняется без дополнительных аллокаций и используется при подготовке индексных таблиц адресов.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void CountFamilies(ReadOnlySpan<IPAddress> addresses, out int v6, out int v4)
    {
        v6 = 0;
        v4 = 0;

        for (var i = 0; i < addresses.Length; i++)
        {
            var af = addresses[i].AddressFamily;
            if (af is AddressFamily.InterNetworkV6) v6++;
            else if (af is AddressFamily.InterNetwork) v4++;
        }
    }

    /// <summary>
    /// Формирует индексные таблицы позиций адресов IPv6 и IPv4 в исходном массиве.
    /// </summary>
    /// <param name="addresses">Исходный массив адресов.</param>
    /// <param name="idxV6">
    /// Целевой буфер индексов для адресов IPv6 (длина должна быть не меньше количества IPv6-адресов).
    /// </param>
    /// <param name="idxV4">
    /// Целевой буфер индексов для адресов IPv4 (длина должна быть не меньше количества IPv4-адресов).
    /// </param>
    /// <param name="iV6">Фактически заполненное количество индексов IPv6.</param>
    /// <param name="iV4">Фактически заполненное количество индексов IPv4.</param>
    /// <remarks>
    /// Буферы <paramref name="idxV6"/> и <paramref name="idxV4"/> заполняются без дополнительных аллокаций.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void FillFamilyIndices([NotNull] IPAddress[] addresses, Span<int> idxV6, Span<int> idxV4, out int iV6, out int iV4)
    {
        iV6 = 0;
        iV4 = 0;

        for (var i = 0; i < addresses.Length; i++)
        {
            var af = addresses[i].AddressFamily;
            if (af is AddressFamily.InterNetworkV6) idxV6[iV6++] = i;
            else if (af is AddressFamily.InterNetwork) idxV4[iV4++] = i;
        }
    }

    /// <summary>
    /// Пытается привязать сокет к локальной конечной точке с мягкой деградацией.
    /// </summary>
    /// <param name="s">Сокет, который необходимо привязать.</param>
    /// <param name="ep">Желаемая локальная конечная точка. Если порт равен 0 или отрицательный, будет выбран произвольный свободный порт.
    /// Для IPv6 link-local адресов убедитесь, что <see cref="IPAddress.ScopeId"/> установлен корректно.</param>
    /// <remarks>
    /// Любые ошибки привязки подавляются для сохранения поведения браузера (браузеры часто делегируют выбор ОС).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void TryBindLocalEndpoint([NotNull] Socket s, IPEndPoint? ep)
    {
        if (ep is null) return;
        var port = ep.Port > 0 ? ep.Port : 0;
        try { s.Bind(new IPEndPoint(ep.Address, port)); } catch { /* мягкая деградация */ }
    }

    /// <summary>
    /// Применяет размеры буферов приёма и отправки к сокету.
    /// </summary>
    /// <param name="s">Сокет.</param>
    /// <param name="receiveBufferSize">Желаемый размер буфера приёма в байтах. Если &lt;= 0, изменение не выполняется.</param>
    /// <param name="sendBufferSize">Желаемый размер буфера отправки в байтах. Если &lt;= 0, изменение не выполняется.</param>
    /// <remarks>
    /// Изменения выполняются без генерации исключений на платформах, где установка недоступна — с мягкой деградацией.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void ApplyBuffers([NotNull] Socket s, int receiveBufferSize, int sendBufferSize)
    {
        if (receiveBufferSize > 0) s.ReceiveBufferSize = receiveBufferSize;
        if (sendBufferSize > 0) s.SendBufferSize = sendBufferSize;
    }

    /// <summary>
    /// Устанавливает TTL (IPv4) и HopLimit (IPv6) для сокета с учётом кроссплатформенности.
    /// </summary>
    /// <param name="s">Сокет.</param>
    /// <param name="ttl">Значение TTL/HopLimit. Если равно 0 — изменения не применяются.</param>
    /// <remarks>
    /// Попытка установки выполняется для обоих семейств; ошибки подавляются, чтобы сохранить поведение браузера.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void ApplyTtlHopLimit([NotNull] Socket s, byte ttl)
    {
        if (ttl is 0) return;

        try { s.Ttl = ttl; } catch { /* страховка */ }
        try { s.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.HopLimit, ttl); } catch { /* страховка */ }
    }

    /// <summary>
    /// Пытается установить поле DSCP (ToS) для IPv4-трафика.
    /// </summary>
    /// <param name="s">Сокет.</param>
    /// <param name="dscp">Значение DSCP (младшие 6 бит). Будет автоматически сдвинуто в ToS (DSCP &lt;&lt; 2).</param>
    /// <param name="remoteAf">Семейство удалённой конечной точки. Установка выполняется только при <see cref="AddressFamily.InterNetwork"/> (IPv4).</param>
    /// <remarks>
    /// На IPv6 поведение TClass неоднородно между платформами, поэтому метод намеренно ограничен IPv4.
    /// Все ошибки подавляются (мягкая деградация), что соответствует практике браузеров.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void TrySetIpv4Dscp([NotNull] Socket s, byte dscp, AddressFamily remoteAf)
    {
        if (dscp is 0 || remoteAf is not AddressFamily.InterNetwork) return;

        try
        {
            var tos = (dscp & 0x3F) << 2; // DSCP << 2
            s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, tos);
        }
        catch { /* страховка */ }
    }
}