using System.Drawing;

namespace Atom.Display.Wayland;

/// <summary>
/// Параметры композитора.
/// </summary>
public sealed record WaylandCompositorSettings
{
    /// <summary>
    /// Разрешение единственного выхода.
    /// </summary>
    /// <remarks>
    /// Оно же становится размером окна браузера: в безголовом режиме окно разворачивается на весь
    /// выход. Отсюда и требование к матрице тачскрина — совпадать с этим размером.
    /// </remarks>
    public Size Resolution { get; init; } = new(1920, 1080);

    /// <summary>Частота обновления в милligерцах, как её объявляет протокол.</summary>
    public int RefreshMilliHertz { get; init; } = 60_000;

    /// <summary>Масштаб выхода.</summary>
    public int Scale { get; init; } = 1;

    /// <summary>Физический размер выхода в миллиметрах.</summary>
    /// <remarks>
    /// Из него браузер вычисляет плотность точек: нулевой размер выдал бы устройство без экрана.
    /// По умолчанию соответствует примерно 96 точкам на дюйм при заданном разрешении.
    /// </remarks>
    public Size PhysicalSizeMillimeters { get; init; } = new(508, 286);

    /// <summary>Имя выхода, видимое клиенту.</summary>
    public string OutputName { get; init; } = "WL-1";

    /// <summary>Производитель выхода.</summary>
    public string OutputMake { get; init; } = "Atom";

    /// <summary>Модель выхода.</summary>
    public string OutputModel { get; init; } = "Virtual Display";

    /// <summary>
    /// Объявлять ли сенсорный ввод.
    /// </summary>
    /// <remarks>
    /// ★ Должно соответствовать личности, под которую маскируется вкладка: заявленный сенсор
    /// делает <c lang="text">navigator.maxTouchPoints</c> ненулевым и включает <c lang="text">ontouchstart</c>.
    /// Настольный профиль с сенсором и мобильный без него одинаково заметны.
    /// </remarks>
    public bool HasTouch { get; init; }

    /// <summary>
    /// Показывать кадры браузера окном в сессии разработчика.
    /// </summary>
    /// <remarks>
    /// Только для отладки. Вывод стоит отображения памяти клиента и покадрового копирования,
    /// поэтому в бою выключен: без него буферы даже не отображаются в память.
    /// </remarks>
    public bool EnablePresentation { get; init; }

    /// <summary>
    /// Заголовок окна до того, как браузер представится сам.
    /// </summary>
    public string? WindowTitle { get; init; }

    /// <summary>
    /// Идентификатор приложения для панели задач.
    /// </summary>
    /// <remarks>
    /// По нему оболочка находит файл <c lang="text">.desktop</c> и берёт иконку. Задаётся заранее,
    /// чтобы окно не появлялось на панели безымянным; дальше его перезадаёт сам браузер.
    /// </remarks>
    public string? ApplicationId { get; init; }
}
