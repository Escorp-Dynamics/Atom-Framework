using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

internal static class JavaScriptRuntimeMetadataReader
{
    private const int SupportedMetadataVersion = 1;
    private const int SmallUniqueExportLinearScanThreshold = 4;
    private enum GeneratorKind : byte
    {
        Object,
        Dictionary,
        Property,
        Function,
        Ignore,
    }

    internal static JavaScriptRuntimeRegistrationDescriptor Read(string registrationName, ImmutableArray<JavaScriptGeneratedTypeMetadata> metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationName);
        registrationName = registrationName.Trim();

        if (metadata.IsDefaultOrEmpty)
            throw new InvalidOperationException("Generated metadata is required for runtime registration.");

        if (metadata.Length == 1)
            return new JavaScriptRuntimeRegistrationDescriptor(registrationName, [ReadType(metadata[0])]);

        var descriptors = new JavaScriptRuntimeTypeDescriptor[metadata.Length];

        for (var i = 0; i < metadata.Length; i++)
            descriptors[i] = ReadType(metadata[i]);

        return new JavaScriptRuntimeRegistrationDescriptor(registrationName, ImmutableCollectionsMarshal.AsImmutableArray(descriptors));
    }

    private static JavaScriptRuntimeTypeDescriptor ReadType(JavaScriptGeneratedTypeMetadata metadata)
    {
        var generatorKind = ValidateType(metadata);
        var typeFlags = GetTypeFlags(metadata);

        if (metadata.Members.Length == 1)
        {
            return new JavaScriptRuntimeTypeDescriptor(
                metadata.EntityName,
                metadata.Generator,
                [ReadMember(generatorKind, metadata.Members[0])],
                typeFlags);
        }

        var members = new JavaScriptRuntimeMemberDescriptor[metadata.Members.Length];
        var requiresUniqueExports = RequiresUniqueExports(generatorKind);
        var useLinearDuplicateScan = requiresUniqueExports && metadata.Members.Length <= SmallUniqueExportLinearScanThreshold;
        var exportNames = CreateExportNameSet(requiresUniqueExports, useLinearDuplicateScan, metadata.Members.Length);

        for (var i = 0; i < metadata.Members.Length; i++)
        {
            var descriptor = ReadMember(generatorKind, metadata.Members[i]);

            if (descriptor.ExportName is not null && useLinearDuplicateScan)
                ThrowIfDuplicateExportName(metadata.EntityName, members, i, descriptor.ExportName);

            if (exportNames is not null
                && descriptor.ExportName is not null
                && !exportNames.Add(descriptor.ExportName))
            {
                throw new InvalidOperationException($"Exported JavaScript name '{descriptor.ExportName}' is duplicated inside type '{metadata.EntityName}'.");
            }

            members[i] = descriptor;
        }

        return new JavaScriptRuntimeTypeDescriptor(metadata.EntityName, metadata.Generator, ImmutableCollectionsMarshal.AsImmutableArray(members), typeFlags);
    }

    private static void ThrowIfDuplicateExportName(
        string entityName,
        JavaScriptRuntimeMemberDescriptor[] members,
        int count,
        string exportName)
    {
        for (var i = 0; i < count; i++)
        {
            if (string.Equals(members[i].ExportName, exportName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Exported JavaScript name '{exportName}' is duplicated inside type '{entityName}'.");
            }
        }
    }

    private static JavaScriptRuntimeMemberDescriptor ReadMember(GeneratorKind generatorKind, JavaScriptGeneratedMemberMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.Name))
            throw new InvalidOperationException("Generated member metadata must contain a non-empty member name.");

        var flags = ReadMemberAttributes(metadata);

        return new JavaScriptRuntimeMemberDescriptor(metadata.Name, metadata.Kind, NormalizeExportName(generatorKind, metadata), flags);
    }

    private static GeneratorKind ValidateType(JavaScriptGeneratedTypeMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.EntityName))
            throw new InvalidOperationException("Generated type metadata must contain a non-empty entity name.");

        if (metadata.MetadataVersion != SupportedMetadataVersion)
        {
            throw new InvalidOperationException("Metadata version '"
                + metadata.MetadataVersion.ToString(CultureInfo.InvariantCulture)
                + "' is not supported. Expected version '"
                + SupportedMetadataVersion.ToString(CultureInfo.InvariantCulture)
                + "'.");
        }

        return ValidateGenerator(metadata.Generator);
    }

    private static GeneratorKind ValidateGenerator(string generator)
    {
        if (string.IsNullOrWhiteSpace(generator))
            throw new InvalidOperationException("Generated type metadata must contain a non-empty generator name.");

        return generator switch
        {
            "JavaScriptObject" => GeneratorKind.Object,
            "JavaScriptDictionary" => GeneratorKind.Dictionary,
            "JavaScriptProperty" => GeneratorKind.Property,
            "JavaScriptFunction" => GeneratorKind.Function,
            "JavaScriptIgnore" => GeneratorKind.Ignore,
            _ => throw new InvalidOperationException($"Generator '{generator}' is not supported by the runtime metadata reader."),
        };
    }

    private static JavaScriptRuntimeMemberAttributes ReadMemberAttributes(JavaScriptGeneratedMemberMetadata metadata)
        => metadata.Kind switch
        {
            JavaScriptGeneratedMemberKind.Property => ReadPropertyAttributes(metadata),
            JavaScriptGeneratedMemberKind.Method => ReadMethodAttributes(metadata),
            _ => ReadDefaultAttributes(metadata),
        };

    private static JavaScriptRuntimeMemberAttributes ReadPropertyAttributes(JavaScriptGeneratedMemberMetadata metadata)
    {
        if (metadata.IsPure || metadata.IsInline)
            throw new InvalidOperationException($"Property '{metadata.Name}' contains function-only flags.");

        var propertyFlags = JavaScriptRuntimeMemberAttributes.None;
        if (metadata.IsReadOnly)
            propertyFlags |= JavaScriptRuntimeMemberAttributes.ReadOnly;
        if (metadata.IsRequired)
            propertyFlags |= JavaScriptRuntimeMemberAttributes.Required;

        return propertyFlags;
    }

    private static JavaScriptRuntimeMemberAttributes ReadMethodAttributes(JavaScriptGeneratedMemberMetadata metadata)
    {
        if (metadata.IsReadOnly || metadata.IsRequired)
            throw new InvalidOperationException($"Method '{metadata.Name}' contains property-only flags.");

        var methodFlags = JavaScriptRuntimeMemberAttributes.None;
        if (metadata.IsPure)
            methodFlags |= JavaScriptRuntimeMemberAttributes.Pure;
        if (metadata.IsInline)
            methodFlags |= JavaScriptRuntimeMemberAttributes.Inline;

        return methodFlags;
    }

    private static JavaScriptRuntimeMemberAttributes ReadDefaultAttributes(JavaScriptGeneratedMemberMetadata metadata)
    {
        if (metadata.IsReadOnly || metadata.IsRequired || metadata.IsPure || metadata.IsInline)
        {
            throw new InvalidOperationException($"Member '{metadata.Name}' contains unsupported flags for kind '{GetMemberKindName(metadata.Kind)}'.");
        }

        return JavaScriptRuntimeMemberAttributes.None;
    }

    private static string GetMemberKindName(JavaScriptGeneratedMemberKind kind)
        => kind switch
        {
            JavaScriptGeneratedMemberKind.Class => nameof(JavaScriptGeneratedMemberKind.Class),
            JavaScriptGeneratedMemberKind.Struct => nameof(JavaScriptGeneratedMemberKind.Struct),
            JavaScriptGeneratedMemberKind.Interface => nameof(JavaScriptGeneratedMemberKind.Interface),
            JavaScriptGeneratedMemberKind.Property => nameof(JavaScriptGeneratedMemberKind.Property),
            JavaScriptGeneratedMemberKind.Method => nameof(JavaScriptGeneratedMemberKind.Method),
            JavaScriptGeneratedMemberKind.Field => nameof(JavaScriptGeneratedMemberKind.Field),
            JavaScriptGeneratedMemberKind.Indexer => nameof(JavaScriptGeneratedMemberKind.Indexer),
            JavaScriptGeneratedMemberKind.Event => nameof(JavaScriptGeneratedMemberKind.Event),
            _ => nameof(JavaScriptGeneratedMemberKind),
        };

    private static string? NormalizeExportName(GeneratorKind generatorKind, JavaScriptGeneratedMemberMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.ExportName))
            return metadata.ExportName;

        if (generatorKind == GeneratorKind.Ignore)
            return null;

        return metadata.Name;
    }

    private static HashSet<string>? CreateExportNameSet(bool requiresUniqueExports, bool useLinearDuplicateScan, int memberCount)
    {
        if (!requiresUniqueExports || useLinearDuplicateScan || memberCount <= 1)
            return null;

        return new HashSet<string>(memberCount, StringComparer.Ordinal);
    }

    private static bool RequiresUniqueExports(GeneratorKind generatorKind)
        => generatorKind is GeneratorKind.Function or GeneratorKind.Property;

    private static JavaScriptRuntimeTypeAttributes GetTypeFlags(JavaScriptGeneratedTypeMetadata metadata)
    {
        var flags = JavaScriptRuntimeTypeAttributes.None;

        if (metadata.IsGlobalExportEnabled)
            flags |= JavaScriptRuntimeTypeAttributes.GlobalExportEnabled;
        if (metadata.IsStringKeysOnly)
            flags |= JavaScriptRuntimeTypeAttributes.StringKeysOnly;
        if (metadata.IsPreserveEnumerationOrder)
            flags |= JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder;

        return flags;
    }
}