using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class ProfileMaterializationLogs
{
    [LoggerMessage(EventId = 1860, Level = LogLevel.Debug, Message = "Материализация профиля пропущена, потому что профиль браузера не задан")]
    public static partial void LogProfileMaterializationSkipped(this ILogger logger);

    [LoggerMessage(EventId = 1861, Level = LogLevel.Information, Message = "Начата материализация профиля браузера для канала {Channel}, временный профиль: {IsTemporary}, путь: {ProfilePath}")]
    public static partial void LogProfileMaterializationStarted(this ILogger logger, string channel, bool isTemporary, string profilePath);

    [LoggerMessage(EventId = 1862, Level = LogLevel.Debug, Message = "Файлы предустановки автоматизации записаны в профиль {ProfilePath}, количество файлов: {Count}")]
    public static partial void LogProfileAutomationFilesWritten(this ILogger logger, string profilePath, int count);

    [LoggerMessage(EventId = 1863, Level = LogLevel.Debug, Message = "Манифест профиля записан в {ManifestPath}")]
    public static partial void LogProfileManifestWritten(this ILogger logger, string manifestPath);

    [LoggerMessage(EventId = 1865, Level = LogLevel.Information, Message = "Managed policy подготовлен, publishPath: {PublishPath}, статус: {Status}, метод: {Method}, detail: {Detail}")]
    public static partial void LogProfileManagedPolicyPublished(this ILogger logger, string publishPath, string status, string method, string? detail);

    [LoggerMessage(EventId = 1866, Level = LogLevel.Warning, Message = "Managed policy требует системной публикации, publishPath: {PublishPath}, метод: {Method}, targetPath: {TargetPath}, detail: {Detail}")]
    public static partial void LogProfileManagedPolicyPublishRequired(this ILogger logger, string publishPath, string method, string? detail, string targetPath);

    [LoggerMessage(EventId = 1867, Level = LogLevel.Information, Message = "Bridge bootstrap стратегия выбрана: installMode {InstallMode}, transportMode {TransportMode}, commandLineExtensionLoad {UseCommandLineExtensionLoad}, hasTransportUrl {HasTransportUrl}, transportUrlScheme {TransportUrlScheme}")]
    public static partial void LogProfileBootstrapStrategyResolved(this ILogger logger, string installMode, string transportMode, bool useCommandLineExtensionLoad, bool hasTransportUrl, string transportUrlScheme);

    [LoggerMessage(EventId = 1864, Level = LogLevel.Information, Message = "Материализация профиля браузера завершена, возвращаемый путь: {ReturnedPath}")]
    public static partial void LogProfileMaterializationCompleted(this ILogger logger, string returnedPath);
}