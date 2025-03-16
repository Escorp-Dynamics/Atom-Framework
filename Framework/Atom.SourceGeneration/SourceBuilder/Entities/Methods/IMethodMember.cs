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
    bool IsUnsafe { get; }

    /// <summary>
    /// Является ли метод абстрактным.
    /// </summary>
    bool IsAbstract { get; }

    /// <summary>
    /// Является ли метод виртуальным.
    /// </summary>
    bool IsVirtual { get; }

    /// <summary>
    /// Является ли метод перезаписанным.
    /// </summary>
    bool IsOverride { get; }

    /// <summary>
    /// Является ли метод переопределённым.
    /// </summary>
    bool IsNew { get; }

    /// <summary>
    /// Является ли метод асинхронным.
    /// </summary>
    bool IsAsync { get; }

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
    T WithArgument(params MethodArgumentMember[] arguments);

    /// <summary>
    /// Добавляет аргумент к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T WithArgument<TArg>(string name, params string[] attributes)
        => WithArgument(MethodArgumentMember.Create<TArg>(name).WithAttribute(attributes));

    /// <summary>
    /// Добавляет входной аргумент к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T WithInArgument<TArg>(string name, params string[] attributes)
        => WithArgument(MethodArgumentMember.CreateIn<TArg>(name).WithAttribute(attributes));

    /// <summary>
    /// Добавляет выходной аргумент к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T WithOutArgument<TArg>(string name, params string[] attributes)
        => WithArgument(MethodArgumentMember.CreateOut<TArg>(name).WithAttribute(attributes));

    /// <summary>
    /// Добавляет ссылочный аргумент к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T WithRefArgument<TArg>(string name, params string[] attributes)
        => WithArgument(MethodArgumentMember.CreateRef<TArg>(name).WithAttribute(attributes));

    /// <summary>
    /// Добавляет параметры аргумента к методу.
    /// </summary>
    /// <param name="name">Имя аргумента.</param>
    /// <param name="attributes">Атрибуты аргумента.</param>
    T WithParamsArgument<TArg>(string name, params string[] attributes)
        => WithArgument(MethodArgumentMember.CreateParams<TArg>(name).WithAttribute(attributes));

    /// <summary>
    /// Добавляет шаблонные типы.
    /// </summary>
    /// <param name="generics">Шаблонные типы.</param>
    T WithGeneric(params GenericEntity[] generics);

    /// <summary>
    /// Добавляет шаблонный тип.
    /// </summary>
    /// <param name="name">Имя типа.</param>
    /// <param name="limitations">Ограничения типа.</param>
    T WithGeneric(string name, params string[] limitations) => WithGeneric(GenericEntity.Create(name, limitations));
}