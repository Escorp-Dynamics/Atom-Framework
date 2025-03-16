using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Типы пользовательских запросов.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<UserPromptType>))]
public enum UserPromptType
{
    /// <summary>
    /// Оповещение, отображаемое для уведомления пользователя.
    /// </summary>
    Alert,
    /// <summary>
    /// Подтверждение, запрашивающее у пользователя выбор "да" или "нет".
    /// </summary>
    Confirm,
    /// <summary>
    /// Запрос, предлагающий пользователю ввести текстовую информацию.
    /// </summary>
    Prompt,
    /// <summary>
    /// Запрос, информирующий пользователя о том, что операция приведет к выгрузке текущей страницы.
    /// </summary>
    BeforeUnload,
}