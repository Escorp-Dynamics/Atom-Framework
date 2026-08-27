using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Atom.Net.Https.Profiles;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Проверяет форму ПОВТОРНОГО приветствия на уровне записей.
/// </summary>
/// <remarks>
/// ★ Проверка смотрит байты, а не результат рукопожатия, и это принципиально. Живой openssl
/// s_server, на котором стоят остальные проверки повтора, к версии в заголовке записи
/// снисходителен и принимает любую — поэтому дефект, из-за которого строгие серверы отвечали
/// фатальным protocol_version (70), проходил мимо всех зелёных проверок. Уличить его может только
/// приёмник, который читает записи и сравнивает их с тем, что отправляет браузер.
///
/// Проверяются две вещи, и обе сняты с настоящих Chrome и curl:
///
/// 1. Версия в заголовке записи. У ПЕРВОГО приветствия 0x0301 — послабление RFC 8446, §5.1 ради
///    посредников; у ПОВТОРНОГО обязана быть 0x0303, потому что послабление дано только начальному
///    сообщению.
/// 2. Пустышка change_cipher_spec перед вторым полётом клиента — режим совместимости с
///    посредниками (RFC 8446, §D.4).
///
/// Сервер здесь поддельный: он отвечает готовым HelloRetryRequest и дальше рукопожатие не ведёт.
/// Настоящий и не нужен — предмет проверки целиком в том, что отправляет КЛИЕНТ.
/// </remarks>
[TestFixture]
[CancelAfter(TestTimeoutMs)]
public sealed class SecondClientHelloShapeTests
{
    private const int TestTimeoutMs = 20_000;

    /// <summary>Группа, которую «требует» поддельный сервер: её объявляют все профили, а долю для неё Chrome не шлёт.</summary>
    private const ushort RequestedGroup = 0x0017;

    [TestCase("chrome")]
    [TestCase("safari")]
    public async Task RetryHelloUsesRecordVersion0303AndIsPrecededByCompatibilityCcs(string browser)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = CaptureClientFlightsAsync(listener);

        // Рукопожатие заведомо не завершится: после повтора сервер молчит и закрывается. Нас
        // интересует только то, что успел отправить клиент, поэтому отказ здесь ожидаем.
        try
        {
            await HandshakeAsync(port, browser);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or SocketException)
        {
            // Ожидаемо: поддельный сервер обрывает обмен сразу после второго приветствия.
        }

        var records = await serverTask;

        var hellos = records.Where(static record => record.Type is 0x16).ToList();
        Assert.That(hellos, Has.Count.EqualTo(2), "клиент обязан отправить второе приветствие в ответ на HelloRetryRequest");

