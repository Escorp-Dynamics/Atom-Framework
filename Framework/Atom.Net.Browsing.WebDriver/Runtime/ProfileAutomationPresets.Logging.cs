using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class ProfileAutomationPresetLogs
{
    [LoggerMessage(EventId = 1900, Level = LogLevel.Information, Message = "Начата сборка пресета автоматизации, канал: {Channel}, семейство: {Family}, путь профиля: {ProfilePath}")]
    public static partial void LogProfileAutomationPresetCreating(this ILogger logger, string channel, string family, string profilePath);

    [LoggerMessage(EventId = 1901, Level = LogLevel.Debug, Message = "Режим браузера без окна вычислен, запрошен режим без окна: {RequestedHeadless}, внешний дисплей задан: {HasDisplay}, итоговый режим браузера без окна: {UseBrowserHeadlessMode}")]
    public static partial void LogProfileAutomationHeadlessModeResolved(this ILogger logger, bool requestedHeadless, bool hasDisplay, bool useBrowserHeadlessMode);

    [LoggerMessage(EventId = 1902, Level = LogLevel.Debug, Message = "Собран Chromium-пресет автоматизации, канал: {Channel}, аргументов по умолчанию: {DefaultArgumentCount}, итоговых аргументов: {EffectiveArgumentCount}")]
    public static partial void LogProfileAutomationChromiumPresetBuilt(this ILogger logger, string channel, int defaultArgumentCount, int effectiveArgumentCount);

    [LoggerMessage(EventId = 1903, Level = LogLevel.Debug, Message = "Собран Firefox-пресет автоматизации, аргументов по умолчанию: {DefaultArgumentCount}, итоговых аргументов: {EffectiveArgumentCount}")]
    public static partial void LogProfileAutomationFirefoxPresetBuilt(this ILogger logger, int defaultArgumentCount, int effectiveArgumentCount);

    [LoggerMessage(EventId = 1904, Level = LogLevel.Debug, Message = "Для Chromium-пресета применён прокси {Scheme}://{Host}:{Port}")]
    public static partial void LogProfileAutomationChromiumProxyApplied(this ILogger logger, string scheme, string host, int port);

    [LoggerMessage(EventId = 1905, Level = LogLevel.Debug, Message = "Для Firefox-пресета применён прокси {Scheme}://{Host}:{Port}")]
    public static partial void LogProfileAutomationFirefoxProxyApplied(this ILogger logger, string scheme, string host, int port);

    [LoggerMessage(EventId = 1906, Level = LogLevel.Warning, Message = "Прокси для Firefox-пресета не удалось преобразовать в абсолютный адрес")]
    public static partial void LogProfileAutomationFirefoxProxyInvalid(this ILogger logger);
}