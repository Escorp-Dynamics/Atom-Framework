using System.Diagnostics;

namespace Atom.Tests;

public static class ProcessCommandHelpers
{
    public static bool IsCommandAvailable(string fileName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                Arguments = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            process?.WaitForExit(3000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string> RunProcessAsync(string fileName, string arguments = "", bool includeStandardError = false)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return includeStandardError && !string.IsNullOrWhiteSpace(error)
            ? output + Environment.NewLine + error
            : output;
    }

    public static Task<string> RunPwDumpAsync() => RunProcessAsync("pw-dump");
}