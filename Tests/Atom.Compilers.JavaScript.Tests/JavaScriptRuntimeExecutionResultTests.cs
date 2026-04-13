namespace Atom.Compilers.JavaScript.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptRuntimeExecutionResultTests
{
    [Test]
    public void ExecutionResultCompletedFactoryCreatesCompletedResultTest()
    {
        var result = JavaScriptRuntimeExecutionResult.Completed(
            sessionEpoch: 4,
            operation: JavaScriptRuntimeExecutionOperationKind.Execute,
            value: JavaScriptRuntimeValue.FromBoolean(true));

        Assert.Multiple(() =>
        {
            Assert.That(result.SessionEpoch, Is.EqualTo(4));
            Assert.That(result.Operation, Is.EqualTo(JavaScriptRuntimeExecutionOperationKind.Execute));
            Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.Completed));
            Assert.That(result.Value, Is.EqualTo(JavaScriptRuntimeValue.FromBoolean(true)));
            Assert.That(result.Diagnostic, Is.Null);
        });
    }

    [Test]
    public void ExecutionResultSpecificationViolationFactoryClearsValueAndKeepsDiagnosticTest()
    {
        var diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.Execution,
            JavaScriptRuntimeExecutionDiagnosticCodes.StrictValueKindUnsupported,
            "strict violation");

        var result = JavaScriptRuntimeExecutionResult.SpecificationViolation(
            sessionEpoch: 5,
            operation: JavaScriptRuntimeExecutionOperationKind.Evaluate,
            diagnostic: diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(result.SessionEpoch, Is.EqualTo(5));
            Assert.That(result.Operation, Is.EqualTo(JavaScriptRuntimeExecutionOperationKind.Evaluate));
            Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.SpecificationViolation));
            Assert.That(result.Value, Is.EqualTo(JavaScriptRuntimeValue.Null));
            Assert.That(result.Diagnostic, Is.EqualTo(diagnostic));
        });
    }

    [Test]
    public void ExecutionResultParserFailedFactoryClearsValueAndKeepsDiagnosticTest()
    {
        var diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.Parser,
            JavaScriptRuntimeExecutionDiagnosticCodes.ParserStageFailed,
            "parser failed");

        var result = JavaScriptRuntimeExecutionResult.ParserFailed(
            sessionEpoch: 6,
            operation: JavaScriptRuntimeExecutionOperationKind.Execute,
            diagnostic: diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(result.SessionEpoch, Is.EqualTo(6));
            Assert.That(result.Operation, Is.EqualTo(JavaScriptRuntimeExecutionOperationKind.Execute));
            Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.ParserFailed));
            Assert.That(result.Value, Is.EqualTo(JavaScriptRuntimeValue.Null));
            Assert.That(result.Diagnostic, Is.EqualTo(diagnostic));
        });
    }

    [Test]
    public void ExecutionResultLoweringFailedFactoryClearsValueAndKeepsDiagnosticTest()
    {
        var diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.Lowering,
            JavaScriptRuntimeExecutionDiagnosticCodes.LoweringStageFailed,
            "lowering failed");

        var result = JavaScriptRuntimeExecutionResult.LoweringFailed(
            sessionEpoch: 7,
            operation: JavaScriptRuntimeExecutionOperationKind.Evaluate,
            diagnostic: diagnostic);

        Assert.Multiple(() =>
        {
            Assert.That(result.SessionEpoch, Is.EqualTo(7));
            Assert.That(result.Operation, Is.EqualTo(JavaScriptRuntimeExecutionOperationKind.Evaluate));
            Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.LoweringFailed));
            Assert.That(result.Value, Is.EqualTo(JavaScriptRuntimeValue.Null));
            Assert.That(result.Diagnostic, Is.EqualTo(diagnostic));
        });
    }
}