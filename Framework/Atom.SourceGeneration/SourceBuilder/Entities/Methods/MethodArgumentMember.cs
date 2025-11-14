using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя аргумента метода.
/// </summary>
public class MethodArgumentMember : Entity<MethodArgumentMember>
{
    /// <summary>
    /// Является ли входным.
    /// </summary>
    public bool IsIn { get; protected set; }

    /// <summary>
    /// Является ли выходным.
    /// </summary>
    public bool IsOut { get; protected set; }

    /// <summary>
    /// Является ли ссылочным.
    /// </summary>
    public bool IsRef { get; protected set; }

    /// <summary>
    /// Является ли параметрическим.
    /// </summary>
    public bool IsParams { get; protected set; }

    /// <summary>
    /// Является ли расширением.
    /// </summary>
    public bool IsExtension { get; protected set; }

    /// <summary>
    /// Тип.
    /// </summary>
    public string Type { get; protected set; } = string.Empty;

    /// <summary>
    /// Значение инициализации.
    /// </summary>
    public string? InitialValue { get; protected set; }

    /// <inheritdoc/>
    public override bool IsValid => !string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(Type);

    /// <inheritdoc/>
    protected override void OnBuildingComment([NotNull] StringBuilder sb, string spaces)
    {
        // Method intentionally left empty.
    }

    /// <inheritdoc/>
    protected override void OnBuildingAttributes([NotNull] StringBuilder sb, string spaces)
    {
        foreach (var attr in Attributes) sb.Append($"[{attr}]");
        if (sb.Length > 0) sb.Append(' ');
    }

