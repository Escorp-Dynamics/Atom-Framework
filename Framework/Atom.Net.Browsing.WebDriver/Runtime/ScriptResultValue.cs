using System.Globalization;
using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Чтение скалярных значений из результата пользовательского скрипта.
/// </summary>
/// <remarks>
/// Расширение приводит результат скрипта к строке (<c>String(result)</c>) прежде, чем отправить его
/// в мост, поэтому логическое значение приходит как <c>"true"</c>, а число — как <c>"42"</c>, а не
/// литералами JSON. Прямые <c>JsonElement.GetBoolean</c>/<c>TryGetInt32</c>/<c>TryGetDouble</c> на
/// такой полезной нагрузке молча дают <see langword="default"/> либо бросают исключение, поэтому
/// разбираем обе формы в одном месте: и литералы JSON, и их строковую запись.
/// </remarks>
internal static class ScriptResultValue
{
    public static bool TryReadBoolean(JsonElement element, out bool value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
            case JsonValueKind.False:
                value = element.GetBoolean();
                return true;

            case JsonValueKind.String:
                return bool.TryParse(element.GetString(), out value);

            default:
                value = false;
                return false;
        }
    }

    public static bool TryReadInt32(JsonElement element, out int value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetInt32(out value);

            case JsonValueKind.String:
                return int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

            default:
                value = 0;
                return false;
        }
    }

    public static bool TryReadDouble(JsonElement element, out double value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out value);

            case JsonValueKind.String:
                // Без AllowThousands: JS отдаёт число через String(...) и разделителей разрядов не
                // ставит, зато с этим флагом «1,5» превратилось бы в 15 — молчаливая порча данных.
                return double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

            default:
                value = 0;
                return false;
        }
    }
}