        Assert.Multiple(() =>
        {
            Assert.That(hellos[0].Version, Is.EqualTo(0x0301), "первое приветствие идёт с послаблением по версии");
            Assert.That(hellos[1].Version, Is.EqualTo(0x0303), "повторное приветствие послабления НЕ получает (RFC 8446, §5.1)");

            Assert.That(
                records.TakeWhile(static record => record.Type is not 0x16 || record.Version is 0x0301).Any(static record => record.Type is 0x14),
                Is.True,
                "перед вторым полётом клиента идёт пустышка change_cipher_spec (RFC 8446, §D.4)");

            Assert.That(ReadFirstKeyShareGroup(hellos[1].Payload), Is.EqualTo(RequestedGroup), "повтор несёт долю для запрошенной группы");
        });
    }

    /// <summary>
    /// Принимает подключение, отвечает HelloRetryRequest и записывает всё, что прислал клиент.
    /// </summary>
    private static async Task<List<CapturedRecord>> CaptureClientFlightsAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync(TestContext.CurrentContext.CancellationToken);
        client.NoDelay = true;

        var stream = client.GetStream();
        var records = new List<CapturedRecord>();

        var first = await ReadRecordAsync(stream);
        if (first is null) return records;

        records.Add(first);

        await stream.WriteAsync(BuildHelloRetryRequest(first.Payload), TestContext.CurrentContext.CancellationToken);

        // Второй полёт клиента: пустышка совместимости и повторное приветствие приходят отдельными
        // записями, поэтому читаем, пока не увидим рукопожатие либо конец потока.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var next = await ReadRecordAsync(stream);
            if (next is null) break;

            records.Add(next);
            if (next.Type is 0x16) break;
        }

        return records;
    }

    private static async Task<CapturedRecord?> ReadRecordAsync(System.Net.Sockets.NetworkStream stream)
    {
        var header = new byte[5];
        if (!await ReadExactAsync(stream, header)) return null;

        var payload = new byte[(header[3] << 8) | header[4]];
        if (!await ReadExactAsync(stream, payload)) return null;

        return new CapturedRecord
        {
            Type = header[0],
            Version = (ushort)((header[1] << 8) | header[2]),
            Payload = payload,
        };
    }

    private static async Task<bool> ReadExactAsync(System.Net.Sockets.NetworkStream stream, byte[] target)
    {
        var offset = 0;

        while (offset < target.Length)
        {
            var read = await stream.ReadAsync(target.AsMemory(offset), TestContext.CurrentContext.CancellationToken);
            if (read <= 0) return false;

            offset += read;
        }

        return true;
    }

    /// <summary>
    /// Собирает HelloRetryRequest, требующий <see cref="RequestedGroup"/>.
    /// </summary>
    /// <param name="clientHello">Первое приветствие клиента: из него берётся эхо идентификатора сессии.</param>
    /// <returns>Готовая запись рукопожатия.</returns>
    /// <remarks>
    /// HelloRetryRequest — это обычный ServerHello с заданным спецификацией значением random
    /// (RFC 8446, §4.1.3); иначе отличить его нельзя, тип сообщения у них общий.
    /// </remarks>
    private static byte[] BuildHelloRetryRequest(byte[] clientHello)
    {
        ReadOnlySpan<byte> retryRandom =
        [
            0xCF, 0x21, 0xAD, 0x74, 0xE5, 0x9A, 0x61, 0x11, 0xBE, 0x1D, 0x8C, 0x02, 0x1E, 0x65, 0xB8, 0x91,
            0xC2, 0xA2, 0x11, 0x16, 0x7A, 0xBB, 0x8C, 0x5E, 0x07, 0x9E, 0x09, 0xE2, 0xC8, 0xA8, 0x33, 0x9C,
        ];

        var sessionIdLength = clientHello[4 + 2 + 32];
        var sessionId = clientHello.AsSpan(4 + 2 + 32 + 1, sessionIdLength).ToArray();

        var body = new List<byte>();
        body.AddRange([0x03, 0x03]);
        body.AddRange(retryRandom.ToArray());
        body.Add(sessionIdLength);
        body.AddRange(sessionId);
        body.AddRange([0x13, 0x01]); // TLS_AES_128_GCM_SHA256 — его предлагают все профили
        body.Add(0x00);

        var extensions = new List<byte>();
        extensions.AddRange([0x00, 0x2B, 0x00, 0x02, 0x03, 0x04]); // supported_versions: TLS 1.3
        extensions.AddRange([0x00, 0x33, 0x00, 0x02, RequestedGroup >> 8, RequestedGroup & 0xFF]);

        body.AddRange([(byte)(extensions.Count >> 8), (byte)extensions.Count]);
        body.AddRange(extensions);

        var message = new List<byte> { 0x02, (byte)(body.Count >> 16), (byte)(body.Count >> 8), (byte)body.Count };
        message.AddRange(body);

        var record = new List<byte> { 0x16, 0x03, 0x03, (byte)(message.Count >> 8), (byte)message.Count };
        record.AddRange(message);

        return [.. record];
    }

    /// <summary>
    /// Достаёт номер первой группы из расширения key_share приветствия.
    /// </summary>
    private static ushort ReadFirstKeyShareGroup(byte[] clientHello)
    {
        var position = 4 + 2 + 32;
        position += 1 + clientHello[position];
        position += 2 + BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(position));
        position += 1 + clientHello[position];

        var end = position + 2 + BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(position));
        position += 2;

        while (position + 4 <= end)
        {
            var id = BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(position));
            var length = BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(position + 2));

            if (id is 0x0033) return BinaryPrimitives.ReadUInt16BigEndian(clientHello.AsSpan(position + 6));

            position += 4 + length;
        }

        return 0;
    }

    private static async Task HandshakeAsync(int port, string browser)
    {
        var profile = browser switch
        {
            "safari" => BrowserProfileCatalog.CreateSafariDesktopMacOs(),
            _ => BrowserProfileCatalog.CreateChromeDesktopWindowsTls13(),
        };

        var settings = profile.Tls with
        {
            CheckCertificateRevocationList = false,
            ServerCertificateValidationCallback = static (_, _, _) => true,
            HandshakeTimeout = TimeSpan.FromSeconds(10),
            Extensions = [.. profile.Tls.Extensions.Select(WithHostName)],
        };

        var tcp = new TcpStream(new TcpSettings());
        await tcp.ConnectAsync("127.0.0.1", port, TestContext.CurrentContext.CancellationToken);

        await using var stream = new Tls13Stream(tcp, settings);
        await stream.HandshakeAsync(TestContext.CurrentContext.CancellationToken);
    }

    private static ITlsExtension WithHostName(ITlsExtension extension)
        => extension is ServerNameTlsExtension serverName
            ? new ServerNameTlsExtension { Id = serverName.Id, HostName = "localhost" }
            : extension;

    private sealed class CapturedRecord
    {
        public required byte Type { get; init; }

        public required ushort Version { get; init; }

        public required byte[] Payload { get; init; }
    }
}
