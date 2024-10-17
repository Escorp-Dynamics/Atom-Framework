using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;

namespace Atom.SourceGeneration;

/// <summary>
/// Структура, представляющая значение перечисления.
/// </summary>
public class EnumMember : Entity<EnumMember>
{
    /// <summary>
    /// Значение перечисления.
    /// </summary>
    public long Value { get; protected set; } = -1;

    /// <inheritdoc/>
    public override bool IsValid => !string.IsNullOrEmpty(Name);

    /// <inheritdoc/>
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, string spaces)
    {
        if (Value < 0)
            sb.AppendLine($"{spaces}{Name},");
        else
            sb.AppendLine($"{spaces}{Name} = {Value},");
    }

    /// <inheritdoc/>
    public override EnumMember WithComment(string comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override EnumMember WithAttributes(params string[] attributes)
    {
        AddAttributes(attributes);
        return this;
    }

    /// <inheritdoc/>
    public override EnumMember WithName(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// Задаёт значение перечисления.
    /// </summary>
    /// <param name="value">Значение перечисления.</param>
    public virtual EnumMember WithValue(long value)
    {
        Value = value;
        return this;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();
        ObjectPool<EnumMember>.Shared.Return(this, x => x.Value = -1);
    }

    /// <summary>
    /// Создаёт новый член перечисления.
    /// </summary>
    public static EnumMember Create() => ObjectPool<EnumMember>.Shared.Rent();

    /// <summary>
    /// Создаёт новый член перечисления.
    /// </summary>
    /// <param name="name">Имя.</param>
    /// <param name="value">Значение.</param>
    public static EnumMember Create(string name, long value) => Create().WithName(name).WithValue(value);

    /// <summary>
    /// Создаёт новый член перечисления.
    /// </summary>
    /// <param name="name">Имя.</param>
    public static EnumMember Create(string name) => Create().WithName(name);
}