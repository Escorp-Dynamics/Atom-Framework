using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя событий.
/// </summary>
public class EventMember : Member<EventMember>, IEventMember<EventMember>
{
    /// <inheritdoc/>
    public EventAddMember? Adder { get; protected set; }

    /// <inheritdoc/>
    public EventRemoveMember? Remover { get; protected set; }

    /// <inheritdoc/>
    public bool IsReadOnly { get; protected set; }

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
    public override bool IsValid => !string.IsNullOrEmpty(Name) && (Adder is not null || Remover is not null) && !string.IsNullOrEmpty(Type);

    private void AppendAccessModifiers(StringBuilder sb, string spaces)
    {
        var access = AccessModifier.AsString();
        if (!string.IsNullOrEmpty(access)) access += ' ';

        sb.Append($"{spaces}{access}");

        if (IsStatic) sb.Append("static ");
        if (IsNew) sb.Append("new ");
        if (IsUnsafe) sb.Append("unsafe ");

        if (IsAbstract)
            sb.Append("abstract ");
        else if (IsVirtual)
            sb.Append("virtual ");
        else if (IsOverride)
            sb.Append("override ");

        if (IsReadOnly) sb.Append("readonly ");
    }

    private void AppendEventHandlers(StringBuilder sb, string spaces)
    {
        var tabs = (spaces.Length / 4) + 1;

        Adder?.ParentAccessModifier = AccessModifier;
        Remover?.ParentAccessModifier = AccessModifier;

        var adder = Adder?.Build(tabs);
        var remover = Remover?.Build(tabs);

        var isAuto = adder?.EndsWith("add;") is true || remover?.EndsWith("remove;") is true;
        var isSimpleAuto = adder?.EndsWith("add;") is true && remover?.EndsWith("remove;") is true;
        var isSimpleAdder = Adder is not null && Remover is null && !isAuto && adder?.Trim().StartsWith("add =>") is true;

        if (!isSimpleAuto)
        {
            if (isSimpleAdder)
                sb.AppendLine(adder!.Trim()[3..]);
            else
                AppendEventHandlersBlock(sb, spaces, adder, remover, isAuto);
        }
        else
        {
            sb.AppendLine(";");
        }
    }

