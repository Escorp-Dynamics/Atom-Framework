using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom;

/// <summary>
/// Представляет расширения для работы с неуправляемым кодом.
/// </summary>
public static class UnsafeExtensions
{
    /// <summary>
    /// Преобразует указатель на строку в управляемую строку.
    /// </summary>
    /// <param name="sourcePointer">Указатель на строку в неуправляемом коде.</param>
    /// <returns>Управляемая строка, соответствующая указателю.</returns>
    public static unsafe string? AsString(this nint sourcePointer)
    {
        if (sourcePointer == nint.Zero) return default;

        var length = 0;
        while (*(ushort*)(sourcePointer + length * 2) is not 0) ++length;

        return new string((char*)sourcePointer, 0, length);
    }

    /// <summary>
    /// Преобразует указатель на массив структур в перечислитель управляемых структур.
    /// </summary>
    /// <typeparam name="T">Тип структуры.</typeparam>
    /// <param name="sourcePointer">Указатель на массив структур в неуправляемом коде.</param>
    /// <returns>Перечислитель управляемых структур, соответствующих указателю.</returns>
    public static unsafe IEnumerable<T> AsEnumerable<T>(this nint sourcePointer)
    {
        var size = Unsafe.SizeOf<T>();
        var tmp = new List<T>();

        while (sourcePointer != nint.Zero)
        {
            var p = Unsafe.Read<T>(sourcePointer.ToPointer());
            tmp.Add(p);
            //yield return p;
            sourcePointer += size;
        }

        return tmp;
        //yield break;

        // TODO: Вернуть итератор на .NET 9
    }

    /// <summary>
    /// Преобразует указатель на структуру версии в управляемую структуру <see cref="Version"/>.
    /// </summary>
    /// <param name="sourcePointer">Указатель на структуру версии в неуправляемом коде.</param>
    /// <returns>Управляемая структура Version, соответствующая указателю.</returns>
    public static unsafe Version AsVersion(this nint sourcePointer)
    {
        var major = Unsafe.Read<int>(sourcePointer.ToPointer());
        sourcePointer += nint.Size;

        var minor = Unsafe.Read<int>(sourcePointer.ToPointer());
        sourcePointer += nint.Size;

        var revision = Unsafe.Read<int>(sourcePointer.ToPointer());
        return new Version(major, minor, revision);
    }

    /// <summary>
    /// Преобразует значение типа T в указатель на это значение в неуправляемом коде.
    /// </summary>
    /// <typeparam name="T">Тип значения, которое нужно преобразовать.</typeparam>
    /// <param name="value">Значение, которое нужно преобразовать.</param>
    /// <returns>Указатель на значение в неуправляемом коде.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe nint AsPointer<T>(this T value) => (nint)Unsafe.AsPointer(ref value);

    /// <summary>
    /// Преобразует значение типа T в указатель на это значение в неуправляемом коде, 
    /// освобождая предыдущий указатель, если он не равен нулю.
    /// </summary>
    /// <typeparam name="T">Тип значения, которое нужно преобразовать.</typeparam>
    /// <param name="value">Значение, которое нужно преобразовать.</param>
    /// <param name="sourcePointer">Указатель, который нужно освободить, если он не равен нулю.</param>
    /// <returns>Указатель на значение в неуправляемом коде.</returns>
    public static nint AsPointer<T>(this T value, ref nint sourcePointer)
    {
        if (sourcePointer != nint.Zero) sourcePointer.Free();
        return sourcePointer = value.AsPointer();
    }

    /// <summary>
    /// Преобразует коллекцию в указатель на неуправляемый массив.
    /// </summary>
    /// <typeparam name="T">Тип элементов коллекции.</typeparam>
    /// <param name="source">Коллекция для преобразования.</param>
    /// <returns>Указатель на неуправляемый массив.</returns>
    public static unsafe nint AsPointer<T>(this IEnumerable<T> source)
    {
        if (source is null) return nint.Zero;

        var elements = source.ToArray();
        if (elements.Length is 0) return nint.Zero;

        return GCHandle.Alloc(elements, GCHandleType.Pinned).AddrOfPinnedObject();
    }

    /// <summary>
    /// Освобождает память, выделенную предыдущим вызовом метода ToPointer, 
    /// и преобразует коллекцию в указатель на неуправляемый массив.
    /// </summary>
    /// <typeparam name="T">Тип элементов коллекции.</typeparam>
    /// <param name="source">Коллекция для преобразования.</param>
    /// <param name="sourcePointer">Указатель на неуправляемый массив, 
    /// который будет освобожден, если он не равен нулю.</param>
    /// <returns>Указатель на неуправляемый массив.</returns>
    public static unsafe nint AsPointer<T>(this IEnumerable<T> source, ref nint sourcePointer)
    {
        if (source is null) return sourcePointer = nint.Zero;
        if (sourcePointer != nint.Zero) sourcePointer.Free(true);
        return sourcePointer = source.AsPointer();
    }

    /// <summary>
    /// Освобождает память, выделенную на указателе.
    /// </summary>
    /// <param name="sourcePointer">Указатель на память, которую нужно освободить.</param>
    /// <param name="isEnumerable">Указывает, является ли указатель указателем на массив.</param>
    public static unsafe void Free(this nint sourcePointer, bool isEnumerable)
    {
        if (sourcePointer != nint.Zero) return;

        if (isEnumerable)
        {
            var ptr = sourcePointer;
            nint p;

            while ((p = Unsafe.Read<nint>(ptr.ToPointer())) != nint.Zero)
            {
                Marshal.FreeHGlobal(p);
                ptr += nint.Size;
            }
        }

        Marshal.FreeHGlobal(sourcePointer);
    }

    /// <summary>
    /// Освобождает память, выделенную на указателе.
    /// </summary>
    /// <param name="sourcePointer">Указатель на память, которую нужно освободить.</param>
    public static unsafe void Free(this nint sourcePointer) => Free(sourcePointer, default);
}