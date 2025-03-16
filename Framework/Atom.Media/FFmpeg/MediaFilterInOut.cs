using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MediaFilterInOut
{
    public char* name;
    public void* filter_ctx;
    public int pad_idx;
    public MediaFilterInOut* next;
}