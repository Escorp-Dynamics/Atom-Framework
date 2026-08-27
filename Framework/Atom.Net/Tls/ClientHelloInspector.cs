using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Atom.Net.Tls;

/// <summary>
/// Разбирает сырое сообщение ClientHello и вычисляет его отпечатки.
/// </summary>
/// <remarks>
/// Нужен для одной задачи: сверять СВОЙ ClientHello с эталонным, снятым у настоящего браузера.
/// Без такой сверки «мимикрия» остаётся утверждением, а не фактом — соединение остаётся рабочим
/// при любом наборе шифров и расширений, и отличие от браузера ничем себя не проявляет, пока не
/// упрётся в защиту.
///
/// Эталон снимается ровно так же, как и разбирается здесь: слушателем, который принимает
/// ClientHello и не отвечает. Способ не зависит ни от платформы, ни от того, чем является
/// клиент, — годится и для настольного браузера, и для телефона, направленного на этот адрес.
/// </remarks>
public sealed class ClientHelloInspector
{
    /// <summary>Наборы шифров в порядке отправки, включая GREASE.</summary>
    public IReadOnlyList<ushort> CipherSuites { get; private init; } = [];

    /// <summary>Типы расширений в порядке отправки, включая GREASE.</summary>
    public IReadOnlyList<ushort> Extensions { get; private init; } = [];

    /// <summary>Группы обмена ключами в порядке предпочтения.</summary>
    public IReadOnlyList<ushort> SupportedGroups { get; private init; } = [];

    /// <summary>Форматы точек эллиптических кривых.</summary>
    public IReadOnlyList<byte> EcPointFormats { get; private init; } = [];

    /// <summary>Алгоритмы подписи в порядке предпочтения.</summary>
    public IReadOnlyList<ushort> SignatureAlgorithms { get; private init; } = [];

    /// <summary>Версии протокола из supported_versions.</summary>
    public IReadOnlyList<ushort> SupportedVersions { get; private init; } = [];

    /// <summary>Протоколы ALPN в порядке предпочтения.</summary>
    public IReadOnlyList<string> AlpnProtocols { get; private init; } = [];

    /// <summary>Группы, для которых отправлена доля ключа.</summary>
    public IReadOnlyList<ushort> KeyShareGroups { get; private init; } = [];

    /// <summary>Имя узла из SNI, если оно есть.</summary>
    public string? ServerName { get; private init; }

    /// <summary>Длина поля legacy_session_id.</summary>
    public int SessionIdLength { get; private init; }

    /// <summary>Сырые параметры транспорта QUIC, если расширение присутствовало.</summary>
    public ReadOnlyMemory<byte> QuicTransportParameters { get; private init; }

    /// <summary>
    /// Разбирает сообщение ClientHello.
    /// </summary>
    /// <param name="message">Сообщение рукопожатия целиком, включая четырёхбайтовый заголовок.</param>
    /// <returns>Разобранное описание.</returns>
    public static ClientHelloInspector Parse(ReadOnlySpan<byte> message)
    {
        if (message.Length < 4 || message[0] is not 0x01) throw new InvalidOperationException("Это не ClientHello");

        var body = message[4..];
        var position = 2 + 32;

        if (body.Length < position + 1) throw new InvalidOperationException("ClientHello оборван");

        var sessionIdLength = body[position++];
        position += sessionIdLength;

        var cipherBytes = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(position, 2));
        position += 2;

        var ciphers = new List<ushort>(cipherBytes / 2);
        for (var offset = 0; offset < cipherBytes; offset += 2) ciphers.Add(BinaryPrimitives.ReadUInt16BigEndian(body.Slice(position + offset, 2)));

        position += cipherBytes;
        position += 1 + body[position];

        var extensions = new List<ushort>();
        var groups = new List<ushort>();
        var formats = new List<byte>();
        var signatures = new List<ushort>();
        var versions = new List<ushort>();
        var alpn = new List<string>();
        var keyShares = new List<ushort>();

        string? serverName = null;
        ReadOnlyMemory<byte> quicParameters = default;

        if (position + 2 <= body.Length)
        {
            var extensionsLength = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(position, 2));
            position += 2;

