namespace Atom.Net.Browsing.WebDriver;

internal static class SurfaceGuards
{
    internal static NotSupportedException Unsupported(string memberName)
        => new($"{memberName} еще не поддерживается текущим scaffold Atom.Net.Browsing.WebDriver.");
}