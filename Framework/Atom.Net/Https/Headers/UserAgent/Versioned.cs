using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Представляет название продукта и его версию.
/// </summary>
public readonly struct Versioned : IEquatable<Versioned>
{
    /// <summary>
    /// Название.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Версия.
    /// </summary>
    public Version Version { get; init; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Versioned"/>.
    /// </summary>
    /// <param name="name">Название.</param>
    /// <param name="version">Версия.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Versioned(string name, Version version) => (Name, Version) = (name, version);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Versioned"/>.
    /// </summary>
    /// <param name="name">Название.</param>
    /// <param name="version">Версия.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Versioned(string name, string version) : this(name, new Version(version)) { }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override readonly int GetHashCode() => HashCode.Combine(Name.GetHashCode(StringComparison.Ordinal), Version.GetHashCode());

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Equals(Versioned other) => Name.Equals(other.Name, StringComparison.Ordinal) && Version.Equals(other.Version);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override readonly bool Equals(object? obj) => obj switch
    {
        Versioned other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Versioned left, Versioned right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Versioned left, Versioned right) => !(left == right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => $"{Name}/{Version}";
}