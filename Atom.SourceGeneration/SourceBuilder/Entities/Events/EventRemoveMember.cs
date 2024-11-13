using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;
using Atom.Text;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя асессора события.
/// </summary>
public class EventRemoveMember : Entity<EventRemoveMember>
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

    /// <inheritdoc/>
    public override bool IsValid => !string.IsNullOrEmpty(Name);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="EventRemoveMember"/>.
    /// </summary>
    public EventRemoveMember() => Name = "remove";

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
    public virtual EventRemoveMember WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <inheritdoc/>
    public override EventRemoveMember WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override EventRemoveMember WithName(string name) => this;

    /// <inheritdoc/>
    public override EventRemoveMember WithAttributes(params string[] attributes)
    {
        AddAttributes(attributes);
        return this;
    }

    /// <summary>
    /// Добавляет исходный код асессора.
    /// </summary>
    /// <param name="code">Исходный код асессора.</param>
    public virtual EventRemoveMember WithCode([NotNull] string code)
    {
        Code = code.Trim();
        if (!string.IsNullOrEmpty(code) && !Code.EndsWith(';')) Code += ';';
        return this;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        ObjectPool<EventRemoveMember>.Shared.Return(this, x =>
        {
            x.ParentAccessModifier = default;
            x.AccessModifier = default;
            x.Code = string.Empty;
            x.Name = "remove";
        });
    }

    /// <summary>
    /// Создаёт новый экземпляр отписчика события.
    /// </summary>
    public static EventRemoveMember Create() => ObjectPool<EventRemoveMember>.Shared.Rent();

    /// <summary>
    /// Создаёт новый экземпляр отписчика события.
    /// </summary>
    /// <param name="body">Код отписчика.</param>
    public static EventRemoveMember Create(string body) => Create().WithCode(body);
}