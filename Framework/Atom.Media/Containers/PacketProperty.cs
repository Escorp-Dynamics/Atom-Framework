#pragma warning disable CA1028

namespace Atom.Media;

/// <summary>
/// Свойства пакета.
/// </summary>
[Flags]
public enum PacketProperty : byte
{
    /// <summary>Нет свойств.</summary>
    None = 0,

    /// <summary>Ключевой кадр.</summary>
    Keyframe = 1 << 0,

    /// <summary>Данные повреждены.</summary>
    Corrupt = 1 << 1,

    /// <summary>Отбросить после декодирования (не показывать).</summary>
    Discard = 1 << 2,

    /// <summary>Конец потока.</summary>
    EndOfStream = 1 << 3,
}
