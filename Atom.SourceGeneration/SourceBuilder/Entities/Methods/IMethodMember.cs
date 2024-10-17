namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя методов.
/// </summary>
/// <typeparam name="T">Тип строителя методов.</typeparam>
public interface IMethodMember<out T> : IMember<T> where T : IEntity
{
    /// <summary>
    /// Шаблонные типы.
    /// </summary>
    IEnumerable<GenericEntity> Generics { get; }

    /// <summary>
    /// Аргументы метода.
    /// </summary>
    IEnumerable<MethodArgumentMember> Arguments { get; }

    /// <summary>
    /// Исходный код метода.
    /// </summary>
    string Code { get; }

    /// <summary>
    /// Является ли метод частичным.
    /// </summary>
    bool IsPartial { get; }

    /// <summary>
    /// Является ли метод только для чтения.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Является ли метод ссылочным.
    /// </summary>
    bool IsRef { get; }

    /// <summary>
    /// Является ли метод ссылочным и только для чтения.
    /// </summary>
    bool IsReadOnlyRef { get; }

    /// <summary>
    /// Является ли метод небезопасным.
    /// </summary>
    public bool IsUnsafe { get; }

    /// <summary>
    /// Является ли метод абстрактным.
    /// </summary>
    public bool IsAbstract { get; }

    /// <summary>
    /// Является ли метод виртуальным.
    /// </summary>
    public bool IsVirtual { get; }

    /// <summary>
    /// Является ли метод перезаписанным.
    /// </summary>
    public bool IsOverride { get; }

    /// <summary>
    /// Является ли метод переопределённым.
    /// </summary>
    public bool IsNew { get; }

    /// <summary>
    /// Является ли метод асинхронным.
    /// </summary>
    public bool IsAsync { get; }

    /// <summary>
    /// Добавляет исходный код метода.
    /// </summary>
    /// <param name="code">Исходный код метода.</param>
    T WithCode(string code);

    /// <summary>
    /// Определяет, что метод должен быть доступно только для чтения.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsReadOnly(bool value);

    /// <summary>
    /// Определяет, что метод должен быть доступно только для чтения.
    /// </summary>
    T AsReadOnly() => AsReadOnly(true);

    /// <summary>
    /// Определяет, что метод должен быть частичным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsPartial(bool value);

    /// <summary>
    /// Определяет, что метод должен быть частичным.
    /// </summary>
    T AsPartial() => AsPartial(true);

    /// <summary>
    /// Определяет, что метод должен возвращать ссылку.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsRef(bool value);

    /// <summary>
    /// Определяет, что метод должен возвращать ссылку.
    /// </summary>
    T AsRef() => AsRef(true);

    /// <summary>
    /// Определяет, что метод должен возвращать ссылку только для чтения.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsReadOnlyRef(bool value);

    /// <summary>
    /// Определяет, что метод должен возвращать ссылку только для чтения.
    /// </summary>
    T AsReadOnlyRef() => AsReadOnlyRef(true);

    /// <summary>
    /// Определяет, что метод должен быть небезопасным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsUnsafe(bool value);

    /// <summary>
    /// Определяет, что метод должен быть небезопасным.
    /// </summary>
    T AsUnsafe() => AsUnsafe(true);

    /// <summary>
    /// Определяет, что метод должен быть абстрактным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsAbstract(bool value);

    /// <summary>
    /// Определяет, что метод должен быть абстрактным.
    /// </summary>
    T AsAbstract() => AsAbstract(true);

    /// <summary>
    /// Определяет, что метод должен быть виртуальным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsVirtual(bool value);

    /// <summary>
    /// Определяет, что метод должен быть виртуальным.
    /// </summary>
    T AsVirtual() => AsVirtual(true);

    /// <summary>
    /// Определяет, что метод должен быть перезаписанным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsOverride(bool value);

    /// <summary>
    /// Определяет, что метод должен быть перезаписанным.
    /// </summary>
    T AsOverride() => AsOverride(true);

    /// <summary>
    /// Определяет, что метод должен быть переназначенным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsNew(bool value);

    /// <summary>
    /// Определяет, что метод должен быть переназначенным.
    /// </summary>
    T AsNew() => AsNew(true);

