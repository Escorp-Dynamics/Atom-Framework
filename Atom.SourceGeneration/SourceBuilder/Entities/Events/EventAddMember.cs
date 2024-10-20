using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;
using Atom.Text;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя асессора события.
/// </summary>
public class EventAddMember : Entity<EventAddMember>
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
    /// Инициализирует новый экземпляр <see cref="EventAddMember"/>.
    /// </summary>
    public EventAddMember() => Name = "add";

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

        if (IsReadOnly)
            sb.Append($"{spaces}{access}readonly {Name}");
        else
            sb.Append($"{spaces}{access}{Name}");

        if (!string.IsNullOrEmpty(Code))
        {
            sb.Append(' ');

            if (Code.StartsWith("return"))
                sb.AppendLine($"=> {Code[6..].TrimStart()}");
            else if (Code.CountOf(';') is 1)
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
    public virtual EventAddMember WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <summary>
    /// Определяет, что событие должно быть доступно только для чтения.
    /// </summary>
    /// <param name="value">Значение события.</param>
    public virtual EventAddMember AsReadOnly(bool value)
    {
        IsReadOnly = value;
        return this;
    }

    /// <inheritdoc/>
    public override EventAddMember WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override EventAddMember WithName(string name) => this;

    /// <inheritdoc/>
    public override EventAddMember WithAttributes(params string[] attributes)
    {
        AddAttributes(attributes);
        return this;
    }

    /// <summary>
    /// Добавляет исходный код асессора.
    /// </summary>
    /// <param name="code">Исходный код асессора.</param>
    public virtual EventAddMember WithCode([NotNull] string code)
    {
        Code = code.Trim();
        if (!string.IsNullOrEmpty(code) && !Code.EndsWith(';')) Code += ';';
        return this;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();
        
        ObjectPool<EventAddMember>.Shared.Return(this, x =>
        {
            x.ParentAccessModifier = default;
            x.AccessModifier = default;
            x.Code = string.Empty;
            x.Name = "add";
            x.IsReadOnly = default;
        });
    }

    /// <summary>
    /// Создаёт новый экземпляр подписчика события.
    /// </summary>
    public static EventAddMember Create() => ObjectPool<EventAddMember>.Shared.Rent();

    /// <summary>
    /// Создаёт новый экземпляр подписчика события.
    /// </summary>
    /// <param name="body">Код подписчика.</param>
    public static EventAddMember Create(string body) => Create().WithCode(body);
}