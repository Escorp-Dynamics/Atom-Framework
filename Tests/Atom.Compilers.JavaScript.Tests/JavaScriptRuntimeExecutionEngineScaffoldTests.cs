namespace Atom.Compilers.JavaScript.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptRuntimeExecutionEngineScaffoldTests
{
    [Test]
    public void ExecutionRequestCapturesCurrentSessionEpochAndSourceShapeTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);
        runtime.ResetState();

        var request = new JavaScriptRuntimeExecutionRequest(
            runtime.CurrentExecutionState,
            "host.connect();".AsSpan(),
            JavaScriptRuntimeExecutionOperationKind.Evaluate);

        Assert.That(request.SessionEpoch, Is.EqualTo(2));
        Assert.That(request.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.Extended));
        Assert.That(request.IsEmptySource, Is.False);
        Assert.That(request.Operation, Is.EqualTo(JavaScriptRuntimeExecutionOperationKind.Evaluate));
        Assert.That(request.Source.Length, Is.EqualTo("host.connect();".Length));
    }

    [Test]
    public void ExecutionRequestCapturesRuntimeSpecificationTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var request = new JavaScriptRuntimeExecutionRequest(
            runtime.CurrentExecutionState,
            "1 + 1".AsSpan(),
            JavaScriptRuntimeExecutionOperationKind.Execute);

        Assert.That(request.Specification, Is.EqualTo(JavaScriptRuntimeSpecification.ECMAScript));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsNullForEmptySourceTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                ReadOnlySpan<char>.Empty,
                JavaScriptRuntimeExecutionOperationKind.Execute));

        Assert.That(result.SessionEpoch, Is.EqualTo(1));
        Assert.That(result.Operation, Is.EqualTo(JavaScriptRuntimeExecutionOperationKind.Execute));
        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.Completed));
        Assert.That(result.Value, Is.EqualTo(JavaScriptRuntimeValue.Null));
        Assert.That(result.Diagnostic, Is.Null);
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsExtendedEngineUnavailableDiagnosticForIdentifierPathTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "value".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute));

        Assert.That(result.SessionEpoch, Is.EqualTo(1));
        Assert.That(result.Operation, Is.EqualTo(JavaScriptRuntimeExecutionOperationKind.Execute));
        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Value, Is.EqualTo(JavaScriptRuntimeValue.Null));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.ExtendedEngineUnavailable,
            "Extended runtime execution engine is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsEcmaScriptEngineUnavailableDiagnosticForIdentifierPathTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "value".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.EcmaScriptEngineUnavailable,
            "ECMAScript execution engine is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsOperatorLoweringUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "1 + 1".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.OperatorLoweringUnavailable,
            "JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsSpecificationViolationForStrictUnsupportedParserFeatureTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "host.connect();".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.SpecificationViolation));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.Parser,
            JavaScriptRuntimeExecutionDiagnosticCodes.StrictParserFeatureUnsupported,
            "JavaScript parser feature 'host.' requires JavaScriptRuntimeSpecification.Extended.")));
    }

    [Test]
    public void ExecutionEngineScaffoldCanMaterializeParserFailureResultTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "1 + 1".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute),
            tryParse: static (JavaScriptRuntimeExecutionRequest request, out JavaScriptRuntimeParsedSource parsedSource) =>
            {
                parsedSource = default;
                return false;
            },
            tryLower: static (JavaScriptRuntimeParsedSource parsedSource, out JavaScriptRuntimeLoweredProgram loweredProgram) =>
            {
                loweredProgram = default;
                return true;
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.ParserFailed));
            Assert.That(result.Value, Is.EqualTo(JavaScriptRuntimeValue.Null));
            Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.Parser,
                JavaScriptRuntimeExecutionDiagnosticCodes.ParserStageFailed,
                "JavaScript parser stage did not return a parsed source contract.")));
        });
    }

    [Test]
    public void ExecutionEngineScaffoldCanMaterializeLoweringFailureResultTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "1 + 1".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            tryParse: static (JavaScriptRuntimeExecutionRequest request, out JavaScriptRuntimeParsedSource parsedSource) =>
            {
                parsedSource = new JavaScriptRuntimeParsedSource(
                    request.SessionEpoch,
                    request.Specification,
                    JavaScriptRuntimeParserFeature.None,
                    request.Operation,
                    request.Source.Length);
                return true;
            },
            tryLower: static (JavaScriptRuntimeParsedSource parsedSource, out JavaScriptRuntimeLoweredProgram loweredProgram) =>
            {
                loweredProgram = default;
                return false;
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.LoweringFailed));
            Assert.That(result.Value, Is.EqualTo(JavaScriptRuntimeValue.Null));
            Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.Lowering,
                JavaScriptRuntimeExecutionDiagnosticCodes.LoweringStageFailed,
                "JavaScript lowering stage did not return a lowered program contract.")));
        });
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsSpecificationViolationForStrictUnsupportedLoweringPolicyTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "1 + 1".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate),
            tryParse: static (JavaScriptRuntimeExecutionRequest request, out JavaScriptRuntimeParsedSource parsedSource) =>
            {
                parsedSource = new JavaScriptRuntimeParsedSource(
                    request.SessionEpoch,
                    request.Specification,
                    JavaScriptRuntimeParserFeature.None,
                    request.Operation,
                    request.Source.Length);
                return true;
            },
            tryLower: static (JavaScriptRuntimeParsedSource parsedSource, out JavaScriptRuntimeLoweredProgram loweredProgram) =>
            {
                loweredProgram = new JavaScriptRuntimeLoweredProgram(
                    parsedSource.SessionEpoch,
                    parsedSource.Specification,
                    JavaScriptRuntimeLoweringPolicy.AllowsExtendedRuntimeSurface,
                    parsedSource.Operation,
                    parsedSource.SourceLength);
                return true;
            });

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.SpecificationViolation));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.Lowering,
            JavaScriptRuntimeExecutionDiagnosticCodes.StrictLoweringPolicyUnsupported,
            "JavaScript lowering policy 'AllowsExtendedRuntimeSurface' requires JavaScriptRuntimeSpecification.Extended.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsMutationLoweringUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "target[index] = value + 1;".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.MutationLoweringUnavailable,
            "JavaScript mutation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsIndexAccessLoweringUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "target[index];".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.IndexAccessLoweringUnavailable,
            "JavaScript index access lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsInvocationLoweringUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "invoke();".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.InvocationLoweringUnavailable,
            "JavaScript invocation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsLiteralMaterializationUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "42".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.LiteralMaterializationUnavailable,
            "JavaScript literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsClosureLoweringUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "value => value".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.ClosureLoweringUnavailable,
            "JavaScript closure lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsTemplateMaterializationUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "`x`".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.TemplateMaterializationUnavailable,
            "JavaScript template materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsShortCircuitLoweringUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "value?.member ?? fallback".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.ShortCircuitLoweringUnavailable,
            "JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsAggregateLiteralMaterializationUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "[]".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.AggregateLiteralMaterializationUnavailable,
            "JavaScript aggregate literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsSpreadLoweringUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "...items".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.SpreadLoweringUnavailable,
            "JavaScript spread lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsConditionalLoweringUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "condition ? left : right".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.ConditionalLoweringUnavailable,
            "JavaScript conditional lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsDestructuringLoweringUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                "const { value } = source".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Execute));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.DestructuringLoweringUnavailable,
            "JavaScript destructuring lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void ExecutionEngineScaffoldReturnsRegularExpressionMaterializationUnavailableDiagnosticTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        var result = JavaScriptRuntimeExecutionEngineScaffold.Run(
            new JavaScriptRuntimeExecutionRequest(
                runtime.CurrentExecutionState,
                @"/\d+/g".AsSpan(),
                JavaScriptRuntimeExecutionOperationKind.Evaluate));

        Assert.That(result.Status, Is.EqualTo(JavaScriptRuntimeExecutionStatus.EngineUnavailable));
        Assert.That(result.Diagnostic, Is.EqualTo(new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
            JavaScriptRuntimeExecutionDiagnosticCodes.RegularExpressionMaterializationUnavailable,
            "JavaScript regular expression materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.")));
    }

    [Test]
    public void RuntimeProjectsExtendedEngineUnavailableDiagnosticAsNotSupportedForIdentifierPathTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Execute("value".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("Extended runtime execution engine is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsEcmaScriptEngineUnavailableDiagnosticAsNotSupportedForIdentifierPathTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Execute("value".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("ECMAScript execution engine is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsOperatorLoweringUnavailableDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Evaluate<int>("1 + 1".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsStrictUnsupportedParserFeatureDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Execute("host.connect();".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript parser feature 'host.' requires JavaScriptRuntimeSpecification.Extended."));
    }

    [Test]
    public void RuntimeProjectsStrictUnsupportedLoweringPolicyDiagnosticAsNotSupportedTest()
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
    public void RuntimeProjectsMutationLoweringUnavailableDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Execute("target[index] = value + 1;".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript mutation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsInvocationLoweringUnavailableDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Execute("invoke();".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript invocation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsLiteralMaterializationUnavailableDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Evaluate<int>("42".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsClosureLoweringUnavailableDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime();

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Evaluate<string>("value => value".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript closure lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsTemplateMaterializationUnavailableDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Evaluate<string>("`x`".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript template materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsShortCircuitLoweringUnavailableDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Evaluate<string>("value?.member ?? fallback".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsAggregateLiteralMaterializationUnavailableDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Evaluate<string>("[]".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript aggregate literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsSpreadLoweringUnavailableDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Evaluate<string>("...items".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript spread lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsConditionalLoweringUnavailableDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Evaluate<string>("condition ? left : right".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript conditional lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsDestructuringLoweringUnavailableDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Execute("const { value } = source".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript destructuring lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }

    [Test]
    public void RuntimeProjectsRegularExpressionMaterializationUnavailableDiagnosticAsNotSupportedTest()
    {
        var runtime = new JavaScriptRuntime(JavaScriptRuntimeSpecification.ECMAScript);

        _ = runtime.Execute(ReadOnlySpan<char>.Empty);

        Assert.That(
            () => runtime.Evaluate<string>(@"/\d+/g".AsSpan()),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript regular expression materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md."));
    }
}