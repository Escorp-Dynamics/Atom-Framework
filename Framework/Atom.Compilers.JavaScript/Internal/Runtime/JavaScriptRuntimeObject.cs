using System.Collections.Frozen;
using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeObject(FrozenDictionary<string, JavaScriptRuntimeValue> Properties);