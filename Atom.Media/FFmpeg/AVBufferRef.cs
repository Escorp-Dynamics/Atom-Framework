using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AVBufferRef
{
    public AVBuffer* buffer;
    public byte* data;
    public long size;
}