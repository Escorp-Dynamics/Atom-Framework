using Microsoft.CodeAnalysis;

namespace Atom.Compilers.JavaScript.SourceGeneration.Tests;

internal static class JavaScriptTestReferences
{
    internal static PortableExecutableReference Atom => Create(
        localFileName: "Escorp.Atom.dll",
        fallbackRelativePath: "Framework/Atom/bin/Debug/net10.0/Escorp.Atom.dll");

    internal static PortableExecutableReference Runtime => Create(
        localFileName: "Escorp.Atom.Compilers.JavaScript.dll",
        fallbackRelativePath: "Framework/Atom.Compilers.JavaScript/bin/Debug/net10.0/Escorp.Atom.Compilers.JavaScript.dll");

    private static PortableExecutableReference Create(string localFileName, string fallbackRelativePath)
    {
        var localPath = Path.Combine(TestContext.CurrentContext.TestDirectory, localFileName);

        if (File.Exists(localPath))
            return MetadataReference.CreateFromFile(localPath);

        var root = GetRepositoryRoot();
        var fullPath = Path.Combine(root, fallbackRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Reference assembly was not found.", fullPath);

        return MetadataReference.CreateFromFile(fullPath);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Atom.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root with Atom.slnx was not found.");
    }
}