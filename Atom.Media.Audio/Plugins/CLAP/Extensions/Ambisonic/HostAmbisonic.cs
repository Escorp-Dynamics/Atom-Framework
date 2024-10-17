using System.Runtime.InteropServices;

namespace Atom.Audio.Plugins.CLAP.Extensions;

/// <summary>
/// Информирует хост о том, что информация изменилась.
/// Информация может измениться только тогда, когда плагин деактивирован.
/// [main-thread]
/// </summary>
/// <param name="host">Указатель на хост.</param>
internal delegate void HostAmbisonicChanged(nint host);

/// <summary>
/// Определяет интерфейс для амбисонического аудио в CLAP хосте.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct HostAmbisonic : IEquatable<HostAmbisonic>
{
    private nint host;

    /// <summary>
    /// Указатель на функцию, которая вызывается, когда информация изменяется.
    /// </summary>
    internal HostAmbisonicChanged changed;

    internal nint Host 
    {
        readonly get => host;
        set => host = value;
    }

    /// <summary>
    /// Указывает хосту, что информация об амбисоническом аудио изменяется в хосте CLAP.
    /// </summary>
    public readonly void Changed() => changed(host);
    
    /// <summary>
    /// Проверяет, равен ли текущий экземпляр другому экземпляру.
    /// </summary>
    /// <param name="other">Другой экземпляр для сравнения.</param>
    /// <returns>true, если текущий экземпляр равен другому экземпляру; в противном случае — false.</returns>
    public readonly bool Equals(HostAmbisonic other) => other.GetHashCode() == GetHashCode();

    /// <summary>
    /// Проверяет, равен ли текущий экземпляр другому экземпляру.
    /// </summary>
    /// <param name="obj">Другой экземпляр для сравнения.</param>
    /// <returns>true, если текущий экземпляр равен другому экземпляру; в противном случае — false.</returns>
    public override readonly bool Equals(object? obj)
    {
        if (obj is not HostAmbisonic other) return default;
        return Equals(other);
    }

    /// <summary>
    /// Возвращает хеш-код для текущего экземпляра.
    /// </summary>
    /// <returns>Хеш-код для текущего экземпляра.</returns>
    public override readonly int GetHashCode() => changed.GetHashCode();

    /// <summary>
    /// Оператор равенства для сравнения двух дескрипторов плагинов CLAP.
    /// </summary>
    /// <param name="left">Левый дескриптор плагина CLAP.</param>
    /// <param name="right">Правый дескриптор плагина CLAP.</param>
    /// <returns>True, если дескрипторы равны, иначе - false.</returns>
    public static bool operator ==(HostAmbisonic left, HostAmbisonic right) => left.Equals(right);

    /// <summary>
    /// Оператор не равенства для сравнения двух дескрипторов плагинов CLAP.
    /// </summary>
    /// <param name="left">Левый дескриптор плагина CLAP.</param>
    /// <param name="right">Правый дескриптор плагина CLAP.</param>
    /// <returns>True, если дескрипторы не равны, иначе - false.</returns>
    public static bool operator !=(HostAmbisonic left, HostAmbisonic right) => !(left == right);
}