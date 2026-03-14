using System.Runtime.InteropServices;

namespace Atom.Media.Audio.Plugins.CLAP;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate nint GetAudioPorts(nint plugin, uint index);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate bool ResizeAudioPorts(nint plugin, uint count);

/// <summary>
/// Структура, представляющая аудиопорты.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct AudioPorts
{
    private readonly uint count;
    private readonly GetAudioPorts get;
    private readonly bool isResizable;
    private readonly ResizeAudioPorts resize;

    internal nint pluginPtr;

    /// <summary>
    /// Количество аудиопортов.
    /// </summary>
    public readonly uint Count => count;

    /// <summary>
    /// Указывает, может ли количество аудиопортов быть изменено.
    /// </summary>
    public readonly bool IsResizable => isResizable;

    /// <summary>
    /// Получает аудиопорт по указанному индексу.
    /// </summary>
    /// <param name="index">Индекс аудиопорта.</param>
    /// <returns>Возвращает структуру AudioPort, представляющую аудиопорт с указанным индексом.</returns>
    public readonly AudioPort this[uint index] => Get(index);

    /// <summary>
    /// Получает аудиопорт по указанному индексу.
    /// </summary>
    /// <param name="index">Индекс аудиопорта.</param>
    /// <returns>Возвращает структуру AudioPort, представляющую аудиопорт с указанным индексом.</returns>
    public readonly AudioPort Get(uint index) => Marshal.PtrToStructure<AudioPort>(get(pluginPtr, index));

    /// <summary>
    /// Изменяет количество аудиопортов.
    /// </summary>
    /// <param name="count">Новое количество аудиопортов.</param>
    /// <returns>Возвращает true, если изменение количества аудиопортов прошло успешно, иначе false.</returns>
    public readonly bool Resize(uint count) => resize(pluginPtr, count);
}