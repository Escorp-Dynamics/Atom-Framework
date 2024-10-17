using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Atom.Reactive;

namespace Atom.Web.Analytics;

/// <summary>
/// Представляет данные о геолокации.
/// </summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
//[JsonConverter(typeof(GeolocationJsonConverter))]
[Serializable]
public class Geolocation : Reactively, IParsable<Geolocation?>, IEquatable<Geolocation>, ISerializable
{
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
    public override int GetHashCode() => base.GetHashCode();

    /// <summary>
    /// Сравнивает текущий объект <see cref="Geolocation"/> с заданным объектом.
    /// </summary>
    /// <param name="obj">Объект, с которым нужно сравнить текущий объект.</param>
    /// <returns>Возвращает true, если заданный объект является экземпляром <see cref="Geolocation"/> и его значения свойств совпадают с текущим объектом; в противном случае возвращает false.</returns>
    public override bool Equals(object? obj) => base.Equals(obj);

    /// <summary>
    /// Сравнивает текущий объект <see cref="Geolocation"/> с заданным объектом <see cref="Geolocation"/>.
    /// </summary>
    /// <param name="other">Объект <see cref="Geolocation"/>, с которым нужно сравнить текущий объект.</param>
    /// <returns>Возвращает true, если заданный объект не равен null и его значения свойств совпадают с текущим объектом; в противном случае возвращает false.</returns>
    public virtual bool Equals(Geolocation? other) => default; // TODO: Доработать.

    /// <summary>
    /// Заполняет <see cref="SerializationInfo"/> данными о текущем объекте <see cref="Geolocation"/>.
    /// </summary>
    /// <param name="info">Объект, который содержит все данные, необходимые для сериализации или десериализации объекта.</param>
    /// <param name="context">Контекст, в котором происходит сериализация или десериализация.</param>
    public virtual void GetObjectData(SerializationInfo info, StreamingContext context) => throw new NotImplementedException();

    /// <summary>
    /// Пытается преобразовать заданную строку в экземпляр класса <see cref="Geolocation"/> с использованием указанного формата и провайдера.
    /// </summary>
    /// <param name="s">Строка, которую необходимо преобразовать в экземпляр класса <see cref="Geolocation"/>.</param>
    /// <param name="provider">Объект, предоставляющий сведения о форматировании.</param>
    /// <param name="result">Когда метод возвращается, содержит экземпляр класса <see cref="Geolocation"/>, если преобразование прошло успешно; в противном случае - null.
    /// Если преобразование прошло успешно, этот параметр будет содержать экземпляр класса <see cref="Geolocation"/>; в противном случае - null.
    /// </param>
    /// <returns>Возвращает true, если преобразование прошло успешно; в противном случае - false.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Geolocation? result) => throw new NotImplementedException();

    /// <summary>
    /// Пытается преобразовать заданную строку в экземпляр класса <see cref="Geolocation"/>.
    /// </summary>
    /// <param name="s">Строка, которую необходимо преобразовать в экземпляр класса <see cref="Geolocation"/>.</param>
    /// <param name="result">Когда метод возвращается, содержит экземпляр класса <see cref="Geolocation"/>, если преобразование прошло успешно; в противном случае - null.
    /// Если преобразование прошло успешно, этот параметр будет содержать экземпляр класса <see cref="Geolocation"/>; в противном случае - null.</param>
    /// <returns>Возвращает true, если преобразование прошло успешно; в противном случае - false.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out Geolocation? result) => TryParse(s, default, out result);

    /// <summary>
    /// Пытается преобразовать заданную строку в экземпляр класса <see cref="Geolocation"/> с использованием указанного формата и провайдера.
    /// </summary>
    /// <param name="s">Строка, которую необходимо преобразовать в экземпляр класса <see cref="Geolocation"/>.</param>
    /// <param name="provider">Объект, предоставляющий сведения о форматировании.</param>
    /// <returns>Возвращает экземпляр класса <see cref="Geolocation"/>, если преобразование прошло успешно; в противном случае - null.</returns>
    public static Geolocation? Parse(string s, IFormatProvider? provider) => throw new NotImplementedException();
}