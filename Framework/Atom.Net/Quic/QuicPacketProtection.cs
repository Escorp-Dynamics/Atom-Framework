using System.Buffers.Binary;

namespace Atom.Net.Quic;

/// <summary>
/// Защита и снятие защиты с пакета QUIC (RFC 9001, §5.3–5.4).
/// </summary>
/// <remarks>
/// Порядок операций жёстко задан и НЕ симметричен по очевидности.
///
/// При отправке сначала шифруется полезная нагрузка (в дополнительные данные входит заголовок с
/// ОТКРЫТЫМ номером пакета), и только потом маскируется сам заголовок. При приёме наоборот:
/// сначала снимается маска — иначе неизвестна длина номера пакета, а значит, и граница между
/// заголовком и нагрузкой.
///
/// Выборка для маски берётся с фиксированного места: четыре байта после начала номера пакета.
/// Именно четыре, независимо от реальной длины номера, — поэтому нагрузка обязана быть не короче
/// четырёх байт плюс размер выборки.
/// </remarks>
public static class QuicPacketProtection
{
    /// <summary>Длина выборки для маски защиты заголовка.</summary>
    public const int SampleLength = 16;

    /// <summary>
    /// Шифрует пакет на месте и маскирует его заголовок.
    /// </summary>
    /// <param name="packet">Весь пакет: заголовок, номер и открытая нагрузка.</param>
    /// <param name="packetNumberOffset">Смещение номера пакета от начала пакета.</param>
    /// <param name="packetNumberLength">Длина закодированного номера пакета: 1–4.</param>
    /// <param name="payloadLength">Длина ОТКРЫТОЙ нагрузки после номера пакета.</param>
    /// <param name="packetNumber">Полный номер пакета.</param>
    /// <param name="keys">Ключи направления.</param>
    /// <returns>Полная длина защищённого пакета.</returns>
    public static int Protect(
        Span<byte> packet,
        int packetNumberOffset,
        int packetNumberLength,
        int payloadLength,
        ulong packetNumber,
        QuicKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var headerLength = packetNumberOffset + packetNumberLength;
        var tagLength = keys.Cipher.TagSize;

        Span<byte> nonce = stackalloc byte[QuicKeys.IvLength];
        keys.BuildNonce(packetNumber, nonce);

        // Заголовок целиком, включая ОТКРЫТЫЙ номер пакета, входит в дополнительные данные:
        // так подпись покрывает и служебные поля, которые дальше будут замаскированы.
        var plaintext = packet.Slice(headerLength, payloadLength).ToArray();
        var destination = packet.Slice(headerLength, payloadLength + tagLength);

        if (!keys.Cipher.TryEncrypt(nonce, packet[..headerLength], plaintext, destination, out var written) || written != payloadLength + tagLength)
            throw new InvalidOperationException("Не удалось зашифровать пакет QUIC");

        ApplyHeaderProtection(packet, packetNumberOffset, packetNumberLength, keys, protect: true);

        return headerLength + payloadLength + tagLength;
    }

    /// <summary>
    /// Снимает защиту с полученного пакета.
    /// </summary>
    /// <param name="packet">Пакет целиком, начиная с первого байта заголовка.</param>
    /// <param name="packetNumberOffset">Смещение номера пакета.</param>
    /// <param name="largestAcknowledged">Наибольший подтверждённый номер для восстановления усечённого.</param>
    /// <param name="keys">Ключи направления.</param>
    /// <param name="packetNumber">Восстановленный полный номер пакета.</param>
    /// <param name="payload">Расшифрованная нагрузка.</param>
    /// <returns><see langword="true"/>, если пакет расшифрован.</returns>
    public static bool TryUnprotect(
        Span<byte> packet,
        int packetNumberOffset,
        ulong largestAcknowledged,
        QuicKeys keys,
        out ulong packetNumber,
        out byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(keys);

        packetNumber = 0;
        payload = [];

        if (packet.Length < packetNumberOffset + 4 + SampleLength) return false;

        var packetNumberLength = ApplyHeaderProtection(packet, packetNumberOffset, packetNumberLength: 4, keys, protect: false);
        var headerLength = packetNumberOffset + packetNumberLength;

        var truncated = 0UL;
        for (var index = 0; index < packetNumberLength; index++) truncated = (truncated << 8) | packet[packetNumberOffset + index];

        packetNumber = DecodePacketNumber(truncated, packetNumberLength, largestAcknowledged);

        var tagLength = keys.Cipher.TagSize;
        if (packet.Length < headerLength + tagLength) return false;

        Span<byte> nonce = stackalloc byte[QuicKeys.IvLength];
        keys.BuildNonce(packetNumber, nonce);

        var protectedLength = packet.Length - headerLength;
        var plaintext = new byte[protectedLength - tagLength];

        if (!keys.Cipher.TryDecrypt(nonce, packet[..headerLength], packet[headerLength..], plaintext, out var written) || written != plaintext.Length)
            return false;

        payload = plaintext;
        return true;
    }