    /// <summary>
    /// Определяет, что метод должен быть асинхронным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsAsync(bool value);

    /// <summary>
    /// Определяет, что метод должен быть асинхронным.
    /// </summary>
    T AsAsync() => AsAsync(true);

    /// <summary>
    /// Добавляет аргументы к методу.
    /// </summary>
    /// <param name="arguments">Массив аргументов метода.</param>
    T AddArguments(params MethodArgumentMember[] arguments);

    /// <summary>
    /// Добавляет аргумент к методу.
    /// </summary>
    /// <param name="argument">Аргумент метода.</param>
    T AddArgument(MethodArgumentMember argument) => AddArguments(argument);

    /// <summary>
    /// Добавляет аргумент к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="comment">Комментарий к аргументу.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T AddArgument<TArg>(string name, string comment, params string[] attributes)
        => AddArgument(MethodArgumentMember.Create<TArg>(name).WithComment(comment).WithAttributes(attributes));

    /// <summary>
    /// Добавляет аргумент к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T AddArgument<TArg>(string name, params string[] attributes) => AddArgument<TArg>(name, string.Empty, attributes);

    /// <summary>
    /// Добавляет входной аргумент к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="comment">Комментарий к аргументу.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T AddInArgument<TArg>(string name, string comment, params string[] attributes)
        => AddArgument(MethodArgumentMember.CreateIn<TArg>(name).WithComment(comment).WithAttributes(attributes));

    /// <summary>
    /// Добавляет входной аргумент к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T AddInArgument<TArg>(string name, params string[] attributes) => AddInArgument<TArg>(name, string.Empty, attributes);

    /// <summary>
    /// Добавляет выходной аргумент к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="comment">Комментарий к аргументу.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T AddOutArgument<TArg>(string name, string comment, params string[] attributes)
        => AddArgument(MethodArgumentMember.CreateOut<TArg>(name).WithComment(comment).WithAttributes(attributes));

    /// <summary>
    /// Добавляет выходной аргумент к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T AddOutArgument<TArg>(string name, params string[] attributes) => AddOutArgument<TArg>(name, string.Empty, attributes);

    /// <summary>
    /// Добавляет ссылочный аргумент к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="comment">Комментарий к аргументу.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T AddRefArgument<TArg>(string name, string comment, params string[] attributes)
        => AddArgument(MethodArgumentMember.CreateRef<TArg>(name).WithComment(comment).WithAttributes(attributes));

    /// <summary>
    /// Добавляет ссылочный аргумент к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T AddRefArgument<TArg>(string name, params string[] attributes) => AddRefArgument<TArg>(name, string.Empty, attributes);

    /// <summary>
    /// Добавляет параметры аргумента к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="comment">Комментарий к аргументу.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T AddParamsArgument<TArg>(string name, string comment, params string[] attributes)
        => AddArgument(MethodArgumentMember.CreateParams<TArg>(name).WithComment(comment).WithAttributes(attributes));

    /// <summary>
    /// Добавляет параметры аргумента к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T AddParamsArgument<TArg>(string name, params string[] attributes) => AddParamsArgument<TArg>(name, string.Empty, attributes);

    /// <summary>
    /// Добавляет шаблонные типы.
    /// </summary>
    /// <param name="generics">Шаблонные типы.</param>
    T AddGenerics(params GenericEntity[] generics);

    /// <summary>
    /// Добавляет шаблонный тип.
    /// </summary>
    /// <param name="generic">Шаблонный тип.</param>
    T AddGeneric(GenericEntity generic) => AddGenerics(generic);

    /// <summary>
    /// Добавляет шаблонный тип.
    /// </summary>
    /// <param name="name">Имя типа.</param>
    /// <param name="comment">Комментарий.</param>
    /// <param name="limitations">Ограничения типа.</param>
    T AddGeneric(string name, string comment, params string[] limitations) => AddGeneric(GenericEntity.Create(name, limitations).WithComment(comment));

    /// <summary>
    /// Добавляет шаблонный тип.
    /// </summary>
    /// <param name="name">Имя типа.</param>
    /// <param name="limitations">Ограничения типа.</param>
    T AddGeneric(string name, params string[] limitations) => AddGeneric(name, string.Empty, limitations);
}