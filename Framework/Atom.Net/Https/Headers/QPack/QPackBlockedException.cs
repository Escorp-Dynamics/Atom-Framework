using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Headers.QPack;

/// <summary>
/// Исключение «поток заблокирован»: RIC блока больше, чем KnownReceivedCount динамической таблицы.
/// </summary>
public sealed class QPackBlockedException : Exception
{
    /// <summary>
    /// 
    /// </summary>
    public int RequiredInsertCount { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="QPackBlockedException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public QPackBlockedException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="QPackBlockedException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public QPackBlockedException(string message) : base(message) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="QPackBlockedException"/>.
    /// </summary>
    public QPackBlockedException() : base() { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="QPackBlockedException"/>.
    /// </summary>
    /// <param name="requiredInsertCount"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public QPackBlockedException(int requiredInsertCount) : this("QPACK: блок заголовков заблокирован (RequiredInsertCount не предоставлен)") => RequiredInsertCount = requiredInsertCount;
}