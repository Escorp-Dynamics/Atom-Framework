using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tcp;

/// <summary>
/// Представляет поток чтения и записи по протоколу TCP.
/// </summary>
public sealed partial class TcpStream : NetworkStream
{
    internal static void ResetHappyEyeballsMemoryForTests()
        => HappyEyeballsMemory.Clear();

    internal static void RememberHappyEyeballsFamilyForTests(string host, AddressFamily family)
        => HappyEyeballsMemory.Remember(host, family);

    internal static bool TryGetHappyEyeballsFamilyForTests(string host, out byte preference)
        => HappyEyeballsMemory.TryGet(host, out preference);

    internal static void SetHappyEyeballsCounterForTests(int value)
        => HappyEyeballsMemory.SetNext(value);

    private static class HappyEyeballsMemory
    {
        // Кольцевой буфер на 128 записей: [fingerprints] и [prefs] (4=v4, 6=v6, 0=нет)
        private const int Capacity = 128;
        private static readonly ulong[] Fps = new ulong[Capacity];
        private static readonly byte[] Prefs = new byte[Capacity];
        private static int next;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Clear()
        {
            Array.Clear(Fps);
            Array.Clear(Prefs);
            SetNext(0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetNext(int value) => Volatile.Write(ref next, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGet(string host, out byte pref)
        {
            pref = 0;
            if (string.IsNullOrEmpty(host)) return default;

            var fp = Fingerprint(host);

            for (var i = 0; i < Capacity; i++)
            {
                var seen = Volatile.Read(ref Fps[i]);
                if (seen != fp) continue;
                return TryPromoteMatch(i, fp, out pref);
            }

            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Remember(string host, AddressFamily family)
        {
            if (string.IsNullOrEmpty(host)) return;

            var fp = Fingerprint(host);
            var p = MapPreference(family);
            if (p is 0) return;

            var idx = unchecked((Interlocked.Increment(ref next) - 1) & (Capacity - 1));
            Prefs[idx] = p; // сначала пишем pref
            Volatile.Write(ref Fps[idx], fp); // затем публикуем fp
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryPromoteMatch(int index, ulong fingerprint, out byte pref)
        {
            pref = 0;

            var candidate = Prefs[index];
            if (candidate is not (4 or 6)) return default;

            var nextIndex = unchecked((Interlocked.Increment(ref next) - 1) & (Capacity - 1));
            Prefs[nextIndex] = candidate;
            Volatile.Write(ref Fps[nextIndex], fingerprint);
            pref = candidate;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte MapPreference(AddressFamily family) => family switch
        {
            AddressFamily.InterNetwork => 4,
            AddressFamily.InterNetworkV6 => 6,
            _ => 0,
        };

        // 64-битный FNV-1a по ASCII-lowercase; без аллокаций и Culture/IDN.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Fingerprint(string s)
        {
            const ulong FnvOffset = 1469598103934665603UL;
            const ulong FnvPrime = 1099511628211UL;

            unchecked
            {
                var hash = FnvOffset;

                for (var i = 0; i < s.Length; i++)
                {
                    var ch = s[i];
                    if ((uint)(ch - 'A') <= ('Z' - 'A')) ch = (char)(ch | 0x20); // ASCII to-lower

                    hash ^= (byte)ch;
                    hash *= FnvPrime;
                }

                return hash;
            }
        }
    }
}