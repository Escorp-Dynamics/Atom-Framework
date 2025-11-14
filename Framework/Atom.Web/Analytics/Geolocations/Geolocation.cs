#pragma warning disable S4035

using System.Text.Json.Serialization;
using Atom.Buffers;
using Atom.Text.Json;

namespace Atom.Web.Analytics;

/// <summary>
/// Представляет данные о геолокации.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Geolocation"/>.
/// </remarks>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
[JsonConverter(typeof(GeolocationJsonConverter))]
[JsonContext(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
)]
public partial class Geolocation : IEquatable<Geolocation>
{
    /// <summary>
    /// Страна геолокации.
    /// </summary>
    public Country? Country { get; set; }

    /// <summary>
    /// Регион геолокации.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Город геолокации.
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// Широта геолокации.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Долгота геолокации.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Высота над уровнем моря геолокации.
    /// </summary>
    public double? Altitude { get; set; }

    /// <summary>
    /// Возвращает хеш-код объекта <see cref="Geolocation"/>.
    /// </summary>
    public override int GetHashCode() => Latitude.GetHashCode() ^ Longitude.GetHashCode();

    /// <inheritdoc/>
    [Pooled]
    public virtual void Reset()
    {
        Country = default;
        Region = default;
        City = default;
        Latitude = default;
        Longitude = default;
        Altitude = default;
    }

    /// <summary>
    /// Сравнивает текущий объект <see cref="Geolocation"/> с заданным объектом.
    /// </summary>
    /// <param name="obj">Объект, с которым нужно сравнить текущий объект.</param>
    /// <returns>Возвращает true, если заданный объект является экземпляром <see cref="Geolocation"/> и его значения свойств совпадают с текущим объектом; в противном случае возвращает false.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is not Geolocation g) return default;
        return Equals(g);
    }

    /// <summary>
    /// Сравнивает текущий объект <see cref="Geolocation"/> с заданным объектом <see cref="Geolocation"/>.
    /// </summary>
    /// <param name="other">Объект <see cref="Geolocation"/>, с которым нужно сравнить текущий объект.</param>
    /// <returns>Возвращает true, если заданный объект не равен null и его значения свойств совпадают с текущим объектом; в противном случае возвращает false.</returns>
    public bool Equals(Geolocation? other)
    {
        if (other is null) return default;
        return other.GetHashCode() == GetHashCode();
    }
}