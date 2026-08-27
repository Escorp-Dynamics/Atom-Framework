using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Нестандартное расширение TLS (0x00FF): application_settings.
/// Используется Chrome в связке с HTTP/3/QUIC.
/// </summary>
public class ApplicationSettingsTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    /// <summary>
    /// Идентификатор расширения.
    /// </summary>
    /// <remarks>
    /// Значение 0x44CD (17613) — то, что отправляет современный Chrome. Более ранние сборки
    /// использовали 0x4469 (17513); подставить не тот номер значит объявить расширение, которого
    /// у заявленной версии браузера нет.
    /// </remarks>
    public override ushort Id { get; set; } = 0x44CD;

    /// <summary>
    /// Создаёт расширение со списком протоколов, для которых применимы настройки приложения.
    /// </summary>
    /// <param name="protocols">Протоколы в порядке предпочтения; Chrome отправляет один «h2».</param>
    /// <returns>Готовое расширение.</returns>
    /// <remarks>
    /// Полезная нагрузка повторяет форму ALPN: двухбайтовая длина списка, затем для каждого
    /// протокола однобайтовая длина и имя.
    /// </remarks>
    public static ApplicationSettingsTlsExtension Create(params ReadOnlyMemory<byte>[] protocols)
    {
        ArgumentNullException.ThrowIfNull(protocols);

        var listLength = 0;
        foreach (var protocol in protocols) listLength += 1 + protocol.Length;

        var payload = new byte[2 + listLength];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), (ushort)listLength);

        var position = 2;

        foreach (var protocol in protocols)
        {
            payload[position++] = (byte)protocol.Length;
            protocol.Span.CopyTo(payload.AsSpan(position));
            position += protocol.Length;
        }

        return new ApplicationSettingsTlsExtension { Data = payload };
    }

    /// <inheritdoc/>
    public override int Size => 2 + 2 + Data.Length;

    /// <summary>
    /// Данные расширения (opaque).
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; set; } = ReadOnlyMemory<byte>.Empty;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        // [Extension ID]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        // [Length]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)Data.Length);
        offset += 2;

        // [Data]
        Data.Span.CopyTo(buffer[offset..]);
        offset += Data.Length;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Data = ReadOnlyMemory<byte>.Empty;
        base.Reset();
    }
}