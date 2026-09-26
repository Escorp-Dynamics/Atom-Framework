using System.Net;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает настройки навигации страницы.
/// </summary>
public sealed class NavigationSettings
{
    /// <summary>
    /// Получает тип навигационного действия.
    /// </summary>
    public NavigationKind Kind { get; init; } = NavigationKind.Default;

    /// <summary>
    /// Получает дополнительные HTTP-заголовки навигации.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Получает прокси, используемый для навигации.
    /// </summary>
    public IWebProxy? Proxy { get; init; }

    /// <summary>
    /// Получает бинарное тело запроса для навигации.
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    /// Получает HTML-содержимое для навигации.
    /// </summary>
    public string? Html { get; init; }

    /// <summary>
    /// Получает код ответа для подставляемого документа (<see cref="Html"/>); по умолчанию 200.
    /// </summary>
    /// <remarks>
    /// ★ Нужен там, где подменяется страница, которую узел отдаёт с ОШИБКОЙ. Страница проверки
    /// Cloudflare всегда приходит с 403 (cf-mitigated), и её скрипт видит документ, который сам себя
    /// считает отказом. Отдав ту же страницу с 200, мы меняем её условия: замер 24.09 — на стенде,
    /// где подстановка шла с 403, Cloudflare гасил проверку формой и возвращал 200 с логином, а там,
    /// где с 200, — уходил в перезагрузку (GET), и узел отвечал 405.
    /// </remarks>
    public int HtmlStatusCode { get; init; } = 200;

    /// <summary>
    /// Отдавать <see cref="Html"/> на КАЖДУЮ навигацию по этому адресу, пока вкладка занята задачей.
    /// </summary>
    /// <remarks>
    /// Нужно там, где страница сама переходит на свой же адрес: проверка Cloudflare после
    /// прохождения делает именно это, и без постоянной подмены такой переход возвращает настоящий
    /// ответ origin вместо подготовленного документа.
    /// </remarks>
    public bool KeepHtmlForRepeatedNavigations { get; init; }

    /// <summary>
    /// Не обрывать POST по этому адресу, а пускать его на origin — при живой подмене документа.
    /// </summary>
    /// <remarks>
    /// Смотровой режим: подставленная страница остаётся за драйвером (повторные GET по-прежнему
    /// обрываются), но запрос, который страница отправляет сама, уходит по-настоящему, и её ответ
    /// виден в браузере. Ценой того, что свежий клиренс расходует браузер, а не вызывающий, —
    /// поэтому по умолчанию выключено.
    /// </remarks>
    public bool AllowRepeatedPostToOrigin { get; init; }
}