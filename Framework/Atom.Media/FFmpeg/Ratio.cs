using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal struct Ratio
{
    public int num;
    public int den;
}