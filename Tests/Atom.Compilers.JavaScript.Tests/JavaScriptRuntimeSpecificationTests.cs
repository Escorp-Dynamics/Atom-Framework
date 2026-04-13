namespace Atom.Compilers.JavaScript.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptRuntimeSpecificationTests
{
    [Test]
    public void RuntimeUsesExtendedSpecificationByDefaultTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(runtime.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.Extended));
    }

    [Test]
    public void RuntimeCanBeCreatedWithEcmaScriptSpecificationTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(runtime.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.ECMAScript));
    }

    [Test]
    public void RuntimeEcmaScriptSpecificationRejectsHostRegistrationTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.Register<object>(),
            Throws.InvalidOperationException.With.Message.EqualTo("Host registration is only available when JavaScriptRuntimeSpecification.Extended is selected."));
    }

    [Test]
    public void RuntimeExtendedSpecificationAllowsHostRegistrationTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.Extended);

        Assert.That(() => runtime.Register<object>(), Throws.Nothing);
        Assert.That(runtime.CanRegister, Is.True);
    }
}