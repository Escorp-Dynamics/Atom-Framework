using System.Runtime.InteropServices;

namespace Atom.Media.Audio.Plugins.CLAP.Extensions;

/// <summary>
/// Определяет делегат для функции, которая проверяет, разрешает ли хост плагину изменять определенный аспект определения аудиопортов.
/// Эта функция должна вызываться из основного потока.
/// </summary>
/// <param name="host"></param>
/// <param name="flag"></param>
/// <returns></returns>
internal delegate bool IsReScanFlagSupported(nint host, AudioPortsReScanFlags flag);

/// <summary>
/// Определяет делегат для функции, которая пересканирует полный список аудиопортов в соответствии с указанными флагами.
/// Недопустимо запрашивать у хоста пересканирование с флагом, который не поддерживается.
/// Некоторые флаги требуют деактивации плагина.
/// Эта функция должна вызываться из основного потока.
/// </summary>
/// <param name="host"></param>
/// <param name="flags"></param>
internal delegate void ReScan(nint host, AudioPortsReScanFlags flags);

/// <summary>
/// Определяет структуру, содержащую указатели на функции, связанные с аудиопортами.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct HostAudioPorts
{
    private nint host;

    internal readonly IsReScanFlagSupported isReScanFlagSupported;
    internal readonly ReScan reScan;

    internal nint Host 
    {
        readonly get => host;
        set => host = value;
    }
}