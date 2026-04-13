namespace Atom.Compilers.JavaScript;

[Flags]
internal enum JavaScriptRuntimeMemberAttributes : byte
{
    None = 0,
    ReadOnly = 1 << 0,
    Required = 1 << 1,
    Pure = 1 << 2,
    Inline = 1 << 3,
}