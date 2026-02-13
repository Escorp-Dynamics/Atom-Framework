using System.Diagnostics.CodeAnalysis;
using Atom.Buffers;
using Atom.Collections;
using Atom.Text;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя интерфейсов.
/// </summary>
public class InterfaceEntity : Entity<InterfaceEntity>, IInterfaceEntity<InterfaceEntity>
{
    private readonly SparseArray<string> parents = new(128);
    private readonly SparseArray<GenericEntity> generics = new(128);
    private readonly SparseArray<PropertyMember> properties = new(128);
    private readonly SparseArray<EventMember> events = new(128);
    private readonly SparseArray<MethodMember> methods = new(128);
    private readonly SparseArray<IEntity> others = new(128);
    private bool HasMembers() => !(properties.IsEmpty && events.IsEmpty && methods.IsEmpty && others.IsEmpty);

    private static void AppendMembers<TMember>(ref ValueStringBuilder sb, int tabs, IEnumerable<TMember> members, params IEnumerable<string> usings)
        where TMember : IEntity
    {
        foreach (var member in members)
        {
            var built = member.Build(tabs, usings);
            if (!string.IsNullOrEmpty(built)) sb.AppendLine(built);
        }
    }

    /// <inheritdoc/>
    public AccessModifier AccessModifier { get; protected set; }

    /// <inheritdoc/>
    public IEnumerable<string> Parents => parents;

    /// <inheritdoc/>
    public IEnumerable<GenericEntity> Generics => generics;

    /// <inheritdoc/>
    public IEnumerable<PropertyMember> Properties => properties;

    /// <inheritdoc/>
    public IEnumerable<EventMember> Events => events;

    /// <inheritdoc/>
    public IEnumerable<MethodMember> Methods => methods;

    /// <inheritdoc/>
    public IEnumerable<IEntity> Others => others;

    /// <inheritdoc/>
    public bool IsPartial { get; protected set; }

    /// <inheritdoc/>
    public bool IsUnsafe { get; protected set; }

    /// <inheritdoc/>
    public override bool IsValid => !string.IsNullOrEmpty(Name);

    private void AppendGenerics(ref ValueStringBuilder sb)
    {
        if (generics.IsEmpty) return;

        sb.Append('<');

        foreach (var generic in generics)
        {
            var g = generic.Build();
            if (string.IsNullOrEmpty(g)) continue;
            sb.Append($"{g}, ");
        }

        sb.Remove(sb.Length - 2, 2);
        sb.Append('>');
    }

    private void AppendParents(ref ValueStringBuilder sb, params IEnumerable<string> usings)
    {
        if (parents.IsEmpty) return;

        sb.Append(" : ");

        foreach (var parent in parents)
        {
            if (string.IsNullOrEmpty(parent)) continue;
            sb.Append($"{parent.GetTypeName(usings)}, ");
        }

        sb.Remove(sb.Length - 2, 2);
    }

    private void AppendGenericsLimitations(ref ValueStringBuilder sb)
    {
        if (generics.IsEmpty) return;

        foreach (var generic in generics)
        {
            var g = generic.BuildLimitations();
            if (string.IsNullOrEmpty(g)) continue;
            sb.Append($" {g}");
        }
    }

    /// <inheritdoc/>
    protected override void OnBuildingDeclaration(ref ValueStringBuilder sb, [NotNull] string spaces, params IEnumerable<string> usings)
    {
        var access = AccessModifier.AsString();
        if (!string.IsNullOrEmpty(access)) access += ' ';

        sb.Append($"{spaces}{access}");
        if (IsPartial) sb.Append("partial ");
        sb.Append($"interface {Name}");

        AppendGenerics(ref sb);
        AppendParents(ref sb, usings);
        AppendGenericsLimitations(ref sb);

        if (!HasMembers())
        {
            sb.Append(';');
            return;
        }

        sb.AppendLine($"\n{spaces}{{");
        var tabs = (spaces.Length / 4) + 1;

        AppendMembers(ref sb, tabs, properties, usings);
        AppendMembers(ref sb, tabs, events, usings);
        AppendMembers(ref sb, tabs, methods, usings);
        AppendMembers(ref sb, tabs, others, usings);

        if (sb[^1] is '\n') sb.Remove(sb.Length - 1, 1);
        sb.AppendLine($"{spaces}}}");
    }

