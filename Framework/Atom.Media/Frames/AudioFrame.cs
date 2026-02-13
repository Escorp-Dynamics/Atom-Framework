#pragma warning disable CA1024, IDE0032

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Представляет аудиокадр как zero-copy view над буфером.
/// Не владеет памятью — время жизни ограничено scope.
/// </summary>
/// <remarks>
/// Для interleaved форматов данные чередуются: L R L R L R ...
/// Для planar форматов каждый канал в отдельной плоскости: LLLL... RRRR...
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly ref struct AudioFrame
{
    // Максимум 8 каналов (7.1 surround)
    private readonly Span<byte> plane0;
    private readonly Span<byte> plane1;
    private readonly Span<byte> plane2;
    private readonly Span<byte> plane3;
    private readonly Span<byte> plane4;
    private readonly Span<byte> plane5;
    private readonly Span<byte> plane6;
    private readonly Span<byte> plane7;

    /// <summary>
    /// Метаданные кадра.
    /// </summary>
    public readonly AudioFrameInfo Info;

    /// <summary>
    /// Количество семплов на канал.
    /// </summary>
    public int SampleCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Info.SampleCount;
    }

    /// <summary>
    /// Количество каналов.
    /// </summary>
    public int ChannelCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Info.ChannelCount;
    }

    /// <summary>
    /// Формат семплов.
    /// </summary>
    public AudioSampleFormat SampleFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Info.SampleFormat;
    }

    /// <summary>
    /// Возвращает true, если кадр пуст.
    /// </summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => plane0.IsEmpty;
    }

    /// <summary>
    /// Возвращает true, если формат planar.
    /// </summary>
    public bool IsPlanar
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Info.SampleFormat.IsPlanar();
    }

    #region Constructors

    /// <summary>
    /// Создаёт аудиокадр для interleaved формата.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AudioFrame(Span<byte> interleavedData, AudioFrameInfo info)
    {
        plane0 = interleavedData;
        plane1 = default;
        plane2 = default;
        plane3 = default;
        plane4 = default;
        plane5 = default;
        plane6 = default;
        plane7 = default;
        Info = info;
    }

    /// <summary>
    /// Создаёт аудиокадр для planar формата (стерео).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AudioFrame(Span<byte> left, Span<byte> right, AudioFrameInfo info)
    {
        plane0 = left;
        plane1 = right;
        plane2 = default;
        plane3 = default;
        plane4 = default;
        plane5 = default;
        plane6 = default;
        plane7 = default;
        Info = info;
    }

    /// <summary>
    /// Создаёт аудиокадр для planar формата с явным указанием каналов (до 8).
    /// Неиспользуемые каналы передавать как default.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AudioFrame(
        Span<byte> ch0,
        Span<byte> ch1,
        Span<byte> ch2,
        Span<byte> ch3,
        Span<byte> ch4,
        Span<byte> ch5,
        Span<byte> ch6,
        Span<byte> ch7,
        AudioFrameInfo info)
    {
        plane0 = ch0;
        plane1 = ch1;
        plane2 = ch2;
        plane3 = ch3;
        plane4 = ch4;
        plane5 = ch5;
        plane6 = ch6;
        plane7 = ch7;
        Info = info;
    }

    #endregion

    #region Data Access

    /// <summary>
    /// Возвращает interleaved данные.
    /// </summary>
    public Span<byte> InterleavedData
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => plane0;
    }

    /// <summary>
    /// Возвращает interleaved данные как типизированный Span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetInterleavedAs<T>() where T : unmanaged
        => MemoryMarshal.Cast<byte, T>(plane0);

    /// <summary>
    /// Возвращает данные канала по индексу (для planar форматов).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetChannel(int index) => index switch
    {
        0 => plane0,
        1 => plane1,
        2 => plane2,
        3 => plane3,
        4 => plane4,
        5 => plane5,
        6 => plane6,
        7 => plane7,
        _ => default,
    };

    /// <summary>
    /// Возвращает данные канала как типизированный Span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetChannelAs<T>(int index) where T : unmanaged
        => MemoryMarshal.Cast<byte, T>(GetChannel(index));

    /// <summary>
    /// Возвращает левый канал (index 0).
    /// </summary>
    public Span<byte> Left
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => plane0;
    }

    /// <summary>
    /// Возвращает правый канал (index 1).
    /// </summary>
    public Span<byte> Right
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => plane1;
    }

    #endregion

    #region Typed Access (convenience)

    /// <summary>
    /// Возвращает interleaved данные как S16.
    /// </summary>
    public Span<short> AsS16
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => MemoryMarshal.Cast<byte, short>(plane0);
    }

    /// <summary>
    /// Возвращает interleaved данные как F32.
    /// </summary>
    public Span<float> AsF32
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => MemoryMarshal.Cast<byte, float>(plane0);
    }

    /// <summary>
    /// Возвращает interleaved данные как S32.
    /// </summary>
    public Span<int> AsS32
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => MemoryMarshal.Cast<byte, int>(plane0);
    }

    #endregion

    #region Raw Pointer Access

    /// <summary>
    /// Возвращает указатель на данные канала 0.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte* GetPointer0()
    {
        fixed (byte* ptr = plane0)
            return ptr;
    }

    /// <summary>
    /// Возвращает указатель на данные канала по индексу.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte* GetPointer(int index)
    {
        var channel = GetChannel(index);
        fixed (byte* ptr = channel)
            return ptr;
    }

    #endregion
}
