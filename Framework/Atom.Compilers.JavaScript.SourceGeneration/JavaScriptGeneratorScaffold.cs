using Atom.Text;

namespace Atom.Compilers.JavaScript.SourceGeneration;

internal static class JavaScriptGeneratorScaffold
{
    internal const int MetadataVersion = 1;
    internal readonly record struct MemberMetadataEntry(string Name, string Kind, string? ExportName = null, IReadOnlyList<MetadataConstantEntry>? AdditionalConstants = null);
    internal readonly record struct MetadataConstantEntry(string TypeName, string Name, string ValueLiteral);

    internal static string BuildTypeSource(string? ns, string entityName, string generatedTypeName, string generatorName, int sourceCount)
    {
        using var builder = new ValueStringBuilder(512);

        if (!string.IsNullOrWhiteSpace(ns))
        {
            builder.Append("namespace ");
            builder.Append(ns);
            builder.AppendLine(";");
            builder.AppendLine();
        }

        builder.AppendLine("[global::System.CodeDom.Compiler.GeneratedCode(\"Escorp.Atom.Compilers.JavaScript.SourceGeneration\", \"0.0.1\")]");
        builder.Append("internal static class ");
        builder.Append(generatedTypeName);
        builder.AppendLine();
        builder.AppendLine("{");
        builder.Append("    internal const string EntityName = \"");
        builder.Append(entityName);
        builder.AppendLine("\";");
        builder.Append("    internal const string Generator = \"");
        builder.Append(generatorName);
        builder.AppendLine("\";");
        builder.Append("    internal const int AnnotatedMemberCount = ");
        builder.Append(sourceCount);
        builder.AppendLine(";");
        builder.AppendLine("}");

        return builder.ToString();
    }

    internal static string BuildMemberSource(string? ns, string entityName, string generatedTypeName, string generatorName, IReadOnlyList<MemberMetadataEntry> members, IReadOnlyList<MetadataConstantEntry>? additionalConstants = null)
    {
        var builder = new ValueStringBuilder(1024);

        try
        {
            AppendFileHeader(ref builder, ns, generatedTypeName, entityName, generatorName, members.Count);

            for (var i = 0; i < members.Count; i++)
                AppendMember(ref builder, i, members[i]);

            AppendAdditionalConstants(ref builder, additionalConstants);

            builder.AppendLine("}");

            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static void AppendFileHeader(ref ValueStringBuilder builder, string? ns, string generatedTypeName, string entityName, string generatorName, int memberCount)
    {
        if (!string.IsNullOrWhiteSpace(ns))
        {
            builder.Append("namespace ");
            builder.Append(ns);
            builder.AppendLine(";");
            builder.AppendLine();
        }

        builder.AppendLine("[global::System.CodeDom.Compiler.GeneratedCode(\"Escorp.Atom.Compilers.JavaScript.SourceGeneration\", \"0.0.1\")]");
        builder.Append("internal static class ");
        builder.Append(generatedTypeName);
        builder.AppendLine();
        builder.AppendLine("{");
        builder.Append("    internal const string EntityName = \"");
        builder.Append(entityName);
        builder.AppendLine("\";");
        builder.Append("    internal const string Generator = \"");
        builder.Append(generatorName);
        builder.AppendLine("\";");
        builder.Append("    internal const int MetadataVersion = ");
        builder.Append(MetadataVersion);
        builder.AppendLine(";");
        builder.Append("    internal const int AnnotatedMemberCount = ");
        builder.Append(memberCount);
        builder.AppendLine(";");
    }

    private static void AppendMember(ref ValueStringBuilder builder, int index, MemberMetadataEntry member)
    {
        builder.Append("    internal const string Member");
        builder.Append(index);
        builder.Append("Name = \"");
        builder.Append(member.Name);
        builder.AppendLine("\";");
        builder.Append("    internal const string Member");
        builder.Append(index);
        builder.Append("Kind = \"");
        builder.Append(member.Kind);
        builder.AppendLine("\";");

        if (string.IsNullOrEmpty(member.ExportName)) return;

        builder.Append("    internal const string Member");
        builder.Append(index);
        builder.Append("ExportName = \"");
        builder.Append(member.ExportName);
        builder.AppendLine("\";");

        AppendPrefixedConstants(ref builder, member.AdditionalConstants, string.Concat("Member", index));
    }

    private static void AppendAdditionalConstants(ref ValueStringBuilder builder, IReadOnlyList<MetadataConstantEntry>? additionalConstants)
        => AppendPrefixedConstants(ref builder, additionalConstants, prefix: null);

    private static void AppendPrefixedConstants(ref ValueStringBuilder builder, IReadOnlyList<MetadataConstantEntry>? additionalConstants, string? prefix)
    {
        if (additionalConstants is null) return;

        for (var i = 0; i < additionalConstants.Count; i++)
        {
            builder.Append("    internal const ");
            builder.Append(additionalConstants[i].TypeName);
            builder.Append(' ');
            if (!string.IsNullOrEmpty(prefix))
                builder.Append(prefix);
            builder.Append(additionalConstants[i].Name);
            builder.Append(" = ");
            builder.Append(additionalConstants[i].ValueLiteral);
            builder.AppendLine(";");
        }
    }
}