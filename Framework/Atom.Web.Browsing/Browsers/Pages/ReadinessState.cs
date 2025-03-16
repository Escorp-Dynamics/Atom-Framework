using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing;

/// <summary>
/// Состояние готовности контекста просмотра.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<ReadinessState>))]
public enum ReadinessState
{
    /// <summary>
    /// Возврат немедленно без проверки состояния готовности.
    /// </summary>
    None,
    /// <summary>
    /// Возврат после того, как состояние готовности станет "interactive".
    /// </summary>
    Interactive,
    /// <summary>
    /// Возврат после того, как состояние готовности станет "complete".
    /// </summary>
    Complete,
}