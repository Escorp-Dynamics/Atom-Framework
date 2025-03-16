namespace Atom.Web.Browsing.BiDi.JsonConverters;

/// <summary>
/// Позволяет отметить перечисляемый тип как десериализуемый из JSON с указанием значения,
/// которое будет использоваться, если сериализованное значение не соответствует ни одному из значений перечисления.
/// Этот атрибут следует использовать временно, только для обхода ошибок в реализации протокола на стороне сервера,
/// допущенных поставщиком.
/// </summary>
/// <typeparam name="T">Перечисляемый тип, к которому применяется значение по умолчанию для десериализации.</typeparam>
/// <remarks>
/// Инициализирует новый экземпляр класса <see cref="JsonEnumUnmatchedValueAttribute{T}"/>.
/// </remarks>
/// <param name="unmatchedValue">
/// Значение перечисления, которое будет возвращено, если сериализованное значение JSON
/// не соответствует ни одному из вариантов перечисления.
/// </param>
[AttributeUsage(AttributeTargets.Enum, Inherited = false, AllowMultiple = false)]
public sealed class JsonEnumUnmatchedValueAttribute<T>(T unmatchedValue) : Attribute where T : struct, Enum
{
    /// <summary>
    /// Получает значение перечисления, которое будет возвращено, если сериализованное значение JSON
    /// не соответствует ни одному из вариантов перечисления.
    /// </summary>
    public T UnmatchedValue { get; } = unmatchedValue;
}