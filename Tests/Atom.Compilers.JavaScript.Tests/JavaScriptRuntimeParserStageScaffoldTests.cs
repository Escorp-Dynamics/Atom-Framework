namespace Atom.Compilers.JavaScript.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptRuntimeParserStageScaffoldTests
{
    [Test]
    public void ParserStageCapturesParsedSourceContractForNonEmptySourceTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "1 + 1".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.SessionEpoch, Is.EqualTo(1));
        Assert.That(parsedSource.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.Extended));
        Assert.That(parsedSource.AllowsExtendedRuntimeFeatures, Is.True);
        Assert.That(parsedSource.ParserFeatures, Is.EqualTo(JavaScriptRuntimeParserFeature.ContainsNumericLiteral | JavaScriptRuntimeParserFeature.ContainsBinaryOperator));
        Assert.That(parsedSource.ContainsIdentifierReference, Is.False);
        Assert.That(parsedSource.ContainsMemberAccess, Is.False);
        Assert.That(parsedSource.ContainsInvocation, Is.False);
        Assert.That(parsedSource.ContainsHostBindingCandidate, Is.False);
        Assert.That(parsedSource.ContainsIndexAccess, Is.False);
        Assert.That(parsedSource.ContainsAssignment, Is.False);
        Assert.That(parsedSource.ContainsStringLiteral, Is.False);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.True);
        Assert.That(parsedSource.ContainsUnaryOperator, Is.False);
        Assert.That(parsedSource.ContainsBinaryOperator, Is.True);
        Assert.That(parsedSource.Operation, Is.EqualTo(JavaScriptRuntimeExecutionOperationKind.Execute));
        Assert.That(parsedSource.SourceLength, Is.EqualTo("1 + 1".Length));
    }

    [Test]
    public void ParserStageCapturesEcmaScriptSpecificationTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);
        const string source = "host.connect();";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.ECMAScript));
        Assert.That(parsedSource.AllowsExtendedRuntimeFeatures, Is.False);
        Assert.That(parsedSource.ParserFeatures, Is.EqualTo(JavaScriptRuntimeParserFeature.ContainsIdentifierReference | JavaScriptRuntimeParserFeature.ContainsMemberAccess | JavaScriptRuntimeParserFeature.ContainsInvocation | JavaScriptRuntimeParserFeature.ContainsHostBindingCandidate));
        Assert.That(parsedSource.ContainsIdentifierReference, Is.True);
        Assert.That(parsedSource.ContainsMemberAccess, Is.True);
        Assert.That(parsedSource.ContainsInvocation, Is.True);
        Assert.That(parsedSource.ContainsHostBindingCandidate, Is.True);
        Assert.That(parsedSource.ContainsIndexAccess, Is.False);
        Assert.That(parsedSource.ContainsAssignment, Is.False);
        Assert.That(parsedSource.ContainsStringLiteral, Is.False);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.False);
        Assert.That(parsedSource.ContainsUnaryOperator, Is.False);
        Assert.That(parsedSource.ContainsBinaryOperator, Is.False);
        Assert.That(parsedSource.ContainsComparisonOperator, Is.False);
        Assert.That(parsedSource.ContainsLogicalOperator, Is.False);
        Assert.That(parsedSource.ContainsTemplateLiteral, Is.False);
        Assert.That(parsedSource.ContainsArrowFunctionCandidate, Is.False);
        Assert.That(parsedSource.ContainsNullishCoalescingOperator, Is.False);
        Assert.That(parsedSource.ContainsOptionalChainingCandidate, Is.False);
        Assert.That(parsedSource.ContainsArrayLiteralCandidate, Is.False);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.False);
        Assert.That(parsedSource.ContainsSpreadOrRestCandidate, Is.False);
        Assert.That(parsedSource.ContainsConditionalOperatorCandidate, Is.False);
    }

    [Test]
    public void ParserStageCapturesExtendedSyntaxFeatureMatrixTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "host['name'] = value + 1; !ready;";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsIdentifierReference, Is.True);
        Assert.That(parsedSource.ContainsMemberAccess, Is.False);
        Assert.That(parsedSource.ContainsInvocation, Is.False);
        Assert.That(parsedSource.ContainsHostBindingCandidate, Is.False);
        Assert.That(parsedSource.ContainsIndexAccess, Is.True);
        Assert.That(parsedSource.ContainsAssignment, Is.True);
        Assert.That(parsedSource.ContainsStringLiteral, Is.True);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.True);
        Assert.That(parsedSource.ContainsUnaryOperator, Is.True);
        Assert.That(parsedSource.ContainsBinaryOperator, Is.True);
        Assert.That(parsedSource.ContainsComparisonOperator, Is.False);
        Assert.That(parsedSource.ContainsLogicalOperator, Is.False);
        Assert.That(parsedSource.ContainsTemplateLiteral, Is.False);
        Assert.That(parsedSource.ContainsArrowFunctionCandidate, Is.False);
        Assert.That(parsedSource.ContainsNullishCoalescingOperator, Is.False);
        Assert.That(parsedSource.ContainsOptionalChainingCandidate, Is.False);
        Assert.That(parsedSource.ContainsArrayLiteralCandidate, Is.False);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.False);
        Assert.That(parsedSource.ContainsSpreadOrRestCandidate, Is.False);
        Assert.That(parsedSource.ContainsConditionalOperatorCandidate, Is.False);
    }

    [Test]
    public void ParserStageCapturesComparisonLogicalTemplateAndArrowFeaturesTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "value => `x`; left < right && ready";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsComparisonOperator, Is.True);
        Assert.That(parsedSource.ContainsLogicalOperator, Is.True);
        Assert.That(parsedSource.ContainsTemplateLiteral, Is.True);
        Assert.That(parsedSource.ContainsArrowFunctionCandidate, Is.True);
        Assert.That(parsedSource.ContainsNullishCoalescingOperator, Is.False);
        Assert.That(parsedSource.ContainsOptionalChainingCandidate, Is.False);
        Assert.That(parsedSource.ContainsArrayLiteralCandidate, Is.False);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.False);
        Assert.That(parsedSource.ContainsSpreadOrRestCandidate, Is.False);
        Assert.That(parsedSource.ContainsConditionalOperatorCandidate, Is.False);
    }

    [Test]
    public void ParserStageDoesNotTreatArrowFunctionAsComparisonOperatorTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "value => next";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsArrowFunctionCandidate, Is.True);
        Assert.That(parsedSource.ContainsComparisonOperator, Is.False);
    }

    [Test]
    public void ParserStageCapturesNullishAndOptionalChainingFeaturesTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "value?.member ?? fallback";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsNullishCoalescingOperator, Is.True);
        Assert.That(parsedSource.ContainsOptionalChainingCandidate, Is.True);
        Assert.That(parsedSource.ContainsArrayLiteralCandidate, Is.False);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.False);
        Assert.That(parsedSource.ContainsSpreadOrRestCandidate, Is.False);
        Assert.That(parsedSource.ContainsConditionalOperatorCandidate, Is.False);
    }

    [Test]
    public void ParserStageCapturesAggregateLiteralAndSpreadFeaturesTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "{ key: [...items] }";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsArrayLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsIndexAccess, Is.False);
        Assert.That(parsedSource.ContainsSpreadOrRestCandidate, Is.True);
        Assert.That(parsedSource.ContainsConditionalOperatorCandidate, Is.False);
    }

    [Test]
    public void ParserStageCapturesConditionalOperatorCandidateTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "condition ? left : right";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsConditionalOperatorCandidate, Is.True);
        Assert.That(parsedSource.ContainsNullishCoalescingOperator, Is.False);
        Assert.That(parsedSource.ContainsOptionalChainingCandidate, Is.False);
    }

    [Test]
    public void ParserStageCapturesDestructuringPatternCandidateTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "const { value } = source";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsDestructuringPatternCandidate, Is.True);
        Assert.That(parsedSource.ContainsAssignment, Is.True);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.False);
    }

    [Test]
    public void ParserStageDoesNotClassifyBlockContextAsObjectLiteralCandidateTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "if (ready) { invoke(); }";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.False);
        Assert.That(parsedSource.ContainsInvocation, Is.True);
    }

    [Test]
    public void ParserStageCapturesExpandedObjectLiteralExpressionContextsTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "ready && { value: 1 }; return { other: 2 };";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsLogicalOperator, Is.True);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.True);
    }

    [Test]
    public void ParserStageCapturesObjectLiteralAfterThrowAndCaseKeywordsTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "throw { error: 1 }; case { other: 2 }:";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.True);
    }

    [Test]
    public void ParserStageCapturesObjectLiteralAfterAwaitKeywordTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "await { value: 1 }";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.True);
    }

    [TestCase("value + { value: 1 }")]
    [TestCase("value > { value: 1 }")]
    public void ParserStageCapturesObjectLiteralAfterOperatorContextsTest(string source)
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.True);
    }

    [TestCase("typeof { value: 1 }")]
    [TestCase("void { value: 1 }")]
    [TestCase("delete { value: 1 }")]
    [TestCase("yield { value: 1 }")]
    [TestCase("in { value: 1 }")]
    [TestCase("instanceof { value: 1 }")]
    public void ParserStageCapturesObjectLiteralAfterExtendedExpressionKeywordsTest(string source)
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.True);
    }

    [Test]
    public void ParserStageCapturesArrayLiteralAfterReturnThrowAndCaseKeywordsTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "return [1, 2]; throw [3, 4]; case [5, 6]:";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsArrayLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.True);
    }

    [TestCase("value + [1, 2]")]
    [TestCase("() => [1, 2]")]
    public void ParserStageCapturesArrayLiteralAfterOperatorContextsTest(string source)
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsArrayLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.True);
    }

    [Test]
    public void ParserStageCapturesArrayLiteralAfterAwaitKeywordTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "await [1, 2]";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsArrayLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.True);
    }

    [TestCase("typeof [1, 2]")]
    [TestCase("void [1, 2]")]
    [TestCase("delete [1, 2]")]
    [TestCase("yield [1, 2]")]
    [TestCase("in [1, 2]")]
    [TestCase("instanceof [1, 2]")]
    public void ParserStageCapturesArrayLiteralAfterExtendedExpressionKeywordsTest(string source)
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsArrayLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.True);
    }

    [Test]
    public void ParserStageCapturesShorthandObjectLiteralCandidateTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "({ value })";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsObjectLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsIdentifierReference, Is.True);
        Assert.That(parsedSource.ContainsInvocation, Is.True);
    }

    [Test]
    public void ParserStageCapturesRegularExpressionLiteralCandidateWithoutBinaryOperatorTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = @"/\d+/g";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsRegularExpressionLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsBinaryOperator, Is.False);
    }

    [Test]
    public void ParserStageKeepsDivisionOnBinaryOperatorPathTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "left / right";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsRegularExpressionLiteralCandidate, Is.False);
        Assert.That(parsedSource.ContainsBinaryOperator, Is.True);
    }

    [Test]
    public void ParserStageCapturesRegularExpressionLiteralCandidateWithCharacterClassSlashTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = @"/[a/b]+/g";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsRegularExpressionLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsBinaryOperator, Is.False);
    }

    [Test]
    public void ParserStageCapturesRegularExpressionLiteralCandidateAfterReturnKeywordTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = @"return /\d+/g";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsRegularExpressionLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsBinaryOperator, Is.False);
    }

    [Test]
    public void ParserStageCapturesRegularExpressionLiteralCandidateAfterAwaitKeywordTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = @"await /\d+/g";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsRegularExpressionLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsBinaryOperator, Is.False);
    }

    [TestCase(@"typeof /\d+/g")]
    [TestCase(@"delete /\d+/g")]
    [TestCase(@"void /\d+/g")]
    [TestCase(@"case /\d+/g")]
    [TestCase(@"in /\d+/g")]
    [TestCase(@"instanceof /\d+/g")]
    public void ParserStageCapturesRegularExpressionLiteralCandidateAfterExtendedKeywordContextTest(string source)
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsRegularExpressionLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsBinaryOperator, Is.False);
    }

    [TestCase(@"value + /\d+/g", true, false)]
    [TestCase(@"value > /\d+/g", false, true)]
    [TestCase(@"() => /\d+/g", false, false)]
    public void ParserStageCapturesRegularExpressionLiteralCandidateAfterOperatorContextTest(
        string source,
        bool containsBinaryOperator,
        bool containsComparisonOperator)
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsRegularExpressionLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsBinaryOperator, Is.EqualTo(containsBinaryOperator));
        Assert.That(parsedSource.ContainsComparisonOperator, Is.EqualTo(containsComparisonOperator));
    }

    [Test]
    public void ParserStageIgnoresHostBindingAndConditionalCandidatesInsideStringLiteralTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "\"host.connect() ? left : right\"";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsStringLiteral, Is.True);
        Assert.That(parsedSource.ContainsHostBindingCandidate, Is.False);
        Assert.That(parsedSource.ContainsConditionalOperatorCandidate, Is.False);
        Assert.That(parsedSource.ContainsInvocation, Is.False);
    }

    [Test]
    public void ParserStageIgnoresHostBindingAndConditionalCandidatesInsideCommentsTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "// host.connect() ? left : right\n0";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsHostBindingCandidate, Is.False);
        Assert.That(parsedSource.ContainsConditionalOperatorCandidate, Is.False);
        Assert.That(parsedSource.ContainsInvocation, Is.False);
        Assert.That(parsedSource.ContainsNumericLiteral, Is.True);
    }

    [Test]
    public void ParserStageIgnoresPatternCandidatesInsideTemplateLiteralTextTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "`host.connect() ? left : right`";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsTemplateLiteral, Is.True);
        Assert.That(parsedSource.ContainsHostBindingCandidate, Is.False);
        Assert.That(parsedSource.ContainsConditionalOperatorCandidate, Is.False);
        Assert.That(parsedSource.ContainsInvocation, Is.False);
    }

    [Test]
    public void ParserStageCapturesPatternCandidatesInsideTemplateInterpolationTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "`${host.connect() ? left : right}`";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsTemplateLiteral, Is.True);
        Assert.That(parsedSource.ContainsHostBindingCandidate, Is.True);
        Assert.That(parsedSource.ContainsConditionalOperatorCandidate, Is.True);
        Assert.That(parsedSource.ContainsInvocation, Is.True);
    }

    [Test]
    public void ParserStageKeepsTemplateInterpolationOpenAcrossStringContainingClosingBraceTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "`${'}' + host.connect()}`";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsTemplateLiteral, Is.True);
        Assert.That(parsedSource.ContainsHostBindingCandidate, Is.True);
        Assert.That(parsedSource.ContainsInvocation, Is.True);
    }

    [Test]
    public void ParserStageKeepsTemplateInterpolationOpenAcrossBlockCommentContainingClosingBraceTest()
    {
        var runtime = new JavaScriptRuntime();
        const string source = "`${/* } */ host.connect()}`";

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsTemplateLiteral, Is.True);
        Assert.That(parsedSource.ContainsHostBindingCandidate, Is.True);
        Assert.That(parsedSource.ContainsInvocation, Is.True);
    }

    [TestCase("`${/}/.test(value)}`")]
    [TestCase("`${/{/.test(value)}`")]
    public void ParserStageKeepsTemplateInterpolationOpenAcrossRegularExpressionContainingBraceTest(string source)
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var parsed = JavaScriptRuntimeParserStageScaffold.TryParse(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                source.AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            out var parsedSource);

        Assert.That(parsed, Is.True);
        Assert.That(parsedSource.ContainsTemplateLiteral, Is.True);
        Assert.That(parsedSource.ContainsRegularExpressionLiteralCandidate, Is.True);
        Assert.That(parsedSource.ContainsInvocation, Is.True);
        Assert.That(parsedSource.ContainsMemberAccess, Is.True);
    }
}