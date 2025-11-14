using System.Runtime.CompilerServices;

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract T WithAccessModifier(AccessModifier modifier);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract T WithType(string type);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T WithType<TType>(bool withNamespaces = true, bool withNullable = true, bool withGenericNullable = true) where TType : allows ref struct => WithType(typeof(TType).GetFriendlyName(withNamespaces, withNullable, withGenericNullable));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract T AsStatic(bool value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T AsStatic() => AsStatic(value: true);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Release()
    {
        base.Release();

        IsStatic = default;
        Type = string.Empty;
        AccessModifier = default;
    }
}