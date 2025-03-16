using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AVBuffer
{
    public byte* data;
    public long size;
    public volatile uint refcount;
    public void* callback;
    public void* opaque;
    public int flags;
    public int flags_internal;
}