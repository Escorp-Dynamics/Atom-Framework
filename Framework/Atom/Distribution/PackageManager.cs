namespace Atom.Distribution;

/// <summary>
/// Представляет методы работы с менеджером пакетов дистрибутива.
/// </summary>
public class PackageManager
{
    private readonly Distributive distribution;

    internal PackageManager(Distributive distribution) => this.distribution = distribution;

    /// <summary>
    /// Определяет, установлен ли пакет в системе.
    /// </summary>
    /// <param name="packageName">Имя пакета.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public ValueTask<bool> CheckExistsAsync(string packageName, CancellationToken cancellationToken) => distribution switch
    {
        Distributive.Debian or Distributive.Ubuntu => OS.Terminal.RunAndWaitAsync($"dpkg -s {packageName} 2>/dev/null | grep -q 'Status: install ok installed'", cancellationToken),
        Distributive.Arch or Distributive.Manjaro => OS.Terminal.RunAndWaitAsync($"pacman -Qi {packageName} >/dev/null 2>&1", cancellationToken),
        Distributive.Fedora => OS.Terminal.RunAndWaitAsync($"rpm -q {packageName} >/dev/null 2>&1", cancellationToken),
        _ => throw new UnsupportedDistributiveException(),
    };

    /// <summary>
    /// Определяет, установлен ли пакет в системе.
    /// </summary>
    /// <param name="packageName">Имя пакета.</param>
    public ValueTask<bool> CheckExistsAsync(string packageName) => CheckExistsAsync(packageName, CancellationToken.None);

    /// <summary>
    /// Устанавливает пакет в систему.
    /// </summary>
    /// <param name="packageName">Имя пакета.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public ValueTask<bool> InstallAsync(string packageName, CancellationToken cancellationToken) => distribution switch
    {
        Distributive.Debian or Distributive.Ubuntu => OS.Terminal.RunAsAdministratorAndWaitAsync($"apt-get install -y {packageName}", cancellationToken),
        Distributive.Arch or Distributive.Manjaro => OS.Terminal.RunAsAdministratorAndWaitAsync($"pacman -Syu --noconfirm {packageName}", cancellationToken),
        Distributive.Fedora => OS.Terminal.RunAsAdministratorAndWaitAsync($"dnf install -y {packageName}", cancellationToken),
        _ => throw new UnsupportedDistributiveException(),
    };

    /// <summary>
    /// Устанавливает пакет в систему.
    /// </summary>
    /// <param name="packageName">Имя пакета.</param>
    public ValueTask<bool> InstallAsync(string packageName) => InstallAsync(packageName, CancellationToken.None);
}