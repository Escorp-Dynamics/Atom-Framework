using System.Collections.Immutable;

namespace Atom.Compilers.JavaScript;

internal readonly record struct JavaScriptGeneratedTypeMetadata(
    string EntityName,
    string Generator,
    ImmutableArray<JavaScriptGeneratedMemberMetadata> Members,
    int MetadataVersion = 1,
    bool IsGlobalExportEnabled = false,
    bool IsStringKeysOnly = true,
    bool IsPreserveEnumerationOrder = true);