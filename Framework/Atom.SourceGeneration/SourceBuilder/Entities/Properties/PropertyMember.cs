using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя свойств.
/// </summary>
public class PropertyMember : Member<PropertyMember>, IPropertyMember<PropertyMember>
{
    /// <inheritdoc/>
    public PropertyAccessorMember? Getter { get; protected set; }

    /// <inheritdoc/>
    public PropertyMutatorMember? Setter { get; protected set; }

    /// <inheritdoc/>
    public bool IsPartial { get; protected set; }

    /// <inheritdoc/>
    public bool IsReadOnly { get; protected set; }

    /// <inheritdoc/>
    public bool IsRef { get; protected set; }

    /// <inheritdoc/>
    public bool IsReadOnlyRef { get; protected set; }

    /// <inheritdoc/>
    public bool IsUnsafe { get; protected set; }

    /// <inheritdoc/>
    public bool IsAbstract { get; protected set; }

    /// <inheritdoc/>
    public bool IsVirtual { get; protected set; }

    /// <inheritdoc/>
    public bool IsOverride { get; protected set; }

    /// <inheritdoc/>
    public bool IsNew { get; protected set; }

    /// <inheritdoc/>
    public string? InitialValue { get; protected set; }

    /// <inheritdoc/>
    public override bool IsValid => !string.IsNullOrEmpty(Name) && (Getter is not null || Setter is not null) && !string.IsNullOrEmpty(Type);

    private void AppendModifiers(StringBuilder sb, string spaces)
    {
        var access = AccessModifier.AsString();
        if (!string.IsNullOrEmpty(access)) access += ' ';

        sb.Append($"{spaces}{access}");

        if (IsNew) sb.Append("new ");
        if (IsStatic) sb.Append("static ");
        if (IsPartial) sb.Append("partial ");
        if (IsUnsafe) sb.Append("unsafe ");

        if (IsAbstract)
            sb.Append("abstract ");
        else if (IsVirtual)
            sb.Append("virtual ");
        else if (IsOverride)
            sb.Append("override ");

        if (IsReadOnlyRef)
            sb.Append("readonly ref ");
        else if (IsRef)
            sb.Append("ref ");

        if (IsReadOnly) sb.Append("readonly ");
    }

    private void AppendAccessors(StringBuilder sb, string spaces)
    {
        var tabs = (spaces.Length / 4) + 1;

        Getter?.ParentAccessModifier = AccessModifier;
        Setter?.ParentAccessModifier = AccessModifier;

        var getter = Getter?.Build(tabs);
        var setter = Setter?.Build(tabs);

        var isAuto = (!string.IsNullOrEmpty(getter) && getter.EndsWith("get;")) || (!string.IsNullOrEmpty(setter) && (setter.EndsWith("set;") || setter.EndsWith("init;")));
        var isSimpleGetter = Getter is not null && Setter is null && !isAuto && !string.IsNullOrEmpty(getter) && getter.Trim().StartsWith("get =>");

        if (isSimpleGetter)
            sb.AppendLine(getter!.Trim()[3..]);
        else
            AppendBody(sb, spaces, getter, setter, isAuto);
    }

    private void AppendBody(StringBuilder sb, string spaces, string? getter, string? setter, bool isAuto)
    {
        if (isAuto)
            sb.Append($" {{");
        else
            sb.AppendLine($"\n{spaces}{{");

        if (!string.IsNullOrEmpty(getter))
        {
            if (isAuto)
                sb.Append($" {getter.TrimStart()}");
            else
                sb.Append($"{getter}");
        }

        if (!string.IsNullOrEmpty(setter))
        {
            if (isAuto)
            {
                sb.Append($" {setter.TrimStart()}");
            }
            else
            {
                if (!string.IsNullOrEmpty(getter) && getter.TrimStart().StartsWith("get =>") && !setter.TrimStart().StartsWith("set =>")) sb.AppendLine();
                sb.Append($"{setter}");
            }
        }

        if (isAuto)
            sb.Append($" }}");
        else
            sb.Append($"{spaces}}}");

        if (!string.IsNullOrEmpty(InitialValue)) sb.Append($" = {InitialValue};");

        sb.AppendLine();
    }

