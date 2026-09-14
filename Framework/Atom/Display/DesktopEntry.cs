using System.Runtime.Versioning;

namespace Atom.Display;

/// <summary>
/// Сведения о приложении из файла <c lang="text">.desktop</c>.
/// </summary>
/// <param name="ApplicationId">Идентификатор для панели задач.</param>
/// <param name="Name">Отображаемое имя приложения.</param>
public readonly record struct DesktopEntry(string ApplicationId, string? Name);

/// <summary>
/// Поиск сведений о приложении по его исполняемому файлу.
/// </summary>
/// <remarks>
/// ★ Оболочка находит иконку окна по идентификатору приложения: он должен совпадать с именем файла
/// <c lang="text">.desktop</c>. Без этого окно на панели задач выглядит безымянным и выдаёт, что
/// приложение работает через прослойку.
///
/// Прямое совпадение имени работает не всегда: у Chrome исполняемый файл называется
/// <c lang="text">google-chrome-stable</c>, а запись — <c lang="text">google-chrome.desktop</c>. Поэтому,
/// когда прямого совпадения нет, записи просматриваются по строке запуска.
/// </remarks>
[SupportedOSPlatform("linux")]
public static class DesktopEntryResolver
{
    /// <summary>
    /// Определяет идентификатор и имя приложения по пути к исполняемому файлу.
    /// </summary>
    /// <param name="executablePath">Путь или имя исполняемого файла.</param>
    /// <returns>Сведения о приложении; при неудаче — имя файла как идентификатор.</returns>
    public static DesktopEntry Resolve(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var binary = Path.GetFileName(executablePath);
        if (binary.Length == 0)
            binary = executablePath;

        foreach (var directory in EnumerateApplicationDirectories())
        {
            var direct = Path.Combine(directory, binary + ".desktop");

            if (File.Exists(direct))
                return new DesktopEntry(binary, ReadName(direct));
        }

        return ResolveByExecLine(binary) ?? new DesktopEntry(binary, Name: null);
    }

    /// <summary>
    /// Ищет запись, запускающую этот исполняемый файл.
    /// </summary>
    private static DesktopEntry? ResolveByExecLine(string binary)
    {
        foreach (var directory in EnumerateApplicationDirectories())
        {
            IEnumerable<string> entries;

            try
            {
                entries = Directory.EnumerateFiles(directory, "*.desktop", SearchOption.TopDirectoryOnly);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (!MatchesExecutable(entry, binary))
                    continue;

                return new DesktopEntry(Path.GetFileNameWithoutExtension(entry), ReadName(entry));
            }
        }

        return null;
    }

    private static bool MatchesExecutable(string entryPath, string binary)
    {
        foreach (var line in ReadLines(entryPath))
        {
            if (!line.StartsWith("Exec=", StringComparison.Ordinal))
                continue;

            // Строка запуска несёт аргументы и подстановки вида %U — нужен только сам файл.
            var command = line.AsSpan(5).Trim();
            var space = command.IndexOf(' ');

            if (space >= 0)
                command = command[..space];

            var candidate = Path.GetFileName(command.ToString()).Trim('"');

            if (string.Equals(candidate, binary, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string? ReadName(string entryPath)
    {
        foreach (var line in ReadLines(entryPath))
        {
            // Локализованные варианты идут как Name[ru]= — нам нужен основной.
            if (line.StartsWith("Name=", StringComparison.Ordinal))
                return line[5..].Trim();
        }

        return null;
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        try
        {
            return File.ReadLines(path);
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>
    /// Каталоги записей приложений по порядку старшинства.
    /// </summary>
    /// <remarks>
    /// Пользовательские записи идут первыми: они перекрывают системные, как того требует
    /// соглашение о каталогах.
    /// </remarks>
    private static IEnumerable<string> EnumerateApplicationDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } dataHome)
            yield return Path.Combine(dataHome, "applications");
        else if (home.Length > 0)
            yield return Path.Combine(home, ".local", "share", "applications");

        var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");

        if (dataDirs is { Length: > 0 })
        {
            foreach (var directory in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
                yield return Path.Combine(directory, "applications");

            yield break;
        }

        yield return "/usr/local/share/applications";
        yield return "/usr/share/applications";
    }
}
