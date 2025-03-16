using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет параметры устройства указателя.
/// </summary>
public class PointerParameters
{
    /// <summary>
    /// Тип устройства указателя.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PointerType? PointerType { get; set; }
}