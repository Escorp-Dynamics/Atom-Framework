using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Состояние клиентского окна.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<ClientWindowState>))]
public enum ClientWindowState
{
    /// <summary>
    /// Клиентское окно находится в нормальном состоянии.
    /// </summary>
    Normal,
    /// <summary>
    /// Клиентское окно свёрнуто, обычно это означает, что окно
    /// представлено в виде иконки и имеет нулевую ширину и высоту.
    /// </summary>
    Minimized,
    /// <summary>
    /// Клиентское окно развёрнуто, обычно это означает, что окно
    /// занимает всю ширину и высоту текущего дисплея системы,
    /// но сохраняет так называемые "хромовые" элементы, такие как строка меню,
    /// панель инструментов и т.д.
    /// </summary>
    Maximized,
    /// <summary>
    /// Клиентское окно находится в полноэкранном режиме, обычно это означает, что содержимое окна
    /// занимает всю ширину и высоту текущего дисплея системы,
    /// и не отображает так называемые "хромовые" элементы, такие как строка меню,
    /// панель инструментов и т.д.
    /// </summary>
    Fullscreen,
}