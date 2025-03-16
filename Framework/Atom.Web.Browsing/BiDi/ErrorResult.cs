using System.Diagnostics.CodeAnalysis;
using Atom.Web.Browsing.BiDi.Protocol;

namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет объект, содержащий ответ с ошибкой на команду.
/// </summary>
public class ErrorResult : CommandResult
{
    /// <summary>
    /// Определяет, являются ли данные ответа ошибкой.
    /// </summary>
    public override bool IsError => true;

    /// <summary>
    /// Тип возникшей ошибки.
    /// </summary>
    public string ErrorType { get; }

    /// <summary>
    /// Сообщение об ошибке.
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Трассировка стека, связанная с этой ошибкой.
    /// </summary>
    public string? StackTrace { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ErrorResult"/>.
    /// </summary>
    /// <param name="response">Ответ с ошибкой, содержащий данные об ошибке.</param>
    public ErrorResult([NotNull] ErrorResponseMessage response)
    {
        ErrorType = response.ErrorType;
        ErrorMessage = response.ErrorMessage;
        StackTrace = response.StackTrace;
        AdditionalData = response.AdditionalData;
    }
}