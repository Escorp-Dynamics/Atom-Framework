using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Https.Http2;

/// <summary>
/// Идентификатор параметра соединения HTTP/2 (RFC 9113, §6.5.2).
/// </summary>
/// <remarks>
/// Значения заданы спецификацией и начинаются с единицы: нулевого параметра в протоколе нет, а
/// набор не является битовой маской — это перечень идентификаторов, а не флаги. Поэтому обычные
/// для перечислений требования «нулевой член» и «атрибут флагов» здесь неприменимы.
///
/// Порядок и состав отправляемых параметров — часть отпечатка HTTP/2: он входит в описания вида
/// «Akamai fingerprint» и различается между браузерами. Поэтому параметры перечисляются профилем
/// явно, а не собираются по усмотрению реализации.
/// </remarks>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "Нулевого параметра в RFC 9113 не существует.")]
[SuppressMessage("Design", "CA1027:Mark enums with FlagsAttribute", Justification = "Это перечень идентификаторов, а не битовая маска.")]
public enum Http2SettingId : ushort
{
    /// <summary>Размер таблицы HPACK.</summary>
    HeaderTableSize = 0x01,

    /// <summary>Разрешён ли server push.</summary>
    EnablePush = 0x02,

    /// <summary>Предел одновременных потоков.</summary>
    MaxConcurrentStreams = 0x03,

    /// <summary>Начальный размер окна потока.</summary>
    InitialWindowSize = 0x04,

    /// <summary>Максимальный размер кадра.</summary>
    MaxFrameSize = 0x05,

    /// <summary>Предел размера списка заголовков.</summary>
    MaxHeaderListSize = 0x06,

    /// <summary>Разрешён ли расширенный CONNECT (RFC 8441).</summary>
    EnableConnectProtocol = 0x08,

    /// <summary>Отключение приоритизации RFC 9218.</summary>
    NoRfc7540Priorities = 0x09,
}
