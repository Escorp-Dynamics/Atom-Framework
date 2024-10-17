namespace Atom.SourceGeneration;

/// <summary>
/// Представляет базового строителя члена сущности.
/// </summary>
/// <typeparam name="T">Тип строителя члена сущности.</typeparam>
public abstract class Member<T> : Entity<T>, IMember<T> where T : IEntity
{
    /// <inheritdoc/>
    public AccessModifier AccessModifier { get; protected set; }

    /// <inheritdoc/>
    public string Type { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public bool IsStatic { get; protected set; }

    /// <inheritdoc/>
    public abstract T WithAccessModifier(AccessModifier modifier);

    /// <inheritdoc/>
    public abstract T WithType(string type);

    /// <inheritdoc/>
    public T WithType<TType>() => WithType(typeof(TType).GetFriendlyName());

    /// <inheritdoc/>
    public abstract T AsStatic(bool value);

    /// <inheritdoc/>
    public T AsStatic() => AsStatic(true);

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        IsStatic = default;
        Type = string.Empty;
        AccessModifier = default;
    }
}