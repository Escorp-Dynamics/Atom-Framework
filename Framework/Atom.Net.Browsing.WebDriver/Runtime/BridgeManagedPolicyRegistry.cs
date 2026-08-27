using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using IOPath = System.IO.Path;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Ведёт общий системный managed policy файл как реестр записей нескольких запусков сразу.
/// </summary>
/// <remarks>
/// Файл в каталоге политик один на всю машину, а браузеров, поднятых драйвером, может быть
/// сколько угодно — из разных процессов. Раньше каждый запуск переписывал файл целиком своей
/// единственной записью, а на освобождении удалял его: соседний запуск в этот момент лишался
/// force-install своего расширения и разваливался. Поэтому запись здесь ведётся по ключу
/// extensionId: свою добавляем, свою же снимаем, чужие не трогаем.
/// <para>
/// Разложить записи по отдельным файлам нельзя: Chromium читает все <c>*.json</c> каталога и при
/// совпадении ключа берёт значение из файла, идущего последним по имени, — то есть запуски
/// молча гасили бы друг друга. Отсюда один файл и слияние секций.
/// </para>
/// </remarks>
internal static class BridgeManagedPolicyRegistry
{
    private const string ForcelistPolicyName = "ExtensionInstallForcelist";
    private const string SettingsPolicyName = "ExtensionSettings";

