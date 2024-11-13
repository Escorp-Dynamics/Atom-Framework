using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;
using Atom.Collections;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя класса.
/// </summary>
public class ClassEntity : Entity<ClassEntity>, IClassEntity<ClassEntity>
{
    private readonly SparseArray<string> parents = new(128);
    private readonly SparseArray<GenericEntity> generics = new(128);
    private readonly SparseArray<FieldMember> fields = new(128);
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
    public IEnumerable<FieldMember> Fields => fields;

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
    public bool IsStatic { get; protected set; }

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

    private void AppendEntities(StringBuilder sb, string spaces)
    {
        sb.AppendLine($"\n{spaces}{{");
        var tabs = (spaces.Length / 4) + 1;

        foreach (var field in fields)
        {
            var p = field.Build(tabs);
            if (!string.IsNullOrEmpty(p)) sb.AppendLine(p);
        }

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
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, [NotNull] string spaces)
    {
        var access = AccessModifier.AsString();
        if (!string.IsNullOrEmpty(access)) access += ' ';

        sb.Append($"{spaces}{access}");
        if (IsPartial) sb.Append("partial ");
        sb.Append($"class {Name}");

        AppendGenerics(sb);
        AppendParents(sb);
        AppendGenericsLimitations(sb);
        AppendEntities(sb, spaces);
    }

    /// <inheritdoc/>
    public virtual ClassEntity AddEvents([NotNull] params EventMember[] events)
    {
        this.events.AddRange(events);
        return this;
    }

    /// <inheritdoc/>
    public ClassEntity AddEvent(EventMember e) => AddEvents(e);

    /// <inheritdoc/>
    public ClassEntity AddEvent<TType>(string name, AccessModifier access) => AddEvent(EventMember.Create<TType>(name, access));

    /// <inheritdoc/>
    public ClassEntity AddEvent<TType>(string name) => AddEvent<TType>(name, default);

    /// <inheritdoc/>
    public virtual ClassEntity AddGenerics([NotNull] params GenericEntity[] generics)
    {
        this.generics.AddRange(generics);
        return this;
    }

    /// <inheritdoc/>
    public ClassEntity AddGeneric(GenericEntity generic) => AddGenerics(generic);

    /// <inheritdoc/>
    public ClassEntity AddGeneric(string name, params string[] limitations) => AddGeneric(GenericEntity.Create(name, limitations));

    /// <inheritdoc/>
    public virtual ClassEntity AddMethods([NotNull] params MethodMember[] methods)
    {
        this.methods.AddRange(methods);
        return this;
    }

    /// <inheritdoc/>
    public ClassEntity AddMethod(MethodMember method) => AddMethods(method);

    /// <inheritdoc/>
    public ClassEntity AddMethod(string name) => AddMethod(MethodMember.Create(name));

    /// <inheritdoc/>
    public ClassEntity AddMethod<TType>(string name) => AddMethod(MethodMember.Create<TType>(name));

    /// <inheritdoc/>
    public virtual ClassEntity AddOthers([NotNull] params IEntity[] entities)
    {
        others.AddRange(entities);
        return this;
    }

    /// <inheritdoc/>
    public ClassEntity AddOther(IEntity entity) => AddOthers(entity);

    /// <inheritdoc/>
    public virtual ClassEntity AddParents([NotNull] params string[] parents)
    {
        this.parents.AddRange(parents);
        return this;
    }

    /// <inheritdoc/>
    public ClassEntity AddParent(string parent) => AddParents(parent);

    /// <inheritdoc/>
    public ClassEntity AddParent<TType>() => AddParent(typeof(TType).GetFriendlyName());

    /// <inheritdoc/>
    public virtual ClassEntity AddProperties([NotNull] params PropertyMember[] properties)
    {
        this.properties.AddRange(properties);
        return this;
    }

    /// <inheritdoc/>
    public ClassEntity AddProperty(PropertyMember property) => AddProperties(property);

    /// <inheritdoc/>
    public ClassEntity AddProperty<TType>(string name) => AddProperty(PropertyMember.CreateWithGetterOnly<TType>(name));

    /// <inheritdoc/>
    public virtual ClassEntity AddFields([NotNull] params FieldMember[] fields)
    {
        this.fields.AddRange(fields);
        return this;
    }

    /// <inheritdoc/>
    public ClassEntity AddField(FieldMember field) => AddFields(field);

    /// <inheritdoc/>
    public ClassEntity AddField<TType>(string name, string? value) => AddField(FieldMember.Create<TType>(name, value));

    /// <inheritdoc/>
    public ClassEntity AddField<TType>(string name, TType value) => AddField(FieldMember.Create(name, value));

    /// <inheritdoc/>
    public ClassEntity AddField<TType>(string name) => AddField(FieldMember.Create<TType>(name));

    /// <inheritdoc/>
    public virtual ClassEntity AsPartial(bool value)
    {
        IsPartial = value;
        return this;
    }

    /// <inheritdoc/>
    public ClassEntity AsPartial() => AsPartial(true);

    /// <inheritdoc/>
    public virtual ClassEntity AsUnsafe(bool value)
    {
        IsUnsafe = value;
        return this;
    }

    /// <inheritdoc/>
    public ClassEntity AsUnsafe() => AsUnsafe(true);

    /// <inheritdoc/>
    public virtual ClassEntity AsStatic(bool value)
    {
        IsStatic = value;
        return this;
    }

    /// <inheritdoc/>
    public ClassEntity AsStatic() => AsStatic(true);

    /// <inheritdoc/>
    public ClassEntity WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <inheritdoc/>
    public override ClassEntity WithAttributes(params string[] attributes)
    {
        AddAttributes(attributes);
        return this;
    }

    /// <inheritdoc/>
    public override ClassEntity WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override ClassEntity WithName(string name)
    {
        Name = name;
        return this;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        ObjectPool<ClassEntity>.Shared.Return(this, x =>
        {
            foreach (var value in x.generics) value.Release();
            foreach (var value in x.fields) value.Release();
            foreach (var value in x.properties) value.Release();
            foreach (var value in x.events) value.Release();
            foreach (var value in x.methods) value.Release();
            foreach (var value in x.others) value.Release();

            x.parents.Reset();
            x.generics.Reset();
            x.fields.Reset();
            x.properties.Reset();
            x.events.Reset();
            x.methods.Reset();
            x.others.Reset();

            x.IsPartial = default;
            x.IsUnsafe = default;
            x.IsStatic = default;
        });
    }

    /// <summary>
    /// Создаёт новый экземпляр строителя интерфейса.
    /// </summary>
    public static ClassEntity Create() => ObjectPool<ClassEntity>.Shared.Rent();

    /// <summary>
    /// Создаёт новый экземпляр строителя интерфейса.
    /// </summary>
    /// <param name="name">Имя интерфейса.</param>
    public static ClassEntity Create(string name) => Create().WithName(name);

    /// <summary>
    /// Создаёт новый экземпляр строителя интерфейса.
    /// </summary>
    /// <param name="name">Имя интерфейса.</param>
    /// <param name="access">Модификатор доступа.</param>
    public static ClassEntity Create(string name, AccessModifier access) => Create(name).WithAccessModifier(access);
}