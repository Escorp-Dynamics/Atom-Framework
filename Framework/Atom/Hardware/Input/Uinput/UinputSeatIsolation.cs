using System.Diagnostics;
using System.Runtime.Versioning;

namespace Atom.Hardware.Input.Uinput;

/// <summary>
/// Изоляция виртуальных устройств по seat.
/// </summary>
/// <remarks>
/// ★ Зачем. uinput-устройство появляется в общей системе и без метки seat попадает на
/// <c lang="text">seat0</c> — то есть становится видно ВСЕМ композиторам сразу. При нескольких
/// параллельных браузерах касание одного слота ушло бы во все окна разом.
///
/// libinput фильтрует устройства по udev-свойству <c lang="text">ID_SEAT</c>, а задать его напрямую из
/// процесса нельзя: свойства проставляет udev. Поэтому имя seat кодируется в ИМЕНИ устройства, а
/// правило извлекает его подстановкой — так число слотов ничем не ограничено.
///
/// Правило ставится автоматически при первом обращении: пользователь драйвера не должен знать ни
/// про udev, ни про seat.
/// </remarks>
[SupportedOSPlatform("linux")]
public static class UinputSeatIsolation
{
    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    private static bool isInstalled;

    /// <summary>Метка seat в имени устройства для указанного слота.</summary>
    public static string BuildSeatTag(int slot) => "atom-seat-" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Добавляет метку seat к имени устройства.
    /// </summary>
    /// <remarks>
    /// Хвост видят только ядро и udev: композитор имя наружу не публикует, поэтому на отпечаток
    /// страницы метка не влияет.
    /// </remarks>
    public static string ApplySeatTag(string deviceName, int slot) => deviceName + " " + BuildSeatTag(slot);

    /// <summary>
    /// Ставит udev-правило изоляции, если его ещё нет.
    /// </summary>
    /// <returns><see langword="true"/>, если правило на месте и изоляция доступна.</returns>
    /// <remarks>
    /// Без прав root правило не поставить — тогда возвращается <see langword="false"/>, и
    /// вызывающий работает без изоляции (один браузер на машину). Ронять запуск из-за этого
    /// нельзя: на машине разработчика одиночный сценарий — основной.
    /// </remarks>
    public static async ValueTask<bool> EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref isInstalled))
            return true;

        await InstallGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref isInstalled))
                return true;

            if (File.Exists(RulePath))
            {
                var existing = await File.ReadAllTextAsync(RulePath, cancellationToken).ConfigureAwait(false);
                if (existing.Contains(RuleMarker, StringComparison.Ordinal))
                {
                    Volatile.Write(ref isInstalled, true);
                    return true;
                }
            }

            if (!await TryWriteRuleAsync(cancellationToken).ConfigureAwait(false))
                return false;

            await ReloadUdevRulesAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref isInstalled, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Нет прав — работаем без изоляции: это ограничивает параллельность, но не ломает запуск.
            return false;
        }
        finally
        {
            _ = InstallGate.Release();
        }
    }

    private static async ValueTask<bool> TryWriteRuleAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(RulesDirectory);
            await File.WriteAllTextAsync(RulePath, RuleContent, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async ValueTask ReloadUdevRulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/udevadm")
            {
                ArgumentList = { "control", "--reload-rules" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process is not null)
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // udevadm может отсутствовать в урезанном образе: правило подхватится при следующей
            // перезагрузке правил системой.
        }
    }

    private const string RulesDirectory = "/etc/udev/rules.d";
    private const string RulePath = RulesDirectory + "/99-atom-input-seats.rules";

    /// <summary>Метка версии: по ней распознаётся уже установленное правило.</summary>
    private const string RuleMarker = "atom-seat-[0-9]*";

    private const string RuleContent = """
        # Изоляция виртуальных устройств Atom по seat. Ставится драйвером автоматически.
        #
        # Без ID_SEAT устройство попадает на seat0 и видно ВСЕМ композиторам сразу: при нескольких
        # параллельных браузерах касание одного слота ушло бы во все окна разом. libinput фильтрует
        # устройства по этому свойству, а задать его из процесса нельзя — свойства проставляет udev.
        #
        # Имя seat кодируется в имени устройства ("<модель> atom-seat-<N>") и извлекается
        # подстановкой, поэтому число слотов ничем не ограничено.
        ACTION=="add|change", SUBSYSTEM=="input", ATTRS{name}=="*atom-seat-*", PROGRAM="/bin/sh -c 'echo \"$attr{name}\" | grep -o \"atom-seat-[0-9]*\"'", ENV{ID_SEAT}="%c"

        """;
}
