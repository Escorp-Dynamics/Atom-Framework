namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя событий.
/// </summary>
/// <typeparam name="T">Тип строителя событий.</typeparam>
public interface IEventMember<out T> : IMember<T> where T : IEntity
{
    /// <summary>
    /// Подписчик.
    /// </summary>
    EventAddMember? Adder { get; }

    /// <summary>
    /// Отписчик.
    /// </summary>
    EventRemoveMember? Remover { get; }

    /// <summary>
    /// Является ли событие только для чтения.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Является ли событие небезопасным.
    /// </summary>
    bool IsUnsafe { get; }

    /// <summary>
    /// Является ли событие абстрактным.
    /// </summary>
    bool IsAbstract { get; }

    /// <summary>
    /// Является ли событие виртуальным.
    /// </summary>
    bool IsVirtual { get; }

    /// <summary>
    /// Является ли событие перезаписанным.
    /// </summary>
    bool IsOverride { get; }

    /// <summary>
    /// Является ли событие переопределённым.
    /// </summary>
    bool IsNew { get; }

    /// <summary>
    /// Добавляет подписчик.
    /// </summary>
    /// <param name="adder">Подписчик.</param>
    T WithAdder(EventAddMember adder);

    /// <summary>
    /// Добавляет подписчик.
    /// </summary>
    /// <param name="body">Тело подписчика.</param>
    /// <param name="isReadOnly">Указывает, являет ли подписчик только для чтения.</param>
    /// <param name="attributes">Атрибуты подписчика.</param>
    T WithAdder(string body, bool isReadOnly, params IEnumerable<string> attributes) => WithAdder(EventAddMember.Create()
        .WithAttribute(attributes)
        .WithCode(body)
        .AsReadOnly(isReadOnly)
    );

    /// <summary>
    /// Добавляет подписчик.
    /// </summary>
    /// <param name="body">Тело подписчика.</param>
    /// <param name="attributes">Атрибуты подписчика.</param>
    T WithAdder(string body, params IEnumerable<string> attributes) => WithAdder(body, default, attributes);

    /// <summary>
    /// Добавляет подписчик.
    /// </summary>
    T WithAdder() => WithAdder(string.Empty);

    /// <summary>
    /// Добавляет отписчик.
    /// </summary>
    /// <param name="remover">отписчик.</param>
    T WithRemover(EventRemoveMember remover);

    /// <summary>
    /// Добавляет отписчик.
    /// </summary>
    /// <param name="body">Тело отписчика.</param>
    /// <param name="comment">Комментарий.</param>
    /// <param name="attributes">Атрибуты отписчика.</param>
    T WithRemover(string body, string comment, params string[] attributes) => WithRemover(EventRemoveMember.Create()
        .WithAttribute(attributes)
        .WithCode(body)
        .WithComment(comment)
    );

    /// <summary>
    /// Добавляет отписчик.
    /// </summary>
    /// <param name="body">Тело отписчика.</param>
    T WithRemover(string body) => WithRemover(body, string.Empty);

    /// <summary>
    /// Добавляет отписчик.
    /// </summary>
    T WithRemover() => WithRemover(string.Empty);

    /// <summary>
    /// Определяет, что событие должно быть доступно только для чтения.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsReadOnly(bool value);

    /// <summary>
    /// Определяет, что событие должно быть доступно только для чтения.
    /// </summary>
    T AsReadOnly() => AsReadOnly(true);

    /// <summary>
    /// Определяет, что событие должно быть небезопасным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsUnsafe(bool value);

    /// <summary>
    /// Определяет, что событие должно быть небезопасным.
    /// </summary>
    T AsUnsafe() => AsUnsafe(true);

    /// <summary>
    /// Определяет, что событие должно быть абстрактным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsAbstract(bool value);

    /// <summary>
    /// Определяет, что событие должно быть абстрактным.
    /// </summary>
    T AsAbstract() => AsAbstract(true);

    /// <summary>
    /// Определяет, что событие должно быть виртуальным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsVirtual(bool value);

    /// <summary>
    /// Определяет, что событие должно быть виртуальным.
    /// </summary>
    T AsVirtual() => AsVirtual(true);

    /// <summary>
    /// Определяет, что событие должно быть перезаписанным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsOverride(bool value);

    /// <summary>
    /// Определяет, что событие должно быть перезаписанным.
    /// </summary>
    T AsOverride() => AsOverride(true);

    /// <summary>
    /// Определяет, что событие должно быть переназначенным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsNew(bool value);

    /// <summary>
    /// Определяет, что событие должно быть переназначенным.
    /// </summary>
    T AsNew() => AsNew(true);
}