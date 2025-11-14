using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Atom.Net.Udp;

namespace Atom.Net.Quic;

/// <summary>
/// Представляет QUIC-соединение поверх UDP-транспорта.
/// Управляет путями, крипто-рукопожатием, лосс-детектором и создаёт логические <see cref="QuicStream"/>.
/// </summary>
public sealed class QuicConnection : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// UDP-транспорт.
    /// </summary>
    private readonly UdpStream udp;

    /// <summary>
    /// Буфер приёма дейтаграмм (переиспользуемый).
    /// </summary>
    private readonly byte[] rxBuffer;

    private bool isDisposed;

    /// <summary>
    /// Поток приёма.
    /// </summary>
    private Task? rxLoop;

    /// <summary>
    /// Настройки QUIC.
    /// </summary>
    public QuicSettings Settings { get; }

    /// <summary>
    /// Текущий удалённый адрес сервера.
    /// </summary>
    public IPEndPoint RemoteEndPoint { get; }

    /// <summary>
    /// Текущая локальная точка пути (обновляется из pktinfo).
    /// </summary>
    public UdpPacketInfo LastPath => udp.LastPacketInfo;

    /// <summary>
    /// Инициализирует клиентское QUIC-соединение.
    /// </summary>
    /// <param name="udpStream">Готовый UDP-поток (желательно с <see cref="UdpSettings.UsePacketInfo"/>=true).</param>
    /// <param name="remote">Удалённая конечная точка сервера.</param>
    /// <param name="settings">Настройки QUIC.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public QuicConnection(UdpStream udpStream, IPEndPoint remote, in QuicSettings settings)
    {
        udp = udpStream;
        RemoteEndPoint = remote;
        Settings = settings;

        var size = settings.MaxUdpPayloadSize > 0 ? settings.MaxUdpPayloadSize : 1252;
        rxBuffer = GC.AllocateUninitializedArray<byte>(size, pinned: false);
    }

    /// <summary>
    /// Подключается к серверу (connected UDP) и запускает цикл приёма.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        // Подключаем UDP (браузеры обычно держат connected UDP к (host,port)).
        await udp.ConnectAsync(host, port, ct).ConfigureAwait(false);

        // Стартуем цикл приёма (без аллокаций в горячем пути).
        rxLoop = Task.Run(ReceiveLoopAsync, CancellationToken.None);
    }

    /// <summary>
    /// Отправляет CRYPTO/STREAM/ACK кадры, упакованные в один QUIC-пакет.
    /// Пока без AEAD/HP — добавим на этапе TLS 1.3 интеграции.
    /// </summary>
    /// <param name="packetBuilder">Делегат, который пишет полезную нагрузку пакета в предоставленный буфер.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Число отправленных байт.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<int> SendPacketAsync(Func<Span<byte>, int> packetBuilder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packetBuilder);
        var spanBuf = rxBuffer.AsSpan(); // временно переиспользуем — отдельный TX-буфер добавим при внедрении AEAD
        var len = packetBuilder(spanBuf);
        if ((uint)len > (uint)spanBuf.Length) throw new ArgumentOutOfRangeException(nameof(packetBuilder));

        // Для connected UDP — обычная запись; дейтаграмма должна уйти целиком.
        // Socket API ожидает ReadOnlyMemory<byte> для асинхронной записи, поэтому используем Memory сегмент.
        var mem = rxBuffer.AsMemory(0, len);
        await udp.WriteAsync(mem, cancellationToken).ConfigureAwait(false);
        return len;
    }

    /// <summary>
    /// Читает дейтаграммы и парсит QUIC-заголовки (без дешифрования).
    /// </summary>
    private async Task ReceiveLoopAsync()
    {
        var mem = rxBuffer.AsMemory();

        while (!Volatile.Read(ref isDisposed))
        {
            int n;

            try
            {
                n = await udp.ReadAsync(mem).ConfigureAwait(false);
                if (n <= 0) continue; // быстрая семантика WouldBlock/EOF
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }
            catch (Exception) { continue; }

            var span = mem.Span[..n];

            // 1) Считываем первый байт и определяем тип заголовка.
            // Long Header: 0xC0.., Short Header: 0x40..
            var first = span[0];

            if ((first & 0x80) != 0) // Long Header
            {
                OnLongHeaderPacket(span, udp.LastPacketInfo);
            }
            else // Short Header
            {
                OnShortHeaderPacket(span, udp.LastPacketInfo);
            }
        }
    }

    /// <summary>
    /// Обработка пакета с Long Header (Initial/Handshake/0-RTT/Retry).
    /// Здесь же будет снятие Header Protection и дешифрование AEAD — в следующем инкременте.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnLongHeaderPacket(ReadOnlySpan<byte> packet, in UdpPacketInfo pkt) =>
        // Минимальный безопасный парсинг: Version, DCID, SCID, тип, PN length
        // (без VarInt перебора всего payload здесь — это сделаем после добавления AEAD/HP).
        // В этом инкременте собираем телеметрию пути:
        _ = pkt; // пока не используем, но фиксируем для Path Tracking/Migration// TODO(next): парсинг полей Long Header + ACK/CRYPTO dispatch.

    /// <summary>
    /// Обработка пакета с Short Header (1-RTT).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnShortHeaderPacket(ReadOnlySpan<byte> packet, in UdpPacketInfo pkt) => _ = pkt;// TODO(next): снять HP, восстановить PN, дешифровать, распаковать STREAM/ACK/… кадры.

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;

        try { udp.Dispose(); } catch { /* ignore */ }
        try { rxLoop?.Wait(); } catch { /* ignore */ }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;

        try { await udp.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }

        if (rxLoop is not null)
        {
            try { await rxLoop.ConfigureAwait(false); } catch { /* ignore */ }
        }
    }
}