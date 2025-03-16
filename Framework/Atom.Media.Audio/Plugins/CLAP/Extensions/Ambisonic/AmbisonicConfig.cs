using System.Runtime.InteropServices;

namespace Atom.Audio.Plugins.CLAP.Extensions.Ambisonic;

/// <summary>
/// Представляет конфигурацию Ambisonic.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct AmbisonicConfig : IEquatable<AmbisonicConfig>
{
    internal uint ordering;

    internal uint normalization;

    /// <summary>
    /// Порядок каналов Ambisonic.
    /// </summary>
    public AmbisonicOrdering Ordering
    {
        readonly get => (AmbisonicOrdering)ordering;
        set => ordering = (uint)value;
    }

    /// <summary>
    /// Метод нормализации Ambisonic.
    /// </summary>
    public AmbisonicNormalization Normalization
    {
        readonly get => (AmbisonicNormalization)ordering;
        set => ordering = (uint)value;
    }

    /// <summary>
    /// Проверяет, равен ли текущий экземпляр другому экземпляру.
    /// </summary>
    /// <param name="other">Другой экземпляр для сравнения.</param>
    /// <returns>true, если текущий экземпляр равен другому экземпляру; в противном случае — false.</returns>
    public readonly bool Equals(AmbisonicConfig other) => other.GetHashCode() == GetHashCode();

    /// <summary>
    /// Проверяет, равен ли текущий экземпляр другому экземпляру.
    /// </summary>
    /// <param name="obj">Другой экземпляр для сравнения.</param>
    /// <returns>true, если текущий экземпляр равен другому экземпляру; в противном случае — false.</returns>
    public override readonly bool Equals(object? obj)
    {
        if (obj is not AmbisonicConfig other) return default;
        return Equals(other);
    }

    /// <summary>
    /// Возвращает хеш-код для текущего экземпляра.
    /// </summary>
    /// <returns>Хеш-код для текущего экземпляра.</returns>
    public override readonly int GetHashCode()
    {
        var hashCode = ordering.GetHashCode();
        hashCode ^= normalization.GetHashCode();
        return hashCode;
    }

    /// <summary>
    /// Оператор равенства для сравнения двух дескрипторов плагинов CLAP.
    /// </summary>
    /// <param name="left">Левый дескриптор плагина CLAP.</param>
    /// <param name="right">Правый дескриптор плагина CLAP.</param>
    /// <returns>True, если дескрипторы равны, иначе - false.</returns>
    public static bool operator ==(AmbisonicConfig left, AmbisonicConfig right) => left.Equals(right);

    /// <summary>
    /// Оператор не равенства для сравнения двух дескрипторов плагинов CLAP.
    /// </summary>
    /// <param name="left">Левый дескриптор плагина CLAP.</param>
    /// <param name="right">Правый дескриптор плагина CLAP.</param>
    /// <returns>True, если дескрипторы не равны, иначе - false.</returns>
    public static bool operator !=(AmbisonicConfig left, AmbisonicConfig right) => !(left == right);
}