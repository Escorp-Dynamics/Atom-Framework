using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет базовый класс для действий с источником.
/// </summary>
[JsonDerivedType(typeof(KeySourceActions))]
[JsonDerivedType(typeof(PointerSourceActions))]
[JsonDerivedType(typeof(WheelSourceActions))]
[JsonDerivedType(typeof(NoneSourceActions))]
public abstract class SourceActions
{
    /// <summary>
    /// Тип действий источника.
    /// </summary>
    public abstract string Type { get; }

    /// <summary>
    /// Идентификатор устройства.
    /// </summary>
    public string Id { get; } = Guid.NewGuid().ToString();
}