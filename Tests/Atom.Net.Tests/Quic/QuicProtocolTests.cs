using System.Linq;
using System.Security.Cryptography;
using Atom.Net.Quic;

namespace Atom.Net.Tests.Quic;

/// <summary>
/// Проверяет протокольный слой QUIC: числа переменной длины, номера пакетов, защиту и кадры.
/// </summary>
/// <remarks>
/// Обмен QUIC зашифрован с ПЕРВОГО пакета, и снаружи видны только датаграммы UDP. Ошибка в любом
/// из этих мест не даёт ни исключения, ни внятного отказа — пакет просто не расшифровывается, и
/// выглядит это как молчание сети. Поэтому нижний слой проверяется отдельно, до всякой сети.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class QuicProtocolTests
{
    private const int TestTimeoutMs = 30000;

    [TestCase(0UL, 1)]
    [TestCase(63UL, 1)]
    [TestCase(64UL, 2)]
    [TestCase(255UL, 2)]
    [TestCase(16383UL, 2)]
    [TestCase(16384UL, 4)]
    [TestCase(1073741823UL, 4)]
    [TestCase(1073741824UL, 8)]
    public void VarintRoundTripsAcrossLengthBoundaries(ulong value, int expectedLength)
    {
        // Значение 255 стоит здесь не случайно: запись младшего байта — усечение, и при
        // включённой проверке переполнения она бросала исключение на всём, что больше 255.
        Span<byte> buffer = stackalloc byte[8];

        var written = QuicCodec.TryWrite(buffer, value, out var length);
        var read = QuicCodec.TryRead(buffer, out var restored, out var consumed);

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.True);
            Assert.That(length, Is.EqualTo(expectedLength));
            Assert.That(read, Is.True);
            Assert.That(consumed, Is.EqualTo(expectedLength));
            Assert.That(restored, Is.EqualTo(value));
        });
    }

    [Test]
    public void PacketNumberDecodingPicksNearestCandidate()
    {
        // Пример из приложения A к RFC 9000: в пакете едут только младшие биты, а полное значение
        // выбирается как ближайшее к ожидаемому.
        var decoded = QuicPacketProtection.DecodePacketNumber(truncated: 0x9B32, length: 2, largestAcknowledged: 0xA82F30EA);

        Assert.That(decoded, Is.EqualTo(0xA82F9B32UL));
    }

    [TestCase(0UL, -1L, 1)]
    [TestCase(255UL, -1L, 2)]
    [TestCase(100UL, 99L, 1)]
    [TestCase(100000UL, 0L, 3)]
    [TestCase(10000000UL, 0L, 4)]
    public void PacketNumberLengthCoversDistanceToAcknowledged(ulong packetNumber, long acknowledged, int expected)
    {
        // Правило простое: записанных бит должно хватать, чтобы ВДВОЕ перекрыть расстояние до
        // подтверждённого номера. Получатель выбирает ближайшее значение, и половина окна — это
        // граница, за которой выбор становится неоднозначным.
        Assert.That(QuicPacketProtection.GetPacketNumberLength(packetNumber, acknowledged), Is.EqualTo(expected));
    }

    [Test]
    public void InitialSecretsDifferPerConnectionId()
    {
        // Начальные ключи выводятся из идентификатора соединения. Совпадение ключей у разных
        // соединений означало бы, что идентификатор в вывод не попал.
        var (firstClient, firstServer) = QuicInitialSecrets.Derive(QuicVersion.V1, RandomNumberGenerator.GetBytes(8));
        var (secondClient, secondServer) = QuicInitialSecrets.Derive(QuicVersion.V1, RandomNumberGenerator.GetBytes(8));

        using (firstClient)
        using (firstServer)
        using (secondClient)
        using (secondServer)
        {
            Assert.Multiple(() =>
            {
                Assert.That(firstClient.Secret.Span.SequenceEqual(secondClient.Secret.Span), Is.False);
                Assert.That(firstClient.Secret.Span.SequenceEqual(firstServer.Secret.Span), Is.False, "клиент и сервер обязаны получить РАЗНЫЕ секреты");
            });
        }
    }

    [Test]
    public void NonceCombinesInitialisationVectorWithPacketNumber()
    {
        var (client, server) = QuicInitialSecrets.Derive(QuicVersion.V1, RandomNumberGenerator.GetBytes(8));

        using (client)
        using (server)
        {
            var first = new byte[QuicKeys.IvLength];
            var second = new byte[QuicKeys.IvLength];

            client.BuildNonce(0, first);
            client.BuildNonce(1, second);

            var prefixMatches = first.AsSpan(0, QuicKeys.IvLength - 1).SequenceEqual(second.AsSpan(0, QuicKeys.IvLength - 1));
            var lastFirst = first[^1];
            var lastSecond = second[^1];

            // Отличаться обязан ровно последний байт: номер складывается с вектором по модулю два.
            Assert.Multiple(() =>
            {
                Assert.That(prefixMatches, Is.True);
                Assert.That(lastFirst, Is.Not.EqualTo(lastSecond));
            });
        }
    }

    [Test]
    public void ProtectedPacketRoundTrips()
    {
        // Сквозная проверка: защита нагрузки, маскирование заголовка и обратный порядок при
        // приёме. Ошибка в любом шаге здесь неотличима от неверных ключей.
        var (client, server) = QuicInitialSecrets.Derive(QuicVersion.V1, RandomNumberGenerator.GetBytes(8));

        using (client)
        using (server)
        {
            const int PacketNumberOffset = 18;
            const ulong PacketNumber = 42;

            var packet = new byte[256];
            RandomNumberGenerator.Fill(packet.AsSpan(0, PacketNumberOffset));

            // Первый байт обязан нести длину номера пакета в двух младших битах: он входит в
            // дополнительные данные AEAD, и получатель узнаёт длину именно оттуда. Записать одно,
            // а зашифровать по другому — значит получить провал расшифровки, неотличимый от
            // неверных ключей.
            packet[0] = 0x40 | (2 - 1);
            QuicPacketProtection.WritePacketNumber(packet.AsSpan(PacketNumberOffset), PacketNumber, 2);

            var payload = new byte[64];
            RandomNumberGenerator.Fill(payload);
            payload.CopyTo(packet.AsSpan(PacketNumberOffset + 2));

            var total = QuicPacketProtection.Protect(packet, PacketNumberOffset, packetNumberLength: 2, payloadLength: payload.Length, PacketNumber, client);

            var restored = QuicPacketProtection.TryUnprotect(packet.AsSpan(0, total), PacketNumberOffset, largestAcknowledged: 40, client, out var number, out var opened);

            Assert.Multiple(() =>
            {
                Assert.That(restored, Is.True);
                Assert.That(number, Is.EqualTo(PacketNumber));
                Assert.That(opened, Is.EqualTo(payload).AsCollection);
            });
        }
    }

    [Test]
    public void FrameWriterAndReaderAgreeOnCrypto()
    {
        var data = RandomNumberGenerator.GetBytes(100);
        var buffer = new byte[256];

        var writer = new QuicFrameWriter(buffer);
        var written = writer.WriteCrypto(offset: 1234, data);

        var reader = new QuicFrameReader(buffer.AsSpan(0, writer.Written));
        var read = reader.Read();

        var frameType = reader.FrameType;
        var offset = reader.Offset;
        var restored = reader.Data.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.True);
            Assert.That(read, Is.True);
            Assert.That(frameType, Is.EqualTo((ulong)QuicFrameType.Crypto));
            Assert.That(offset, Is.EqualTo(1234UL));
            Assert.That(restored, Is.EqualTo(data).AsCollection);
        });
    }

    [Test]
    public void FrameWriterAndReaderAgreeOnStream()
    {
        var data = RandomNumberGenerator.GetBytes(64);
        var buffer = new byte[256];

        var writer = new QuicFrameWriter(buffer);
        _ = writer.WriteStream(streamId: 4, offset: 8, data, fin: true);

        var reader = new QuicFrameReader(buffer.AsSpan(0, writer.Written));
        _ = reader.Read();

        var streamId = reader.StreamId;
        var offset = reader.Offset;
        var fin = reader.IsFin;
        var restored = reader.Data.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(streamId, Is.EqualTo(4UL));
            Assert.That(offset, Is.EqualTo(8UL));
            Assert.That(fin, Is.True);
            Assert.That(restored, Is.EqualTo(data).AsCollection);
        });
    }

    [Test]
    public void AckRangesSurviveEncoding()
    {
        // Диапазоны кодируются от наибольшего к наименьшему промежутками между ними. Формат
        // экономный, но требует строгого порядка: перепутав его, получим разрыв соединения.
        var space = new QuicPacketNumberSpace();

        foreach (var number in new ulong[] { 0, 1, 2, 5, 6, 9 }) space.OnPacketReceived(number, ackEliciting: true);

        var buffer = new byte[128];
        var writer = new QuicFrameWriter(buffer);

        Span<(ulong Start, ulong End)> ranges = stackalloc (ulong Start, ulong End)[16];
        var rangeCount = space.CopyReceivedRanges(ranges);

        _ = writer.WriteAck(ranges[..rangeCount], ackDelayMicroseconds: 0, ackDelayExponent: 3);

        var reader = new QuicFrameReader(buffer.AsSpan(0, writer.Written));
        _ = reader.Read();

        var largest = reader.LargestAcknowledged;
        var restored = reader.AckRanges.OrderBy(static range => range.Start).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(largest, Is.EqualTo(9UL));
            Assert.That(restored, Has.Length.EqualTo(3));
            Assert.That(restored[0], Is.EqualTo((0UL, 2UL)));
            Assert.That(restored[1], Is.EqualTo((5UL, 6UL)));
            Assert.That(restored[2], Is.EqualTo((9UL, 9UL)));
        });
    }

    [Test]
    public void ReceivedRangesMergeWhenAdjacent()
    {
        var space = new QuicPacketNumberSpace();

        space.OnPacketReceived(0, ackEliciting: true);
        space.OnPacketReceived(2, ackEliciting: true);
        space.OnPacketReceived(1, ackEliciting: true);

        // Три подряд идущих номера обязаны превратиться в ОДИН диапазон: иначе кадр ACK растёт
        // линейно по числу пакетов и перестаёт помещаться в пакет.
        Span<(ulong Start, ulong End)> ranges = stackalloc (ulong Start, ulong End)[16];
        var rangeCount = space.CopyReceivedRanges(ranges);

        Assert.Multiple(() =>
        {
            Assert.That(rangeCount, Is.EqualTo(1));
            Assert.That(space.ReceivedRangeCount, Is.EqualTo(1));
        });

        Assert.That(ranges[0], Is.EqualTo((0UL, 2UL)));
    }

    [Test]
    public void TransportParametersRoundTrip()
    {
        var source = new QuicTransportParameters
        {
            InitialMaxData = 15_728_640,
            InitialMaxStreamsBidi = 100,
            MaxIdleTimeout = 30_000,
            InitialSourceConnectionId = RandomNumberGenerator.GetBytes(8),
        };

        var buffer = new byte[512];
        var length = source.Write(buffer);
        var restored = QuicTransportParameters.Read(buffer.AsSpan(0, length));

        Assert.Multiple(() =>
        {
            Assert.That(restored.InitialMaxData, Is.EqualTo(source.InitialMaxData));
            Assert.That(restored.InitialMaxStreamsBidi, Is.EqualTo(source.InitialMaxStreamsBidi));
            Assert.That(restored.MaxIdleTimeout, Is.EqualTo(source.MaxIdleTimeout));
            Assert.That(restored.InitialSourceConnectionId.ToArray(), Is.EqualTo(source.InitialSourceConnectionId.ToArray()).AsCollection);
        });
    }

    [Test]
    public void CryptoStreamAssemblesOutOfOrderFragments()
    {
        // Сообщение рукопожатия свободно пересекает границу пакета, а пакеты приходят не по
        // порядку. Разбор половины сообщения даёт «выход за границы» — то есть выглядит как
        // испорченные данные, а не как нехватка байт.
        var stream = new QuicCryptoStream();

        var message = new byte[4 + 300];
        message[0] = 0x02;
        message[1] = 0;
        message[2] = 300 >> 8;
        message[3] = 300 & 0xFF;
        RandomNumberGenerator.Fill(message.AsSpan(4));

        stream.Write(200, message.AsSpan(200));

        Assert.That(stream.TryReadMessage(out _), Is.False, "сообщение не должно отдаваться по частям");

        stream.Write(0, message.AsSpan(0, 200));

        var complete = stream.TryReadMessage(out var assembled);

        Assert.Multiple(() =>
        {
            Assert.That(complete, Is.True);
            Assert.That(assembled.ToArray(), Is.EqualTo(message).AsCollection);
        });
    }

    [Test]
    public void QuicStreamRestoresOrderOfReceivedData()
    {
        var stream = new QuicStream(id: 0, initialSendWindow: 1000);

        var second = "вторая часть"u8.ToArray();
        var first = "первая часть"u8.ToArray();

        stream.OnDataReceived((ulong)first.Length, second, fin: true);
        stream.OnDataReceived(0, first, fin: false);
        stream.Complete();

        var collected = new List<byte>();

        while (stream.Body.TryRead(out var chunk)) collected.AddRange(chunk.Span);

        Assert.That(collected, Is.EqualTo(first.Concat(second).ToArray()).AsCollection);
    }
}
