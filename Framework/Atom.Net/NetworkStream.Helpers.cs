using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Atom.Net;

public abstract partial class NetworkStream
{
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
            try
            {
                var remoteEndPoint = Socket.RemoteEndPoint as IPEndPoint;
                if (remoteEndPoint is not null) return remoteEndPoint.AddressFamily;
            }
            catch
            {
                // Сокет ещё не подключён или уже разрушен; используем его базовое семейство.
            }

            return Socket.AddressFamily;
        }
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
    /// Определяет, установлено ли соединение с сервером.
    /// </summary>
    public virtual bool IsConnected
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            try
            {
                return Socket.Connected && Socket.RemoteEndPoint is not null;
            }
            catch
            {
                return default;
            }
        }
    }

    /// <summary>
    /// Асинхронно разрешает DNS-имя хоста в набор IP-адресов.
    /// </summary>
    /// <param name="host">Хост для разрешения. Допускается как доменное имя, так и строковое представление IP-адреса.</param>
    /// <param name="cancellationToken">Токен отмены операции. При отмене будет выброшено исключение <see cref="OperationCanceledException"/>.</param>
    /// <returns>Массив IP-адресов, возвращённый системным резолвером.</returns>
    /// <exception cref="OperationCanceledException">Операция была отменена через <paramref name="cancellationToken"/>.</exception>
    /// <exception cref="SocketException">Не удалось разрешить <paramref name="host"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static async ValueTask<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }
    }

    /// <summary>
    /// Проверяет, присутствуют ли одновременно адреса IPv4 и IPv6 в заданном наборе.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool HasBothFamilies(ReadOnlySpan<IPAddress> addresses)
    {
        var v4 = false;
        var v6 = false;

        for (var i = 0; i < addresses.Length; i++)
        {
            var addressFamily = addresses[i].AddressFamily;

            if (addressFamily is AddressFamily.InterNetworkV6)
                v6 = true;
            else if (addressFamily is AddressFamily.InterNetwork)
                v4 = true;

            if (v4 && v6) return true;
        }

        return default;
    }

    /// <summary>
    /// Подсчитывает количество адресов каждого семейства (IPv4/IPv6).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void CountFamilies(ReadOnlySpan<IPAddress> addresses, out int v6, out int v4)
    {
        v6 = 0;
        v4 = 0;

        for (var i = 0; i < addresses.Length; i++)
        {
            var addressFamily = addresses[i].AddressFamily;
            if (addressFamily is AddressFamily.InterNetworkV6) v6++;
            else if (addressFamily is AddressFamily.InterNetwork) v4++;
        }
    }

    /// <summary>
    /// Формирует индексные таблицы позиций адресов IPv6 и IPv4 в исходном массиве.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void FillFamilyIndices([NotNull] IPAddress[] addresses, Span<int> idxV6, Span<int> idxV4, out int iV6, out int iV4)
    {
        iV6 = 0;
        iV4 = 0;

        for (var i = 0; i < addresses.Length; i++)
        {
            var addressFamily = addresses[i].AddressFamily;
            if (addressFamily is AddressFamily.InterNetworkV6) idxV6[iV6++] = i;
            else if (addressFamily is AddressFamily.InterNetwork) idxV4[iV4++] = i;
        }
    }

    /// <summary>
    /// Пытается привязать сокет к локальной конечной точке с мягкой деградацией.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void TryBindLocalEndpoint([NotNull] Socket s, IPEndPoint? ep)
    {
        if (ep is null) return;

        var port = 0;
        if (ep.Port > 0) port = ep.Port;

        try
        {
            s.Bind(new IPEndPoint(ep.Address, port));
        }
        catch
        {
            // Браузерный клиент обычно оставляет окончательное решение ОС, если bind недоступен.
        }
    }

    /// <summary>
    /// Применяет размеры буферов приёма и отправки к сокету.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void ApplyBuffers([NotNull] Socket s, int receiveBufferSize, int sendBufferSize)
    {
        if (receiveBufferSize > 0) s.ReceiveBufferSize = receiveBufferSize;
        if (sendBufferSize > 0) s.SendBufferSize = sendBufferSize;
    }

    /// <summary>
    /// Устанавливает TTL (IPv4) и HopLimit (IPv6) для сокета с учётом кроссплатформенности.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void ApplyTtlHopLimit([NotNull] Socket s, byte ttl)
    {
        if (ttl is 0) return;

        try { s.Ttl = ttl; } catch { /* страховка */ }
        try { s.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.HopLimit, ttl); } catch { /* страховка */ }
    }

    /// <summary>
    /// Пытается применить traffic class для уже определённого семейства удалённой стороны.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void ApplyTrafficClass([NotNull] Socket s, byte dscp, bool useEcn, AddressFamily remoteAf)
    {
        var trafficClass = ComposeTrafficClass(dscp, useEcn);
        if (trafficClass is 0) return;

        try
        {
            if (remoteAf is AddressFamily.InterNetwork)
            {
                s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, trafficClass);
                return;
            }

            if (remoteAf is AddressFamily.InterNetworkV6)
                s.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.TypeOfService, trafficClass);
        }
        catch { /* страховка */ }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComposeTrafficClass(byte dscp, bool useEcn)
    {
        var trafficClass = (dscp & 0x3F) << 2;
        if (useEcn) trafficClass |= 0b10;
        return trafficClass;
    }
}