    /// <summary>Сколько ждём межпроцессную блокировку, прежде чем работать без неё.</summary>
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Служебные файлы лежат ВНЕ каталога политик: любой <c>*.json</c> рядом Chromium попытается
    /// прочитать как политику.
    /// </summary>
    private static string StateDirectory
        => IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".atom-webdriver");

    private static string LockPath => IOPath.Combine(StateDirectory, "managed-policy.lock");

    private static string OwnersPath => IOPath.Combine(StateDirectory, "managed-policy-owners.json");

    /// <summary>
    /// Добавляет запись запуска в общий файл и возвращает его новое содержимое.
    /// </summary>
    internal static string BuildPolicyWithEntry(string systemPolicyPath, string extensionId, JsonObject entryPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPolicyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(entryPolicy);

        using var registryLock = AcquireLock();

        var policy = ReadPolicy(systemPolicyPath);
        var owners = ReadOwners();

        PruneAbandonedEntries(policy, owners);
        MergeEntry(policy, entryPolicy);
        owners[extensionId] = Environment.ProcessId;

        WriteOwners(owners);
        return policy.ToJsonString();
    }

    /// <summary>
    /// Снимает запись запуска.
    /// </summary>
    /// <returns>
    /// Новое содержимое файла или <see langword="null"/>, если записей не осталось и файл
    /// нужно удалить целиком.
    /// </returns>
    internal static string? BuildPolicyWithoutEntry(string systemPolicyPath, string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPolicyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        using var registryLock = AcquireLock();

        var policy = ReadPolicy(systemPolicyPath);
        var owners = ReadOwners();

        RemoveEntry(policy, extensionId);
        _ = owners.Remove(extensionId);
        PruneAbandonedEntries(policy, owners);
        WriteOwners(owners);

        return HasEntries(policy) ? policy.ToJsonString() : null;
    }

    /// <summary>
    /// Снимает записи запусков, чьи процессы уже не живут.
    /// </summary>
    /// <remarks>
    /// Процесс может уйти, не сняв запись: падение, kill, отключение питания. Без этой чистки
    /// такая запись осталась бы навсегда и заставляла бы каждый последующий старт браузера —
    /// в том числе обычный, пользовательский — тянуть расширение с мёртвого порта.
    /// </remarks>
    private static void PruneAbandonedEntries(JsonObject policy, Dictionary<string, int> owners)
    {
        foreach (var extensionId in owners.Keys.ToArray())
        {
            if (IsProcessAlive(owners[extensionId]))
                continue;

            RemoveEntry(policy, extensionId);
            _ = owners.Remove(extensionId);
        }

        // Записи без известного владельца остались от версий без реестра: их некому снять,
        // а их порт заведомо мёртв, поэтому убираем при первой же возможности.
        foreach (var extensionId in EnumerateEntryIds(policy))
        {
            if (!owners.ContainsKey(extensionId))
                RemoveEntry(policy, extensionId);
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        if (processId <= 0)
            return false;

        if (processId == Environment.ProcessId)
            return true;

        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void MergeEntry(JsonObject policy, JsonObject entryPolicy)
    {
        if (entryPolicy[ForcelistPolicyName] is JsonArray entryForcelist)
        {
            var forcelist = EnsureArray(policy, ForcelistPolicyName);
            foreach (var value in entryForcelist.Select(static node => node?.GetValue<string>()).Where(static value => !string.IsNullOrWhiteSpace(value)))
            {
                // Именно JsonValue.Create, а не Add<T>: обобщённая перегрузка уходит в рефлексию
                // сериализатора, которая в этой сборке отключена и падает NotSupportedException.
                if (!forcelist.Any(node => string.Equals(node?.GetValue<string>(), value, StringComparison.Ordinal)))
                    forcelist.Add(JsonValue.Create(value!));
            }
        }

        if (entryPolicy[SettingsPolicyName] is not JsonObject entrySettings)
            return;

        var settings = EnsureObject(policy, SettingsPolicyName);
        foreach (var (key, value) in entrySettings)
        {
            settings[key] = value?.DeepClone();
        }
    }

    private static void RemoveEntry(JsonObject policy, string extensionId)
    {
        if (policy[ForcelistPolicyName] is JsonArray forcelist)
        {
            for (var index = forcelist.Count - 1; index >= 0; index--)
            {
                if (ExtractForcelistExtensionId(forcelist[index]?.GetValue<string>()) is { } entryId
                    && string.Equals(entryId, extensionId, StringComparison.Ordinal))
                {
                    forcelist.RemoveAt(index);
                }
            }
        }

        if (policy[SettingsPolicyName] is JsonObject settings)
            _ = settings.Remove(extensionId);
    }

    private static HashSet<string> EnumerateEntryIds(JsonObject policy)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);

        if (policy[ForcelistPolicyName] is JsonArray forcelist)
        {
            foreach (var node in forcelist)
            {
                if (ExtractForcelistExtensionId(node?.GetValue<string>()) is { } entryId)
                    _ = ids.Add(entryId);
            }
        }

        if (policy[SettingsPolicyName] is JsonObject settings)
        {
            foreach (var (key, _) in settings)
                _ = ids.Add(key);
        }

        return ids;
    }

    private static string? ExtractForcelistExtensionId(string? forcelistEntry)
    {
        if (string.IsNullOrWhiteSpace(forcelistEntry))
            return null;

        var separatorIndex = forcelistEntry.IndexOf(';', StringComparison.Ordinal);
        var id = separatorIndex >= 0 ? forcelistEntry[..separatorIndex] : forcelistEntry;
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private static bool HasEntries(JsonObject policy)
        => (policy[ForcelistPolicyName] is JsonArray { Count: > 0 })
            || (policy[SettingsPolicyName] is JsonObject { Count: > 0 });

    private static JsonArray EnsureArray(JsonObject policy, string name)
    {
        if (policy[name] is JsonArray existing)
            return existing;

        var created = new JsonArray();
        policy[name] = created;
        return created;
    }

    private static JsonObject EnsureObject(JsonObject policy, string name)
    {
        if (policy[name] is JsonObject existing)
            return existing;

        var created = new JsonObject();
        policy[name] = created;
        return created;
    }

    private static JsonObject ReadPolicy(string systemPolicyPath)
    {
        try
        {
            if (!File.Exists(systemPolicyPath))
                return [];

            return JsonNode.Parse(File.ReadAllText(systemPolicyPath)) as JsonObject ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // Нечитаемый или повреждённый файл заменяем своим содержимым: держаться за него
            // значило бы оставить систему без рабочей политики вообще.
            return [];
        }
    }

    private static Dictionary<string, int> ReadOwners()
    {
        try
        {
            if (!File.Exists(OwnersPath))
                return new Dictionary<string, int>(StringComparer.Ordinal);

            if (JsonNode.Parse(File.ReadAllText(OwnersPath)) is not JsonObject owners)
                return new Dictionary<string, int>(StringComparer.Ordinal);

            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (key, value) in owners)
            {
                if (value?.GetValue<int>() is { } processId)
                    result[key] = processId;
            }

            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    private static void WriteOwners(Dictionary<string, int> owners)
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);

            var payload = new JsonObject();
            foreach (var (key, value) in owners)
                payload[key] = JsonValue.Create(value);

            File.WriteAllText(OwnersPath, payload.ToJsonString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Реестр владельцев — вспомогательный: без него теряется только чистка брошенных
            // записей, а публикация политики обязана состояться.
        }
    }

    /// <summary>
    /// Межпроцессная блокировка на время чтения-изменения-записи общего файла.
    /// </summary>
    private static IDisposable AcquireLock()
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new NoOpLock();
        }

        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            try
            {
                return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(LockRetryDelay);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Взять блокировку не удалось — работаем без неё: потерять запись хуже, чем
                // рискнуть гонкой, которая и так закрыта повторной чисткой по владельцам.
                return new NoOpLock();
            }
        }
    }

    private sealed class NoOpLock : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
