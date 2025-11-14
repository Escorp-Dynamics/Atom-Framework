using System.Runtime.InteropServices;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Один элемент таблицы (имя/значение в ASCII, уже в нижнем регистре для имён).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly ref struct TableEntry
{
    public readonly ReadOnlySpan<byte> Name;

    public readonly ReadOnlySpan<byte> Value;

    public TableEntry(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        Name = name;
        Value = value;
    }
}