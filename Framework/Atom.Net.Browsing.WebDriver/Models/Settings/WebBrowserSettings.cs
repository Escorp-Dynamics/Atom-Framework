using System.Drawing;
using System.Net;
using Atom.Hardware.Display;
using Atom.Hardware.Input;
using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает настройки запуска браузера.
/// </summary>
public sealed class WebBrowserSettings
{
    /// <summary>
    /// Получает профиль браузера.
    /// </summary>
    public WebBrowserProfile? Profile { get; init; }

    /// <summary>
    /// Получает прокси браузера.
    /// </summary>
    public IWebProxy? Proxy { get; init; }

    /// <summary>
    /// Получает журнал для браузера.
    /// </summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Получает или задаёт виртуальный дисплей для запуска браузера.
    /// Если значение не задано, на Linux рантайм может создать display автоматически.
    /// </summary>
    public VirtualDisplay? Display { get; set; }

    /// <summary>
    /// Получает или задаёт виртуальную мышь браузера.
    /// Используется как источник по умолчанию для окон и вкладок.
    /// </summary>
    public VirtualMouse? Mouse { get; set; }

    /// <summary>
    /// Получает или задаёт виртуальную клавиатуру браузера.
    /// Используется как источник по умолчанию для окон и вкладок.
    /// </summary>
    public VirtualKeyboard? Keyboard { get; set; }

    /// <summary>
    /// Получает признак запуска в headless-режиме.
    /// </summary>
    public bool UseHeadlessMode { get; init; }

    /// <summary>
    /// Получает признак запуска в режиме инкогнито.
    /// </summary>
    public bool UseIncognitoMode { get; init; }

    /// <summary>
    /// Получает признак opt-in rootless bootstrap для Chromium.
    /// На Linux для stable branded Chromium (Chrome/Edge/Brave/Opera/Vivaldi) переключает extension bootstrap с system managed policy
    /// на profile-seeded режим без публикации в системный policy-каталог.
    /// </summary>
    public bool UseRootlessChromiumBootstrap { get; init; }

    /// <summary>
    /// Получает позицию окна браузера.
    /// </summary>
    public Point Position { get; init; } = Point.Empty;

    /// <summary>
    /// Получает размер окна браузера.
    /// </summary>
    public Size Size { get; init; } = new Size(1280, 768);

    /// <summary>
    /// Получает аргументы запуска браузера.
    /// </summary>
    public IEnumerable<string>? Args { get; init; }

    /// <summary>
    /// Получает устройство для эмуляции.
    /// </summary>
    public Device? Device { get; init; }
}