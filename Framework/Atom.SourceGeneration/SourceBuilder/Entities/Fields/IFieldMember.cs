namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя полей.
/// </summary>
/// <typeparam name="T">Тип строителя полей.</typeparam>
public interface IFieldMember<out T> : IMember<T> where T : IEntity
{
    /// <summary>
    /// Значение поля.
    /// </summary>
    string? Value { get; }

    /// <summary>
    /// Является ли только для чтения.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Является ли поле волатильным.
    /// </summary>
    bool IsVolatile { get; }

    /// <summary>
    /// Является ли поле константным.
    /// </summary>
    bool IsConstant { get; }

    /// <summary>
    /// Является ли поле ссылочным.
    /// </summary>
    bool IsRef { get; }

    /// <summary>
    /// Добавляет значение поля.
    /// </summary>
    /// <param name="value">Выражение значения.</param>
    T WithValue(string? value);

    /// <summary>
    /// Добавляет значение поля.
    /// </summary>
    /// <param name="value">Значение поля.</param>
    /// <typeparam name="TValue">Тип значения.</typeparam>
    T WithValue<TValue>(TValue value) => WithValue(value is string ? $"\"{value}\"" : value?.ToString());

    /// <summary>
    /// Определяет, что поле должно быть доступно только для чтения.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsReadOnly(bool value);

    /// <summary>
    /// Определяет, что поле должно быть доступно только для чтения.
    /// </summary>
    T AsReadOnly() => AsReadOnly(value: true);

    /// <summary>
    /// Определяет, что поле должно быть волатильным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsVolatile(bool value);

    /// <summary>
    /// Определяет, что поле должно быть волатильным.
    /// </summary>
    T AsVolatile() => AsVolatile(value: true);

    /// <summary>
    /// Определяет, что поле должно быть константным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsConstant(bool value);

    /// <summary>
    /// Определяет, что поле должно быть константным.
    /// </summary>
    T AsConstant() => AsConstant(value: true);

    /// <summary>
    /// Определяет, что поле должно быть ссылочным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsRef(bool value);

    /// <summary>
    /// Определяет, что поле должно быть ссылочным.
    /// </summary>
    T AsRef() => AsRef(value: true);
}