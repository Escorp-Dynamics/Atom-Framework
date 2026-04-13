using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeTypedArray(JavaScriptRuntimeTypedArrayKind ViewKind, int Length, int ByteOffset);