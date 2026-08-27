using System.Linq;
using Atom.Net.Quic;

namespace Atom.Net.Tests.Quic;

/// <summary>
/// Закрепляет параметры транспорта QUIC — то, что сервер видит внутри ClientHello.
/// </summary>
/// <remarks>
/// Эталон снят с настоящего Chrome на этой машине через его журнал сети — событие
/// <c>QUIC_SESSION_TRANSPORT_PARAMETERS_SENT</c>:
///
/// <code>
/// max_idle_timeout 30000  max_udp_payload_size 1472  initial_max_data 15728640
/// initial_max_stream_data_bidi_local 6291456  initial_max_stream_data_bidi_remote 6291456
/// initial_max_stream_data_uni 6291456  initial_max_streams_bidi 100  initial_max_streams_uni 103
/// initial_source_connection_id 0  max_datagram_frame_size 65536
/// </code>
///
/// Важен не только состав, но и его ГРАНИЦЫ: браузер НЕ отправляет ни показатель масштаба
/// задержки, ни наибольшую задержку подтверждения, ни предел идентификаторов соединения. Лишний
/// параметр отличает нас от него ровно так же, как недостающий, — а поводов заметить это в
/// обычной работе не будет: соединение при любом наборе останется рабочим.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class ChromeQuicParityTests
{
    private const int TestTimeoutMs = 30000;

    /// <summary>Идентификаторы параметров, которые отправляет Chrome.</summary>
    private static readonly ulong[] ChromeParameterIds = [0x01, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0F, 0x20];

    /// <summary>Идентификаторы, которых у Chrome НЕТ.</summary>
    private static readonly ulong[] AbsentParameterIds = [0x0A, 0x0B, 0x0C, 0x0E];

    [Test]
    public void ParameterSetMatchesChrome()
    {
        var written = WriteDefaults();
        var ids = Decode(written).Select(static entry => entry.Id).ToArray();

        Assert.That(ids, Is.EqualTo(ChromeParameterIds).AsCollection);
    }

    [Test]
    public void ParametersChromeOmitsAreNotSent()
    {
        var ids = Decode(WriteDefaults()).Select(static entry => entry.Id).ToArray();

        foreach (var absent in AbsentParameterIds)
            Assert.That(ids, Does.Not.Contain(absent), $"Chrome не отправляет параметр 0x{absent:X}");
    }

    [TestCase(0x01UL, 30_000UL)]
    [TestCase(0x03UL, 1472UL)]
    [TestCase(0x04UL, 15_728_640UL)]
    [TestCase(0x05UL, 6_291_456UL)]
    [TestCase(0x06UL, 6_291_456UL)]
    [TestCase(0x07UL, 6_291_456UL)]
    [TestCase(0x08UL, 100UL)]
    [TestCase(0x09UL, 103UL)]
    [TestCase(0x20UL, 65_536UL)]
    public void ParameterValuesMatchChrome(ulong id, ulong expected)
    {
        var entry = Decode(WriteDefaults()).Single(candidate => candidate.Id == id);

        Assert.That(entry.Value, Is.EqualTo(expected));
    }

    [Test]
    public void ClientConnectionIdIsEmptyLikeChrome()
    {
        // Пустой идентификатор — не мелочь: он уезжает в КАЖДОМ пакете ответа, и его длина
        // наблюдаема в заголовке. Клиенту он не нужен вовсе: адресует нас пара адресов UDP.
        var entry = Decode(WriteDefaults()).Single(static candidate => candidate.Id == 0x0F);

        Assert.That(entry.Length, Is.Zero);
    }

    [Test]
    public void ParametersRoundTripThroughTheWireFormat()
    {
        var source = new QuicTransportParameters { InitialSourceConnectionId = ReadOnlyMemory<byte>.Empty };
        var buffer = new byte[512];
        var length = source.Write(buffer);

        var restored = QuicTransportParameters.Read(buffer.AsSpan(0, length));

        Assert.Multiple(() =>
        {
            Assert.That(restored.MaxIdleTimeout, Is.EqualTo(source.MaxIdleTimeout));
            Assert.That(restored.InitialMaxData, Is.EqualTo(source.InitialMaxData));
            Assert.That(restored.InitialMaxStreamsUni, Is.EqualTo(source.InitialMaxStreamsUni));
            Assert.That(restored.MaxDatagramFrameSize, Is.EqualTo(source.MaxDatagramFrameSize));
        });
    }

    private static byte[] WriteDefaults()
    {
        var parameters = new QuicTransportParameters { InitialSourceConnectionId = ReadOnlyMemory<byte>.Empty };
        var buffer = new byte[512];
        var length = parameters.Write(buffer);

        return buffer[..length];
    }

    private static List<(ulong Id, ulong Value, int Length)> Decode(byte[] encoded)
    {
        var result = new List<(ulong Id, ulong Value, int Length)>();
        var offset = 0;

        while (offset < encoded.Length)
        {
            if (!QuicCodec.TryRead(encoded.AsSpan(offset), out var id, out var read)) break;
            offset += read;

            if (!QuicCodec.TryRead(encoded.AsSpan(offset), out var length, out read)) break;
            offset += read;

            var value = 0UL;
            if (length > 0) _ = QuicCodec.TryRead(encoded.AsSpan(offset, (int)length), out value, out _);

            offset += (int)length;
            result.Add((id, value, (int)length));
        }

        return result;
    }
}