    /// <summary>
    /// Накладывает или снимает маску защиты заголовка.
    /// </summary>
    /// <returns>Длина номера пакета: при снятии маски она становится известна только здесь.</returns>
    /// <remarks>
    /// Маскируются младшие биты первого байта — четыре у длинного заголовка и пять у короткого,
    /// потому что короткий несёт ещё и бит фазы ключа, — и все байты номера пакета.
    /// </remarks>
    private static int ApplyHeaderProtection(Span<byte> packet, int packetNumberOffset, int packetNumberLength, QuicKeys keys, bool protect)
    {
        var sampleOffset = packetNumberOffset + 4;

        Span<byte> mask = stackalloc byte[5];
        keys.ComputeHeaderMask(packet.Slice(sampleOffset, SampleLength), mask);

        var isLongHeader = (packet[0] & 0x80) is not 0;
        var firstByteMask = (byte)(isLongHeader ? 0x0F : 0x1F);

        packet[0] ^= (byte)(mask[0] & firstByteMask);

        // Длину номера пакета несут два младших бита первого байта. При отправке она известна
        // заранее; при приёме прочитать её можно ТОЛЬКО после снятия маски с первого байта.
        var length = protect ? packetNumberLength : (packet[0] & 0x03) + 1;

        for (var index = 0; index < length; index++)
            packet[packetNumberOffset + index] ^= mask[1 + index];

        return length;
    }

    /// <summary>
    /// Восстанавливает полный номер пакета из усечённого (RFC 9000, приложение A).
    /// </summary>
    /// <param name="truncated">Усечённое значение из пакета.</param>
    /// <param name="length">Число байт, которыми записан номер.</param>
    /// <param name="largestAcknowledged">Наибольший уже принятый номер.</param>
    /// <returns>Полный номер пакета.</returns>
    /// <remarks>
    /// В пакете передаются только младшие биты. Полное значение выбирается как ближайшее к
    /// ожидаемому — то есть к следующему за наибольшим принятым. Наивная склейка старших битов
    /// ошибается ровно на границе окна, и ошибка проявляется провалом расшифровки: nonce
    /// строится из номера.
    /// </remarks>
    public static ulong DecodePacketNumber(ulong truncated, int length, ulong largestAcknowledged)
    {
        var bits = length * 8;
        var window = 1UL << bits;
        var halfWindow = window / 2;
        var expected = largestAcknowledged + 1;

        var candidate = (expected & ~(window - 1)) | truncated;

        if (candidate + halfWindow <= expected && candidate + window < (1UL << 62)) return candidate + window;
        if (candidate > expected + halfWindow && candidate >= window) return candidate - window;

        return candidate;
    }

    /// <summary>
    /// Определяет, сколькими байтами записать номер пакета.
    /// </summary>
    /// <param name="packetNumber">Отправляемый номер.</param>
    /// <param name="largestAcknowledged">Наибольший подтверждённый номер или -1, если подтверждений не было.</param>
    /// <returns>Число байт: 1–4.</returns>
    /// <remarks>
    /// Записать надо столько, чтобы получатель однозначно восстановил значение: диапазон между
    /// отправляемым и подтверждённым номерами обязан укладываться в половину окна.
    /// </remarks>
    public static int GetPacketNumberLength(ulong packetNumber, long largestAcknowledged)
    {
        var range = largestAcknowledged < 0 ? packetNumber + 1 : packetNumber - (ulong)largestAcknowledged;
        var doubled = range * 2;

        if (doubled < 1UL << 8) return 1;
        if (doubled < 1UL << 16) return 2;
        if (doubled < 1UL << 24) return 3;

        return 4;
    }

    /// <summary>
    /// Записывает номер пакета усечённым до указанной длины.
    /// </summary>
    /// <param name="destination">Буфер.</param>
    /// <param name="packetNumber">Номер пакета.</param>
    /// <param name="length">Число байт.</param>
    public static void WritePacketNumber(Span<byte> destination, ulong packetNumber, int length)
    {
        unchecked
        {
            for (var index = 0; index < length; index++)
                destination[index] = (byte)(packetNumber >> ((length - 1 - index) * 8));
        }
    }

    /// <summary>
    /// Читает 32-битную версию из длинного заголовка.
    /// </summary>
    /// <param name="packet">Пакет.</param>
    /// <returns>Значение поля версии.</returns>
    public static uint ReadVersion(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(1, 4));
}
