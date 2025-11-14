using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Atom.Text.Json;

namespace Atom.Web.Analytics;

/// <summary>
/// Представляет данные континента.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Continent"/>.
/// </remarks>
/// <param name="name">Название (на русском).</param>
/// <param name="internationalName">Название (интернациональное).</param>
/// <param name="code">Двухсимвольный код страны.</param>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
[JsonConverter(typeof(ContinentJsonConverter))]
[JsonContext(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
)]
public sealed partial class Continent
(
    string name,
    string internationalName,
    string code
) : IParsable<Continent?>, IEquatable<Continent>
{
    /// <summary>
    /// Название.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Название (интернациональное).
    /// </summary>
    public string InternationalName { get; } = internationalName;

    /// <summary>
    /// Двухсимвольный код страны.
    /// </summary>
    public string Code { get; } = code;

    #region Инициализации

    private static readonly Lazy<Continent> af = new(() => new Continent("Африка", "Africa", "AF"), isThreadSafe: true);

    private static readonly Lazy<Continent> an = new(() => new Continent("Антарктида", "Antarctica", "AN"), isThreadSafe: true);

    private static readonly Lazy<Continent> @as = new(() => new Continent("Азия", "Asia", "AS"), isThreadSafe: true);

    private static readonly Lazy<Continent> eu = new(() => new Continent("Европа", "Europe", "EU"), isThreadSafe: true);

    private static readonly Lazy<Continent> na = new(() => new Continent("Северная Америка", "North America", "NA"), isThreadSafe: true);

    private static readonly Lazy<Continent> oc = new(() => new Continent("Океания", "Oceania", "OC"), isThreadSafe: true);

    private static readonly Lazy<Continent> sa = new(() => new Continent("Южная Америка", "South America", "SA"), isThreadSafe: true);

    #endregion

    #region Коды

    /// <summary>
    /// Африка.
    /// </summary>
    public static Continent AF => af.Value;

    /// <summary>
    /// Антарктида.
    /// </summary>
    public static Continent AN => an.Value;

    /// <summary>
    /// Азия.
    /// </summary>
    public static Continent AS => @as.Value;

    /// <summary>
    /// Европа.
    /// </summary>
    public static Continent EU => eu.Value;

    /// <summary>
    /// Северная Америка.
    /// </summary>
    public static Continent NA => na.Value;

    /// <summary>
    /// Океания.
    /// </summary>
    public static Continent OC => oc.Value;

    /// <summary>
    /// Южная Америка.
    /// </summary>
    public static Continent SA => sa.Value;

    #endregion

    /// <summary>
    /// Коллекция всех континентов.
    /// </summary>
    public static IEnumerable<Continent> All => [AF, AN, AS, EU, NA, OC, SA];

    /// <summary>
    /// Возвращает хеш-код для объекта.
    /// </summary>
    /// <returns>Хеш-код для объекта.</returns>
    public override int GetHashCode() => Code.GetHashCode(StringComparison.InvariantCultureIgnoreCase);

    /// <summary>
    /// Сравнивает текущий экземпляр <see cref="Continent"/> с заданным объектом.
    /// </summary>
    /// <param name="obj">Объект для сравнения.</param>
    /// <returns>
    /// <c>True</c>, если хеш-коды объектов совпадают, иначе <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj)
    {
        if (obj is string str)
        {
            if (str.Length is 2) return str.GetHashCode(StringComparison.InvariantCultureIgnoreCase) == Code.GetHashCode(StringComparison.InvariantCultureIgnoreCase);
            return false;
        }

        if (obj is Continent continent) return continent.GetHashCode() == GetHashCode();
        return false;
    }

    /// <summary>
    /// Сравнивает текущий экземпляр <see cref="Continent"/> с заданным экземпляром <see cref="Continent"/>.
    /// </summary>
    /// <param name="other">Экземпляр <see cref="Continent"/> для сравнения.</param>
    /// <returns>
    /// <c>True</c>, если хеш-коды объектов совпадают, иначе <see langword="false"/>.
    /// </returns>
    public bool Equals(Continent? other) => Equals(other as object);

    /// <summary>
    /// Преобразует текущий экземпляр <see cref="Continent"/> в трехсимвольный код страны.
    /// </summary>
    /// <returns>Трехсимвольный код страны.</returns>
    public override string ToString() => Code;

    /// <summary>
    /// Возвращает экземпляр <see cref="Continent"/> по его символьному коду.
    /// </summary>
    /// <param name="s">Символьный код континента.</param>
    /// <param name="provider">Параметры форматирования.</param>
    /// <param name="result">Экземпляр <see cref="Continent"/>.</param>
    /// <returns><c>True</c>, если экземпляр был найден, иначе <see langword="false"/>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Continent? result)
    {
        result = s?.Trim().ToUpperInvariant() switch
        {
            "AF" => AF,
            "AN" => AN,
            "AS" => AS,
            "EU" => EU,
            "NA" => NA,
            "OC" => OC,
            "SA" => SA,
            _ => default,
        };

        return result is not null;
    }

    /// <summary>
    /// Возвращает экземпляр <see cref="Continent"/> по его символьному коду.
    /// </summary>
    /// <param name="s">Символьный код континента.</param>
    /// <param name="result">Экземпляр <see cref="Continent"/>.</param>
    /// <returns><c>True</c>, если экземпляр был найден, иначе <see langword="false"/>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out Continent? result) => TryParse(s, default, out result);

    /// <summary>
    /// Возвращает экземпляр <see cref="Continent"/> по его символьному коду. 
    /// </summary>
    /// <param name="s">Символьный код континента.</param>
    /// <param name="provider">Параметры форматирования.</param>
    /// <returns>Экземпляр <see cref="Continent"/>.</returns>
    /// <exception cref="FormatException"/>
    public static Continent Parse(string s, IFormatProvider? provider) => !TryParse(s, provider, out var result) || result is null ? throw new FormatException() : result;

    /// <summary>
    /// Возвращает экземпляр <see cref="Continent"/> по его символьному коду. 
    /// </summary>
    /// <param name="s">Символьный код континента.</param>
    /// <returns>Экземпляр <see cref="Continent"/>.</returns>
    /// <exception cref="FormatException"/>
    public static Continent Parse(string s) => Parse(s, default);

    /// <summary>
    /// Приводит символьный код страны к типу <see cref="Continent"/>.
    /// </summary>
    /// <param name="isoCode">Символьный код континента.</param>
    /// <returns>Экземпляр <see cref="Continent"/>.</returns>
    /// <exception cref="FormatException"/>
    public static Continent FromString(string isoCode) => Parse(isoCode);

    /// <summary>
    /// Приводит символьный код страны к типу <see cref="Continent"/>.
    /// </summary>
    /// <param name="isoCode">Символьный код континента.</param>
    public static explicit operator Continent(string isoCode) => FromString(isoCode);

    /// <summary>
    /// Неявно преобразует экземпляр <see cref="Continent"/> в символьный код страны.
    /// </summary>
    /// <param name="continent">Экземпляр <see cref="Continent"/>.</param>
    public static implicit operator string(Continent? continent) => continent is not null ? continent.ToString() : string.Empty;

    /// <summary>
    /// Сравнивает экземпляр <see cref="Continent"/> с заданной строкой.
    /// </summary>
    /// <param name="continent">Экземпляр <see cref="Continent"/>.</param>
    /// <param name="str">Строка для сравнения.</param>
    /// <returns>
    /// <c>True</c>, если хеш-коды объектов совпадают, иначе <see langword="false"/>.
    /// </returns>
    public static bool operator ==(Continent? continent, string? str) => (continent is null && str is null) || (continent is not null && str is not null && continent.Equals(str));

    /// <summary>
    /// Сравнивает экземпляр <see cref="Continent"/> с заданной строкой.
    /// </summary>
    /// <param name="continent">Экземпляр <see cref="Continent"/>.</param>
    /// <param name="str">Строка для сравнения.</param>
    /// <returns>
    /// <c>True</c>, если хеш-коды объектов не совпадают, иначе <see langword="false"/>.
    /// </returns>
    public static bool operator !=(Continent? continent, string? str) => !(continent == str);
}