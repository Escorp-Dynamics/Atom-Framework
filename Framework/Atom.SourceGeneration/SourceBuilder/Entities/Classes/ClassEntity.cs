using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Buffers;
using Atom.Collections;
using Atom.Text;

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

    private bool HasEntities
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !(fields.IsEmpty && properties.IsEmpty && events.IsEmpty && methods.IsEmpty && others.IsEmpty);
    }

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
    public bool IsSealed { get; protected set; }

    /// <inheritdoc/>
    public override bool IsValid => !string.IsNullOrEmpty(Name);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendEntities(ref ValueStringBuilder sb, string spaces, params IEnumerable<string> usings)
    {
        if (!HasEntities)
        {
            sb.Append(';');
            return;
        }

        sb.AppendLine($"\n{spaces}{{");
        var tabs = (spaces.Length / 4) + 1;

        AppendMembers(ref sb, tabs, fields, usings);
        AppendMembers(ref sb, tabs, properties, usings);
        AppendMembers(ref sb, tabs, events, usings);
        AppendMembers(ref sb, tabs, methods, usings);
        AppendMembers(ref sb, tabs, others, usings);

        if (sb[^1] is '\n') sb.Remove(sb.Length - 1, 1);
        sb.AppendLine($"{spaces}}}");
    }

    /// <inheritdoc/>
    protected override void OnBuildingDeclaration(ref ValueStringBuilder sb, [NotNull] string spaces, params IEnumerable<string> usings)
    {
        var access = AccessModifier.AsString();
        if (!string.IsNullOrEmpty(access)) access += ' ';

        sb.Append(spaces).Append(access);
        if (IsSealed) sb.Append("sealed ");
        if (IsPartial) sb.Append("partial ");
        sb.Append($"class {Name}");

        AppendGenerics(ref sb);
        AppendParents(ref sb, usings);
        AppendGenericsLimitations(ref sb);
        AppendEntities(ref sb, spaces, usings);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ClassEntity WithEvent([NotNull] params IEnumerable<EventMember> events)
    {
        this.events.AddRange(events);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity WithEvent<TType>(string name, AccessModifier access, string? comment = default) => WithEvent(EventMember.Create<TType>(name, access).WithComment(comment));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity WithEvent<TType>(string name) => WithEvent<TType>(name, default);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ClassEntity WithGeneric([NotNull] params IEnumerable<GenericEntity> generics)
    {
        this.generics.AddRange(generics);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity WithGeneric(string name, params IEnumerable<string> limitations) => WithGeneric(GenericEntity.Create(name, limitations));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ClassEntity WithMethod([NotNull] params IEnumerable<MethodMember> methods)
    {
        this.methods.AddRange(methods);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity WithMethod(string name) => WithMethod(MethodMember.Create(name));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity WithMethod<TType>(string name) => WithMethod(MethodMember.Create<TType>(name));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ClassEntity WithOther([NotNull] params IEnumerable<IEntity> entities)
    {
        others.AddRange(entities);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ClassEntity WithParent([NotNull] params IEnumerable<string> parents)
    {
        this.parents.AddRange(parents);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity WithParent<TType>(bool withNamespaces = true, bool withNullable = true, bool withGenericNullable = true) => WithParent(typeof(TType).GetFriendlyName(withNamespaces, withNullable, withGenericNullable));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ClassEntity WithProperty([NotNull] params IEnumerable<PropertyMember> properties)
    {
        this.properties.AddRange(properties);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity WithProperty<TType>(string name) => WithProperty(PropertyMember.CreateWithGetterOnly<TType>(name));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ClassEntity WithField([NotNull] params IEnumerable<FieldMember> fields)
    {
        this.fields.AddRange(fields);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity WithField<TType>(string name, string? value) => WithField(FieldMember.Create<TType>(name, value));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity WithField<TType>(string name, TType value) => WithField(FieldMember.Create(name, value));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity WithField<TType>(string name) => WithField(FieldMember.Create<TType>(name));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ClassEntity AsPartial(bool value)
    {
        IsPartial = value;
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity AsPartial() => AsPartial(value: true);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ClassEntity AsUnsafe(bool value)
    {
        IsUnsafe = value;
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity AsUnsafe() => AsUnsafe(value: true);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ClassEntity AsStatic(bool value)
    {
        IsStatic = value;
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity AsStatic() => AsStatic(value: true);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ClassEntity AsSealed(bool value)
    {
        IsSealed = value;
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity AsSealed() => AsSealed(value: true);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClassEntity WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ClassEntity WithAttribute(params IEnumerable<string> attributes)
    {
        AddAttribute(attributes);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ClassEntity WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ClassEntity WithName(string name)
    {
        Name = name;
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            x.IsSealed = default;
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendMembers<TMember>(ref ValueStringBuilder sb, int tabs, IEnumerable<TMember> members, params IEnumerable<string> usings) where TMember : IEntity
    {
        foreach (var member in members)
        {
            var built = member.Build(tabs, usings);
            if (!string.IsNullOrEmpty(built)) sb.AppendLine(built);
        }
    }

    /// <summary>
    /// Создаёт новый экземпляр строителя интерфейса.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ClassEntity Create() => ObjectPool<ClassEntity>.Shared.Rent();

    /// <summary>
    /// Создаёт новый экземпляр строителя интерфейса.
    /// </summary>
    /// <param name="name">Имя интерфейса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ClassEntity Create(string name) => Create().WithName(name);

    /// <summary>
    /// Создаёт новый экземпляр строителя интерфейса.
    /// </summary>
    /// <param name="name">Имя интерфейса.</param>
    /// <param name="access">Модификатор доступа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ClassEntity Create(string name, AccessModifier access) => Create(name).WithAccessModifier(access);
}
