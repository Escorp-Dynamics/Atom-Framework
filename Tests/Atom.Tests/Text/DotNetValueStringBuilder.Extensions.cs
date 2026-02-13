using System.Globalization;
using System.Runtime.CompilerServices;

namespace Atom.Text.Tests;

internal ref partial struct DotNetValueStringBuilder
{
    public ref DotNetValueStringBuilder Clear()
    {
        RawChars[.._pos].Clear();
        _pos = 0;
        return ref Unsafe.AsRef(ref this);
    }

    public ref DotNetValueStringBuilder AppendLine()
    {
        Append(Environment.NewLine);
        return ref Unsafe.AsRef(ref this);
    }

    public ref DotNetValueStringBuilder AppendLine(string? value)
    {
        Append(value);
        AppendLine();
        return ref Unsafe.AsRef(ref this);
    }

    public ref DotNetValueStringBuilder AppendFormat(string format, params object?[] args)
    {
        Append(string.Format(CultureInfo.CurrentCulture, format, args));
        return ref Unsafe.AsRef(ref this);
    }

    public ref DotNetValueStringBuilder AppendFormat(IFormatProvider? provider, string format, params object?[] args)
    {
        Append(string.Format(provider ?? CultureInfo.CurrentCulture, format, args));
        return ref Unsafe.AsRef(ref this);
    }

    public ref DotNetValueStringBuilder Remove(int startIndex, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, _pos);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _pos - startIndex);
        if (count is 0) return ref Unsafe.AsRef(ref this);

        RawChars[(startIndex + count).._pos].CopyTo(RawChars[startIndex..]);
        _pos -= count;
        return ref Unsafe.AsRef(ref this);
    }

    public ref DotNetValueStringBuilder Replace(string oldValue, string? newValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldValue);
        newValue ??= string.Empty;

        var oldSpan = oldValue.AsSpan();
        var newSpan = newValue.AsSpan();
        var index = 0;

        while (index < _pos)
        {
            var slice = RawChars[index.._pos];
            var occurrence = slice.IndexOf(oldSpan, StringComparison.Ordinal);
            if (occurrence < 0) break;

            index += occurrence;
            var delta = newSpan.Length - oldSpan.Length;
            var required = _pos + delta;
            EnsureCapacity(required);

            RawChars[(index + oldSpan.Length).._pos].CopyTo(RawChars[(index + newSpan.Length)..required]);
            newSpan.CopyTo(RawChars[index..]);
            _pos = required;
            index += newSpan.Length;
        }

        return ref Unsafe.AsRef(ref this);
    }

    public readonly void Replace(char oldChar, char newChar)
    {
        if (oldChar == newChar) return;
        var span = RawChars[.._pos];
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == oldChar) span[i] = newChar;
        }
    }

    public readonly void CopyTo(int sourceIndex, scoped Span<char> destination, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, destination.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceIndex, _pos);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _pos - sourceIndex);

        RawChars.Slice(sourceIndex, count).CopyTo(destination);
    }

    public readonly string ToString(int startIndex, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, _pos);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _pos - startIndex);

        return RawChars.Slice(startIndex, count).ToString();
    }
}