            var end = Math.Min(position + extensionsLength, body.Length);

            while (position + 4 <= end)
            {
                var id = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(position, 2));
                var length = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(position + 2, 2));
                position += 4;

                if (position + length > end) break;

                var payload = body.Slice(position, length);
                extensions.Add(id);

                switch (id)
                {
                    case 0x0000: serverName = ReadServerName(payload); break;
                    case 0x000A: ReadUInt16List(payload, prefixBytes: 2, groups); break;
                    case 0x000B: ReadByteList(payload, formats); break;
                    case 0x000D: ReadUInt16List(payload, prefixBytes: 2, signatures); break;
                    case 0x0010: ReadAlpn(payload, alpn); break;
                    case 0x002B: ReadUInt16List(payload, prefixBytes: 1, versions); break;
                    case 0x0033: ReadKeyShareGroups(payload, keyShares); break;
                    case 0x0039: quicParameters = payload.ToArray(); break;
                    default: break;
                }

                position += length;
            }
        }

        return new ClientHelloInspector
        {
            CipherSuites = ciphers,
            Extensions = extensions,
            SupportedGroups = groups,
            EcPointFormats = formats,
            SignatureAlgorithms = signatures,
            SupportedVersions = versions,
            AlpnProtocols = alpn,
            KeyShareGroups = keyShares,
            ServerName = serverName,
            SessionIdLength = sessionIdLength,
            QuicTransportParameters = quicParameters,
        };
    }

    /// <summary>
    /// Вычисляет строку JA3.
    /// </summary>
    /// <returns>Строка вида «версия,шифры,расширения,группы,форматы».</returns>
    /// <remarks>
    /// Значения GREASE исключаются: они случайны на каждое соединение, и без их удаления отпечаток
    /// не совпал бы даже у браузера с самим собой.
    /// </remarks>
    public string ComputeJa3()
    {
        // JA3 всегда записывает 771: это legacy_version из заголовка ClientHello, а не реально
        // согласуемая версия — та живёт в supported_versions и в отпечаток JA3 не входит.
        const int LegacyVersion = 771;

        var ciphers = string.Join('-', CipherSuites.Where(static value => !IsGrease(value)));
        var extensions = string.Join('-', Extensions.Where(static value => !IsGrease(value)));
        var groups = string.Join('-', SupportedGroups.Where(static value => !IsGrease(value)));
        var formats = string.Join('-', EcPointFormats);

        return string.Create(CultureInfo.InvariantCulture, $"{LegacyVersion},{ciphers},{extensions},{groups},{formats}");
    }

    /// <summary>Вычисляет хэш JA3.</summary>
    /// <returns>Шестнадцатеричная строка MD5.</returns>
    /// <remarks>
    /// MD5 здесь не выбор и не защита: так определён сам отпечаток JA3, и другой хэш дал бы
    /// значение, несравнимое ни с одной публикацией и ни с одним сервисом.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351:Do not use broken cryptographic algorithms", Justification = "Хэш JA3 определён на MD5; заменить его — значит получить величину, несравнимую с эталонами.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S4790:Using weak hashing algorithms is security-sensitive", Justification = "См. CA5351: формат отпечатка, а не защита.")]
    public string ComputeJa3Hash() => Convert.ToHexStringLower(MD5.HashData(Encoding.ASCII.GetBytes(ComputeJa3())));

    /// <summary>
    /// Вычисляет отпечаток JA4.
    /// </summary>
    /// <param name="isQuic">Снят ли ClientHello с транспорта QUIC.</param>
    /// <returns>Строка вида <c>t13d1516h2_..._...</c>.</returns>
    /// <remarks>
    /// В отличие от JA3, здесь наборы и расширения СОРТИРУЮТСЯ, поэтому отпечаток устойчив к
    /// перестановкам — а современный Chrome перемешивает порядок расширений на каждое соединение.
    /// Из счётчика расширений исключаются SNI и ALPN: первое зависит от узла, второе выносится в
    /// отдельное поле.
    /// </remarks>
    public string ComputeJa4(bool isQuic = false)
    {
        var ciphers = CipherSuites.Where(static value => !IsGrease(value)).ToArray();
        var extensions = Extensions.Where(static value => !IsGrease(value)).ToArray();

        var highest = SupportedVersions.Where(static value => !IsGrease(value)).DefaultIfEmpty((ushort)0x0303).Max();

        var version = highest switch
        {
            0x0304 => "13",
            0x0303 => "12",
            0x0302 => "11",
            _ => "10",
        };

        var sni = ServerName is null ? 'i' : 'd';
        var alpn = AlpnProtocols.Count is 0 ? "00" : $"{AlpnProtocols[0][0]}{AlpnProtocols[0][^1]}";

        // В СЧЁТЧИК входят все расширения, кроме GREASE, — включая SNI и ALPN. А вот из ХЭША их
        // исключают: имя узла зависит от адреса, а протокол вынесен в отдельное поле отпечатка.
        // Сверено с независимой реализацией: счётчик 16 при 14 хэшируемых.
        var hashed = extensions.Where(static value => value is not 0x0000 and not 0x0010).ToArray();

        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"{(isQuic ? 'q' : 't')}{version}{sni}{Math.Min(ciphers.Length, 99):00}{Math.Min(extensions.Length, 99):00}{alpn}");

        var cipherPart = Truncate(string.Join(',', ciphers.Select(static value => value.ToString("x4", CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal)));

        var signatures = string.Join(',', SignatureAlgorithms.Where(static value => !IsGrease(value)).Select(static value => value.ToString("x4", CultureInfo.InvariantCulture)));
        var extensionList = string.Join(',', hashed.Select(static value => value.ToString("x4", CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal));
        var extensionPart = Truncate(signatures.Length is 0 ? extensionList : extensionList + '_' + signatures);

        return $"{header}_{cipherPart}_{extensionPart}";
    }

    /// <summary>
    /// Является ли значение зарезервированным под GREASE (RFC 8701).
    /// </summary>
    /// <param name="value">Проверяемое значение.</param>
    /// <returns><see langword="true"/>, если значение из таблицы GREASE.</returns>
    public static bool IsGrease(ushort value) => (value & 0x0F0F) is 0x0A0A && (value >> 8) == (value & 0xFF);

    private static string Truncate(string source)
        => source.Length is 0
            ? "000000000000"
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(source)))[..12];

    private static string? ReadServerName(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 5) return null;

        var nameLength = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(3, 2));

        return 5 + nameLength <= payload.Length ? Encoding.ASCII.GetString(payload.Slice(5, nameLength)) : null;
    }

    private static void ReadUInt16List(ReadOnlySpan<byte> payload, int prefixBytes, List<ushort> destination)
    {
        if (payload.Length < prefixBytes) return;

        var length = prefixBytes is 1 ? payload[0] : BinaryPrimitives.ReadUInt16BigEndian(payload[..2]);
        var end = Math.Min(prefixBytes + length, payload.Length);

        for (var offset = prefixBytes; offset + 2 <= end; offset += 2) destination.Add(BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset, 2)));
    }

    private static void ReadByteList(ReadOnlySpan<byte> payload, List<byte> destination)
    {
        if (payload.Length < 1) return;

        var end = Math.Min(1 + payload[0], payload.Length);
        for (var offset = 1; offset < end; offset++) destination.Add(payload[offset]);
    }

    private static void ReadAlpn(ReadOnlySpan<byte> payload, List<string> destination)
    {
        if (payload.Length < 2) return;

        var end = Math.Min(2 + BinaryPrimitives.ReadUInt16BigEndian(payload[..2]), payload.Length);
        var offset = 2;

        while (offset < end)
        {
            var length = payload[offset++];
            if (offset + length > end) break;

            destination.Add(Encoding.ASCII.GetString(payload.Slice(offset, length)));
            offset += length;
        }
    }

    private static void ReadKeyShareGroups(ReadOnlySpan<byte> payload, List<ushort> destination)
    {
        if (payload.Length < 2) return;

        var end = Math.Min(2 + BinaryPrimitives.ReadUInt16BigEndian(payload[..2]), payload.Length);
        var offset = 2;

        while (offset + 4 <= end)
        {
            destination.Add(BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset, 2)));

            var keyLength = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset + 2, 2));
            offset += 4 + keyLength;
        }
    }
}
