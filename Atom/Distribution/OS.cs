namespace Atom.Distribution;

/// <summary>
/// Представляет методы работы с базовыми функциями операционной системы.
/// </summary>
public static class OS
{
    /// <summary>
    /// Тип дистрибутива.
    /// </summary>
    public static Distributive Distribution { get; }

    /// <summary>
    /// Менеджер пакетов.
    /// </summary>
    public static PackageManager PM { get; }

    /// <summary>
    /// Терминал операционной системы.
    /// </summary>
    public static Terminal Terminal { get; }

    static OS()
    {
        Distribution = GetDistribution();
        PM = new PackageManager(Distribution);
        Terminal = new Terminal(Distribution);
    }

    private static Distributive GetDistribution()
    {
        if (!File.Exists("/etc/os-release")) return default;

        var lines = File.ReadAllLines("/etc/os-release");

        foreach (var line in lines)
            if (line.StartsWith("ID=") && Enum.TryParse<Distributive>(line[3..].Trim('"'), true, out var kind))
                return kind;

        return default;
    }
}