    /// <inheritdoc/>
    protected override void OnBuildingComment([NotNull] StringBuilder sb, string spaces)
    {
        var comment = Comment;
        var valueComment = Remover?.Comment;

        if (string.IsNullOrEmpty(comment) && Adder is not null) comment = Adder.Comment;

        if (!string.IsNullOrEmpty(comment) || !string.IsNullOrEmpty(valueComment))
        {
            if (comment is "<inheritdoc/>")
            {
                sb.AppendLine($"{spaces}/// <inheritdoc/>");
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
        AppendAccessModifiers(sb, spaces);
        sb.Append($"event {Type.GetTypeName(usings)} {Name}");
        AppendEventHandlers(sb, spaces);
    }

    /// <inheritdoc/>
    public virtual EventMember AsReadOnly(bool value)
    {
        IsReadOnly = value;
        return this;
    }

    /// <inheritdoc/>
    public EventMember AsReadOnly() => AsReadOnly(true);

    /// <inheritdoc/>
    public virtual EventMember AsUnsafe(bool value)
    {
        IsUnsafe = value;
        return this;
    }

    /// <inheritdoc/>
    public EventMember AsUnsafe() => AsUnsafe(true);

    /// <inheritdoc/>
    public virtual EventMember AsAbstract(bool value)
    {
        IsAbstract = value;
        return this;
    }

    /// <inheritdoc/>
    public EventMember AsAbstract() => AsAbstract(true);

    /// <inheritdoc/>
    public virtual EventMember AsVirtual(bool value)
    {
        IsVirtual = value;
        return this;
    }

    /// <inheritdoc/>
    public EventMember AsVirtual() => AsVirtual(true);

    /// <inheritdoc/>
    public virtual EventMember AsOverride(bool value)
    {
        IsOverride = value;
        return this;
    }

    /// <inheritdoc/>
    public EventMember AsOverride() => AsOverride(true);

    /// <inheritdoc/>
    public virtual EventMember AsNew(bool value)
    {
        IsNew = value;
        return this;
    }

    /// <inheritdoc/>
    public EventMember AsNew() => AsNew(true);

    /// <inheritdoc/>
    public virtual EventMember WithAdder(EventAddMember adder)
    {
        Adder = adder;
        return this;
    }

    /// <inheritdoc/>
    public EventMember WithAdder(string body, bool isReadOnly, params IEnumerable<string> attributes) => WithAdder(EventAddMember.Create()
        .WithAttribute(attributes)
        .WithCode(body)
        .AsReadOnly(isReadOnly)
        .WithAccessModifier(AccessModifier)
    );

    /// <inheritdoc/>
    public EventMember WithAdder(string body, params IEnumerable<string> attributes) => WithAdder(body, default, attributes);

    /// <inheritdoc/>
    public EventMember WithAdder() => WithAdder(string.Empty);

    /// <inheritdoc/>
    public virtual EventMember WithRemover(EventRemoveMember remover)
    {
        Remover = remover;
        return this;
    }

    /// <inheritdoc/>
    public EventMember WithRemover(string body, string comment, params IEnumerable<string> attributes) => WithRemover(EventRemoveMember.Create()
        .WithAttribute(attributes)
        .WithCode(body)
        .WithComment(comment)
        .WithAccessModifier(AccessModifier)
    );

    /// <inheritdoc/>
    public EventMember WithRemover(string body) => WithRemover(body, string.Empty);

    /// <inheritdoc/>
    public EventMember WithRemover() => WithRemover(string.Empty);

    /// <inheritdoc/>
    public override EventMember WithType(string type)
    {
        Type = type;
        return this;
    }

    /// <inheritdoc/>
    public override EventMember AsStatic(bool value)
    {
        IsStatic = value;
        return this;
    }

    /// <inheritdoc/>
    public override EventMember WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <inheritdoc/>
    public override EventMember WithAttribute(params IEnumerable<string> attributes)
    {
        AddAttribute(attributes);
        return this;
    }

    /// <inheritdoc/>
    public override EventMember WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override EventMember WithName(string name)
    {
        Name = name;
        return this;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        ObjectPool<EventMember>.Shared.Return(this, x =>
        {
            x.Adder?.Release();
            x.Adder = default;
            x.Remover?.Release();
            x.Remover = default;
            x.IsReadOnly = default;
            x.IsUnsafe = default;
            x.IsAbstract = default;
            x.IsNew = default;
            x.IsOverride = default;
            x.IsVirtual = default;
        });
    }

    private static void AppendEventHandlersBlock(StringBuilder sb, string spaces, string? adder, string? remover, bool isAuto)
    {
        if (isAuto)
            sb.Append($" {{");
        else
            sb.AppendLine($"\n{spaces}{{");

        if (!string.IsNullOrEmpty(adder))
        {
            if (isAuto)
                sb.Append($" {adder.TrimStart()}");
            else
                sb.Append($"{adder}");
        }

        if (!string.IsNullOrEmpty(remover))
        {
            if (isAuto)
            {
                sb.Append($" {remover.TrimStart()}");
            }
            else
            {
                if (!string.IsNullOrEmpty(adder) && adder.TrimStart().StartsWith("add =>") && !remover.TrimStart().StartsWith("remove =>")) sb.AppendLine();
                sb.Append($"{remover}");
            }
        }

        if (isAuto)
            sb.AppendLine($" }}");
        else
            sb.AppendLine($"{spaces}}}");
    }

    /// <summary>
    /// Создаёт новый экземпляр строителя событий.
    /// </summary>
    public static EventMember Create() => ObjectPool<EventMember>.Shared.Rent();

    /// <summary>
    /// Создаёт новый экземпляр строителя событий.
    /// </summary>
    /// <param name="name">Имя события.</param>
    /// <param name="accessModifier">Модификатор доступа.</param>
    /// <typeparam name="T">Тип события.</typeparam>
    public static EventMember Create<T>(string name, AccessModifier accessModifier)
        => Create().WithType<T>().WithName(name).WithAccessModifier(accessModifier).WithAdder().WithRemover();

    /// <summary>
    /// Создаёт новый экземпляр строителя событий.
    /// </summary>
    /// <param name="name">Имя события.</param>
    /// <param name="accessModifier">Модификатор доступа.</param>
    public static EventMember Create(string name, AccessModifier accessModifier)
        => Create().WithType<MutableEventHandler>().WithName(name).WithAccessModifier(accessModifier).WithAdder().WithRemover();

    /// <summary>
    /// Создаёт новый экземпляр строителя событий.
    /// </summary>
    /// <param name="name">Имя события.</param>
    /// <typeparam name="T">Тип события.</typeparam>
    public static EventMember Create<T>(string name) => Create<T>(name, default);

    /// <summary>
    /// Создаёт новый экземпляр строителя событий c подписчиком.
    /// </summary>
    /// <param name="name">Имя события.</param>
    /// <param name="accessModifier">Модификатор доступа.</param>
    /// <typeparam name="T">Тип события.</typeparam>
    public static EventMember CreateWithAdderOnly<T>(string name, AccessModifier accessModifier)
        => Create().WithType<T>().WithName(name).WithAccessModifier(accessModifier).WithAdder();

    /// <summary>
    /// Создаёт новый экземпляр строителя событий c подписчиком.
    /// </summary>
    /// <param name="name">Имя события.</param>
    /// <typeparam name="T">Тип события.</typeparam>
    public static EventMember CreateWithAdderOnly<T>(string name) => CreateWithAdderOnly<T>(name, default);

    /// <summary>
    /// Создаёт новый экземпляр строителя событий c отписчиком.
    /// </summary>
    /// <param name="name">Имя события.</param>
    /// <param name="accessModifier">Модификатор доступа.</param>
    /// <typeparam name="T">Тип события.</typeparam>
    public static EventMember CreateWithRemoverOnly<T>(string name, AccessModifier accessModifier)
        => Create().WithType<T>().WithName(name).WithAccessModifier(accessModifier).WithRemover();

    /// <summary>
    /// Создаёт новый экземпляр строителя событий c отписчиком.
    /// </summary>
    /// <param name="name">Имя события.</param>
    /// <typeparam name="T">Тип события.</typeparam>
    public static EventMember CreateWithRemoverOnly<T>(string name) => CreateWithRemoverOnly<T>(name, default);
}