    /// <inheritdoc/>
    public virtual InterfaceEntity WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <inheritdoc/>
    public virtual InterfaceEntity WithEvent([NotNull] params IEnumerable<EventMember> events)
    {
        this.events.AddRange(events);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity WithEvent<TType>(string name, AccessModifier access) => WithEvent(EventMember.Create<TType>(name, access));

    /// <inheritdoc/>
    public InterfaceEntity WithEvent<TType>(string name) => WithEvent<TType>(name, default);

    /// <inheritdoc/>
    public virtual InterfaceEntity WithGeneric([NotNull] params IEnumerable<GenericEntity> generics)
    {
        this.generics.AddRange(generics);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity WithGeneric(string name, params IEnumerable<string> limitations) => WithGeneric(GenericEntity.Create(name, limitations));

    /// <inheritdoc/>
    public virtual InterfaceEntity WithMethod([NotNull] params IEnumerable<MethodMember> methods)
    {
        foreach (var method in methods)
        {
            method.IsInterface = true;
            this.methods.Add(method);
        }

        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity WithMethod(string name) => WithMethod(MethodMember.Create(name));

    /// <inheritdoc/>
    public InterfaceEntity WithMethod<TType>(string name) => WithMethod(MethodMember.Create<TType>(name));

    /// <inheritdoc/>
    public virtual InterfaceEntity WithOther([NotNull] params IEnumerable<IEntity> entities)
    {
        others.AddRange(entities);
        return this;
    }

    /// <inheritdoc/>
    public virtual InterfaceEntity WithParent([NotNull] params IEnumerable<string> parents)
    {
        this.parents.AddRange(parents);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity WithParent<TType>(bool withNamespaces = true, bool withNullable = true, bool withGenericNullable = true) => WithParent(typeof(TType).GetFriendlyName(withNamespaces, withNullable, withGenericNullable));

    /// <inheritdoc/>
    public virtual InterfaceEntity WithProperty([NotNull] params IEnumerable<PropertyMember> properties)
    {
        this.properties.AddRange(properties);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity WithProperty<TType>(string name) => WithProperty(PropertyMember.CreateWithGetterOnly<TType>(name));

    /// <inheritdoc/>
    public override InterfaceEntity WithName(string name)
    {
        Name = name;
        return this;
    }

    /// <inheritdoc/>
    public override InterfaceEntity WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override InterfaceEntity WithAttribute(params IEnumerable<string> attributes)
    {
        AddAttribute(attributes);
        return this;
    }

    /// <inheritdoc/>
    public virtual InterfaceEntity AsPartial(bool value)
    {
        IsPartial = value;
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity AsPartial() => AsPartial(value: true);

    /// <inheritdoc/>
    public virtual InterfaceEntity AsUnsafe(bool value)
    {
        IsUnsafe = value;
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity AsUnsafe() => AsUnsafe(value: true);

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        ObjectPool<InterfaceEntity>.Shared.Return(this, x =>
        {
            foreach (var value in x.generics) value.Release();
            foreach (var value in x.properties) value.Release();
            foreach (var value in x.events) value.Release();
            foreach (var value in x.methods) value.Release();
            foreach (var value in x.others) value.Release();

            x.parents.Reset();
            x.generics.Reset();
            x.properties.Reset();
            x.events.Reset();
            x.methods.Reset();
            x.others.Reset();

            x.IsPartial = default;
            x.IsUnsafe = default;
        });
    }

    /// <summary>
    /// Создаёт новый экземпляр строителя интерфейса.
    /// </summary>
    public static InterfaceEntity Create() => ObjectPool<InterfaceEntity>.Shared.Rent();

    /// <summary>
    /// Создаёт новый экземпляр строителя интерфейса.
    /// </summary>
    /// <param name="name">Имя интерфейса.</param>
    public static InterfaceEntity Create(string name) => Create().WithName(name);

    /// <summary>
    /// Создаёт новый экземпляр строителя интерфейса.
    /// </summary>
    /// <param name="name">Имя интерфейса.</param>
    /// <param name="access">Модификатор доступа.</param>
    public static InterfaceEntity Create(string name, AccessModifier access) => Create(name).WithAccessModifier(access);
}
