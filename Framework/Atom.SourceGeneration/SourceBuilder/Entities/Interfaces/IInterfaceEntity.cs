namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя интерфейсов.
/// </summary>
/// <typeparam name="T">Тип строителя интерфейсов.</typeparam>
public interface IInterfaceEntity<out T> : IEntity<T> where T : IEntity
{
    /// <summary>
    /// Модификатор доступа.
    /// </summary>
    AccessModifier AccessModifier { get; }

    /// <summary>
    /// Родители.
    /// </summary>
    /// <value></value>
    IEnumerable<string> Parents { get; }

    /// <summary>
    /// Шаблонные типы.
    /// </summary>
    IEnumerable<GenericEntity> Generics { get; }

    /// <summary>
    /// Свойства.
    /// </summary>
    IEnumerable<PropertyMember> Properties { get; }

    /// <summary>
    /// События.
    /// </summary>
    /// <value></value>
    IEnumerable<EventMember> Events { get; }

    /// <summary>
    /// Методы.
    /// </summary>
    IEnumerable<MethodMember> Methods { get; }

    /// <summary>
    /// Прочие сущности.
    /// </summary>
    IEnumerable<IEntity> Others { get; }

    /// <summary>
    /// Является ли частичным.
    /// </summary>
    bool IsPartial { get; }

    /// <summary>
    /// Является ли небезопасным.
    /// </summary>
    bool IsUnsafe { get; }

    /// <summary>
    /// Добавляет модификатор доступа.
    /// </summary>
    /// <param name="modifier">Модификатор доступа.</param>
    T WithAccessModifier(AccessModifier modifier);

    /// <summary>
    /// Добавляет родителей для наследования.
    /// </summary>
    /// <param name="parents">Родители.</param>
    T WithParent(params string[] parents);

    /// <summary>
    /// Добавляет родителя для наследования.
    /// </summary>
    /// <typeparam name="TType">Тип родителя.</typeparam>
    /// <param name="withNamespaces">Указывает, нужно ли возвращать полное имя типа с пространством имён.</param>
    /// <param name="withNullable">Указывает, нужно ли возвращать имя типа с nullable-модификатором.</param>
    /// <param name="withGenericNullable">Указывает, нужно ли возвращать имя типа дженерика с nullable-модификатором.</param>
    T WithParent<TType>(bool withNamespaces = true, bool withNullable = true, bool withGenericNullable = true) => WithParent(typeof(TType).GetFriendlyName(withNamespaces, withNullable, withGenericNullable));

    /// <summary>
    /// Добавляет шаблоны типов.
    /// </summary>
    /// <param name="generics">Шаблоны типов.</param>
    T WithGeneric(params GenericEntity[] generics);

    /// <summary>
    /// Добавляет шаблон типа.
    /// </summary>
    /// <param name="name">Имя шаблона.</param>
    /// <param name="limitations">Ограничения шаблона.</param>
    T WithGeneric(string name, params string[] limitations) => WithGeneric(GenericEntity.Create(name, limitations));

    /// <summary>
    /// Добавляет свойства.
    /// </summary>
    /// <param name="properties">Свойства.</param>
    T WithProperty(params PropertyMember[] properties);

    /// <summary>
    /// Добавляет свойство.
    /// </summary>
    /// <param name="name">Имя свойства.</param>
    /// <typeparam name="TType">Тип свойства.</typeparam>
    T WithProperty<TType>(string name) => WithProperty(PropertyMember.CreateWithGetterOnly<TType>(name));

    /// <summary>
    /// Добавляет события.
    /// </summary>
    /// <param name="events">События.</param>
    T WithEvent(params EventMember[] events);

    /// <summary>
    /// Добавляет событие.
    /// </summary>
    /// <param name="name">Имя события.</param>
    /// <param name="access">Модификатор доступа.</param>
    /// <param name="comment">Комментарий.</param>
    /// <typeparam name="TType">Тип события.</typeparam>
    T WithEvent<TType>(string name, AccessModifier access, string? comment = default) => WithEvent(EventMember.Create<TType>(name, access));

    /// <summary>
    /// Добавляет событие.
    /// </summary>
    /// <param name="name">Имя события.</param>
    /// <typeparam name="TType">Тип события.</typeparam>
    T WithEvent<TType>(string name) => WithEvent<TType>(name, default);

    /// <summary>
    /// Добавляет методы.
    /// </summary>
    /// <param name="methods">Методы.</param>
    T WithMethod(params MethodMember[] methods);

    /// <summary>
    /// Добавляет метод.
    /// </summary>
    /// <param name="name">Имя метода.</param>
    T WithMethod(string name) => WithMethod(MethodMember.Create(name));

    /// <summary>
    /// Добавляет метод.
    /// </summary>
    /// <param name="name">Имя метода.</param>
    /// <typeparam name="TType">Тип возврата метода.</typeparam>
    T WithMethod<TType>(string name) => WithMethod(MethodMember.Create<TType>(name));

    /// <summary>
    /// Добавляет прочие вложенные сущности.
    /// </summary>
    /// <param name="entities">Сущности.</param>
    T WithOther(params IEntity[] entities);

    /// <summary>
    /// Указывает, является ли частичным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsPartial(bool value);

    /// <summary>
    /// Указывает, является ли частичным.
    /// </summary>
    T AsPartial() => AsPartial(true);

    /// <summary>
    /// Указывает, является ли небезопасным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsUnsafe(bool value);

    /// <summary>
    /// Указывает, является ли небезопасным.
    /// </summary>
    T AsUnsafe() => AsUnsafe(true);
}