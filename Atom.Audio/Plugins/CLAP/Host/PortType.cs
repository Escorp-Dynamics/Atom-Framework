namespace Atom.Audio.Plugins.CLAP;

/// <summary>
/// Тип аудиопорта.
/// </summary>
[Flags]
public enum PortType : uint
{
    /// <summary>
    /// Аудио.
    /// </summary>
    Audio = 1 << 0,
    /// <summary>
    /// Напряжение.
    /// </summary>
    CV = 1 << 1,
    /// <summary>
    /// Нота.
    /// </summary>
    Note = 1 << 2,
    /// <summary>
    /// Событие.
    /// </summary>
    Event = 1 << 3,
    /// <summary>
    /// Параметр.
    /// </summary>
    Param = 1 << 4,
}