    /// <inheritdoc/>
    protected override void OnBuildingComment([NotNull] StringBuilder sb, string spaces)
    {
        var comment = Comment;
        var valueComment = Setter?.Comment;

        if (string.IsNullOrEmpty(comment) && Getter is not null) comment = Getter.Comment;

        if (!string.IsNullOrEmpty(comment) || !string.IsNullOrEmpty(valueComment))
        {
            if (comment is "<inheritdoc/>")
            {
                sb.AppendLine($"{spaces}/// {comment}");
                return;
            }

            sb.AppendLine($"{spaces}/// <summary>");

            if (!string.IsNullOrEmpty(comment))
                sb.AppendLine($"{spaces}/// {comment}");
            else
                sb.AppendLine($"{spaces}/// {valueComment}");

            sb.AppendLine($"{spaces}/// </summary>");

            if (!string.IsNullOrEmpty(comment) && !string.IsNullOrEmpty(valueComment))
                sb.AppendLine($"{spaces}/// <value>{valueComment}</value>");
        }
    }

    /// <inheritdoc/>
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, [NotNull] string spaces, [NotNull] params IEnumerable<string> usings)
    {
        AppendModifiers(sb, spaces);
        sb.Append($"{Type.GetTypeName(usings)} {Name}");
        AppendAccessors(sb, spaces);
    }

    /// <inheritdoc/>
    public virtual PropertyMember AsPartial(bool value)
    {
        IsPartial = value;
        return this;
    }

    /// <inheritdoc/>
    public PropertyMember AsPartial() => AsPartial(true);

    /// <inheritdoc/>
    public virtual PropertyMember AsReadOnly(bool value)
    {
        IsReadOnly = value;
        return this;
    }

    /// <inheritdoc/>
    public PropertyMember AsReadOnly() => AsReadOnly(true);

    /// <inheritdoc/>
    public virtual PropertyMember AsReadOnlyRef(bool value)
    {
        IsReadOnlyRef = value;
        return this;
    }

    /// <inheritdoc/>
    public PropertyMember AsReadOnlyRef() => AsReadOnlyRef(true);

    /// <inheritdoc/>
    public virtual PropertyMember AsRef(bool value)
    {
        IsRef = value;
        return this;
    }

    /// <inheritdoc/>
    public PropertyMember AsRef() => AsRef(true);

    /// <inheritdoc/>
    public virtual PropertyMember AsUnsafe(bool value)
    {
        IsUnsafe = value;
        return this;
    }

    /// <inheritdoc/>
    public PropertyMember AsUnsafe() => AsUnsafe(true);

    /// <inheritdoc/>
    public virtual PropertyMember AsAbstract(bool value)
    {
        IsAbstract = value;
        return this;
    }

    /// <inheritdoc/>
    public PropertyMember AsAbstract() => AsAbstract(true);

    /// <inheritdoc/>
    public virtual PropertyMember AsVirtual(bool value)
    {
        IsVirtual = value;
        return this;
    }

    /// <inheritdoc/>
    public PropertyMember AsVirtual() => AsVirtual(true);

    /// <inheritdoc/>
    public virtual PropertyMember AsOverride(bool value)
    {
        IsOverride = value;
        return this;
    }

    /// <inheritdoc/>
    public PropertyMember AsOverride() => AsOverride(true);

    /// <inheritdoc/>
    public virtual PropertyMember AsNew(bool value)
    {
        IsNew = value;
        return this;
    }

    /// <inheritdoc/>
    public PropertyMember AsNew() => AsNew(true);

    /// <inheritdoc/>
    public virtual PropertyMember WithGetter(PropertyAccessorMember getter)
    {
        Getter = getter;
        return this;
    }

    /// <inheritdoc/>
    public PropertyMember WithGetter(string body, bool isReadOnly, params IEnumerable<string> attributes) => WithGetter(PropertyAccessorMember.Create()
        .WithAttribute(attributes)
        .WithCode(body)
        .AsReadOnly(isReadOnly)
        .WithAccessModifier(AccessModifier)
    );

    /// <inheritdoc/>
    public PropertyMember WithGetter(string body, params IEnumerable<string> attributes) => WithGetter(body, default, attributes);

    /// <inheritdoc/>
    public PropertyMember WithGetter() => WithGetter(string.Empty);

    /// <inheritdoc/>
    public virtual PropertyMember WithSetter(PropertyMutatorMember setter)
    {
        Setter = setter;
        return this;
    }

    /// <inheritdoc/>
    public PropertyMember WithSetter(string body, bool isInit, string comment, params IEnumerable<string> attributes) => WithSetter(PropertyMutatorMember.Create()
        .WithAttribute(attributes)
        .WithCode(body)
        .AsInit(isInit)
        .WithComment(comment)
        .WithAccessModifier(AccessModifier)
    );

    /// <inheritdoc/>
    public PropertyMember WithSetter(string body, bool isInit) => WithSetter(body, isInit, string.Empty);

    /// <inheritdoc/>
    public PropertyMember WithSetter(bool isInit) => WithSetter(string.Empty, isInit);

    /// <inheritdoc/>
    public PropertyMember WithSetter(string body, string comment, params IEnumerable<string> attributes) => WithSetter(body, default, comment, attributes);

    /// <inheritdoc/>
    public PropertyMember WithSetter(string body) => WithSetter(body, string.Empty);

    /// <inheritdoc/>
    public PropertyMember WithSetter() => WithSetter(string.Empty);

    /// <inheritdoc/>
    public virtual PropertyMember WithInitialValue(string? value)
    {
        InitialValue = value?.TrimEnd(';');
        return this;
    }

    /// <inheritdoc/>
    public PropertyMember WithInitialValue<TValue>(TValue value) => WithInitialValue(value is string ? $"\"{value}\"" : value?.ToString());

    /// <inheritdoc/>
    public override PropertyMember WithType(string type)
    {
        Type = type;
        return this;
    }

    /// <inheritdoc/>
    public override PropertyMember AsStatic(bool value)
    {
        IsStatic = value;
        return this;
    }

    /// <inheritdoc/>
    public override PropertyMember WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <inheritdoc/>
    public override PropertyMember WithAttribute(params IEnumerable<string> attributes)
    {
        AddAttribute(attributes);
        return this;
    }

    /// <inheritdoc/>
    public override PropertyMember WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override PropertyMember WithName(string name)
    {
        Name = name;
        return this;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        ObjectPool<PropertyMember>.Shared.Return(this, x =>
        {
            x.Getter?.Release();
            x.Getter = default;
            x.Setter?.Release();
            x.Setter = default;
            x.IsPartial = default;
            x.IsReadOnly = default;
            x.IsReadOnlyRef = default;
            x.IsRef = default;
            x.IsUnsafe = default;
            x.InitialValue = string.Empty;
            x.IsNew = default;
            x.IsVirtual = default;
            x.IsAbstract = default;
        });
    }

    /// <summary>
    /// Создаёт новый экземпляр строителя свойств.
    /// </summary>
    public static PropertyMember Create() => ObjectPool<PropertyMember>.Shared.Rent();

    /// <summary>
    /// Создаёт новый экземпляр строителя свойств c асессором и мутатором.
    /// </summary>
    /// <param name="name">Имя свойства.</param>
    /// <param name="isInit">Указывает, что мутатор должен быть инициализируемым.</param>
    /// <param name="accessModifier">Модификатор доступа.</param>
    /// <typeparam name="T">Тип свойства.</typeparam>
    public static PropertyMember Create<T>(string name, bool isInit, AccessModifier accessModifier)
        => Create().WithAccessModifier(accessModifier).WithType<T>().WithName(name).WithGetter().WithSetter(isInit);

    /// <summary>
    /// Создаёт новый экземпляр строителя свойств c асессором и мутатором.
    /// </summary>
    /// <param name="name">Имя свойства.</param>
    /// <param name="isInit">Указывает, что мутатор должен быть инициализируемым.</param>
    /// <typeparam name="T">Тип свойства.</typeparam>
    public static PropertyMember Create<T>(string name, bool isInit) => Create<T>(name, isInit, default);

    /// <summary>
    /// Создаёт новый экземпляр строителя свойств c асессором и мутатором.
    /// </summary>
    /// <param name="name">Имя свойства.</param>
    /// <param name="accessModifier">Модификатор доступа.</param>
    /// <typeparam name="T">Тип свойства.</typeparam>
    public static PropertyMember Create<T>(string name, AccessModifier accessModifier) => Create<T>(name, default, accessModifier);

    /// <summary>
    /// Создаёт новый экземпляр строителя свойств c асессором и мутатором.
    /// </summary>
    /// <param name="name">Имя свойства.</param>
    /// <typeparam name="T">Тип свойства.</typeparam>
    public static PropertyMember Create<T>(string name) => Create<T>(name, false);

    /// <summary>
    /// Создаёт новый экземпляр строителя свойств c асессором.
    /// </summary>
    /// <param name="name">Имя свойства.</param>
    /// <param name="accessModifier">Модификатор доступа.</param>
    /// <typeparam name="T">Тип свойства.</typeparam>
    public static PropertyMember CreateWithGetterOnly<T>(string name, AccessModifier accessModifier)
        => Create().WithAccessModifier(accessModifier).WithType<T>().WithName(name).WithGetter();

    /// <summary>
    /// Создаёт новый экземпляр строителя свойств c асессором.
    /// </summary>
    /// <param name="name">Имя свойства.</param>
    /// <param name="accessModifier">Модификатор доступа.</param>
    public static PropertyMember CreateWithGetterOnly(string name, AccessModifier accessModifier) => CreateWithGetterOnly<object>(name, accessModifier);

    /// <summary>
    /// Создаёт новый экземпляр строителя свойств c асессором.
    /// </summary>
    /// <param name="name">Имя свойства.</param>
    /// <typeparam name="T">Тип свойства.</typeparam>
    public static PropertyMember CreateWithGetterOnly<T>(string name) => CreateWithGetterOnly<T>(name, default);

    /// <summary>
    /// Создаёт новый экземпляр строителя свойств c мутатором.
    /// </summary>
    /// <param name="name">Имя свойства.</param>
    /// <param name="isInit">Указывает, что мутатор должен быть инициализируемым.</param>
    /// <param name="accessModifier">Модификатор доступа.</param>
    /// <typeparam name="T">Тип свойства.</typeparam>
    public static PropertyMember CreateWithSetterOnly<T>(string name, bool isInit, AccessModifier accessModifier)
        => Create().WithAccessModifier(accessModifier).WithType<T>().WithName(name).WithSetter(isInit);

    /// <summary>
    /// Создаёт новый экземпляр строителя свойств c мутатором.
    /// </summary>
    /// <param name="name">Имя свойства.</param>
    /// <param name="isInit">Указывает, что мутатор должен быть инициализируемым.</param>
    /// <typeparam name="T">Тип свойства.</typeparam>
    public static PropertyMember CreateWithSetterOnly<T>(string name, bool isInit) => CreateWithSetterOnly<T>(name, isInit, default);

    /// <summary>
    /// Создаёт новый экземпляр строителя свойств c мутатором.
    /// </summary>
    /// <param name="name">Имя свойства.</param>
    /// <param name="accessModifier">Модификатор доступа.</param>
    /// <typeparam name="T">Тип свойства.</typeparam>
    public static PropertyMember CreateWithSetterOnly<T>(string name, AccessModifier accessModifier) => CreateWithSetterOnly<T>(name, default, accessModifier);

    /// <summary>
    /// Создаёт новый экземпляр строителя свойств c мутатором.
    /// </summary>
    /// <param name="name">Имя свойства.</param>
    /// <typeparam name="T">Тип свойства.</typeparam>
    public static PropertyMember CreateWithSetterOnly<T>(string name) => CreateWithSetterOnly<T>(name, false);
}