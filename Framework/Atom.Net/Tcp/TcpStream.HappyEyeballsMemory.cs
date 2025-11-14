using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tcp;

/// <summary>
/// Представляет поток чтения и записи по протоколу TCP.
/// </summary>
public sealed partial class TcpStream : NetworkStream
{
    private static class HappyEyeballsMemory
    {
        // Кольцевой буфер на 128 записей: [fingerprints] и [prefs] (4=v4, 6=v6, 0=нет)
        private const int Capacity = 128;
        private static readonly ulong[] Fps = new ulong[Capacity];
        private static readonly byte[] Prefs = new byte[Capacity];
        private static int next;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGet(string host, out byte pref)
        {
            pref = 0;
            if (string.IsNullOrEmpty(host)) return default;

            var fp = Fingerprint(host);

            for (var i = 0; i < Capacity; i++)
            {
                var seen = Volatile.Read(ref Fps[i]);

                if (seen == fp)
                {
                    // Подтягиваем к «голове» — записываем в next-слот
                    var p = Prefs[i];

                    if (p is 4 or 6)
                    {
                        var idx = (Interlocked.Increment(ref next) - 1) & (Capacity - 1);
                        Prefs[idx] = p;
                        Volatile.Write(ref Fps[idx], fp);
                        pref = p;
                        return true;
                    }

                    return default;
                }
            }

            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Remember(string host, AddressFamily family)
        {
            if (string.IsNullOrEmpty(host)) return;

            var fp = Fingerprint(host);
            var p = family is AddressFamily.InterNetwork ? (byte)4 : family is AddressFamily.InterNetworkV6 ? (byte)6 : (byte)0;
            if (p is 0) return;

            var idx = (Interlocked.Increment(ref next) - 1) & (Capacity - 1);
            Prefs[idx] = p; // сначала пишем pref
            Volatile.Write(ref Fps[idx], fp); // затем публикуем fp
        }

        // 64-битный FNV-1a по ASCII-lowercase; без аллокаций и Culture/IDN.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Fingerprint(string s)
        {
            const ulong FnvOffset = 1469598103934665603UL;
            const ulong FnvPrime = 1099511628211UL;

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