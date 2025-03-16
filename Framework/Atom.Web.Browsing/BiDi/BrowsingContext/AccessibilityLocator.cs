using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет локатор для поиска узлов с использованием атрибутов доступности.
/// </summary>
public class AccessibilityLocator : Locator
{
    private readonly string type = "accessibility";
    private readonly Dictionary<string, string> accessibilityAttributes = [];

    /// <summary>
    /// Тип локатора.
    /// </summary>
    public override string Type => type;

    /// <summary>
    /// Атрибуты доступности для поиска узлов.
    /// </summary>
    public override object Value => new ReadOnlyDictionary<string, string>(accessibilityAttributes);

    /// <summary>
    /// Доступное имя для поиска узлов.
    /// </summary>
    [JsonIgnore]
    public string? Name
    {
        get => GetAccessiblePropertyValue("name");
        set => SetAccessiblePropertyValue("name", value);
    }

    /// <summary>
    /// Доступная роль для поиска узлов.
    /// </summary>
    [JsonIgnore]
    public string? Role
    {
        get => GetAccessiblePropertyValue("role");
        set => SetAccessiblePropertyValue("role", value);
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="AccessibilityLocator"/>.
    /// </summary>
    public AccessibilityLocator() : base() { }

    private string? GetAccessiblePropertyValue(string propertyName) => accessibilityAttributes.TryGetValue(propertyName, out var value) ? value : null;

    private void SetAccessiblePropertyValue(string propertyName, string? value)
    {
        if (value is not null)
            accessibilityAttributes[propertyName] = value;
        else
            accessibilityAttributes.Remove(propertyName);
    }
}