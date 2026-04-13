namespace Atom.Compilers.JavaScript.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptRuntimeExecutionResultPolicyTests
{
    [Test]
    public void ExecutionResultPolicyPreservesCompletedResultForExtendedSpecificationTest()
    {
        var result = new JavaScriptRuntimeExecutionResult(
            SessionEpoch: 1,
            Operation: JavaScriptRuntimeExecutionOperationKind.Execute,
            Status: JavaScriptRuntimeExecutionStatus.Completed,
            Value: JavaScriptRuntimeValue.FromHostObject(new object()),
            Diagnostic: null);

        var normalized = JavaScriptRuntimeExecutionResultPolicy.Apply(result, JavaScriptRuntimeSpecification.Extended);

        Assert.That(normalized, Is.EqualTo(result));
    }

    [TestCase(nameof(JavaScriptRuntimeValueKind.HostObject))]
    [TestCase(nameof(JavaScriptRuntimeValueKind.InternalError))]
    [TestCase(nameof(JavaScriptRuntimeValueKind.StackOverflowError))]
    [TestCase(nameof(JavaScriptRuntimeValueKind.TimeoutError))]
    [TestCase(nameof(JavaScriptRuntimeValueKind.MemoryLimitError))]
    [TestCase(nameof(JavaScriptRuntimeValueKind.CancellationError))]
    [TestCase(nameof(JavaScriptRuntimeValueKind.HostInteropError))]
    [TestCase(nameof(JavaScriptRuntimeValueKind.ResourceExhaustedError))]
    public void ExecutionResultPolicyConvertsExtendedOnlyKindsToSpecificationViolationForEcmaScriptTest(string valueKindName)
    {
        var valueKind = Enum.Parse<JavaScriptRuntimeValueKind>(valueKindName);

        var result = new JavaScriptRuntimeExecutionResult(
            SessionEpoch: 3,
            Operation: JavaScriptRuntimeExecutionOperationKind.Evaluate,
            Status: JavaScriptRuntimeExecutionStatus.Completed,
            Value: CreateValue(valueKind),
            Diagnostic: null);

        var normalized = JavaScriptRuntimeExecutionResultPolicy.Apply(result, JavaScriptRuntimeSpecification.ECMAScript);

        Assert.Multiple(() =>
        {
            Assert.That(normalized.SessionEpoch, Is.EqualTo(3));
            Assert.That(normalized.Operation, Is.EqualTo(JavaScriptRuntimeExecutionOperationKind.Evaluate));
            Assert.That(normalized.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.SpecificationViolation));
            Assert.That(normalized.Value, Is.EqualTo(JavaScriptRuntimeValue.Null));
            Assert.That(normalized.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.Execution,
                JavaScriptRuntimeExecutionDiagnosticCodes.StrictValueKindUnsupported,
                $"JavaScript runtime value kind '{valueKind}' is not available when JavaScriptRuntimeSpecification.ECMAScript is selected.")));
        });
    }

    private static JavaScriptRuntimeValue CreateValue(JavaScriptRuntimeValueKind valueKind)
        => valueKind switch
        {
            JavaScriptRuntimeValueKind.HostObject => JavaScriptRuntimeValue.FromHostObject(new object()),
            JavaScriptRuntimeValueKind.InternalError => JavaScriptRuntimeValue.FromInternalError(new JavaScriptRuntimeInternalError("internal")),
            JavaScriptRuntimeValueKind.StackOverflowError => JavaScriptRuntimeValue.FromStackOverflowError(new JavaScriptRuntimeStackOverflowError("stack", 1)),
            JavaScriptRuntimeValueKind.TimeoutError => JavaScriptRuntimeValue.FromTimeoutError(new JavaScriptRuntimeTimeoutError("timeout", 1)),
            JavaScriptRuntimeValueKind.MemoryLimitError => JavaScriptRuntimeValue.FromMemoryLimitError(new JavaScriptRuntimeMemoryLimitError("memory", 1)),
            JavaScriptRuntimeValueKind.CancellationError => JavaScriptRuntimeValue.FromCancellationError(new JavaScriptRuntimeCancellationError("cancel", true)),
            JavaScriptRuntimeValueKind.HostInteropError => JavaScriptRuntimeValue.FromHostInteropError(new JavaScriptRuntimeHostInteropError("interop", "member")),
            JavaScriptRuntimeValueKind.ResourceExhaustedError => JavaScriptRuntimeValue.FromResourceExhaustedError(new JavaScriptRuntimeResourceExhaustedError("resource", "pool")),
            _ => throw new ArgumentOutOfRangeException(nameof(valueKind)),
        };
}