using System.Collections.Immutable;

namespace Atom.Compilers.JavaScript.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptRuntimeMetadataReaderTests
{
    [Test]
    public void RuntimeMetadataReaderBuildsRegistrationDescriptorTest()
    {
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptFunction",
                Members: ImmutableArray.Create(
                    new JavaScriptGeneratedMemberMetadata("Invoke", JavaScriptGeneratedMemberKind.Method, IsPure: true, IsInline: true),
                    new JavaScriptGeneratedMemberMetadata("Reset", JavaScriptGeneratedMemberKind.Method, ExportName: "reset"))));

        var descriptor = JavaScriptRuntimeMetadataReader.Read("host", metadata);

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.RegistrationName, Is.EqualTo("host"));
            Assert.That(descriptor.Types.Length, Is.EqualTo(1));
            Assert.That(descriptor.Types[0].EntityName, Is.EqualTo("HostBridge"));
            Assert.That(descriptor.Types[0].Generator, Is.EqualTo("JavaScriptFunction"));
            Assert.That(descriptor.Types[0].Attributes, Is.EqualTo(JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder));
            Assert.That(descriptor.Types[0].Members.Length, Is.EqualTo(2));
            Assert.That(descriptor.Types[0].Members[0], Is.EqualTo(new JavaScriptRuntimeMemberDescriptor("Invoke", JavaScriptGeneratedMemberKind.Method, "Invoke", JavaScriptRuntimeMemberAttributes.Pure | JavaScriptRuntimeMemberAttributes.Inline)));
            Assert.That(descriptor.Types[0].Members[1], Is.EqualTo(new JavaScriptRuntimeMemberDescriptor("Reset", JavaScriptGeneratedMemberKind.Method, "reset", JavaScriptRuntimeMemberAttributes.None)));
        });
    }

    [Test]
    public void RuntimeMetadataReaderRejectsUnsupportedMetadataVersionTest()
    {
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptObject",
                Members: ImmutableArray.Create(new JavaScriptGeneratedMemberMetadata("HostBridge", JavaScriptGeneratedMemberKind.Class, ExportName: "host")),
                MetadataVersion: 2));

        Assert.That(
            () => JavaScriptRuntimeMetadataReader.Read("host", metadata),
            Throws.InvalidOperationException.With.Message.EqualTo("Metadata version '2' is not supported. Expected version '1'."));
    }

    [Test]
    public void RuntimeMetadataReaderRejectsDuplicateFunctionExportNamesTest()
    {
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptFunction",
                Members: ImmutableArray.Create(
                    new JavaScriptGeneratedMemberMetadata("Invoke", JavaScriptGeneratedMemberKind.Method, ExportName: "call"),
                    new JavaScriptGeneratedMemberMetadata("Run", JavaScriptGeneratedMemberKind.Method, ExportName: "call"))));

        Assert.That(
            () => JavaScriptRuntimeMetadataReader.Read("host", metadata),
            Throws.InvalidOperationException.With.Message.EqualTo("Exported JavaScript name 'call' is duplicated inside type 'HostBridge'."));
    }

    [Test]
    public void RuntimeMetadataReaderRejectsUnknownGeneratorTest()
    {
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptPortal",
                Members: ImmutableArray.Create(new JavaScriptGeneratedMemberMetadata("HostBridge", JavaScriptGeneratedMemberKind.Class))));

        Assert.That(
            () => JavaScriptRuntimeMetadataReader.Read("host", metadata),
            Throws.InvalidOperationException.With.Message.EqualTo("Generator 'JavaScriptPortal' is not supported by the runtime metadata reader."));
    }

    [Test]
    public void RuntimeRegistersGeneratedMetadataDescriptorTest()
    {
        var runtime = new JavaScriptRuntime();
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptObject",
                Members: ImmutableArray.Create(new JavaScriptGeneratedMemberMetadata("HostBridge", JavaScriptGeneratedMemberKind.Class, ExportName: "host")),
                IsGlobalExportEnabled: true));

        runtime.Register("host", metadata);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Registrations.Length, Is.EqualTo(1));
            Assert.That(runtime.Registrations[0].RegistrationName, Is.EqualTo("host"));
            Assert.That(runtime.Registrations[0].Types[0].Attributes, Is.EqualTo(JavaScriptRuntimeTypeAttributes.GlobalExportEnabled | JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder));
        });
    }

    [Test]
    public void RuntimeMetadataReaderNormalizesRegistrationNameTest()
    {
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptObject",
                Members: ImmutableArray.Create(new JavaScriptGeneratedMemberMetadata("HostBridge", JavaScriptGeneratedMemberKind.Class, ExportName: "host"))));

        var descriptor = JavaScriptRuntimeMetadataReader.Read("  host.bridge  ", metadata);

        Assert.That(descriptor.RegistrationName, Is.EqualTo("host.bridge"));
    }

    [Test]
    public void RuntimeRejectsDuplicateGeneratedRegistrationNameTest()
    {
        var runtime = new JavaScriptRuntime();
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptObject",
                Members: ImmutableArray.Create(new JavaScriptGeneratedMemberMetadata("HostBridge", JavaScriptGeneratedMemberKind.Class, ExportName: "host"))));

        runtime.Register("host", metadata);

        Assert.That(
            () => runtime.Register(" host ", metadata),
            Throws.InvalidOperationException.With.Message.EqualTo("Registration name 'host' is already registered."));
    }
}