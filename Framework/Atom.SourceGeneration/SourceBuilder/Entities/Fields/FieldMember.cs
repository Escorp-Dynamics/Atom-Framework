using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя полей.
/// </summary>
public class FieldMember : Member<FieldMember>, IFieldMember<FieldMember>
{
    /// <inheritdoc/>
    public string? Value { get; protected set; }

    /// <inheritdoc/>
    public bool IsReadOnly { get; protected set; }

    /// <inheritdoc/>
    public bool IsVolatile { get; protected set; }

    /// <inheritdoc/>
    public bool IsConstant { get; protected set; }

    /// <inheritdoc/>
    public bool IsRef { get; protected set; }

    /// <inheritdoc/>
    public override bool IsValid => !string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(Type);

    /// <inheritdoc/>
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, string spaces, [NotNull] params IEnumerable<string> usings)
    {
        var access = AccessModifier.AsString();
        if (!string.IsNullOrEmpty(access)) access += ' ';

        sb.Append($"{spaces}{access}");

        if (IsConstant)
        {
            sb.Append("const ");
        }
        else
        {
            if (IsStatic) sb.Append("static ");
            if (IsReadOnly) sb.Append("readonly ");
            if (IsVolatile) sb.Append("volatile ");
            if (IsRef) sb.Append("ref ");
        }

        sb.Append($"{Type.GetTypeName(usings)} {Name}");
        if (Value is not null) sb.Append($" = {Value};");
        sb.AppendLine();
    }

    /// <inheritdoc/>
    public virtual FieldMember AsConstant(bool value)
    {
        IsConstant = value;
        return this;
    }

    /// <inheritdoc/>
    public FieldMember AsConstant() => AsConstant(value: true);

    /// <inheritdoc/>
    public virtual FieldMember AsReadOnly(bool value)
    {
        IsReadOnly = value;
        return this;
    }

    /// <inheritdoc/>
    public FieldMember AsReadOnly() => AsReadOnly(value: true);

    /// <inheritdoc/>
    public virtual FieldMember AsVolatile(bool value)
    {
        IsVolatile = value;
        return this;
    }

    /// <inheritdoc/>
    public FieldMember AsVolatile() => AsVolatile(value: true);

    /// <inheritdoc/>
    public override FieldMember AsStatic(bool value)
    {
        IsStatic = value;
        return this;
    }

    /// <inheritdoc/>
    public virtual FieldMember AsRef(bool value)
    {
        IsRef = value;
        return this;
    }

    /// <inheritdoc/>
    public FieldMember AsRef() => AsRef(value: true);

    /// <inheritdoc/>
    public virtual FieldMember WithValue(string? value)
    {
        Value = value;
        return this;
    }

    /// <inheritdoc/>
    public FieldMember WithValue<TValue>(TValue value) => WithValue(value is string ? $"\"{value}\"" : value?.ToString());

    /// <inheritdoc/>
    public override FieldMember WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <inheritdoc/>
    public override FieldMember WithAttribute(params IEnumerable<string> attributes)
    {
        AddAttribute(attributes);
        return this;
    }

    /// <inheritdoc/>
    public override FieldMember WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override FieldMember WithName(string name)
    {
        Name = name;
        return this;
    }

    /// <inheritdoc/>
    public override FieldMember WithType(string type)
    {
        Type = type;
        return this;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        ObjectPool<FieldMember>.Shared.Return(this, x =>
        {
            x.Value = default;
            x.IsReadOnly = default;
            x.IsVolatile = default;
            x.IsConstant = default;
            x.IsRef = default;
        });
    }

    /// <summary>
    /// Создаёт новое поле.
    /// </summary>
    public static FieldMember Create() => ObjectPool<FieldMember>.Shared.Rent();

    /// <summary>
    /// Создаёт новое поле.
    /// </summary>
    /// <param name="name">Имя поля.</param>
    public static FieldMember Create(string name) => Create().WithName(name);

    /// <summary>
    /// Создаёт новое поле.
    /// </summary>
    /// <param name="name">Имя поля.</param>
    /// <typeparam name="T">Тип поля.</typeparam>
    public static FieldMember Create<T>(string name) => Create(name).WithType<T>();

    /// <summary>
    /// Создаёт новое поле.
    /// </summary>
    /// <param name="name">Имя поля.</param>
    /// <param name="value">Значение поля.</param>
    /// <typeparam name="T">Тип поля.</typeparam>
    /// <returns></returns>
    public static FieldMember Create<T>(string name, string? value) => Create<T>(name).WithValue(value);

    /// <summary>
    /// Создаёт новое поле.
    /// </summary>
    /// <param name="name">Имя поля.</param>
    /// <param name="value">Значение поля.</param>
    /// <typeparam name="T">Тип поля.</typeparam>
    /// <returns></returns>
    public static FieldMember Create<T>(string name, T value) => Create<T>(name).WithValue(value);
}