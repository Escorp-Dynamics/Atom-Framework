using System.Collections.ObjectModel;

namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет доступный только для чтения словарь, содержащий словарь дополнительных данных из результата команды.
/// </summary>
public sealed class ReceivedDataDictionary : ReadOnlyDictionary<string, object?>
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReceivedDataDictionary"/>.
    /// </summary>
    /// <param name="dictionary">Словарь дополнительных данных.</param>
    public ReceivedDataDictionary(IDictionary<string, object?> dictionary) : base(dictionary)
    {
        foreach (var pair in Dictionary) Dictionary[pair.Key] = SealValue(pair.Value);
    }

    /// <summary>
    /// Пустой словарь.
    /// </summary>
    public static new ReceivedDataDictionary Empty => new(new Dictionary<string, object?>());

    /// <summary>
    /// Получает изменяемую копию этого словаря.
    /// </summary>
    /// <returns>Изменяемый словарь, содержащий копию данных из этого <see cref="ReceivedDataDictionary"/>.</returns>
    public IDictionary<string, object?> ToWritableCopy()
    {
        var result = new Dictionary<string, object?>();

        foreach (var pair in Dictionary)
        {
            result[pair.Key] = pair.Value is ReceivedDataDictionary dictionary
                ? dictionary.ToWritableCopy() : pair.Value is ReceivedDataList list
                ? list.ToWritableCopy() : pair.Value;
        }

        return result;
    }

    private static object? SealValue(object? valueToSeal) => valueToSeal is Dictionary<string, object?> dictionaryValue
        ? new ReceivedDataDictionary(dictionaryValue) : valueToSeal is List<object?> listValue
        ? new ReceivedDataList(listValue) : valueToSeal;
}