    /// <inheritdoc/>
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, string spaces, [NotNull] params IEnumerable<string> usings)
    {
        if (IsIn)
            sb.Append("in ");
        else if (IsOut)
            sb.Append("out ");
        else if (IsRef)
            sb.Append("ref ");
        else if (IsParams)
            sb.Append("params ");

        sb.Append($"{Type.GetTypeName(usings)} {Name}");

        if (!IsIn && !IsOut && !IsRef && !IsParams && !string.IsNullOrEmpty(InitialValue))
            sb.Append($" = {InitialValue}");
    }

    /// <summary>
    /// Указывает, что аргумент будет входным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    public virtual MethodArgumentMember AsIn(bool value)
    {
        IsIn = value;
        return this;
    }

    /// <summary>
    /// Указывает, что аргумент будет входным.
    /// </summary>
    public MethodArgumentMember AsIn() => AsIn(value: true);

    /// <summary>
    /// Указывает, что аргумент будет выходным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    public virtual MethodArgumentMember AsOut(bool value)
    {
        IsOut = value;
        return this;
    }

    /// <summary>
    /// Указывает, что аргумент будет выходным.
    /// </summary>
    public MethodArgumentMember AsOut() => AsOut(value: true);

    /// <summary>
    /// Указывает, что аргумент будет ссылочным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    public virtual MethodArgumentMember AsRef(bool value)
    {
        IsRef = value;
        return this;
    }

    /// <summary>
    /// Указывает, что аргумент будет ссылочным.
    /// </summary>
    public MethodArgumentMember AsRef() => AsRef(value: true);

    /// <summary>
    /// Указывает, что аргумент будет параметрическим.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    public virtual MethodArgumentMember AsParams(bool value)
    {
        IsParams = value;
        return this;
    }

    /// <summary>
    /// Указывает, что аргумент будет параметрическим.
    /// </summary>
    public MethodArgumentMember AsParams() => AsParams(value: true);

    /// <summary>
    /// Указывает, что аргумент будет расширяемым.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    public virtual MethodArgumentMember AsExtension(bool value)
    {
        IsExtension = value;
        return this;
    }

    /// <summary>
    /// Указывает, что аргумент будет расширяемым.
    /// </summary>
    public MethodArgumentMember AsExtension() => AsExtension(value: true);

    /// <summary>
    /// Назначает тип.
    /// </summary>
    /// <param name="type">Тип.</param>
    public virtual MethodArgumentMember WithType(string type)
    {
        Type = type;
        return this;
    }

    /// <summary>
    /// Назначает тип.
    /// </summary>
    /// <typeparam name="T">Тип.</typeparam>
    /// <param name="withNamespaces">Указывает, нужно ли возвращать полное имя типа с пространством имён.</param>
    /// <param name="withNullable">Указывает, нужно ли возвращать имя типа с nullable-модификатором.</param>
    /// <param name="withGenericNullable">Указывает, нужно ли возвращать имя типа дженерика с nullable-модификатором.</param>
    public MethodArgumentMember WithType<T>(bool withNamespaces = true, bool withNullable = true, bool withGenericNullable = true) where T : allows ref struct => WithType(typeof(T).GetFriendlyName(withNamespaces, withNullable, withGenericNullable));

    /// <summary>
    /// Добавляет значение инициализации.
    /// </summary>
    /// <param name="value">Значение инициализации.</param>
    public virtual MethodArgumentMember WithInitialValue(string? value)
    {
        InitialValue = value;
        return this;
    }

    /// <summary>
    /// Добавляет значение инициализации.
    /// </summary>
    /// <param name="value">Значение инициализации.</param>
    /// <typeparam name="TValue">Тип значения инициализации.</typeparam>
    public MethodArgumentMember WithInitialValue<TValue>(TValue value) => WithInitialValue(value is string ? $"\"{value}\"" : value?.ToString());

    /// <inheritdoc/>
    public override MethodArgumentMember WithAttribute(params IEnumerable<string> attributes)
    {
        AddAttribute(attributes);
        return this;
    }

    /// <inheritdoc/>
    public override MethodArgumentMember WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override MethodArgumentMember WithName(string name)
    {
        Name = name;
        return this;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        ObjectPool<MethodArgumentMember>.Shared.Return(this, x =>
        {
            IsIn = default;
            IsOut = default;
            IsRef = default;
            IsParams = default;
            IsExtension = default;
            Type = string.Empty;
            InitialValue = default;
        });
    }

    /// <summary>
    /// Создаёт новый экземпляр аргумента метода.
    /// </summary>
    public static MethodArgumentMember Create() => ObjectPool<MethodArgumentMember>.Shared.Rent();

    /// <summary>
    /// Создаёт новый экземпляр аргумента метода.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <typeparam name="T">Тип аргумента.</typeparam>
    public static MethodArgumentMember Create<T>(string name) => Create().WithName(name).WithType<T>();

    /// <summary>
    /// Создаёт новый экземпляр аргумента метода.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    public static MethodArgumentMember Create(string name) => Create().WithName(name).WithType<object>();

    /// <summary>
    /// Создаёт новый экземпляр аргумента метода.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="value">Значение инициализации.</param>
    /// <typeparam name="T">Тип аргумента.</typeparam>
    public static MethodArgumentMember Create<T>(string name, string? value) => Create<T>(name).WithInitialValue(value);

    /// <summary>
    /// Создаёт новый экземпляр аргумента метода.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="value">Значение инициализации.</param>
    /// <typeparam name="T">Тип аргумента.</typeparam>
    public static MethodArgumentMember Create<T>(string name, T value) => Create<T>(name).WithInitialValue(value);

    /// <summary>
    /// Создаёт новый экземпляр входного аргумента метода.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <typeparam name="T">Тип аргумента.</typeparam>
    public static MethodArgumentMember CreateIn<T>(string name) => Create<T>(name).AsIn();

    /// <summary>
    /// Создаёт новый экземпляр входного аргумента метода.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="value">Значение инициализации.</param>
    /// <typeparam name="T">Тип аргумента.</typeparam>
    public static MethodArgumentMember CreateIn<T>(string name, string? value) => CreateIn<T>(name).WithInitialValue(value);

    /// <summary>
    /// Создаёт новый экземпляр входного аргумента метода.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="value">Значение инициализации.</param>
    /// <typeparam name="T">Тип аргумента.</typeparam>
    public static MethodArgumentMember CreateIn<T>(string name, T value) => CreateIn<T>(name).WithInitialValue(value);

    /// <summary>
    /// Создаёт новый экземпляр выходного аргумента метода.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <typeparam name="T">Тип аргумента.</typeparam>
    public static MethodArgumentMember CreateOut<T>(string name) => Create<T>(name).AsOut();

    /// <summary>
    /// Создаёт новый экземпляр ссылочного аргумента метода.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <typeparam name="T">Тип аргумента.</typeparam>
    public static MethodArgumentMember CreateRef<T>(string name) => Create<T>(name).AsRef();

    /// <summary>
    /// Создаёт новый экземпляр параметрического аргумента метода.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <typeparam name="T">Тип аргумента.</typeparam>
    public static MethodArgumentMember CreateParams<T>(string name) => Create<T[]>(name).AsParams();

    /// <summary>
    /// Создаёт новый экземпляр аргумента метода расширения.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <typeparam name="T">Тип аргумента.</typeparam>
    public static MethodArgumentMember CreateExtension<T>(string name) => Create<T>(name).AsExtension();
}