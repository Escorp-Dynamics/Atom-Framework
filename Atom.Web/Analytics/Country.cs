using Atom.Reactive;

namespace Atom.Web.Analytics;

/// <summary>
/// Данные о стране.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Country"/>.
/// </remarks>
/// <param name="code">Двухсимвольный код страны.</param>
/// <param name="isoCode">Трёхсимвольный код страны.</param>
/// <param name="currency">Валюта страны.</param>
/// <param name="dialCode">Международный телефонный код страны.</param>
public class Country(string code, string isoCode, Currency currency, ushort dialCode) : Reactively
{
    //private static readonly Lazy<Country> Rus = new(() => new Country("RU", "RUS", Currency.RUB, 7), true);

    /// <summary>
    /// Двухсимвольный код страны.
    /// </summary>
    public string Code
    {
        get => code;
        set => SetProperty(ref code, value);
    }

    /// <summary>
    /// Трёхсимвольный код страны.
    /// </summary>
    public string IsoCode
    {
        get => isoCode;
        set => SetProperty(ref isoCode, value);
    }

    /// <summary>
    /// Валюта страны.
    /// </summary>
    public Currency Currency
    {
        get => currency;
        set => SetProperty(ref currency, value);
    }

    /// <summary>
    /// Международный телефонный код страны.
    /// </summary>
    public ushort DialCode
    {
        get => dialCode;
        set => SetProperty(ref dialCode, value);
    }
}