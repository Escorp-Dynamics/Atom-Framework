namespace Atom.Compilers.JavaScript.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptRuntimeLoweringStageScaffoldTests
{
    [Test]
    public void LoweringStageCapturesLoweredProgramContractTest()
    {
        var parsedSource = new JavaScriptRuntimeParsedSource(
            SessionEpoch: 2,
            Specification: JavaScriptRuntimeSpecification.Extended,
            ParserFeatures: JavaScriptRuntimeParserFeature.ContainsIdentifierReference | JavaScriptRuntimeParserFeature.ContainsMemberAccess | JavaScriptRuntimeParserFeature.ContainsInvocation | JavaScriptRuntimeParserFeature.ContainsHostBindingCandidate,
            Operation: JavaScriptRuntimeExecutionOperationKind.Evaluate,
            SourceLength: "host.connect();".Length);

        var lowered = JavaScriptRuntimeLoweringStageScaffold.TryLower(parsedSource, out var loweredProgram);

        Assert.That(lowered, Is.True);
        Assert.That(loweredProgram.SessionEpoch, Is.EqualTo(2));
        Assert.That(loweredProgram.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.Extended));
        Assert.That(loweredProgram.PolicyFlags, Is.EqualTo(JavaScriptRuntimeLoweringPolicy.AllowsExtendedRuntimeSurface | JavaScriptRuntimeLoweringPolicy.RequiresInvocationLowering));
        Assert.That(loweredProgram.AllowsExtendedRuntimeFeatures, Is.True);
        Assert.That(loweredProgram.RequiresStrictRuntimeSurface, Is.False);
        Assert.That(loweredProgram.RequiresInvocationLowering, Is.True);
        Assert.That(loweredProgram.Operation, Is.EqualTo(JavaScriptRuntimeExecutionOperationKind.Evaluate));
        Assert.That(loweredProgram.SourceLength, Is.EqualTo("host.connect();".Length));
    }

    [Test]
    public void LoweringStagePreservesEcmaScriptSpecificationTest()
    {
        var parsedSource = new JavaScriptRuntimeParsedSource(
            SessionEpoch: 3,
            Specification: JavaScriptRuntimeSpecification.ECMAScript,
            ParserFeatures: JavaScriptRuntimeParserFeature.None,
            Operation: JavaScriptRuntimeExecutionOperationKind.Execute,
            SourceLength: "1 + 1".Length);

        var lowered = JavaScriptRuntimeLoweringStageScaffold.TryLower(parsedSource, out var loweredProgram);

        Assert.That(lowered, Is.True);
        Assert.That(loweredProgram.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.ECMAScript));
        Assert.That(loweredProgram.PolicyFlags, Is.EqualTo(JavaScriptRuntimeLoweringPolicy.RequiresStrictRuntimeSurface));
        Assert.That(loweredProgram.AllowsExtendedRuntimeFeatures, Is.False);
        Assert.That(loweredProgram.RequiresStrictRuntimeSurface, Is.True);
    }

    [Test]
    public void LoweringStagePromotesHostBindingCandidateToExtendedSurfacePolicyTest()
    {
        var parsedSource = new JavaScriptRuntimeParsedSource(
            SessionEpoch: 4,
            Specification: JavaScriptRuntimeSpecification.ECMAScript,
            ParserFeatures: JavaScriptRuntimeParserFeature.ContainsIdentifierReference | JavaScriptRuntimeParserFeature.ContainsMemberAccess | JavaScriptRuntimeParserFeature.ContainsInvocation | JavaScriptRuntimeParserFeature.ContainsHostBindingCandidate,
            Operation: JavaScriptRuntimeExecutionOperationKind.Execute,
            SourceLength: "host.connect();".Length);

        var lowered = JavaScriptRuntimeLoweringStageScaffold.TryLower(parsedSource, out var loweredProgram);

        Assert.That(lowered, Is.True);
        Assert.That(loweredProgram.PolicyFlags, Is.EqualTo(JavaScriptRuntimeLoweringPolicy.AllowsExtendedRuntimeSurface | JavaScriptRuntimeLoweringPolicy.RequiresInvocationLowering));
        Assert.That(loweredProgram.AllowsExtendedRuntimeFeatures, Is.True);
        Assert.That(loweredProgram.RequiresInvocationLowering, Is.True);
    }

    [Test]
    public void LoweringStageMaterializesPolicyFlagsFromExtendedParserMatrixTest()
    {
        var parsedSource = new JavaScriptRuntimeParsedSource(
            SessionEpoch: 5,
            Specification: JavaScriptRuntimeSpecification.ECMAScript,
            ParserFeatures: JavaScriptRuntimeParserFeature.ContainsIdentifierReference
                | JavaScriptRuntimeParserFeature.ContainsIndexAccess
                | JavaScriptRuntimeParserFeature.ContainsAssignment
                | JavaScriptRuntimeParserFeature.ContainsStringLiteral
                | JavaScriptRuntimeParserFeature.ContainsNumericLiteral
                | JavaScriptRuntimeParserFeature.ContainsUnaryOperator
                | JavaScriptRuntimeParserFeature.ContainsBinaryOperator,
            Operation: JavaScriptRuntimeExecutionOperationKind.Execute,
            SourceLength: "host['name'] = value + 1; !ready;".Length);

        var lowered = JavaScriptRuntimeLoweringStageScaffold.TryLower(parsedSource, out var loweredProgram);

        Assert.That(lowered, Is.True);
        Assert.That(loweredProgram.PolicyFlags, Is.EqualTo(
            JavaScriptRuntimeLoweringPolicy.RequiresStrictRuntimeSurface
            | JavaScriptRuntimeLoweringPolicy.RequiresIndexAccessLowering
            | JavaScriptRuntimeLoweringPolicy.RequiresMutationLowering
            | JavaScriptRuntimeLoweringPolicy.RequiresLiteralMaterialization
            | JavaScriptRuntimeLoweringPolicy.RequiresOperatorLowering));
        Assert.That(loweredProgram.RequiresIndexAccessLowering, Is.True);
        Assert.That(loweredProgram.RequiresMutationLowering, Is.True);
        Assert.That(loweredProgram.RequiresLiteralMaterialization, Is.True);
        Assert.That(loweredProgram.RequiresOperatorLowering, Is.True);
    }

    [Test]
    public void LoweringStageMaterializesTemplateAndClosureCapabilitiesTest()
    {
        var parsedSource = new JavaScriptRuntimeParsedSource(
            SessionEpoch: 6,
            Specification: JavaScriptRuntimeSpecification.ECMAScript,
            ParserFeatures: JavaScriptRuntimeParserFeature.ContainsTemplateLiteral
                | JavaScriptRuntimeParserFeature.ContainsArrowFunctionCandidate
                | JavaScriptRuntimeParserFeature.ContainsComparisonOperator
                | JavaScriptRuntimeParserFeature.ContainsLogicalOperator,
            Operation: JavaScriptRuntimeExecutionOperationKind.Evaluate,
            SourceLength: "value => `x`; left < right && ready".Length);

        var lowered = JavaScriptRuntimeLoweringStageScaffold.TryLower(parsedSource, out var loweredProgram);

        Assert.That(lowered, Is.True);
        Assert.That(loweredProgram.RequiresOperatorLowering, Is.True);
        Assert.That(loweredProgram.RequiresTemplateMaterialization, Is.True);
        Assert.That(loweredProgram.RequiresClosureLowering, Is.True);
    }

    [Test]
    public void LoweringStageMaterializesShortCircuitCapabilityTest()
    {
        var parsedSource = new JavaScriptRuntimeParsedSource(
            SessionEpoch: 7,
            Specification: JavaScriptRuntimeSpecification.ECMAScript,
            ParserFeatures: JavaScriptRuntimeParserFeature.ContainsNullishCoalescingOperator
                | JavaScriptRuntimeParserFeature.ContainsOptionalChainingCandidate,
            Operation: JavaScriptRuntimeExecutionOperationKind.Evaluate,
            SourceLength: "value?.member ?? fallback".Length);

        var lowered = JavaScriptRuntimeLoweringStageScaffold.TryLower(parsedSource, out var loweredProgram);

        Assert.That(lowered, Is.True);
        Assert.That(loweredProgram.RequiresShortCircuitLowering, Is.True);
    }

    [Test]
    public void LoweringStageMaterializesAggregateLiteralAndSpreadCapabilitiesTest()
    {
        var parsedSource = new JavaScriptRuntimeParsedSource(
            SessionEpoch: 8,
            Specification: JavaScriptRuntimeSpecification.ECMAScript,
            ParserFeatures: JavaScriptRuntimeParserFeature.ContainsObjectLiteralCandidate
                | JavaScriptRuntimeParserFeature.ContainsArrayLiteralCandidate
                | JavaScriptRuntimeParserFeature.ContainsSpreadOrRestCandidate,
            Operation: JavaScriptRuntimeExecutionOperationKind.Evaluate,
            SourceLength: "{ key: [...items] }".Length);

        var lowered = JavaScriptRuntimeLoweringStageScaffold.TryLower(parsedSource, out var loweredProgram);

        Assert.That(lowered, Is.True);
        Assert.That(loweredProgram.RequiresAggregateLiteralMaterialization, Is.True);
        Assert.That(loweredProgram.RequiresSpreadLowering, Is.True);
    }

    [Test]
    public void LoweringStageMaterializesConditionalCapabilityTest()
    {
        var parsedSource = new JavaScriptRuntimeParsedSource(
            SessionEpoch: 9,
            Specification: JavaScriptRuntimeSpecification.ECMAScript,
            ParserFeatures: JavaScriptRuntimeParserFeature.ContainsConditionalOperatorCandidate,
            Operation: JavaScriptRuntimeExecutionOperationKind.Evaluate,
            SourceLength: "condition ? left : right".Length);

        var lowered = JavaScriptRuntimeLoweringStageScaffold.TryLower(parsedSource, out var loweredProgram);

        Assert.That(lowered, Is.True);
        Assert.That(loweredProgram.RequiresConditionalLowering, Is.True);
    }

    [Test]
    public void LoweringStageMaterializesDestructuringCapabilityTest()
    {
        var parsedSource = new JavaScriptRuntimeParsedSource(
            SessionEpoch: 10,
            Specification: JavaScriptRuntimeSpecification.ECMAScript,
            ParserFeatures: JavaScriptRuntimeParserFeature.ContainsDestructuringPatternCandidate
                | JavaScriptRuntimeParserFeature.ContainsAssignment
                | JavaScriptRuntimeParserFeature.ContainsObjectLiteralCandidate,
            Operation: JavaScriptRuntimeExecutionOperationKind.Execute,
            SourceLength: "const { value } = source".Length);

        var lowered = JavaScriptRuntimeLoweringStageScaffold.TryLower(parsedSource, out var loweredProgram);

        Assert.That(lowered, Is.True);
        Assert.That(loweredProgram.RequiresDestructuringLowering, Is.True);
    }

    [Test]
    public void LoweringStageMaterializesRegularExpressionCapabilityTest()
    {
        var parsedSource = new JavaScriptRuntimeParsedSource(
            SessionEpoch: 11,
            Specification: JavaScriptRuntimeSpecification.ECMAScript,
            ParserFeatures: JavaScriptRuntimeParserFeature.ContainsRegularExpressionLiteralCandidate,
            Operation: JavaScriptRuntimeExecutionOperationKind.Evaluate,
            SourceLength: @"/\d+/g".Length);

        var lowered = JavaScriptRuntimeLoweringStageScaffold.TryLower(parsedSource, out var loweredProgram);

        Assert.That(lowered, Is.True);
        Assert.That(loweredProgram.RequiresRegularExpressionMaterialization, Is.True);
    }
}