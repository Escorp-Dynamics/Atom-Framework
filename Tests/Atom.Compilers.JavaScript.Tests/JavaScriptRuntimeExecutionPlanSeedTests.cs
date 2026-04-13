namespace Atom.Compilers.JavaScript.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptRuntimeExecutionPlanSeedTests
{
    [Test]
    public void LoweredProgramCanMaterializeExecutionPlanSeedTest()
    {
        var loweredProgram = new JavaScriptRuntimeLoweredProgram(
            SessionEpoch: 7,
            Specification: JavaScriptRuntimeSpecification.Extended,
            PolicyFlags: JavaScriptRuntimeLoweringPolicy.AllowsExtendedRuntimeSurface
                | JavaScriptRuntimeLoweringPolicy.RequiresInvocationLowering
                | JavaScriptRuntimeLoweringPolicy.RequiresLiteralMaterialization,
            Operation: JavaScriptRuntimeExecutionOperationKind.Evaluate,
            SourceLength: "host.connect('x');".Length);

        var executionPlanSeed = loweredProgram.CreateExecutionPlanSeed();

        Assert.Multiple(() =>
        {
            Assert.That(executionPlanSeed.SessionEpoch, Is.EqualTo(7));
            Assert.That(executionPlanSeed.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.Extended));
            Assert.That(executionPlanSeed.PolicyFlags, Is.EqualTo(loweredProgram.PolicyFlags));
            Assert.That(executionPlanSeed.Operation, Is.EqualTo(JavaScriptRuntimeExecutionOperationKind.Evaluate));
            Assert.That(executionPlanSeed.SourceLength, Is.EqualTo("host.connect('x');".Length));
            Assert.That(executionPlanSeed.AllowsExtendedRuntimeFeatures, Is.True);
            Assert.That(executionPlanSeed.RequiresInvocationLowering, Is.True);
            Assert.That(executionPlanSeed.RequiresLiteralMaterialization, Is.True);
            Assert.That(executionPlanSeed.RequiresStrictRuntimeSurface, Is.False);
        });
    }

    [Test]
    public void ExecutionPlanSeedRetainsStrictLoweringCapabilitiesTest()
    {
        var loweredProgram = new JavaScriptRuntimeLoweredProgram(
            SessionEpoch: 8,
            Specification: JavaScriptRuntimeSpecification.ECMAScript,
            PolicyFlags: JavaScriptRuntimeLoweringPolicy.RequiresStrictRuntimeSurface
                | JavaScriptRuntimeLoweringPolicy.RequiresIndexAccessLowering
                | JavaScriptRuntimeLoweringPolicy.RequiresMutationLowering
                | JavaScriptRuntimeLoweringPolicy.RequiresOperatorLowering,
            Operation: JavaScriptRuntimeExecutionOperationKind.Execute,
            SourceLength: "target[index] = value + 1;".Length);

        var executionPlanSeed = loweredProgram.CreateExecutionPlanSeed();

        Assert.Multiple(() =>
        {
            Assert.That(executionPlanSeed.RequiresStrictRuntimeSurface, Is.True);
            Assert.That(executionPlanSeed.RequiresIndexAccessLowering, Is.True);
            Assert.That(executionPlanSeed.RequiresMutationLowering, Is.True);
            Assert.That(executionPlanSeed.RequiresOperatorLowering, Is.True);
            Assert.That(executionPlanSeed.AllowsExtendedRuntimeFeatures, Is.False);
        });
    }
}