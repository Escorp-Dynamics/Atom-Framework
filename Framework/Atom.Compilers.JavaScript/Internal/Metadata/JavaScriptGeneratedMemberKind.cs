namespace Atom.Compilers.JavaScript;

internal enum JavaScriptGeneratedMemberKind : byte
{
    Class,
    Struct,
    Interface,
    Property,
    Method,
    Field,
    Indexer,
    Event,
    Unknown,
}