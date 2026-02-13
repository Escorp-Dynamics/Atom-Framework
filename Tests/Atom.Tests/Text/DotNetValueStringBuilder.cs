// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Source: https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Text/ValueStringBuilder.cs

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DiagnosticsDebug = System.Diagnostics.Debug;

namespace Atom.Text.Tests;

internal ref partial struct DotNetValueStringBuilder
{
    private char[]? _arrayToReturnToPool;
    private int _pos;

    public DotNetValueStringBuilder(Span<char> initialBuffer)
    {
        _arrayToReturnToPool = null;
        RawChars = initialBuffer;
        _pos = 0;
    }

    public DotNetValueStringBuilder(int initialCapacity)
    {
        _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(initialCapacity);
        RawChars = _arrayToReturnToPool;
        _pos = 0;
    }

    public int Length
    {
        readonly get => _pos;
        set
        {
            DiagnosticsDebug.Assert(value >= 0);
            DiagnosticsDebug.Assert(value <= RawChars.Length);
            _pos = value;
        }
    }

    public readonly int Capacity => RawChars.Length;

    public void EnsureCapacity(int capacity)
    {
        DiagnosticsDebug.Assert(capacity >= 0);

        if ((uint)capacity > (uint)RawChars.Length)
            Grow(capacity - _pos);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NullTerminate()
    {
        EnsureCapacity(_pos + 1);
        RawChars[_pos] = '\0';
    }

    public readonly ref char GetPinnableReference() => ref MemoryMarshal.GetReference(RawChars);

    public readonly ref char this[int index]
    {
        get
        {
            DiagnosticsDebug.Assert(index < _pos);
            return ref RawChars[index];
        }
    }

    public override string ToString()
    {
        return RawChars[.._pos].ToString();
    }

    public Span<char> RawChars { get; private set; }

    public readonly ReadOnlySpan<char> AsSpan() => RawChars[.._pos];
    public readonly ReadOnlySpan<char> AsSpan(int start) => RawChars[start.._pos];
    public readonly ReadOnlySpan<char> AsSpan(int start, int length) => RawChars.Slice(start, length);

    public void Insert(int index, char value, int count)
    {
        if (_pos > RawChars.Length - count)
        {
            Grow(count);
        }

        var remaining = _pos - index;
        RawChars.Slice(index, remaining).CopyTo(RawChars[(index + count)..]);
        RawChars.Slice(index, count).Fill(value);
        _pos += count;
    }

    public ref DotNetValueStringBuilder Insert(int index, string? s)
    {
        if (s is null)
        {
            return ref Unsafe.AsRef(ref this);
        }

        var count = s.Length;

        if (_pos > RawChars.Length - count)
        {
            Grow(count);
        }

        var remaining = _pos - index;
        RawChars.Slice(index, remaining).CopyTo(RawChars[(index + count)..]);
        s.AsSpan().CopyTo(RawChars[index..]);
        _pos += count;

        return ref Unsafe.AsRef(ref this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref DotNetValueStringBuilder Append(char c)
    {
        var pos = _pos;
        var chars = RawChars;
        if ((uint)pos < (uint)chars.Length)
        {
            chars[pos] = c;
            _pos = pos + 1;
        }
        else
        {
            GrowAndAppend(c);
        }

        return ref Unsafe.AsRef(ref this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref DotNetValueStringBuilder Append(string? s)
    {
        if (s is null)
        {
            return ref Unsafe.AsRef(ref this);
        }

        var pos = _pos;
        if (s.Length == 1 && (uint)pos < (uint)RawChars.Length)
        {
            RawChars[pos] = s[0];
            _pos = pos + 1;
        }
        else
        {
            AppendSlow(s);
        }

        return ref Unsafe.AsRef(ref this);
    }

    private void AppendSlow(string s)
    {
        var pos = _pos;
        if (pos > RawChars.Length - s.Length)
        {
            Grow(s.Length);
        }

        s.AsSpan().CopyTo(RawChars[pos..]);
        _pos += s.Length;
    }

    public ref DotNetValueStringBuilder Append(char c, int count)
    {
        if (_pos > RawChars.Length - count)
        {
            Grow(count);
        }

        var dst = RawChars.Slice(_pos, count);
        for (var i = 0; i < dst.Length; i++)
        {
            dst[i] = c;
        }
        _pos += count;

        return ref Unsafe.AsRef(ref this);
    }

    public void Append(ReadOnlySpan<char> value)
    {
        var pos = _pos;
        if (pos > RawChars.Length - value.Length)
        {
            Grow(value.Length);
        }

        value.CopyTo(RawChars[_pos..]);
        _pos += value.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<char> AppendSpan(int length)
    {
        var origPos = _pos;
        if (origPos > RawChars.Length - length)
        {
            Grow(length);
        }

        _pos = origPos + length;
        return RawChars.Slice(origPos, length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowAndAppend(char c)
    {
        Grow(1);
        Append(c);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int additionalCapacityBeyondPos)
    {
        DiagnosticsDebug.Assert(additionalCapacityBeyondPos > 0);
        DiagnosticsDebug.Assert(_pos > RawChars.Length - additionalCapacityBeyondPos);

        const uint ArrayMaxLength = 0x7FFFFFC7;

        var newCapacity = (int)Math.Max(
            (uint)(_pos + additionalCapacityBeyondPos),
            Math.Min((uint)RawChars.Length * 2, ArrayMaxLength));

        var poolArray = ArrayPool<char>.Shared.Rent(newCapacity);

        RawChars[.._pos].CopyTo(poolArray);

        var toReturn = _arrayToReturnToPool;
        RawChars = _arrayToReturnToPool = poolArray;
        if (toReturn is not null)
        {
            ArrayPool<char>.Shared.Return(toReturn);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        var toReturn = _arrayToReturnToPool;
        this = default;
        if (toReturn is not null)
        {
            ArrayPool<char>.Shared.Return(toReturn);
        }
    }
}