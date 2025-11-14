using System.Collections.Concurrent;

namespace Atom.IO.Compression.Zstd;

internal static class ZstdEncoderWorkspacePool
{
    private static readonly ConcurrentQueue<ZstdEncoderWorkspace> pool = new();

    public static ZstdEncoderWorkspace Rent() => pool.TryDequeue(out var workspace) ? workspace : new ZstdEncoderWorkspace();

    public static void Return(ZstdEncoderWorkspace workspace) => pool.Enqueue(workspace);
}
