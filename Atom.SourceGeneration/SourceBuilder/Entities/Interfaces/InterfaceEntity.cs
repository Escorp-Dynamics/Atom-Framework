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

    private void AppendParents(StringBuilder sb)
    {
        if (parents.IsEmpty) return;

        sb.Append(" : ");

        foreach (var parent in parents)
        {
            if (string.IsNullOrEmpty(parent)) continue;
            sb.Append($"{parent}, ");
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
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, [NotNull] string spaces)
    {
        var access = AccessModifier.AsString();
        if (!string.IsNullOrEmpty(access)) access += ' ';

        sb.Append($"{spaces}{access}");
        if (IsPartial) sb.Append("partial ");
        sb.Append($"interface {Name}");

        AppendGenerics(sb);
        AppendParents(sb);
        AppendGenericsLimitations(sb);

        sb.AppendLine($"\n{spaces}{{");
        var tabs = (spaces.Length / 4) + 1;

        foreach (var property in properties)
        {
            var p = property.Build(tabs);
            if (!string.IsNullOrEmpty(p)) sb.AppendLine(p);
        }

        foreach (var e in events)
        {
            var p = e.Build(tabs);
            if (!string.IsNullOrEmpty(p)) sb.AppendLine(p);
        }

        foreach (var method in methods)
        {
            var p = method.Build(tabs);
            if (!string.IsNullOrEmpty(p)) sb.AppendLine(p);
        }

        foreach (var other in others)
        {
            var p = other.Build(tabs);
            if (!string.IsNullOrEmpty(p)) sb.AppendLine(p);
        }

        sb.AppendLine("}");
    }

    /// <inheritdoc/>
    public virtual InterfaceEntity WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <inheritdoc/>
    public virtual InterfaceEntity AddEvents([NotNull] params EventMember[] events)
    {
        this.events.AddRange(events);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity AddEvent(EventMember e) => AddEvents(e);

    /// <inheritdoc/>
    public InterfaceEntity AddEvent<TType>(string name, AccessModifier access) => AddEvent(EventMember.Create<TType>(name, access));

    /// <inheritdoc/>
    public InterfaceEntity AddEvent<TType>(string name) => AddEvent<TType>(name, default);

    /// <inheritdoc/>
    public virtual InterfaceEntity AddGenerics([NotNull] params GenericEntity[] generics)
    {
        this.generics.AddRange(generics);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity AddGeneric(GenericEntity generic) => AddGenerics(generic);

    /// <inheritdoc/>
    public InterfaceEntity AddGeneric(string name, params string[] limitations) => AddGeneric(GenericEntity.Create(name, limitations));

    /// <inheritdoc/>
    public virtual InterfaceEntity AddMethods([NotNull] params MethodMember[] methods)
    {
        this.methods.AddRange(methods);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity AddMethod(MethodMember method) => AddMethods(method);

    /// <inheritdoc/>
    public InterfaceEntity AddMethod(string name) => AddMethod(MethodMember.Create(name));

    /// <inheritdoc/>
    public InterfaceEntity AddMethod<TType>(string name) => AddMethod(MethodMember.Create<TType>(name));

    /// <inheritdoc/>
    public virtual InterfaceEntity AddOthers([NotNull] params IEntity[] entities)
    {
        others.AddRange(entities);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity AddOther(IEntity entity) => AddOthers(entity);

    /// <inheritdoc/>
    public virtual InterfaceEntity AddParents([NotNull] params string[] parents)
    {
        this.parents.AddRange(parents);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity AddParent(string parent) => AddParents(parent);

    /// <inheritdoc/>
    public InterfaceEntity AddParent<TType>() => AddParent(typeof(TType).GetFriendlyName());

    /// <inheritdoc/>
    public virtual InterfaceEntity AddProperties([NotNull] params PropertyMember[] properties)
    {
        this.properties.AddRange(properties);
        return this;
    }

    /// <inheritdoc/>
    public InterfaceEntity AddProperty(PropertyMember property) => AddProperties(property);

    /// <inheritdoc/>
    public InterfaceEntity AddProperty<TType>(string name) => AddProperty(PropertyMember.CreateWithGetterOnly<TType>(name));

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
    public override InterfaceEntity WithAttributes(params string[] attributes)
    {
        AddAttributes(attributes);
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