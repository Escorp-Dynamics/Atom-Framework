using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;
using Atom.Text;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя асессора.
/// </summary>
public class PropertyAccessorMember : Entity<PropertyAccessorMember>
{
    internal AccessModifier ParentAccessModifier { get; set; }

    /// <summary>
    /// Модификатор доступа.
    /// </summary>
    public AccessModifier AccessModifier { get; protected set; }

    /// <summary>
    /// Является ли только для чтения.
    /// </summary>
    public bool IsReadOnly { get; protected set; }

    /// <summary>
    /// Исходный код асессора.
    /// </summary>
    public string Code { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public override bool IsValid => !string.IsNullOrEmpty(Name);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PropertyAccessorMember"/>.
    /// </summary>
    public PropertyAccessorMember() => Name = "get";

    /// <inheritdoc/>
    protected override void OnBuildingComment([NotNull] StringBuilder sb, string spaces)
    {
        // Method intentionally left empty.
    }

    /// <inheritdoc/>
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, string spaces, params IEnumerable<string> usings)
    {
        var access = AccessModifier.AsString(ParentAccessModifier);
        if (!string.IsNullOrEmpty(access)) access += ' ';

        sb.Append(spaces).Append(access);
        if (IsReadOnly) sb.Append("readonly ");
        sb.Append(Name);

        if (!string.IsNullOrEmpty(Code))
        {
            sb.Append(' ');

            if (Code.StartsWith("return", StringComparison.Ordinal))
            {
                sb.AppendLine($"=> {Code[6..].TrimStart()}");
            }
            else if (Code.CountOf(';', StringComparison.Ordinal) is 1)
            {
                sb.AppendLine($"=> {Code}");
            }
            else
            {
                sb.AppendLine($"\n{spaces}{{");

                foreach (var line in Code.Split('\n', StringSplitOptions.TrimEntries))
                    sb.AppendLine($"{spaces}    {line}");

                sb.AppendLine($"{spaces}}}");
            }
        }
        else
        {
            sb.Append(';');
        }
    }

    /// <summary>
    /// Добавляет модификатор доступа.
    /// </summary>
    /// <param name="modifier">Модификатор доступа.</param>
    public virtual PropertyAccessorMember WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <inheritdoc/>
    public override PropertyAccessorMember WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override PropertyAccessorMember WithName(string name) => this;

    /// <summary>
    /// Определяет, что поле должно быть доступно только для чтения.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    public virtual PropertyAccessorMember AsReadOnly(bool value)
    {
        IsReadOnly = value;
        return this;
    }

    /// <summary>
    /// Определяет, что поле должно быть доступно только для чтения.
    /// </summary>
    public PropertyAccessorMember AsReadOnly() => AsReadOnly(value: true);

    /// <inheritdoc/>
    public override PropertyAccessorMember WithAttribute(params IEnumerable<string> attributes)
    {
        AddAttribute(attributes);
        return this;
    }

    /// <summary>
    /// Добавляет исходный код асессора.
    /// </summary>
    /// <param name="code">Исходный код асессора.</param>
    public virtual PropertyAccessorMember WithCode([NotNull] string code)
    {
        Code = code.Trim();
        if (!string.IsNullOrEmpty(code) && !Code.EndsWith(';')) Code += ';';
        return this;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        ObjectPool<PropertyAccessorMember>.Shared.Return(this, x =>
        {
            x.ParentAccessModifier = default;
            x.AccessModifier = default;
            x.Name = "get";
            x.Code = string.Empty;
            x.IsReadOnly = default;
        });
    }

    /// <summary>
    /// Создаёт новый экземпляр асессора свойства.
    /// </summary>
    public static PropertyAccessorMember Create() => ObjectPool<PropertyAccessorMember>.Shared.Rent();
}