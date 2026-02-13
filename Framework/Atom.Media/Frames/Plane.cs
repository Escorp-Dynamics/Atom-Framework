#pragma warning disable CA1720, CA2225

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Представляет плоскость (plane) видео/аудио данных.
/// Zero-copy view над буфером.
/// </summary>
/// <typeparam name="T">Тип элемента (byte, ushort, float и т.д.).</typeparam>
[StructLayout(LayoutKind.Auto)]
public readonly ref struct Plane<T> where T : unmanaged
{
    /// <summary>
    /// Данные плоскости.
    /// </summary>
    public readonly Span<T> Data;

    /// <summary>
    /// Stride (шаг строки) в элементах типа T.
    /// Для выравнивания может быть больше ширины.
    /// </summary>
    public readonly int Stride;

    /// <summary>
    /// Ширина в элементах.
    /// </summary>
    public readonly int Width;

    /// <summary>
    /// Высота в строках.
    /// </summary>
    public readonly int Height;

    /// <summary>
    /// Создаёт плоскость из Span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Plane(Span<T> data, int stride, int width, int height)
    {
        Data = data;
        Stride = stride;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Создаёт плоскость из указателя.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe Plane(T* pointer, int stride, int width, int height)
    {
        Data = new Span<T>(pointer, stride * height);
        Stride = stride;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Возвращает true, если плоскость пуста.
    /// </summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.IsEmpty;
    }

    /// <summary>
    /// Возвращает строку по индексу.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetRow(int y) => Data.Slice(y * Stride, Width);

    /// <summary>
    /// Возвращает элемент по координатам.
    /// </summary>
    public ref T this[int x, int y]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Data[(y * Stride) + x];
    }

    /// <summary>
    /// Копирует данные в другую плоскость.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CopyTo(Plane<T> destination)
    {
        // Если stride совпадает — можно копировать целиком
        if (Stride == destination.Stride && Width == destination.Width)
        {
            Data[..(Height * Stride)].CopyTo(destination.Data);
            return;
        }

        // Иначе — построчно
        for (var y = 0; y < Height; y++)
        {
            GetRow(y).CopyTo(destination.GetRow(y));
        }
    }

    /// <summary>
    /// Заполняет плоскость значением.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Fill(T value) => Data.Fill(value);

    /// <summary>
    /// Очищает плоскость (заполняет нулями).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear() => Data.Clear();
}

/// <summary>
/// Readonly версия <see cref="Plane{T}"/>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly ref struct ReadOnlyPlane<T> where T : unmanaged
{
    /// <summary>
    /// Данные плоскости.
    /// </summary>
    public readonly ReadOnlySpan<T> Data;

    /// <summary>
    /// Stride (шаг строки) в элементах.
    /// </summary>
    public readonly int Stride;

    /// <summary>
    /// Ширина в элементах.
    /// </summary>
    public readonly int Width;

    /// <summary>
    /// Высота в строках.
    /// </summary>
    public readonly int Height;

    /// <summary>
    /// Создаёт readonly плоскость из Span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyPlane(ReadOnlySpan<T> data, int stride, int width, int height)
    {
        Data = data;
        Stride = stride;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Создаёт readonly плоскость из Plane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyPlane(Plane<T> plane)
    {
        Data = plane.Data;
        Stride = plane.Stride;
        Width = plane.Width;
        Height = plane.Height;
    }

    /// <summary>
    /// Возвращает true, если плоскость пуста.
    /// </summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.IsEmpty;
    }

    /// <summary>
    /// Возвращает строку по индексу.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<T> GetRow(int y) => Data.Slice(y * Stride, Width);

    /// <summary>
    /// Возвращает элемент по координатам.
    /// </summary>
    public ref readonly T this[int x, int y]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Data[(y * Stride) + x];
    }

    /// <summary>
    /// Копирует данные в плоскость.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CopyTo(Plane<T> destination)
    {
        if (Stride == destination.Stride && Width == destination.Width)
        {
            Data[..(Height * Stride)].CopyTo(destination.Data);
            return;
        }

        for (var y = 0; y < Height; y++)
        {
            GetRow(y).CopyTo(destination.GetRow(y));
        }
    }

    /// <summary>
    /// Неявное преобразование из Plane в ReadOnlyPlane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ReadOnlyPlane<T>(Plane<T> plane) => new(plane);
}
