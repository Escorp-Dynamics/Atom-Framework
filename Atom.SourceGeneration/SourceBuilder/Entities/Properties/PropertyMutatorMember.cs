using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;
using Atom.Text;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя мутатора свойства.
/// </summary>
public class PropertyMutatorMember : Entity<PropertyMutatorMember>
{
    internal AccessModifier ParentAccessModifier { get; set; }

    /// <summary>
    /// Модификатор доступа.
    /// </summary>
    public AccessModifier AccessModifier { get; protected set; }

    /// <summary>
    /// Исходный код мутатора.
    /// </summary>
    public string Code { get; protected set; } = string.Empty;

    /// <summary>
    /// Является ли мутатор инициализируемым.
    /// </summary>
    public bool IsInit => Name is "init";

    /// <inheritdoc/>
    public override bool IsValid => !string.IsNullOrEmpty(Name);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PropertyMutatorMember"/>.
    /// </summary>
    public PropertyMutatorMember() => Name = "set";

    /// <inheritdoc/>
    protected override void OnBuildingComment([NotNull] StringBuilder sb, string spaces)
    {
        // Method intentionally left empty.
    }

    /// <inheritdoc/>
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, string spaces)
    {
        var access = AccessModifier.AsString(ParentAccessModifier);
        if (!string.IsNullOrEmpty(access)) access += ' ';

        sb.Append($"{spaces}{access}{Name}");

        if (!string.IsNullOrEmpty(Code))
        {
            sb.Append(' ');
            
            if (Code.CountOf(';') is 1)
                sb.AppendLine($"=> {Code}");
            else
            {
                sb.AppendLine($"\n{spaces}{{");

                foreach (var line in Code.Split('\n', StringSplitOptions.TrimEntries))
                    sb.AppendLine($"{spaces}    {line}");

                sb.AppendLine($"{spaces}}}");
            }
        }
        else
            sb.Append(';');
    }

    /// <summary>
    /// Добавляет модификатор доступа.
    /// </summary>
    /// <param name="modifier">Модификатор доступа.</param>
    public virtual PropertyMutatorMember WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <inheritdoc/>
    public override PropertyMutatorMember WithComment(string comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override PropertyMutatorMember WithName(string name) => this;

    /// <inheritdoc/>
    public override PropertyMutatorMember WithAttributes(params string[] attributes)
    {
        AddAttributes(attributes);
        return this;
    }

    /// <summary>
    /// Добавляет исходный код мутатора.
    /// </summary>
    /// <param name="code">Исходный код мутатора.</param>
    public virtual PropertyMutatorMember WithCode([NotNull] string code)
    {
        Code = code.Trim();
        if (!string.IsNullOrEmpty(code) && !Code.EndsWith(';')) Code += ';';
        return this;
    }

    /// <summary>
    /// Указывает, должен ли быть мутатор инициализируемым.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    public virtual PropertyMutatorMember AsInit(bool value)
    {
        Name = value ? "init" : "set";
        return this;
    }

    /// <summary>
    /// Указывает, должен ли быть мутатор инициализируемым.
    /// </summary>
    public PropertyMutatorMember AsInit() => AsInit(true);

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        ObjectPool<PropertyMutatorMember>.Shared.Return(this, x =>
        {
            x.ParentAccessModifier = default;
            x.AccessModifier = default;
            x.Name = "set";
            x.Code = string.Empty;
        });
    }

    /// <summary>
    /// Создаёт новый экземпляр мутатора свойства.
    /// </summary>
    public static PropertyMutatorMember Create() => ObjectPool<PropertyMutatorMember>.Shared.Rent();
}