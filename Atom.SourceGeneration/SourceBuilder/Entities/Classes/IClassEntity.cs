namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя класса.
/// </summary>
/// <typeparam name="T">Тип строителя.</typeparam>
public interface IClassEntity<out T> : IInterfaceEntity<T> where T : IEntity
{
    /// <summary>
    /// Поля.
    /// </summary>
    IEnumerable<FieldMember> Fields { get; }

    /// <summary>
    /// Является ли статическим.
    /// </summary>
    bool IsStatic { get; }

    /// <summary>
    /// Добавляет поля.
    /// </summary>
    /// <param name="fields">Поля.</param>
    T AddFields(params FieldMember[] fields);

    /// <summary>
    /// Добавляет поле.
    /// </summary>
    /// <param name="field">Поле.</param>
    T AddField(FieldMember field) => AddFields(field);

    /// <summary>
    /// Добавляет поле.
    /// </summary>
    /// <param name="name">Имя поля.</param>
    /// <param name="value">Значение поля.</param>
    /// <typeparam name="TType">Тип поля.</typeparam>
    T AddField<TType>(string name, string? value) => AddField(FieldMember.Create<TType>(name, value));

    /// <summary>
    /// Добавляет поле.
    /// </summary>
    /// <param name="name">Имя поля.</param>
    /// <param name="value">Значение поля.</param>
    /// <typeparam name="TType">Тип поля.</typeparam>
    T AddField<TType>(string name, TType value) => AddField(FieldMember.Create(name, value));

    /// <summary>
    /// Добавляет поле.
    /// </summary>
    /// <param name="name">Имя поля.</param>
    /// <typeparam name="TType">Тип поля.</typeparam>
    T AddField<TType>(string name) => AddField(FieldMember.Create<TType>(name));

    /// <summary>
    /// Указывает, является ли статическим.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsStatic(bool value);

    /// <summary>
    /// Указывает, является ли статическим.
    /// </summary>
    T AsStatic() => AsStatic(true);
}