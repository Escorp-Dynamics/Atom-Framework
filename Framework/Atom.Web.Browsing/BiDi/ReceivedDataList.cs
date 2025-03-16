using System.Collections.ObjectModel;

namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет доступный только для чтения список, содержащий список дополнительных данных из результата команды.
/// </summary>
public sealed class ReceivedDataList : ReadOnlyCollection<object?>
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReceivedDataList"/>.
    /// </summary>
    /// <param name="list">Список полученных данных.</param>
    public ReceivedDataList(IList<object?> list) : base(list)
    {
        for (var i = 0; i < Items.Count; ++i) Items[i] = SealValue(Items[i]);
    }

    /// <summary>
    /// Пустой список.
    /// </summary>
    public static new ReceivedDataList Empty => new([]);

    /// <summary>
    /// Получает изменяемую копию этого списка.
    /// </summary>
    /// <returns>Изменяемый список, содержащий копию данных из этого <see cref="ReceivedDataList"/>.</returns>
    public IList<object?> ToWritableCopy()
    {
        var result = new List<object?>();

        foreach (var item in Items)
        {
            if (item is ReceivedDataDictionary dictionary)
                result.Add(dictionary.ToWritableCopy());
            else if (item is ReceivedDataList list)
                result.Add(list.ToWritableCopy());
            else
                result.Add(item);
        }

        return result;
    }

    private static object? SealValue(object? valueToSeal) => valueToSeal is Dictionary<string, object?> dictionaryValue
        ? new ReceivedDataDictionary(dictionaryValue) : valueToSeal is List<object?> listValue
        ? new ReceivedDataList(listValue) : valueToSeal;
}