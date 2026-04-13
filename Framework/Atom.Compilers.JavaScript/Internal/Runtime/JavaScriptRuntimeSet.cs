using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeSet(ImmutableArray<JavaScriptRuntimeValue> Items);