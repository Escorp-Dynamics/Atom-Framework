namespace Atom.Architect.Components;

/// <summary>
/// Представляет аргументы событий компонента.
/// </summary>
public class ComponentEventArgs : MutableEventArgs
{
    /// <summary>
    /// Владелец компонента.
    /// </summary>
    public IComponentOwner? Owner { get; set; }

    /// <summary>
    /// Текущий компонент.
    /// </summary>
    public IComponent? Component { get; set; }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        Owner = default;
        Component = default;
    }
}