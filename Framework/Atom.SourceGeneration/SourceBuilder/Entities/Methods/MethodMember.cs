using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;
using Atom.Collections;
using Atom.Text;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя методов.
/// </summary>
public class MethodMember : Member<MethodMember>, IMethodMember<MethodMember>
{
    private readonly SparseArray<GenericEntity> generics = new(128);
    private readonly SparseArray<MethodArgumentMember> arguments = new(128);

    internal bool IsInterface { get; set; }

    /// <inheritdoc/>
    public IEnumerable<GenericEntity> Generics => generics;

    /// <inheritdoc/>
    public IEnumerable<MethodArgumentMember> Arguments => arguments;

    /// <inheritdoc/>
    public string Code { get; protected set; } = string.Empty;

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
    public bool IsAsync { get; protected set; }

    /// <inheritdoc/>
    public override bool IsValid => !string.IsNullOrEmpty(Name);

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
        if (IsAsync) sb.Append("async ");
    }

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

    private void AppendArguments(StringBuilder sb, params string[] usings)
    {
        if (arguments.IsEmpty) return;

        foreach (var arg in arguments)
        {
            var sign = arg.Build(usings);
            if (string.IsNullOrEmpty(sign)) continue;
            sb.Append($"{sign}, ");
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

    private void AppendSignature(StringBuilder sb, params string[] usings)
    {
        sb.Append($"{Type.GetTypeName(usings)} {Name}");
        AppendGenerics(sb);
        sb.Append('(');
        AppendArguments(sb, usings);
        sb.Append(')');
        AppendGenericsLimitations(sb);
    }

    private void AppendBody(StringBuilder sb, string spaces)
    {
        if (Code.StartsWith("return"))
        {
            sb.AppendLine($" => {Code[6..].TrimStart()}");
        }
        else if (Code.CountOf(';') is 1 && !Code.Contains("if") && !Code.Contains("for") && !Code.Contains("while") && !Code.Contains("do"))
        {
            sb.AppendLine($" => {Code}");
        }
        else
        {
            sb.AppendLine($"\n{spaces}{{");

            foreach (var line in Code.Split('\n', StringSplitOptions.TrimEntries))
            {
                if (line.EndsWith('}')) spaces = spaces[..^4];

                var l = $"{spaces}    {line}";
                if (string.IsNullOrWhiteSpace(l)) l = null;

                sb.AppendLine(l);
                if (line.EndsWith('{')) spaces += "    ";
            }

            sb.AppendLine($"{spaces}}}");
        }
    }

    /// <inheritdoc/>
    protected override void OnBuildingComment([NotNull] StringBuilder sb, string spaces)
    {
        if (string.IsNullOrEmpty(Comment)) return;

        if (Comment is "<inheritdoc/>")
        {
            sb.AppendLine($"{spaces}/// {Comment}");
            return;
        }

        sb.AppendLine($"{spaces}/// <summary>")
          .AppendLine($"{spaces}/// {Comment}")
          .AppendLine($"{spaces}/// </summary>");

        foreach (var arg in arguments)
        {
            if (!string.IsNullOrEmpty(arg.Comment) && !string.IsNullOrEmpty(arg.Name))
                sb.AppendLine($"{spaces}/// <param name=\"{arg.Name}\">{arg.Comment}</param>");
        }

        foreach (var generic in generics)
        {
            if (!string.IsNullOrEmpty(generic.Comment) && !string.IsNullOrEmpty(generic.Name))
                sb.AppendLine($"{spaces}/// <typeparam name=\"{generic.Name}\">{generic.Comment}</typeparam>");
        }
    }

    /// <inheritdoc/>
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, string spaces, params string[] usings)
    {
        AppendModifiers(sb, spaces);
        AppendSignature(sb, usings);

        if (IsAbstract)
        {
            sb.AppendLine(";");
            return;
        }

        if (string.IsNullOrEmpty(Code))
        {
            if (IsPartial || IsInterface)
                sb.AppendLine(";");
            else
                sb.AppendLine(" { }");

            return;
        }

        AppendBody(sb, spaces);
    }

    /// <inheritdoc/>
    public virtual MethodMember WithCode([NotNull] string code)
    {
        Code = code.Trim();
        if (!string.IsNullOrEmpty(code) && !Code.EndsWith(';') && !Code.EndsWith('}')) Code += ';';
        return this;
    }

    /// <inheritdoc/>
    public virtual MethodMember AsReadOnly(bool value)
    {
        IsReadOnly = value;
        return this;
    }

    /// <inheritdoc/>
    public MethodMember AsReadOnly() => AsReadOnly(true);

    /// <inheritdoc/>
    public virtual MethodMember AsPartial(bool value)
    {
        IsPartial = value;
        return this;
    }

    /// <inheritdoc/>
    public MethodMember AsPartial() => AsPartial(true);

    /// <inheritdoc/>
    public virtual MethodMember AsRef(bool value)
    {
        IsRef = value;
        return this;
    }

    /// <inheritdoc/>
    public MethodMember AsRef() => AsRef(true);

    /// <inheritdoc/>
    public virtual MethodMember AsReadOnlyRef(bool value)
    {
        IsReadOnlyRef = value;
        return this;
    }

    /// <inheritdoc/>
    public MethodMember AsReadOnlyRef() => AsReadOnlyRef(true);

    /// <inheritdoc/>
    public virtual MethodMember AsUnsafe(bool value)
    {
        IsUnsafe = value;
        return this;
    }

    /// <inheritdoc/>
    public MethodMember AsUnsafe() => AsUnsafe(true);

    /// <inheritdoc/>
    public virtual MethodMember AsAbstract(bool value)
    {
        IsAbstract = value;
        return this;
    }

    /// <inheritdoc/>
    public MethodMember AsAbstract() => AsAbstract(true);

    /// <inheritdoc/>
    public virtual MethodMember AsVirtual(bool value)
    {
        IsVirtual = value;
        return this;
    }

    /// <inheritdoc/>
    public MethodMember AsVirtual() => AsVirtual(true);

    /// <inheritdoc/>
    public virtual MethodMember AsOverride(bool value)
    {
        IsOverride = value;
        return this;
    }

    /// <inheritdoc/>
    public MethodMember AsOverride() => AsOverride(true);

    /// <inheritdoc/>
    public virtual MethodMember AsNew(bool value)
    {
        IsNew = value;
        return this;
    }

    /// <inheritdoc/>
    public MethodMember AsNew() => AsNew(true);

    /// <inheritdoc/>
    public virtual MethodMember AsAsync(bool value)
    {
        IsAsync = value;
        return this;
    }

    /// <inheritdoc/>
    public MethodMember AsAsync() => AsAsync(true);

    /// <inheritdoc/>
    public virtual MethodMember WithArgument([NotNull] params MethodArgumentMember[] arguments)
    {
        this.arguments.AddRange(arguments);
        return this;
    }

    /// <inheritdoc/>
    public MethodMember WithArgument<TArg>(string name, params string[] attributes)
        => WithArgument(MethodArgumentMember.Create<TArg>(name).WithAttribute(attributes));

    /// <inheritdoc/>
    public MethodMember WithInArgument<TArg>(string name, params string[] attributes)
        => WithArgument(MethodArgumentMember.CreateIn<TArg>(name).WithAttribute(attributes));

    /// <inheritdoc/>
    public MethodMember WithOutArgument<TArg>(string name, params string[] attributes)
        => WithArgument(MethodArgumentMember.CreateOut<TArg>(name).WithAttribute(attributes));

    /// <inheritdoc/>
    public MethodMember WithRefArgument<TArg>(string name, params string[] attributes)
        => WithArgument(MethodArgumentMember.CreateRef<TArg>(name).WithAttribute(attributes));

    /// <inheritdoc/>
    public MethodMember WithParamsArgument<TArg>(string name, params string[] attributes)
        => WithArgument(MethodArgumentMember.CreateParams<TArg>(name).WithAttribute(attributes));

    /// <inheritdoc/>
    public virtual MethodMember WithGeneric([NotNull] params GenericEntity[] generics)
    {
        this.generics.AddRange(generics);
        return this;
    }

    /// <inheritdoc/>
    public MethodMember WithGeneric(string name, params string[] limitations) => WithGeneric(GenericEntity.Create(name, limitations));

    /// <inheritdoc/>
    public override MethodMember AsStatic(bool value)
    {
        IsStatic = value;
        return this;
    }

    /// <inheritdoc/>
    public override MethodMember WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <inheritdoc/>
    public override MethodMember WithAttribute(params string[] attributes)
    {
        AddAttribute(attributes);
        return this;
    }

    /// <inheritdoc/>
    public override MethodMember WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override MethodMember WithName(string name)
    {
        Name = name;
        return this;
    }

    /// <inheritdoc/>
    public override MethodMember WithType(string type)
    {
        Type = type;
        return this;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        ObjectPool<MethodMember>.Shared.Return(this, x =>
        {
            foreach (var value in x.generics) value.Release();
            foreach (var value in x.arguments) value.Release();

            x.generics.Reset();
            x.arguments.Reset();

            x.generics.Reset();
            x.arguments.Reset();

            x.IsPartial = default;
            x.IsReadOnly = default;
            x.IsReadOnlyRef = default;
            x.IsRef = default;
            x.IsUnsafe = default;
            x.IsAsync = default;
            x.IsNew = default;
            x.IsAbstract = default;
            x.IsVirtual = default;
            x.IsOverride = default;
            x.Code = string.Empty;
            x.IsInterface = default;
        });
    }

    /// <summary>
    /// Создаёт новый экземпляр строителя метода.
    /// </summary>
    public static MethodMember Create() => ObjectPool<MethodMember>.Shared.Rent();

    /// <summary>
    /// Создаёт новый экземпляр строителя метода.
    /// </summary>
    /// <param name="name">Имя метода.</param>
    /// <param name="modifier">Модификатор доступа.</param>
    /// <param name="isStatic">Указывает, является ли метод статическим.</param>
    public static MethodMember Create(string name, AccessModifier modifier, bool isStatic)
        => Create().WithName(name).WithAccessModifier(modifier).AsStatic(isStatic);

    /// <summary>
    /// Создаёт новый экземпляр строителя метода.
    /// </summary>
    /// <param name="name">Имя метода.</param>
    /// <param name="modifier">Модификатор доступа.</param>
    public static MethodMember Create(string name, AccessModifier modifier) => Create(name, modifier, default);

    /// <summary>
    /// Создаёт новый экземпляр строителя метода.
    /// </summary>
    /// <param name="name">Имя метода.</param>
    /// <param name="isStatic">Указывает, является ли метод статическим.</param>
    public static MethodMember Create(string name, bool isStatic) => Create(name, default, isStatic);

    /// <summary>
    /// Создаёт новый экземпляр строителя метода.
    /// </summary>
    /// <param name="name">Имя метода.</param>
    public static MethodMember Create(string name) => Create(name, false);

    /// <summary>
    /// Создаёт новый экземпляр строителя метода.
    /// </summary>
    /// <param name="name">Имя метода.</param>
    /// <param name="modifier">Модификатор доступа.</param>
    /// <param name="isStatic">Указывает, является ли метод статическим.</param>
    /// <typeparam name="T">Тип возврата из метода.</typeparam>
    public static MethodMember Create<T>(string name, AccessModifier modifier, bool isStatic) => Create(name, modifier, isStatic).WithType<T>();

    /// <summary>
    /// Создаёт новый экземпляр строителя метода.
    /// </summary>
    /// <param name="name">Имя метода.</param>
    /// <param name="modifier">Модификатор доступа.</param>
    /// <typeparam name="T">Тип возврата из метода.</typeparam>
    public static MethodMember Create<T>(string name, AccessModifier modifier) => Create<T>(name, modifier, default);

    /// <summary>
    /// Создаёт новый экземпляр строителя метода.
    /// </summary>
    /// <param name="name">Имя метода.</param>
    /// <param name="isStatic">Указывает, является ли метод статическим.</param>
    /// <typeparam name="T">Тип возврата из метода.</typeparam>
    public static MethodMember Create<T>(string name, bool isStatic) => Create<T>(name, default, isStatic);

    /// <summary>
    /// Создаёт новый экземпляр строителя метода.
    /// </summary>
    /// <param name="name">Имя метода.</param>
    /// <typeparam name="T">Тип возврата из метода.</typeparam>
    public static MethodMember Create<T>(string name) => Create<T>(name, false);
}