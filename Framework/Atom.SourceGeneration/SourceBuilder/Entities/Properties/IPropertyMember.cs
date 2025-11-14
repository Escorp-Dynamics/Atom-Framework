namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя свойств.
/// </summary>
/// <typeparam name="T">Тип строителя свойств.</typeparam>
public interface IPropertyMember<out T> : IMember<T> where T : IEntity
{
    /// <summary>
    /// Асессор.
    /// </summary>
    PropertyAccessorMember? Getter { get; }

    /// <summary>
    /// Мутатор.
    /// </summary>
    PropertyMutatorMember? Setter { get; }

    /// <summary>
    /// Является ли свойство частичным.
    /// </summary>
    bool IsPartial { get; }

    /// <summary>
    /// Является ли свойство только для чтения.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Является ли свойство ссылочным.
    /// </summary>
    bool IsRef { get; }

    /// <summary>
    /// Является ли свойство ссылочным и только для чтения.
    /// </summary>
    bool IsReadOnlyRef { get; }

    /// <summary>
    /// Является ли свойство небезопасным.
    /// </summary>
    bool IsUnsafe { get; }

    /// <summary>
    /// Является ли свойство абстрактным.
    /// </summary>
    bool IsAbstract { get; }

    /// <summary>
    /// Является ли свойство виртуальным.
    /// </summary>
    bool IsVirtual { get; }

    /// <summary>
    /// Является ли свойство перезаписанным.
    /// </summary>
    bool IsOverride { get; }

    /// <summary>
    /// Является ли свойство переопределённым.
    /// </summary>
    bool IsNew { get; }

    /// <summary>
    /// Значение инициализации.
    /// </summary>
    string? InitialValue { get; }

    /// <summary>
    /// Добавляет асессор.
    /// </summary>
    /// <param name="getter">Асессор.</param>
    T WithGetter(PropertyAccessorMember getter);

    /// <summary>
    /// Добавляет асессор.
    /// </summary>
    /// <param name="body">Тело асессора.</param>
    /// <param name="isReadOnly">Указывает, являет ли асессор только для чтения.</param>
    /// <param name="attributes">Атрибуты асессора.</param>
    T WithGetter(string body, bool isReadOnly, params IEnumerable<string> attributes) => WithGetter(PropertyAccessorMember.Create()
        .WithAttribute(attributes)
        .WithCode(body)
        .AsReadOnly(isReadOnly)
    );

    /// <summary>
    /// Добавляет асессор.
    /// </summary>
    /// <param name="body">Тело асессора.</param>
    /// <param name="attributes">Атрибуты асессора.</param>
    T WithGetter(string body, params IEnumerable<string> attributes) => WithGetter(body, default, attributes);

    /// <summary>
    /// Добавляет асессор.
    /// </summary>
    T WithGetter() => WithGetter(string.Empty);

    /// <summary>
    /// Добавляет мутатор.
    /// </summary>
    /// <param name="setter">Мутатор.</param>
    T WithSetter(PropertyMutatorMember setter);

    /// <summary>
    /// Добавляет мутатор.
    /// </summary>
    /// <param name="body">Тело мутатора.</param>
    /// <param name="isInit">Указывает, что мутатор будет инициализируемым.</param>
    /// <param name="comment">Комментарий.</param>
    /// <param name="attributes">Атрибуты мутатора.</param>
    T WithSetter(string body, bool isInit, string comment, params IEnumerable<string> attributes) => WithSetter(PropertyMutatorMember.Create()
        .WithAttribute(attributes)
        .WithCode(body)
        .AsInit(isInit)
        .WithComment(comment)
    );

    /// <summary>
    /// Добавляет мутатор.
    /// </summary>
    /// <param name="body">Тело мутатора.</param>
    /// <param name="isInit">Указывает, что мутатор будет инициализируемым.</param>
    T WithSetter(string body, bool isInit) => WithSetter(body, isInit, string.Empty);

    /// <summary>
    /// Добавляет мутатор.
    /// </summary>
    /// <param name="isInit">Указывает, что мутатор будет инициализируемым.</param>
    T WithSetter(bool isInit) => WithSetter(string.Empty, isInit);

    /// <summary>
    /// Добавляет мутатор.
    /// </summary>
    /// <param name="body">Тело мутатора.</param>
    /// <param name="comment">Комментарий.</param>
    /// <param name="attributes">Атрибуты мутатора.</param>
    T WithSetter(string body, string comment, params IEnumerable<string> attributes) => WithSetter(body, default, comment, attributes);

    /// <summary>
    /// Добавляет мутатор.
    /// </summary>
    /// <param name="body">Тело мутатора.</param>
    T WithSetter(string body) => WithSetter(body, string.Empty);

    /// <summary>
    /// Добавляет мутатор.
    /// </summary>
    T WithSetter() => WithSetter(string.Empty);

    /// <summary>
    /// Устанавливает значение инициализации.
    /// </summary>
    /// <param name="value">Выражение значения.</param>
    T WithInitialValue(string? value);

    /// <summary>
    /// Устанавливает значение инициализации.
    /// </summary>
    /// <param name="value">Значение инициализации.</param>
    /// <typeparam name="TValue">Тип значение.</typeparam>
    T WithInitialValue<TValue>(TValue value) => WithInitialValue(value is string ? $"\"{value}\"" : value?.ToString());

    /// <summary>
    /// Определяет, что свойство должно быть доступно только для чтения.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsReadOnly(bool value);

    /// <summary>
    /// Определяет, что свойство должно быть доступно только для чтения.
    /// </summary>
    T AsReadOnly() => AsReadOnly(value: true);

    /// <summary>
    /// Определяет, что свойство должно быть частичным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsPartial(bool value);

    /// <summary>
    /// Определяет, что свойство должно быть частичным.
    /// </summary>
    T AsPartial() => AsPartial(value: true);

    /// <summary>
    /// Определяет, что свойство должно возвращать ссылку.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsRef(bool value);

    /// <summary>
    /// Определяет, что свойство должно возвращать ссылку.
    /// </summary>
    T AsRef() => AsRef(value: true);

    /// <summary>
    /// Определяет, что свойство должно возвращать ссылку только для чтения.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsReadOnlyRef(bool value);

    /// <summary>
    /// Определяет, что свойство должно возвращать ссылку только для чтения.
    /// </summary>
    T AsReadOnlyRef() => AsReadOnlyRef(value: true);

    /// <summary>
    /// Определяет, что свойство должно быть небезопасным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsUnsafe(bool value);

    /// <summary>
    /// Определяет, что свойство должно быть небезопасным.
    /// </summary>
    T AsUnsafe() => AsUnsafe(value: true);

    /// <summary>
    /// Определяет, что свойство должно быть абстрактным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsAbstract(bool value);

    /// <summary>
    /// Определяет, что свойство должно быть абстрактным.
    /// </summary>
    T AsAbstract() => AsAbstract(value: true);

    /// <summary>
    /// Определяет, что свойство должно быть виртуальным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsVirtual(bool value);

    /// <summary>
    /// Определяет, что свойство должно быть виртуальным.
    /// </summary>
    T AsVirtual() => AsVirtual(value: true);

    /// <summary>
    /// Определяет, что свойство должно быть перезаписанным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsOverride(bool value);

    /// <summary>
    /// Определяет, что свойство должно быть перезаписанным.
    /// </summary>
    T AsOverride() => AsOverride(value: true);

    /// <summary>
    /// Определяет, что свойство должно быть переназначенным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsNew(bool value);

    /// <summary>
    /// Определяет, что свойство должно быть переназначенным.
    /// </summary>
    T AsNew() => AsNew(value: true);
}