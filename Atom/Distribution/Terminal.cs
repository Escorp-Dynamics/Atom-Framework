using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Atom.Buffers;

namespace Atom.Distribution;

/// <summary>
/// Представляет терминал операционной системы.
/// </summary>
public partial class Terminal
{
    private unsafe struct Command(char* name, char** args)
    {
        public char* Name = name;
        public char** Args = args;

        public readonly void Deconstruct(out char* name, out char** args)
        {
            name = Name;
            args = Args;
        }
    }

    private unsafe struct PipeInfo(int* input, int lastPid)
    {
        public int* Input = input;
        public int LastPID = lastPid;

        public readonly void Deconstruct(out int* input, out int lastPid)
        {
            input = Input;
            lastPid = LastPID;
        }
    }

    private readonly Distributive distribution;

    /// <summary>
    /// Пароль администратора.
    /// </summary>
    public string? RootPassword { get; set; }

    /// <summary>
    /// Вывод последнего запуска.
    /// </summary>
    public string LastOutput { get; set; } = string.Empty;

    /// <summary>
    /// Ошибка последнего запуска.
    /// </summary>
    public string LastError { get; set; } = string.Empty;

    internal Terminal(Distributive distribution) => this.distribution = distribution;

    private async ValueTask<bool> RunAsync(nint commands, int length, CancellationToken cancellationToken)
    {
        nint input;
        int pipe1, pipe2, lastPid;

        unsafe
        {
            (var inputPipe, lastPid) = ProcessPipe((Command*)commands, length);
            input = (nint)inputPipe;
            pipe1 = inputPipe[0];
            pipe2 = inputPipe[1];
        }
        
        LastOutput = await ReadPipeAsync(pipe1).ConfigureAwait(false);
        LastError = await ReadPipeAsync(pipe2).ConfigureAwait(false);

        unsafe
        {
            ClosePipe((int*)input);
        }
        
        return await WaitPidAsync(lastPid, cancellationToken).ConfigureAwait(false) == 0;
    }

    private unsafe nint ParseCommand(string command, out int length)
    {
        if (!string.IsNullOrEmpty(RootPassword)) command = $"echo \"{RootPassword}\" | sudo -S ";

        var parts = command.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        length = parts.Length;

        var result = stackalloc Command[length];

        for (var i = 0; i < length; ++i)
        {
            var tokens = parts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).AsSpan();
            var a = tokens[1..];

            fixed (char* name = tokens[0])
            fixed (char* firstArg = a[0])
                result[i] = new(name, &firstArg);
        }
        
        return (nint)result;
    }

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public ValueTask<bool> RunAsync([NotNull] string command, CancellationToken cancellationToken)
        => RunAsync(ParseCommand(command, out var length), length, cancellationToken);

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    public ValueTask<bool> RunAsync(string command) => RunAsync(command, CancellationToken.None);

    private unsafe static int* CreatePipe()
    {
        var pipe = stackalloc int[2];
        Pipe(pipe);
        return pipe;
    }

    private unsafe static void ConfigureChildProcess(int index, int totalCommands, int* inputPipe, int* outputPipe, int* errorPipe)
    {
        if (index > 0)
        {
            Dup2(inputPipe[0], 0);
            Close(inputPipe[0]);
            Close(inputPipe[1]);
        }

        if (index < totalCommands - 1)
        {
            Dup2(outputPipe[1], 1);
            Close(outputPipe[0]);
            Close(outputPipe[1]);
        }

        Dup2(errorPipe[1], 2);
        Close(errorPipe[0]);
        Close(errorPipe[1]);
    }

    private unsafe static void ClosePipe(int* pipe)
    {
        Close(pipe[0]);
        Close(pipe[1]);
    }

    private static unsafe PipeInfo ProcessPipe(Command* commands, int length)
    {
        var lastPid = -1;
        var inputPipe = CreatePipe();

        for (var i = 0; i < length; ++i)
        {
            var (command, args) = commands[i];

            var outputPipe = CreatePipe();
            var errorPipe = CreatePipe();

            var pid = Fork();

            if (pid is -1) throw new NativeException("Не удалось выполнить разветвление процесса");

            if (pid is 0)
            {
                ConfigureChildProcess(i, length, inputPipe, outputPipe, errorPipe);
                ExecVP(command, args);
                Environment.Exit(1);
            }

            ClosePipe(inputPipe);
            inputPipe = outputPipe;
            lastPid = pid;
        }

        return new(inputPipe, lastPid);
    }

    private static async ValueTask<string> ReadPipeAsync(int fd)
    {
        const int bufferSize = 4096;
        var buffer = ArrayPool<char>.Shared.Rent(bufferSize);
        var sb = ObjectPool<StringBuilder>.Shared.Rent();

        while (true)
        {
            unsafe
            {
                fixed (char* p = buffer)
                {
                    var bytesRead = Read(fd, p, bufferSize);
                    if (bytesRead is 0) break;

                    sb.Append(buffer[..bytesRead]);
                }
            }

            await Task.Yield();
        }

        var result = sb.ToString();

        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());
        ArrayPool<char>.Shared.Return(buffer);

        return result;
    }

    private static async ValueTask<int> WaitPidAsync(int pid, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = WaitPID(pid, out var status, 1);
            if (result == pid) return status;
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    [LibraryImport("libc", EntryPoint = "fork", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Fork();

    [LibraryImport("libc", EntryPoint = "pipe", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private unsafe static partial int Pipe(int* pipe);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private unsafe static partial int Close(int fd);

    [LibraryImport("libc", EntryPoint = "dup2", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private unsafe static partial int Dup2(int oldVal, int newVal);

    [LibraryImport("libc", EntryPoint = "execvp", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private unsafe static partial int ExecVP(char* file, char** argv);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private unsafe static partial int Read(int fd, char* buf, int count);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private unsafe static partial int Write(int fd, char* buf, int count);

    [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int WaitPID(int pid, out int status, int options);
}