using System.Buffers.Binary;
namespace Atom.Net.Quic;

/// <summary>
/// Параметры транспорта QUIC (RFC 9000, §18).
/// </summary>
/// <remarks>
/// Едут внутри ClientHello отдельным расширением TLS (0x0039) и потому наблюдаемы сервером
/// вместе с остальным отпечатком рукопожатия. Состав и значения у браузеров устойчивы и
/// различаются между ними — то же по смыслу, что SETTINGS в HTTP/2.
///
/// Значения по умолчанию соответствуют наблюдаемым у Chromium.
/// </remarks>
public readonly struct QuicTransportParameters() : IEquatable<QuicTransportParameters>
{
    /// <summary>Идентификатор расширения TLS, несущего параметры.</summary>
    public const ushort ExtensionId = 0x0039;

    /// <summary>Предел данных соединения.</summary>
    public ulong InitialMaxData { get; init; } = 15_728_640;

    /// <summary>Предел данных для потоков, открытых нами.</summary>
    public ulong InitialMaxStreamDataBidiLocal { get; init; } = 6_291_456;

    /// <summary>Предел данных для двунаправленных потоков, открытых партнёром.</summary>
    public ulong InitialMaxStreamDataBidiRemote { get; init; } = 6_291_456;

    /// <summary>Предел данных для однонаправленных потоков.</summary>
    public ulong InitialMaxStreamDataUni { get; init; } = 6_291_456;

    /// <summary>Предел числа двунаправленных потоков партнёра.</summary>
    public ulong InitialMaxStreamsBidi { get; init; } = 100;

    /// <summary>Предел числа однонаправленных потоков партнёра.</summary>
    public ulong InitialMaxStreamsUni { get; init; } = 103;

    /// <summary>Предельное время простоя в миллисекундах.</summary>
    public ulong MaxIdleTimeout { get; init; } = 30_000;

    /// <summary>Наибольшая датаграмма UDP, которую мы готовы принять.</summary>
    public ulong MaxUdpPayloadSize { get; init; } = 1472;

    /// <summary>Показатель масштаба поля задержки подтверждения.</summary>
    public ulong AckDelayExponent { get; init; } = 3;

    /// <summary>Наибольшая задержка подтверждения в миллисекундах.</summary>
    public ulong MaxAckDelay { get; init; } = 25;

    /// <summary>Сколько идентификаторов соединения партнёр может держать одновременно.</summary>
    public ulong ActiveConnectionIdLimit { get; init; } = 8;

    /// <summary>Запрещена ли миграция соединения на другой путь.</summary>
    public bool DisableActiveMigration { get; init; }

    /// <summary>Наибольший размер кадра датаграммы, который мы готовы принять.</summary>
    public ulong MaxDatagramFrameSize { get; init; } = 65536;

    /// <summary>Идентификатор соединения, выбранный отправителем параметров.</summary>
    public ReadOnlyMemory<byte> InitialSourceConnectionId { get; init; }

    /// <summary>Идентификатор, к которому клиент обращался первым пакетом; присылает только сервер.</summary>
    public ReadOnlyMemory<byte> OriginalDestinationConnectionId { get; init; }

    /// <summary>Идентификатор из пакета Retry; присылает только сервер.</summary>
    public ReadOnlyMemory<byte> RetrySourceConnectionId { get; init; }

    /// <summary>Наименьшая задержка подтверждения в микросекундах; отправляет только Firefox.</summary>
    public ulong MinAckDelay { get; init; } = 1000;

    /// <summary>Чьи параметры воспроизводить: набор и ПОРЯДОК у браузеров разные.</summary>
    public QuicTransportProfile Profile { get; init; } = QuicTransportProfile.Chromium;

    /// <summary>
    /// Параметры транспорта, наблюдаемые у Firefox.
    /// </summary>
    /// <returns>Готовый набор.</returns>
    /// <remarks>
    /// ★ Значения прослежены до кода, который их порождает: ветка выпуска самого Firefox (154) и
    /// вендоренный в ней neqo. От движков Chromium набор отличается почти целиком — и величинами,
    /// и составом, и порядком.
    ///
    /// Три особенности, каждая наблюдаема:
    ///
    /// Идентификатор источника у Firefox ТРЁХБАЙТОВЫЙ, тогда как Chrome отправляет пустой. Его
    /// длина видна в каждом пакете ответа, а не только в рукопожатии.
    ///
    /// Отправляется наименьшая задержка подтверждения (0xFF02DE1A) — параметр из черновика,
    /// которого у движков Chromium нет вовсе.
    ///
    /// Размер кадра датаграммы (0x20) идёт ПОСЛЕДНИМ, уже после четырёхбайтового идентификатора
    /// предыдущего параметра. Порядок здесь — порядок объявления в перечислении реализации, и
    /// сама эта перестановка выдаёт браузер.
    /// </remarks>
    public static QuicTransportParameters CreateFirefox() => new()
    {
        Profile = QuicTransportProfile.Firefox,
        MaxIdleTimeout = 30_000,
        InitialMaxData = 25_165_824,
        InitialMaxStreamDataBidiLocal = 12_582_912,
        InitialMaxStreamDataBidiRemote = 1_048_576,
        InitialMaxStreamDataUni = 1_048_576,
        InitialMaxStreamsBidi = 100,
        InitialMaxStreamsUni = 100,
        MaxAckDelay = 20,
        ActiveConnectionIdLimit = 8,
        MinAckDelay = 1000,
        MaxDatagramFrameSize = 65_535,
    };

    /// <summary>
    /// Записывает параметры в буфер.
    /// </summary>
    /// <param name="destination">Буфер.</param>
    /// <returns>Число записанных байт.</returns>
    /// <remarks>
    /// Каждый параметр — пара «идентификатор, длина» и значение, все числами переменной длины.
    /// Порядок наблюдаем сервером; он выбран совпадающим с наблюдаемым у браузера.
    /// </remarks>
    public int Write(Span<byte> destination)
        => Profile is QuicTransportProfile.Firefox ? WriteFirefox(destination) : WriteChromium(destination);

    /// <summary>
    /// Записывает набор в порядке, наблюдаемом у Firefox.
    /// </summary>
    /// <param name="destination">Буфер.</param>
    /// <returns>Число записанных байт.</returns>
    private int WriteFirefox(Span<byte> destination)
    {
        var offset = 0;

        WriteInteger(destination, ref offset, 0x01, MaxIdleTimeout);
        WriteInteger(destination, ref offset, 0x04, InitialMaxData);
        WriteInteger(destination, ref offset, 0x05, InitialMaxStreamDataBidiLocal);
        WriteInteger(destination, ref offset, 0x06, InitialMaxStreamDataBidiRemote);
        WriteInteger(destination, ref offset, 0x07, InitialMaxStreamDataUni);
        WriteInteger(destination, ref offset, 0x08, InitialMaxStreamsBidi);
        WriteInteger(destination, ref offset, 0x09, InitialMaxStreamsUni);
        WriteInteger(destination, ref offset, 0x0B, MaxAckDelay);
        WriteInteger(destination, ref offset, 0x0E, ActiveConnectionIdLimit);
        WriteBytes(destination, ref offset, 0x0F, InitialSourceConnectionId.Span);
        WriteVersionInformation(destination, ref offset);
        WriteInteger(destination, ref offset, 0xFF02_DE1A, MinAckDelay);
        WriteInteger(destination, ref offset, 0x20, MaxDatagramFrameSize);

        return offset;
    }

    /// <summary>
    /// Записывает сведения о версиях (RFC 9368).
    /// </summary>
    /// <param name="destination">Буфер.</param>
    /// <param name="offset">Текущее смещение.</param>
    /// <remarks>
    /// Тело — выбранная версия, затем перечень доступных. На выпускной сборке Firefox перечень
    /// состоит из подставного значения и единицы, причём подставное идёт ПЕРВЫМ и служит той же
    /// цели, что GREASE в TLS: не давать посредникам полагаться на закрытый набор.
    /// </remarks>
    private static void WriteVersionInformation(Span<byte> destination, ref int offset)
    {
        Span<byte> body = stackalloc byte[12];

        BinaryPrimitives.WriteUInt32BigEndian(body, 0x0000_0001);
        BinaryPrimitives.WriteUInt32BigEndian(body[4..], GreaseVersion());
        BinaryPrimitives.WriteUInt32BigEndian(body[8..], 0x0000_0001);

        WriteBytes(destination, ref offset, 0x11, body);
    }

    /// <summary>
    /// Выдаёт подставное значение версии.
    /// </summary>
    /// <returns>Версия вида, зарезервированного под GREASE.</returns>
    /// <remarks>
    /// Форма задана реализацией: каждый байт с младшей половиной, равной десяти. Постоянное
    /// значение здесь было бы хуже отсутствия — оно опознавалось бы само по себе.
    /// </remarks>
    private static uint GreaseVersion()
    {
        Span<byte> random = stackalloc byte[4];
        System.Security.Cryptography.RandomNumberGenerator.Fill(random);

        return (BinaryPrimitives.ReadUInt32BigEndian(random) & 0xF0F0_F0F0u) | 0x0A0A_0A0Au;
    }

    private int WriteChromium(Span<byte> destination)
    {
        var offset = 0;

        // Состав снят с настоящего Chrome через его журнал сети (QUIC_SESSION_TRANSPORT_PARAMETERS_SENT).
        // Показатель масштаба задержки, наибольшая задержка подтверждения, предел идентификаторов
        // и запрет миграции браузер НЕ отправляет — он полагается на значения по умолчанию.
        // Лишний параметр так же заметен, как недостающий.
        WriteInteger(destination, ref offset, 0x01, MaxIdleTimeout);
        WriteInteger(destination, ref offset, 0x03, MaxUdpPayloadSize);
        WriteInteger(destination, ref offset, 0x04, InitialMaxData);
        WriteInteger(destination, ref offset, 0x05, InitialMaxStreamDataBidiLocal);
        WriteInteger(destination, ref offset, 0x06, InitialMaxStreamDataBidiRemote);
        WriteInteger(destination, ref offset, 0x07, InitialMaxStreamDataUni);
        WriteInteger(destination, ref offset, 0x08, InitialMaxStreamsBidi);
        WriteInteger(destination, ref offset, 0x09, InitialMaxStreamsUni);

        if (DisableActiveMigration) WriteEmpty(destination, ref offset, 0x0C);

        WriteBytes(destination, ref offset, 0x0F, InitialSourceConnectionId.Span);
        WriteInteger(destination, ref offset, 0x20, MaxDatagramFrameSize);

        return offset;
    }

    /// <summary>
    /// Разбирает параметры, присланные сервером.
    /// </summary>
    /// <param name="source">Тело расширения.</param>
    /// <returns>Разобранные параметры.</returns>
    /// <remarks>
    /// Неизвестные идентификаторы пропускаются молча: расширяемость здесь заложена намеренно, и
    /// именно поэтому в параметры вставляют GREASE.
    /// </remarks>
    public static QuicTransportParameters Read(ReadOnlySpan<byte> source)
    {
        var result = new QuicTransportParameters();
        var offset = 0;

        while (offset < source.Length)
        {
            if (!QuicCodec.TryRead(source[offset..], out var id, out var read)) break;
            offset += read;

            if (!QuicCodec.TryRead(source[offset..], out var length, out read)) break;
            offset += read;

            if (offset + (int)length > source.Length) break;

            var value = source.Slice(offset, (int)length);
            offset += (int)length;

            result = id switch
            {
                0x01 => result with { MaxIdleTimeout = ReadInteger(value) },
                0x03 => result with { MaxUdpPayloadSize = ReadInteger(value) },
                0x04 => result with { InitialMaxData = ReadInteger(value) },
                0x05 => result with { InitialMaxStreamDataBidiLocal = ReadInteger(value) },
                0x06 => result with { InitialMaxStreamDataBidiRemote = ReadInteger(value) },
                0x07 => result with { InitialMaxStreamDataUni = ReadInteger(value) },
                0x08 => result with { InitialMaxStreamsBidi = ReadInteger(value) },
                0x09 => result with { InitialMaxStreamsUni = ReadInteger(value) },
                0x0A => result with { AckDelayExponent = ReadInteger(value) },
                0x0B => result with { MaxAckDelay = ReadInteger(value) },
                0x0C => result with { DisableActiveMigration = true },
                0x0E => result with { ActiveConnectionIdLimit = ReadInteger(value) },
                0x0F => result with { InitialSourceConnectionId = value.ToArray() },
                0x20 => result with { MaxDatagramFrameSize = ReadInteger(value) },
                0x00 => result with { OriginalDestinationConnectionId = value.ToArray() },
                0x10 => result with { RetrySourceConnectionId = value.ToArray() },
                _ => result,
            };
        }

        return result;
    }

    private static ulong ReadInteger(ReadOnlySpan<byte> value)
        => QuicCodec.TryRead(value, out var result, out _) ? result : 0;

    private static void WriteInteger(Span<byte> destination, ref int offset, ulong id, ulong value)
    {
        var valueLength = QuicCodec.EncodedLength(value);

        WriteVarint(destination, ref offset, id);
        WriteVarint(destination, ref offset, (ulong)valueLength);
        WriteVarint(destination, ref offset, value);
    }

    private static void WriteEmpty(Span<byte> destination, ref int offset, ulong id)
    {
        WriteVarint(destination, ref offset, id);
        WriteVarint(destination, ref offset, 0);
    }

    private static void WriteBytes(Span<byte> destination, ref int offset, ulong id, ReadOnlySpan<byte> value)
    {
        WriteVarint(destination, ref offset, id);
        WriteVarint(destination, ref offset, (ulong)value.Length);

        value.CopyTo(destination[offset..]);
        offset += value.Length;
    }

    private static void WriteVarint(Span<byte> destination, ref int offset, ulong value)
    {
        if (!QuicCodec.TryWrite(destination[offset..], value, out var written)) throw new InvalidOperationException("Не хватило места для параметра транспорта");
        offset += written;
    }

    /// <inheritdoc/>
    public bool Equals(QuicTransportParameters other)
        => InitialMaxData == other.InitialMaxData
        && InitialMaxStreamDataBidiLocal == other.InitialMaxStreamDataBidiLocal
        && InitialMaxStreamDataBidiRemote == other.InitialMaxStreamDataBidiRemote
        && InitialMaxStreamDataUni == other.InitialMaxStreamDataUni
        && InitialMaxStreamsBidi == other.InitialMaxStreamsBidi
        && InitialMaxStreamsUni == other.InitialMaxStreamsUni
        && MaxIdleTimeout == other.MaxIdleTimeout
        && MaxUdpPayloadSize == other.MaxUdpPayloadSize
        && AckDelayExponent == other.AckDelayExponent
        && MaxAckDelay == other.MaxAckDelay
        && ActiveConnectionIdLimit == other.ActiveConnectionIdLimit
        && DisableActiveMigration == other.DisableActiveMigration;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is QuicTransportParameters other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(InitialMaxData);
        hash.Add(InitialMaxStreamDataBidiLocal);
        hash.Add(InitialMaxStreamsBidi);
        hash.Add(MaxIdleTimeout);
        hash.Add(ActiveConnectionIdLimit);

        return hash.ToHashCode();
    }

    /// <summary>
    /// Определяет, равны ли параметры.
    /// </summary>
    /// <param name="left">Левый операнд.</param>
    /// <param name="right">Правый операнд.</param>
    /// <returns><see langword="true"/>, если параметры совпадают.</returns>
    public static bool operator ==(QuicTransportParameters left, QuicTransportParameters right) => left.Equals(right);

    /// <summary>
    /// Определяет, различаются ли параметры.
    /// </summary>
    /// <param name="left">Левый операнд.</param>
    /// <param name="right">Правый операнд.</param>
    /// <returns><see langword="true"/>, если параметры различаются.</returns>
    public static bool operator !=(QuicTransportParameters left, QuicTransportParameters right) => !left.Equals(right);
}

/// <summary>
/// Чьи параметры транспорта воспроизводить.
/// </summary>
/// <remarks>
/// Набор, значения и ПОРЯДОК параметров у браузеров различаются целиком, и сервер видит их
/// внутри ClientHello наравне с остальным отпечатком.
/// </remarks>
public enum QuicTransportProfile
{
    /// <summary>Движки на Chromium.</summary>
    Chromium = 0,

    /// <summary>Firefox.</summary>
    Firefox = 1,
}
