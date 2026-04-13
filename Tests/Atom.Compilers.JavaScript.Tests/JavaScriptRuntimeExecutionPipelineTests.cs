using System.Collections.Immutable;

namespace Atom.Compilers.JavaScript.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptRuntimeExecutionPipelineTests
{
    [Test]
    public void RuntimeEmptyExecuteFreezesRegistrationsTest()
    {
        var runtime = new JavaScriptRuntime();
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptObject",
                Members: ImmutableArray.Create(new JavaScriptGeneratedMemberMetadata("HostBridge", JavaScriptGeneratedMemberKind.Class, ExportName: "host"))));

        runtime.Register("host", metadata);

        var result = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(runtime.IsRunning, Is.True);
            Assert.That(runtime.CanRegister, Is.False);
            Assert.That(runtime.HasExecutionState, Is.True);
            Assert.That(runtime.HasFrozenRegistrations, Is.True);
            Assert.That(runtime.CurrentExecutionState.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.Extended));
            Assert.That(runtime.CurrentExecutionState.SessionEpoch, Is.EqualTo(1));
            Assert.That(runtime.CurrentExecutionState.Registrations, Is.EqualTo(runtime.FrozenRegistrations));
            Assert.That(runtime.FrozenRegistrations, Is.EqualTo(runtime.Registrations));
        });
    }

    [Test]
    public void RuntimeExecutionStateRetainsEcmaScriptSpecificationTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.IsRunning, Is.True);
            Assert.That(runtime.HasExecutionState, Is.True);
            Assert.That(runtime.CurrentExecutionState.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.ECMAScript));
        });
    }

    [Test]
    public void RuntimeResetStatePreservesFrozenRegistrationsTest()
    {
        var runtime = new JavaScriptRuntime();
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptObject",
                Members: ImmutableArray.Create(new JavaScriptGeneratedMemberMetadata("HostBridge", JavaScriptGeneratedMemberKind.Class, ExportName: "host"))));

        runtime.Register("host", metadata);
        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        runtime.ResetState();

        Assert.Multiple(() =>
        {
            Assert.That(runtime.IsRunning, Is.True);
            Assert.That(runtime.FrozenRegistrations.Length, Is.EqualTo(1));
            Assert.That(runtime.FrozenRegistrations[0].RegistrationName, Is.EqualTo("host"));
            Assert.That(runtime.CurrentExecutionState.Registrations, Is.EqualTo(runtime.FrozenRegistrations));
            Assert.That(runtime.CurrentExecutionState.SessionEpoch, Is.EqualTo(2));
            Assert.That(runtime.ResetCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void RuntimeRejectsRegistrationsAfterExecutionTransitionTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Register<object>(),
            Throws.InvalidOperationException.With.Message.EqualTo("Registration is only allowed before the first script execution."));
    }

    [Test]
    public void RuntimeRepeatedExecuteDoesNotRepeatBootstrapTest()
    {
        var runtime = new JavaScriptRuntime();
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptObject",
                Members: ImmutableArray.Create(new JavaScriptGeneratedMemberMetadata("HostBridge", JavaScriptGeneratedMemberKind.Class, ExportName: "host"))));

        runtime.Register("host", metadata);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Execute("host.reset();".AsSpan()),
            Throws.TypeOf<NotSupportedException>());

        Assert.Multiple(() =>
        {
            Assert.That(runtime.ExecutionBootstrapCount, Is.EqualTo(1));
            Assert.That(runtime.CurrentExecutionState.Registrations, Is.EqualTo(runtime.FrozenRegistrations));
            Assert.That(runtime.CurrentSessionEpoch, Is.EqualTo(1));
            Assert.That(runtime.FrozenRegistrations.Length, Is.EqualTo(1));
        });
    }

    [Test]
    public void RuntimeResetStateAdvancesSessionEpochWithoutRebootstrapTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);
        runtime.ResetState();
        runtime.ResetState();

        Assert.Multiple(() =>
        {
            Assert.That(runtime.ExecutionBootstrapCount, Is.EqualTo(1));
            Assert.That(runtime.HasExecutionState, Is.True);
            Assert.That(runtime.ResetCount, Is.EqualTo(2));
            Assert.That(runtime.CurrentExecutionState.SessionEpoch, Is.EqualTo(3));
            Assert.That(runtime.CurrentSessionEpoch, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task RuntimeExecuteAsyncSynchronousPolicyCompletesOnFastPathTest()
    {
        var runtime = new JavaScriptRuntime();

        var operation = runtime.ExecuteAsync(ReadOnlyMemory<char>.Empty);
        var result = await operation;

        Assert.Multiple(() =>
        {
            Assert.That(operation.IsCompletedSuccessfully, Is.True);
            Assert.That(result, Is.Null);
            Assert.That(runtime.IsRunning, Is.True);
            Assert.That(runtime.ExecutionBootstrapCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RuntimeEcmaScriptExecuteAsyncSynchronousPolicyRetainsSpecificationTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        var result = await runtime.ExecuteAsync(ReadOnlyMemory<char>.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(runtime.IsRunning, Is.True);
            Assert.That(runtime.CurrentExecutionState.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.ECMAScript));
        });
    }

    [Test]
    public async Task RuntimeExecuteAsyncWorkerThreadPolicyBuildsExecutionStateTest()
    {
        var runtime = new JavaScriptRuntime();

        var result = await runtime.ExecuteAsyncCore(
            ReadOnlyMemory<char>.Empty,
            JavaScriptRuntime.AsyncDispatchMode.WorkerThread);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(runtime.IsRunning, Is.True);
            Assert.That(runtime.HasExecutionState, Is.True);
            Assert.That(runtime.ExecutionBootstrapCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void RuntimeExecuteAsyncHonorsPreCanceledTokenTest()
    {
        var runtime = new JavaScriptRuntime();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await runtime.ExecuteAsyncCore(
                ReadOnlyMemory<char>.Empty,
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread,
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task RuntimeEvaluateAsyncReturnsTypedDefaultForEmptySourceTest()
    {
        var runtime = new JavaScriptRuntime();

        var result = await runtime.EvaluateAsyncCore<string>(
            ReadOnlyMemory<char>.Empty,
            JavaScriptRuntime.AsyncDispatchMode.Synchronous);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void RuntimeBuildsSessionTablesFromFrozenRegistrationsTest()
    {
        var runtime = new JavaScriptRuntime();
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptObject",
                Members: ImmutableArray.Create(
                    new JavaScriptGeneratedMemberMetadata("Connect", JavaScriptGeneratedMemberKind.Method, ExportName: "connect"),
                    new JavaScriptGeneratedMemberMetadata("Disconnect", JavaScriptGeneratedMemberKind.Method, ExportName: "disconnect"))),
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostDictionary",
                Generator: "JavaScriptDictionary",
                Members: ImmutableArray.Create(
                    new JavaScriptGeneratedMemberMetadata("Item", JavaScriptGeneratedMemberKind.Property, ExportName: "item"))));

        runtime.Register("host", metadata);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.CurrentExecutionState.Tables.IsInitialized, Is.True);
            Assert.That(runtime.CurrentExecutionState.BindingTables.IsInitialized, Is.True);
            Assert.That(runtime.CurrentExecutionState.LookupCache.IsInitialized, Is.True);
            Assert.That(runtime.CurrentExecutionState.BindingPlanCache.IsInitialized, Is.True);
            Assert.That(runtime.CurrentExecutionState.MarshallingPlanCache.IsInitialized, Is.True);
            Assert.That(runtime.CurrentExecutionState.Tables.TotalTypeCount, Is.EqualTo(2));
            Assert.That(runtime.CurrentExecutionState.Tables.TotalMemberCount, Is.EqualTo(3));
            Assert.That(runtime.CurrentExecutionState.Tables.Registrations.Length, Is.EqualTo(1));
            Assert.That(runtime.CurrentExecutionState.Tables.Registrations[0], Is.EqualTo(new JavaScriptRuntimeSessionTableEntry("host", 0, 2, 0, 3)));
            Assert.That(runtime.CurrentExecutionState.BindingTables.Types.Length, Is.EqualTo(2));
            Assert.That(runtime.CurrentExecutionState.BindingTables.Members.Length, Is.EqualTo(3));
            Assert.That(runtime.CurrentExecutionState.BindingTables.Types[0], Is.EqualTo(new JavaScriptRuntimeTypeBindingTableEntry(0, "HostBridge", "JavaScriptObject", 0, 2, JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder)));
            Assert.That(runtime.CurrentExecutionState.BindingTables.Types[1], Is.EqualTo(new JavaScriptRuntimeTypeBindingTableEntry(0, "HostDictionary", "JavaScriptDictionary", 2, 1, JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder)));
            Assert.That(runtime.CurrentExecutionState.BindingTables.Members[0], Is.EqualTo(new JavaScriptRuntimeMemberBindingTableEntry(0, "Connect", "connect", JavaScriptGeneratedMemberKind.Method, JavaScriptRuntimeMemberAttributes.None)));
            Assert.That(runtime.CurrentExecutionState.BindingTables.Members[2], Is.EqualTo(new JavaScriptRuntimeMemberBindingTableEntry(1, "Item", "item", JavaScriptGeneratedMemberKind.Property, JavaScriptRuntimeMemberAttributes.None)));
            Assert.That(runtime.CurrentExecutionState.LookupCache.RegistrationIndexes!["host"], Is.Zero);
            Assert.That(runtime.CurrentExecutionState.LookupCache.TypeIndexes![("host", "HostBridge")], Is.Zero);
            Assert.That(runtime.CurrentExecutionState.LookupCache.TypeIndexes![("host", "HostDictionary")], Is.EqualTo(1));
            Assert.That(runtime.CurrentExecutionState.LookupCache.MemberIndexes![("host", "HostBridge", "connect")], Is.Zero);
            Assert.That(runtime.CurrentExecutionState.LookupCache.MemberIndexes![("host", "HostDictionary", "item")], Is.EqualTo(2));
            Assert.That(runtime.CurrentExecutionState.BindingPlanCache.MemberPlansByMemberIndex[0], Is.EqualTo(new JavaScriptRuntimeBindingPlan(0, 0, 0, JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder, JavaScriptGeneratedMemberKind.Method, JavaScriptRuntimeMemberAttributes.None)));
            Assert.That(runtime.CurrentExecutionState.MarshallingPlanCache.MemberPlansByMemberIndex[2], Is.EqualTo(new JavaScriptRuntimeMarshallingPlan(0, 1, 2, JavaScriptRuntimeMarshallingChannel.PropertyAccess, false, false, false, false)));
        });
    }

    [Test]
    public void RuntimeResetStatePreservesEcmaScriptSpecificationTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);
        runtime.ResetState();

        Assert.Multiple(() =>
        {
            Assert.That(runtime.IsRunning, Is.True);
            Assert.That(runtime.CurrentExecutionState.SessionEpoch, Is.EqualTo(2));
            Assert.That(runtime.CurrentExecutionState.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.ECMAScript));
        });
    }

    [Test]
    public void RuntimeProjectsStrictValuePolicyViolationsAsDiagnosticsTest()
    {
        var result = JavaScriptRuntimeExecutionResultPolicy.Apply(
            new JavaScriptRuntimeExecutionResult(
                SessionEpoch: 1,
                Operation: JavaScriptRuntimeExecutionOperationKind.Execute,
                Status: JavaScriptRuntimeExecutionStatus.Completed,
                Value: JavaScriptRuntimeValue.FromHostObject(new object()),
                Diagnostic: null),
            JavaScriptRuntimeSpecification.ECMAScript);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.SpecificationViolation));
            Assert.That(result.Diagnostic?.Code, Is.EqualTo(JavaScriptRuntimeExecutionDiagnosticCodes.StrictValueKindUnsupported));
            Assert.That(result.Diagnostic?.Phase, Is.EqualTo(JavaScriptRuntimeExecutionPhaseKind.Execution));
        });
    }

    [Test]
    public void RuntimeFacadeProjectsParserFailedResultAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "1 + 1".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.ParserFailed(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Parser,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ParserStageFailed,
                            "JavaScript parser stage did not return a parsed source contract."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript parser stage did not return a parsed source contract."));
    }

    [Test]
    public void RuntimeFacadeProjectsLoweringFailedResultAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "1 + 1".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.LoweringFailed(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Lowering,
                            JavaScriptRuntimeExecutionDiagnosticCodes.LoweringStageFailed,
                            "JavaScript lowering stage did not return a lowered program contract."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript lowering stage did not return a lowered program contract."));
    }

    [Test]
    public void RuntimeFacadeProjectsSpecificationViolationResultAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "1 + 1".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.SpecificationViolation(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Execution,
                            JavaScriptRuntimeExecutionDiagnosticCodes.StrictValueKindUnsupported,
                            "JavaScript runtime value kind 'HostObject' is not available when JavaScriptRuntimeSpecification.ECMAScript is selected."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript runtime value kind 'HostObject' is not available when JavaScriptRuntimeSpecification.ECMAScript is selected."));
    }

    [Test]
    public void RuntimeFacadeProjectsParserFeatureSpecificationViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "host.connect();".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.SpecificationViolation(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Parser,
                            JavaScriptRuntimeExecutionDiagnosticCodes.StrictParserFeatureUnsupported,
                            "JavaScript parser feature 'host.' requires JavaScriptRuntimeSpecification.Extended."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript parser feature 'host.' requires JavaScriptRuntimeSpecification.Extended."));
    }

    [Test]
    public void RuntimeFacadeProjectsLoweringPolicySpecificationViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "1 + 1".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.SpecificationViolation(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Lowering,
                            JavaScriptRuntimeExecutionDiagnosticCodes.StrictLoweringPolicyUnsupported,
                            "JavaScript lowering policy 'AllowsExtendedRuntimeSurface' requires JavaScriptRuntimeSpecification.Extended."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript lowering policy 'AllowsExtendedRuntimeSurface' requires JavaScriptRuntimeSpecification.Extended."));
    }

    [Test]
    public void RuntimeFacadeProjectsMutationLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "target[index] = value + 1;".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.MutationLoweringUnavailable,
                            "JavaScript mutation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript mutation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeEvaluateFacadeProjectsIndexAccessLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.EvaluateWithRunner<string>(
                "target[index];".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.IndexAccessLoweringUnavailable,
                            "JavaScript index access lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript index access lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeFacadeProjectsInvocationLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "invoke();".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.InvocationLoweringUnavailable,
                            "JavaScript invocation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript invocation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeEvaluateFacadeProjectsLiteralMaterializationUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.EvaluateWithRunner<string>(
                "'x'".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.LiteralMaterializationUnavailable,
                            "JavaScript literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeFacadeProjectsOperatorLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "1 + 1".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.OperatorLoweringUnavailable,
                            "JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeEvaluateFacadeProjectsOperatorLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.EvaluateWithRunner<int>(
                "1 + 1".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.OperatorLoweringUnavailable,
                            "JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeFacadeProjectsClosureLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "value => value".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ClosureLoweringUnavailable,
                            "JavaScript closure lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript closure lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeEvaluateFacadeProjectsTemplateMaterializationUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.EvaluateWithRunner<string>(
                "`x`".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.TemplateMaterializationUnavailable,
                            "JavaScript template materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript template materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeFacadeProjectsShortCircuitLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "value?.member ?? fallback".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ShortCircuitLoweringUnavailable,
                            "JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeEvaluateFacadeProjectsShortCircuitLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.EvaluateWithRunner<string>(
                "value?.member ?? fallback".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ShortCircuitLoweringUnavailable,
                            "JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeFacadeProjectsAggregateLiteralMaterializationUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "[]".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.AggregateLiteralMaterializationUnavailable,
                            "JavaScript aggregate literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript aggregate literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeEvaluateFacadeProjectsSpreadLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.EvaluateWithRunner<string>(
                "...items".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.SpreadLoweringUnavailable,
                            "JavaScript spread lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript spread lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeFacadeProjectsConditionalLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "condition ? left : right".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ConditionalLoweringUnavailable,
                            "JavaScript conditional lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript conditional lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeFacadeProjectsDestructuringLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "const { value } = source".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.DestructuringLoweringUnavailable,
                            "JavaScript destructuring lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript destructuring lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeFacadeProjectsStrictValuePolicyViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.ExecuteWithRunner(
                "1 + 1".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.Completed(
                        state.SessionEpoch,
                        operation,
                        JavaScriptRuntimeValue.FromHostObject(new object()))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript runtime value kind 'HostObject' is not available when JavaScriptRuntimeSpecification.ECMAScript is selected."));
    }

    [Test]
    public void RuntimeEvaluateFacadeProjectsParserFailedResultAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            () => runtime.EvaluateWithRunner<string>(
                "1 + 1".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.ParserFailed(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Parser,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ParserStageFailed,
                            "JavaScript parser stage did not return a parsed source contract."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript parser stage did not return a parsed source contract."));
    }

    [Test]
    public void RuntimeEvaluateFacadeProjectsLoweringFailedResultAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            () => runtime.EvaluateWithRunner<string>(
                "1 + 1".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.LoweringFailed(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Lowering,
                            JavaScriptRuntimeExecutionDiagnosticCodes.LoweringStageFailed,
                            "JavaScript lowering stage did not return a lowered program contract."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript lowering stage did not return a lowered program contract."));
    }

    [Test]
    public void RuntimeEvaluateFacadeProjectsSpecificationViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.EvaluateWithRunner<string>(
                "1 + 1".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.Completed(
                        state.SessionEpoch,
                        operation,
                        JavaScriptRuntimeValue.FromHostObject(new object()))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript runtime value kind 'HostObject' is not available when JavaScriptRuntimeSpecification.ECMAScript is selected."));
    }

    [Test]
    public void RuntimeEvaluateFacadeProjectsParserFeatureSpecificationViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.EvaluateWithRunner<string>(
                "host.connect();".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.SpecificationViolation(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Parser,
                            JavaScriptRuntimeExecutionDiagnosticCodes.StrictParserFeatureUnsupported,
                            "JavaScript parser feature 'host.' requires JavaScriptRuntimeSpecification.Extended."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript parser feature 'host.' requires JavaScriptRuntimeSpecification.Extended."));
    }

    [Test]
    public void RuntimeEvaluateFacadeProjectsLoweringPolicySpecificationViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            () => runtime.EvaluateWithRunner<string>(
                "1 + 1".AsSpan(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.SpecificationViolation(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Lowering,
                            JavaScriptRuntimeExecutionDiagnosticCodes.StrictLoweringPolicyUnsupported,
                            "JavaScript lowering policy 'AllowsExtendedRuntimeSurface' requires JavaScriptRuntimeSpecification.Extended."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript lowering policy 'AllowsExtendedRuntimeSurface' requires JavaScriptRuntimeSpecification.Extended."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncFacadeProjectsParserFailedResultAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.ParserFailed(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Parser,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ParserStageFailed,
                            "JavaScript parser stage did not return a parsed source contract."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript parser stage did not return a parsed source contract."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncFacadeProjectsLoweringFailedResultAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.LoweringFailed(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Lowering,
                            JavaScriptRuntimeExecutionDiagnosticCodes.LoweringStageFailed,
                            "JavaScript lowering stage did not return a lowered program contract."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript lowering stage did not return a lowered program contract."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncFacadeProjectsSpecificationViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.Completed(
                        state.SessionEpoch,
                        operation,
                        JavaScriptRuntimeValue.FromHostObject(new object()))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript runtime value kind 'HostObject' is not available when JavaScriptRuntimeSpecification.ECMAScript is selected."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncFacadeProjectsParserFeatureSpecificationViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "host.connect();".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.SpecificationViolation(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Parser,
                            JavaScriptRuntimeExecutionDiagnosticCodes.StrictParserFeatureUnsupported,
                            "JavaScript parser feature 'host.' requires JavaScriptRuntimeSpecification.Extended."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript parser feature 'host.' requires JavaScriptRuntimeSpecification.Extended."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncFacadeProjectsLoweringPolicySpecificationViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.SpecificationViolation(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Lowering,
                            JavaScriptRuntimeExecutionDiagnosticCodes.StrictLoweringPolicyUnsupported,
                            "JavaScript lowering policy 'AllowsExtendedRuntimeSurface' requires JavaScriptRuntimeSpecification.Extended."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript lowering policy 'AllowsExtendedRuntimeSurface' requires JavaScriptRuntimeSpecification.Extended."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncFacadeProjectsMutationLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "target[index] = value + 1;".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.MutationLoweringUnavailable,
                            "JavaScript mutation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript mutation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncFacadeProjectsIndexAccessLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "target[index];".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.IndexAccessLoweringUnavailable,
                            "JavaScript index access lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript index access lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncFacadeProjectsInvocationLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "invoke();".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.InvocationLoweringUnavailable,
                            "JavaScript invocation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript invocation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncFacadeProjectsLiteralMaterializationUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "'x'".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.LiteralMaterializationUnavailable,
                            "JavaScript literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncFacadeProjectsOperatorLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.OperatorLoweringUnavailable,
                            "JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncFacadeProjectsOperatorLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<int>(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.OperatorLoweringUnavailable,
                            "JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncFacadeProjectsClosureLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "value => value".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ClosureLoweringUnavailable,
                            "JavaScript closure lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript closure lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncFacadeProjectsTemplateMaterializationUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "`x`".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.TemplateMaterializationUnavailable,
                            "JavaScript template materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript template materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncFacadeProjectsShortCircuitLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "value?.member ?? fallback".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ShortCircuitLoweringUnavailable,
                            "JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncFacadeProjectsShortCircuitLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "value?.member ?? fallback".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ShortCircuitLoweringUnavailable,
                            "JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncFacadeProjectsAggregateLiteralMaterializationUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "[]".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.AggregateLiteralMaterializationUnavailable,
                            "JavaScript aggregate literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript aggregate literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncFacadeProjectsSpreadLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "...items".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.SpreadLoweringUnavailable,
                            "JavaScript spread lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript spread lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncFacadeProjectsConditionalLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "condition ? left : right".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ConditionalLoweringUnavailable,
                            "JavaScript conditional lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript conditional lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncFacadeProjectsDestructuringLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "const { value } = source".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.DestructuringLoweringUnavailable,
                            "JavaScript destructuring lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."))),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript destructuring lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncWorkerThreadFacadeProjectsParserFailedResultAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.ParserFailed(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Parser,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ParserStageFailed,
                            "JavaScript parser stage did not return a parsed source contract.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript parser stage did not return a parsed source contract."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncWorkerThreadFacadeProjectsParserFailedResultAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.ParserFailed(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Parser,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ParserStageFailed,
                            "JavaScript parser stage did not return a parsed source contract.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript parser stage did not return a parsed source contract."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncWorkerThreadFacadeProjectsSpecificationViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.Completed(
                        state.SessionEpoch,
                        operation,
                        JavaScriptRuntimeValue.FromHostObject(new object())),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript runtime value kind 'HostObject' is not available when JavaScriptRuntimeSpecification.ECMAScript is selected."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncWorkerThreadFacadeProjectsParserFeatureSpecificationViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "host.connect();".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.SpecificationViolation(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Parser,
                            JavaScriptRuntimeExecutionDiagnosticCodes.StrictParserFeatureUnsupported,
                            "JavaScript parser feature 'host.' requires JavaScriptRuntimeSpecification.Extended.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript parser feature 'host.' requires JavaScriptRuntimeSpecification.Extended."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncWorkerThreadFacadeProjectsLoweringPolicySpecificationViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.SpecificationViolation(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Lowering,
                            JavaScriptRuntimeExecutionDiagnosticCodes.StrictLoweringPolicyUnsupported,
                            "JavaScript lowering policy 'AllowsExtendedRuntimeSurface' requires JavaScriptRuntimeSpecification.Extended.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript lowering policy 'AllowsExtendedRuntimeSurface' requires JavaScriptRuntimeSpecification.Extended."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncWorkerThreadFacadeProjectsMutationLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "target[index] = value + 1;".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.MutationLoweringUnavailable,
                            "JavaScript mutation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript mutation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncWorkerThreadFacadeProjectsIndexAccessLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "target[index];".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.IndexAccessLoweringUnavailable,
                            "JavaScript index access lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript index access lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncWorkerThreadFacadeProjectsInvocationLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "invoke();".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.InvocationLoweringUnavailable,
                            "JavaScript invocation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript invocation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncWorkerThreadFacadeProjectsLiteralMaterializationUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "'x'".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.LiteralMaterializationUnavailable,
                            "JavaScript literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncWorkerThreadFacadeProjectsOperatorLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.OperatorLoweringUnavailable,
                            "JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncWorkerThreadFacadeProjectsOperatorLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<int>(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.OperatorLoweringUnavailable,
                            "JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncWorkerThreadFacadeProjectsClosureLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "value => value".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ClosureLoweringUnavailable,
                            "JavaScript closure lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript closure lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncWorkerThreadFacadeProjectsTemplateMaterializationUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "`x`".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.TemplateMaterializationUnavailable,
                            "JavaScript template materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript template materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncWorkerThreadFacadeProjectsShortCircuitLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "value?.member ?? fallback".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ShortCircuitLoweringUnavailable,
                            "JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncWorkerThreadFacadeProjectsShortCircuitLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "value?.member ?? fallback".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ShortCircuitLoweringUnavailable,
                            "JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncWorkerThreadFacadeProjectsAggregateLiteralMaterializationUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "[]".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.AggregateLiteralMaterializationUnavailable,
                            "JavaScript aggregate literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript aggregate literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncWorkerThreadFacadeProjectsSpreadLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "...items".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.SpreadLoweringUnavailable,
                            "JavaScript spread lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript spread lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncWorkerThreadFacadeProjectsConditionalLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "condition ? left : right".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.ConditionalLoweringUnavailable,
                            "JavaScript conditional lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript conditional lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncWorkerThreadFacadeProjectsDestructuringLoweringUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "const { value } = source".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.DestructuringLoweringUnavailable,
                            "JavaScript destructuring lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript destructuring lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncWorkerThreadFacadeProjectsRegularExpressionMaterializationUnavailableAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                @"/\d+/g".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.EngineUnavailable(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                            JavaScriptRuntimeExecutionDiagnosticCodes.RegularExpressionMaterializationUnavailable,
                            "JavaScript regular expression materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript regular expression materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public async Task RuntimeEvaluateAsyncWorkerThreadFacadeProjectsLoweringFailedResultAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            async () => await runtime.EvaluateAsyncWithRunner<string>(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.LoweringFailed(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Lowering,
                            JavaScriptRuntimeExecutionDiagnosticCodes.LoweringStageFailed,
                            "JavaScript lowering stage did not return a lowered program contract.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript lowering stage did not return a lowered program contract."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncWorkerThreadFacadeProjectsLoweringFailedResultAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.LoweringFailed(
                        state.SessionEpoch,
                        operation,
                        new JavaScriptRuntimeExecutionDiagnostic(
                            JavaScriptRuntimeExecutionPhaseKind.Lowering,
                            JavaScriptRuntimeExecutionDiagnosticCodes.LoweringStageFailed,
                            "JavaScript lowering stage did not return a lowered program contract.")),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript lowering stage did not return a lowered program contract."));
    }

    [Test]
    public async Task RuntimeExecuteAsyncWorkerThreadFacadeProjectsSpecificationViolationAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        Assert.That(
            async () => await runtime.ExecuteAsyncWithRunner(
                "1 + 1".AsMemory(),
                static (JavaScriptRuntimeExecutionState state, ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
                    => JavaScriptRuntimeExecutionResult.Completed(
                        state.SessionEpoch,
                        operation,
                        JavaScriptRuntimeValue.FromHostObject(new object())),
                JavaScriptRuntime.AsyncDispatchMode.WorkerThread),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript runtime value kind 'HostObject' is not available when JavaScriptRuntimeSpecification.ECMAScript is selected."));
    }

    [Test]
    public void RuntimeResolvesHostBindingTargetsFromLookupCacheTest()
    {
        var runtime = new JavaScriptRuntime();
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptObject",
                Members: ImmutableArray.Create(
                    new JavaScriptGeneratedMemberMetadata("Connect", JavaScriptGeneratedMemberKind.Method, ExportName: "connect"),
                    new JavaScriptGeneratedMemberMetadata("Disconnect", JavaScriptGeneratedMemberKind.Method, ExportName: "disconnect"))),
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostDictionary",
                Generator: "JavaScriptDictionary",
                Members: ImmutableArray.Create(
                    new JavaScriptGeneratedMemberMetadata("Item", JavaScriptGeneratedMemberKind.Property, ExportName: "item"))));

        runtime.Register("host", metadata);
        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var registrationResolved = runtime.TryResolveRegistration("host", out var registration);
        var typeResolved = runtime.TryResolveType("host", "HostDictionary", out var type);
        var memberResolved = runtime.TryResolveMember("host", "HostBridge", "disconnect", out var member);
        var planResolved = runtime.TryResolveBindingPlan("host", "HostBridge", "disconnect", out var plan);
        var marshallingResolved = runtime.TryResolveMarshallingPlan("host", "HostDictionary", "item", out var marshallingPlan);
        var missingMemberResolved = runtime.TryResolveMember("host", "HostBridge", "missing", out _);

        Assert.Multiple(() =>
        {
            Assert.That(registrationResolved, Is.True);
            Assert.That(registration, Is.EqualTo(new JavaScriptRuntimeSessionTableEntry("host", 0, 2, 0, 3)));
            Assert.That(typeResolved, Is.True);
            Assert.That(type, Is.EqualTo(new JavaScriptRuntimeTypeBindingTableEntry(0, "HostDictionary", "JavaScriptDictionary", 2, 1, JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder)));
            Assert.That(memberResolved, Is.True);
            Assert.That(member, Is.EqualTo(new JavaScriptRuntimeMemberBindingTableEntry(0, "Disconnect", "disconnect", JavaScriptGeneratedMemberKind.Method, JavaScriptRuntimeMemberAttributes.None)));
            Assert.That(planResolved, Is.True);
            Assert.That(plan, Is.EqualTo(new JavaScriptRuntimeBindingPlan(0, 0, 1, JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder, JavaScriptGeneratedMemberKind.Method, JavaScriptRuntimeMemberAttributes.None)));
            Assert.That(marshallingResolved, Is.True);
            Assert.That(marshallingPlan, Is.EqualTo(new JavaScriptRuntimeMarshallingPlan(0, 1, 2, JavaScriptRuntimeMarshallingChannel.PropertyAccess, false, false, false, false)));
            Assert.That(missingMemberResolved, Is.False);
        });
    }

    [Test]
    public void RuntimeKeepsEmptyExportGraphDeterministicTest()
    {
        var runtime = new JavaScriptRuntime();
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "IgnoredBridge",
                Generator: "JavaScriptIgnore",
                Members: ImmutableArray.Create(
                    new JavaScriptGeneratedMemberMetadata("IgnoredBridge", JavaScriptGeneratedMemberKind.Class),
                    new JavaScriptGeneratedMemberMetadata("Hidden", JavaScriptGeneratedMemberKind.Method))));

        runtime.Register("ignored", metadata);
        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var memberResolved = runtime.TryResolveMember("ignored", "IgnoredBridge", "Hidden", out _);
        var bindingPlanResolved = runtime.TryResolveBindingPlan("ignored", "IgnoredBridge", "Hidden", out _);
        var marshallingPlanResolved = runtime.TryResolveMarshallingPlan("ignored", "IgnoredBridge", "Hidden", out _);
        var dispatchResolved = runtime.TryResolveDispatchTarget("ignored", "IgnoredBridge", "Hidden", out _);
        var invocationResolved = runtime.TryPrepareInvocation("ignored", "IgnoredBridge", "Hidden", out _);
        var engineEntryResolved = runtime.TryPrepareEngineEntry("ignored", "IgnoredBridge", "Hidden", out _);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.CurrentExecutionState.LookupCache.IsInitialized, Is.True);
            Assert.That(runtime.CurrentExecutionState.LookupCache.MemberIndexes, Is.Not.Null);
            Assert.That(runtime.CurrentExecutionState.LookupCache.MemberIndexes!.Count, Is.Zero);
            Assert.That(runtime.CurrentExecutionState.BindingPlanCache.IsInitialized, Is.True);
            Assert.That(runtime.CurrentExecutionState.BindingPlanCache.MemberPlansByMemberIndex.Length, Is.EqualTo(2));
            Assert.That(runtime.CurrentExecutionState.MarshallingPlanCache.IsInitialized, Is.True);
            Assert.That(runtime.CurrentExecutionState.MarshallingPlanCache.MemberPlansByMemberIndex.Length, Is.EqualTo(2));
            Assert.That(memberResolved, Is.False);
            Assert.That(bindingPlanResolved, Is.False);
            Assert.That(marshallingPlanResolved, Is.False);
            Assert.That(dispatchResolved, Is.False);
            Assert.That(invocationResolved, Is.False);
            Assert.That(engineEntryResolved, Is.False);
        });
    }

    [Test]
    public void RuntimeResolvesDispatchTargetFromBindingPlanCacheTest()
    {
        var runtime = new JavaScriptRuntime();
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptObject",
                Members: ImmutableArray.Create(
                    new JavaScriptGeneratedMemberMetadata("Connect", JavaScriptGeneratedMemberKind.Method, ExportName: "connect"),
                    new JavaScriptGeneratedMemberMetadata("Disconnect", JavaScriptGeneratedMemberKind.Method, ExportName: "disconnect"))));

        runtime.Register("host", metadata);
        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var resolved = runtime.TryResolveDispatchTarget("host", "HostBridge", "disconnect", out var target);

        runtime.ResetState();

        var resolvedAfterReset = runtime.TryResolveDispatchTarget("host", "HostBridge", "disconnect", out var targetAfterReset);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(target, Is.EqualTo(new JavaScriptRuntimeDispatchTarget(1, 0, 0, 1, JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder, JavaScriptGeneratedMemberKind.Method, JavaScriptRuntimeMemberAttributes.None)));
            Assert.That(resolvedAfterReset, Is.True);
            Assert.That(targetAfterReset, Is.EqualTo(new JavaScriptRuntimeDispatchTarget(2, 0, 0, 1, JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder, JavaScriptGeneratedMemberKind.Method, JavaScriptRuntimeMemberAttributes.None)));
        });
    }

    [Test]
    public void RuntimePreparesInvocationPlanFromDispatchAndMarshallingLayersTest()
    {
        var runtime = new JavaScriptRuntime();
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptObject",
                Members: ImmutableArray.Create(
                    new JavaScriptGeneratedMemberMetadata("Connect", JavaScriptGeneratedMemberKind.Method, ExportName: "connect"),
                    new JavaScriptGeneratedMemberMetadata("Disconnect", JavaScriptGeneratedMemberKind.Method, ExportName: "disconnect"))));

        runtime.Register("host", metadata);
        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var prepared = runtime.TryPrepareInvocation("host", "HostBridge", "connect", out var invocationPlan);

        runtime.ResetState();

        var preparedAfterReset = runtime.TryPrepareInvocation("host", "HostBridge", "connect", out var invocationPlanAfterReset);

        Assert.Multiple(() =>
        {
            Assert.That(prepared, Is.True);
            Assert.That(invocationPlan, Is.EqualTo(new JavaScriptRuntimeInvocationPlan(1, 0, 0, 0, JavaScriptRuntimeMarshallingChannel.MethodCall, JavaScriptGeneratedMemberKind.Method, JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder, JavaScriptRuntimeMemberAttributes.None)));
            Assert.That(preparedAfterReset, Is.True);
            Assert.That(invocationPlanAfterReset, Is.EqualTo(new JavaScriptRuntimeInvocationPlan(2, 0, 0, 0, JavaScriptRuntimeMarshallingChannel.MethodCall, JavaScriptGeneratedMemberKind.Method, JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder, JavaScriptRuntimeMemberAttributes.None)));
        });
    }

    [Test]
    public void RuntimePreparesEngineEntryFromInvocationPlanTest()
    {
        var runtime = new JavaScriptRuntime();
        var metadata = ImmutableArray.Create(
            new JavaScriptGeneratedTypeMetadata(
                EntityName: "HostBridge",
                Generator: "JavaScriptObject",
                Members: ImmutableArray.Create(
                    new JavaScriptGeneratedMemberMetadata("Connect", JavaScriptGeneratedMemberKind.Method, ExportName: "connect"))));

        runtime.Register("host", metadata);
        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var prepared = runtime.TryPrepareEngineEntry("host", "HostBridge", "connect", out var entry);

        runtime.ResetState();

        var preparedAfterReset = runtime.TryPrepareEngineEntry("host", "HostBridge", "connect", out var entryAfterReset);

        Assert.Multiple(() =>
        {
            Assert.That(prepared, Is.True);
            Assert.That(entry, Is.EqualTo(new JavaScriptRuntimeEngineEntry(1, 0, 0, 0, JavaScriptRuntimeMarshallingChannel.MethodCall, JavaScriptGeneratedMemberKind.Method, JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder, JavaScriptRuntimeMemberAttributes.None)));
            Assert.That(preparedAfterReset, Is.True);
            Assert.That(entryAfterReset, Is.EqualTo(new JavaScriptRuntimeEngineEntry(2, 0, 0, 0, JavaScriptRuntimeMarshallingChannel.MethodCall, JavaScriptGeneratedMemberKind.Method, JavaScriptRuntimeTypeAttributes.StringKeysOnly | JavaScriptRuntimeTypeAttributes.PreserveEnumerationOrder, JavaScriptRuntimeMemberAttributes.None)));
        });
    }
}