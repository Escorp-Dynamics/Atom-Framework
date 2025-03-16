using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;
using Atom.Collections;

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

    private void AppendGenerics(StringBuilder sb)
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

    private void AppendParents(StringBuilder sb, params string[] usings)
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

    private void AppendGenericsLimitations(StringBuilder sb)
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
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, [NotNull] string spaces, params string[] usings)
    {
        var access = AccessModifier.AsString();
        if (!string.IsNullOrEmpty(access)) access += ' ';

        sb.Append($"{spaces}{access}");
        if (IsPartial) sb.Append("partial ");
        sb.Append($"interface {Name}");

        AppendGenerics(sb);
        AppendParents(sb, usings);
        AppendGenericsLimitations(sb);

        if (properties.IsEmpty && events.IsEmpty && methods.IsEmpty && others.IsEmpty)
        {
            sb.Append(';');
            return;
        }

        sb.AppendLine($"\n{spaces}{{");
        var tabs = (spaces.Length / 4) + 1;

        foreach (var property in properties)
        {
            var p = property.Build(tabs, usings);
            if (!string.IsNullOrEmpty(p)) sb.AppendLine(p);
        }

        foreach (var e in events)
        {
            var p = e.Build(tabs, usings);
            if (!string.IsNullOrEmpty(p)) sb.AppendLine(p);
        }

        foreach (var method in methods)
        {
            var p = method.Build(tabs, usings);
            if (!string.IsNullOrEmpty(p)) sb.AppendLine(p);
        }

        foreach (var other in others)
        {
            var p = other.Build(tabs, usings);
            if (!string.IsNullOrEmpty(p)) sb.AppendLine(p);
        }

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
    public virtual InterfaceEntity WithEvent([NotNull] params EventMember[] events)
    {
        this.events.AddRange(events);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity WithEvent<TType>(string name, AccessModifier access) => WithEvent(EventMember.Create<TType>(name, access));

    /// <inheritdoc/>
    public InterfaceEntity WithEvent<TType>(string name) => WithEvent<TType>(name, default);

    /// <inheritdoc/>
    public virtual InterfaceEntity WithGeneric([NotNull] params GenericEntity[] generics)
    {
        this.generics.AddRange(generics);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity WithGeneric(string name, params string[] limitations) => WithGeneric(GenericEntity.Create(name, limitations));

    /// <inheritdoc/>
    public virtual InterfaceEntity WithMethod([NotNull] params MethodMember[] methods)
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
    public virtual InterfaceEntity WithOther([NotNull] params IEntity[] entities)
    {
        others.AddRange(entities);
        return this;
    }

    /// <inheritdoc/>
    public virtual InterfaceEntity WithParent([NotNull] params string[] parents)
    {
        this.parents.AddRange(parents);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity WithParent<TType>(bool withNamespaces = true, bool withNullable = true, bool withGenericNullable = true) => WithParent(typeof(TType).GetFriendlyName(withNamespaces, withNullable, withGenericNullable));

    /// <inheritdoc/>
    public virtual InterfaceEntity WithProperty([NotNull] params PropertyMember[] properties)
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
    public override InterfaceEntity WithAttribute(params string[] attributes)
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
    public InterfaceEntity AsPartial() => AsPartial(true);

    /// <inheritdoc/>
    public virtual InterfaceEntity AsUnsafe(bool value)
    {
        IsUnsafe = value;
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity AsUnsafe() => AsUnsafe(true);

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