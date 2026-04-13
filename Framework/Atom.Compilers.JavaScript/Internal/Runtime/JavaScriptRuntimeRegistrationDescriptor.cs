using System.Collections.Immutable;

namespace Atom.Compilers.JavaScript;

internal readonly record struct JavaScriptRuntimeRegistrationDescriptor(
    string RegistrationName,
    ImmutableArray<JavaScriptRuntimeTypeDescriptor> Types);