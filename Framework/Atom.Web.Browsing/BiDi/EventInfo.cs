using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Класс, содержащий информацию, используемую при вызове обработчика события.
/// </summary>
/// <typeparam name="T">Тип данных, описывающих событие.</typeparam>
/// <remarks>
/// Инициализирует новый экземпляр класса <see cref="EventInfo{T}"/>.
/// </remarks>
/// <param name="eventData">Данные для вызова события.</param>
/// <param name="additionalData">Дополнительные данные, возвращённые для события.</param>
public class EventInfo<T>(T eventData, ReceivedDataDictionary additionalData)
{
    /// <summary>
    /// Данные для вызова события.
    /// </summary>
    public T EventData { get; } = eventData;

    /// <summary>
    /// Дополнительные данные, возвращённые для события.
    /// </summary>
    public ReceivedDataDictionary AdditionalData { get; } = additionalData;

    /// <summary>
    /// Создаёт объект, производный от <see cref="BiDiEventArgs"/>, который содержит информацию о событии.
    /// </summary>
    /// <typeparam name="TEventArgs">
    /// Тип, производный от <see cref="BiDiEventArgs"/>. Тип должен быть таким же, как тип этого класса или должен иметь публичный конструктор, принимающий аргумент типа T.
    /// </typeparam>
    /// <returns>Объект, содержащий информацию о событии.</returns>
    /// <exception cref="BiDiException">
    /// Выбрасывается, если:
    /// <list type="bulleted">
    ///   <item>
    ///     <description>
    ///       Тип TEventArgs не совпадает с типом T
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Тип TEventArgs не имеет публичного конструктора, принимающего аргумент типа T
    ///     </description>
    ///   </item>
    /// </list>
    /// </exception>
    public TEventArgs ToEventArgs<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEventArgs>() where TEventArgs : BiDiEventArgs
    {
        TEventArgs? result = null;

        if (typeof(T) == typeof(TEventArgs))
        {
            result = EventData as TEventArgs;
        }
        else
        {
            var constructorInfo = typeof(TEventArgs).GetConstructor([typeof(T)]);

            if (constructorInfo is not null)
            {
                var ctorArgExpression = Expression.Parameter(typeof(T), "eventData");
                var ctorExpression = Expression.New(constructorInfo, ctorArgExpression);
                var lambdaExpression = Expression.Lambda<Func<T, TEventArgs>>(ctorExpression, ctorArgExpression);
                var invoker = lambdaExpression.Compile();
                result = invoker(EventData);
            }
        }

        if (result is null) throw new BiDiException($"Не удалось создать EventArgs типа {typeof(TEventArgs)} из информации о событии с типом {typeof(T)}");

        result.AdditionalData = AdditionalData;
        return result!;
    }
}