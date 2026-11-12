using System.Drawing;
using Atom.Hardware.Input;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Канал команд вкладки, который умеет вводить события прямо в страницу и подменять документ
/// навигации без участия ОС и локального прокси (протокол отладки).
/// </summary>
/// <remarks>
/// Координаты задаются в CSS-пикселях области просмотра документа, а не в экранных: у такого
/// транспорта нет окна на экране, и события доставляются движком напрямую как доверенные.
/// </remarks>
internal interface IPageDirectControlClient
{
    ValueTask MoveMouseAsync(PointF viewportPoint, CancellationToken cancellationToken);

    ValueTask ClickAsync(PointF viewportPoint, VirtualMouseButton button, int clickCount, CancellationToken cancellationToken);

    ValueTask TapAsync(PointF viewportPoint, CancellationToken cancellationToken);

    ValueTask TypeTextAsync(string text, CancellationToken cancellationToken);

    ValueTask PressKeyAsync(ConsoleKey key, ConsoleModifiers modifiers, CancellationToken cancellationToken);

    /// <summary>
    /// Переходит на <paramref name="url"/>, отдавая вместо ответа сервера документ <paramref name="html"/>.
    /// </summary>
    ValueTask NavigateWithDocumentAsync(Uri url, string html, CancellationToken cancellationToken);
}
