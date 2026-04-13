using System.Collections.Immutable;

namespace Atom.Compilers.JavaScript;

internal readonly record struct JavaScriptRuntimeTypeDescriptor(
    string EntityName,
    string Generator,
    ImmutableArray<JavaScriptRuntimeMemberDescriptor> Members,
    JavaScriptRuntimeTypeAttributes Attributes);