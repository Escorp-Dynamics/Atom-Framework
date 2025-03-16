using System.Runtime.CompilerServices;

namespace Atom.Collections;

/// <summary>
/// Представляет перечислитель для <see cref="SparseSpan{T}"/>.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="SparseEnumerator{T}"/>.
/// </remarks>
/// <param name="values">Исходный массив.</param>
/// <param name="indexes">Исходный массив индексов.</param>
/// <param name="currentIndex">Текущий индекс массива.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public ref struct SparseEnumerator<T>(ReadOnlySpan<T> values, ReadOnlySpan<int> indexes, int currentIndex)
{
    private readonly ReadOnlySpan<T> values = values;
    private readonly ReadOnlySpan<int> indexes = indexes;
    private readonly int currentIndex = currentIndex;

    private int index = -1;

    /// <summary>
    /// Текущий элемент перечисления.
    /// </summary>
    public readonly ref readonly T Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref values[indexes[index]];
    }

    /// <summary>
    /// Перемещает курсор к следующему элементу перечисления.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (currentIndex < 0) return default;

        if (index < currentIndex)
        {
            ++index;
            return true;
        }

        Reset();
        return default;
    }

    /// <summary>
    /// Сбрасывает позицию перечислителя в начало.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => index = -1;
}