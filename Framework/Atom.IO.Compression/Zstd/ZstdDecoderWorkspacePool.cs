using System.Collections.Concurrent;

namespace Atom.IO.Compression.Zstd;

internal static class ZstdDecoderWorkspacePool
{
    private static readonly ConcurrentQueue<ZstdDecoderWorkspace> pool = new();

    public static ZstdDecoderWorkspace Rent() => pool.TryDequeue(out var workspace) ? workspace : new ZstdDecoderWorkspace();

    public static void Return(ZstdDecoderWorkspace workspace) => pool.Enqueue(workspace);
}
