namespace Atom.Compilers.JavaScript;

[Flags]
internal enum JavaScriptRuntimeTypeAttributes : byte
{
    None = 0,
    GlobalExportEnabled = 1 << 0,
    StringKeysOnly = 1 << 1,
    PreserveEnumerationOrder = 1 << 2,
}