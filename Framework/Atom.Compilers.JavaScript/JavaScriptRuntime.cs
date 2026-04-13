using System.Collections.Immutable;

namespace Atom.Compilers.JavaScript;

/// <summary>
/// Публичная точка входа в JavaScript runtime.
/// </summary>
/// <remarks>
/// Финальная версия должна инкапсулировать состояние сессии исполнения,
/// регистрацию пользовательских сущностей и строго контролируемый lifecycle.
/// </remarks>
public sealed class JavaScriptRuntime(JavaScriptRuntimeSpecification specification = JavaScriptRuntimeSpecification.Extended) : IAsyncDisposable
{
    internal delegate JavaScriptRuntimeExecutionResult ExecutionRunner(
        JavaScriptRuntimeExecutionState state,
        ReadOnlySpan<char> source,
        JavaScriptRuntimeExecutionOperationKind operation);

    /// <summary>
    /// Определяет способ dispatch для async entry points runtime.
    /// Значение влияет на то, где стартует выполнение runtime work,
    /// а не на continuation semantics после await.
    /// </summary>
    internal enum AsyncDispatchMode
    {
        /// <summary>
        /// Выполнение происходит на вызывающем потоке и обычно завершается синхронно.
        /// </summary>
        Synchronous,

        /// <summary>
        /// Выполнение передаётся на рабочий поток, чтобы не удерживать вызывающий поток во время bootstrap/execute path.
        /// </summary>
        WorkerThread,
    }

    private enum RuntimeState : byte
    {
        Configuring,
        Running,
        Disposed,
    }

    private bool IsDisposed { get; set; }
    private int BootstrapCount { get; set; }
    private JavaScriptRuntimeExecutionState ExecutionState { get; set; }
    private ImmutableArray<JavaScriptRuntimeRegistrationDescriptor> FrozenRegistrationDescriptors { get; set; } = [];
    private HashSet<string>? RegistrationNames { get; set; } = new(StringComparer.Ordinal);
    private ImmutableArray<JavaScriptRuntimeRegistrationDescriptor>.Builder? RegistrationDescriptorBuilder { get; set; }
        = ImmutableArray.CreateBuilder<JavaScriptRuntimeRegistrationDescriptor>();
    private int StateResetCount { get; set; }
    private RuntimeState State { get; set; } = RuntimeState.Configuring;

    /// <summary>
    /// Возвращает выбранную спецификацию runtime surface.
    /// </summary>
    public JavaScriptRuntimeSpecification Specification { get; } = specification;

    /// <summary>
    /// Возвращает <see langword="true"/>, если runtime находится в фазе конфигурирования.
    /// </summary>
    public bool CanRegister => State == RuntimeState.Configuring;

    /// <summary>
    /// Возвращает <see langword="true"/>, если runtime уже переведён в фазу исполнения.
    /// </summary>
    public bool IsRunning => State == RuntimeState.Running;

    internal bool HasExecutionState => ExecutionState.IsInitialized;
    internal JavaScriptRuntimeExecutionState CurrentExecutionState => ExecutionState;
    internal bool HasFrozenRegistrations => !FrozenRegistrationDescriptors.IsDefaultOrEmpty;
    internal int ExecutionBootstrapCount => BootstrapCount;
    internal ImmutableArray<JavaScriptRuntimeRegistrationDescriptor> FrozenRegistrations => FrozenRegistrationDescriptors;
    internal ImmutableArray<JavaScriptRuntimeRegistrationDescriptor> Registrations
    {
        get
        {
            if (RegistrationDescriptorBuilder is null)
                return FrozenRegistrationDescriptors;

            if (RegistrationDescriptorBuilder.Count == 0)
                return [];

            return RegistrationDescriptorBuilder.ToImmutable();
        }
    }
    internal int CurrentSessionEpoch => ExecutionState.SessionEpoch;
    internal int ResetCount => StateResetCount;

    internal bool TryResolveRegistration(string registrationName, out JavaScriptRuntimeSessionTableEntry registration)
    {
        ThrowIfDisposed();
        ValidateRegistrationKey(registrationName);
        return JavaScriptRuntimeHostBindingResolver.TryResolveRegistration(ExecutionState, registrationName, out registration);
    }

    internal bool TryResolveType(string registrationName, string entityName, out JavaScriptRuntimeTypeBindingTableEntry type)
    {
        ThrowIfDisposed();
        ValidateBindingKey(registrationName, entityName);
        return JavaScriptRuntimeHostBindingResolver.TryResolveType(ExecutionState, registrationName, entityName, out type);
    }

    internal bool TryResolveMember(
        string registrationName,
        string entityName,
        string exportName,
        out JavaScriptRuntimeMemberBindingTableEntry member)
    {
        ThrowIfDisposed();
        ValidateBindingKey(registrationName, entityName, exportName);
        return JavaScriptRuntimeHostBindingResolver.TryResolveMember(ExecutionState, registrationName, entityName, exportName, out member);
    }

    internal bool TryResolveBindingPlan(
        string registrationName,
        string entityName,
        string exportName,
        out JavaScriptRuntimeBindingPlan plan)
    {
        ThrowIfDisposed();
        ValidateBindingKey(registrationName, entityName, exportName);
        return JavaScriptRuntimeHostBindingResolver.TryResolveBindingPlan(ExecutionState, registrationName, entityName, exportName, out plan);
    }

    internal bool TryResolveDispatchTarget(
        string registrationName,
        string entityName,
        string exportName,
        out JavaScriptRuntimeDispatchTarget target)
    {
        ThrowIfDisposed();
        ValidateBindingKey(registrationName, entityName, exportName);

        target = default;

        if (!JavaScriptRuntimeHostBindingResolver.TryResolveBindingPlan(ExecutionState, registrationName, entityName, exportName, out var plan))
            return false;

        target = new JavaScriptRuntimeDispatchTarget(
            ExecutionState.SessionEpoch,
            plan.RegistrationIndex,
            plan.TypeIndex,
            plan.MemberIndex,
            plan.TypeAttributes,
            plan.MemberKind,
            plan.MemberAttributes);

        return true;
    }

    internal bool TryResolveMarshallingPlan(
        string registrationName,
        string entityName,
        string exportName,
        out JavaScriptRuntimeMarshallingPlan plan)
    {
        ThrowIfDisposed();
        ValidateBindingKey(registrationName, entityName, exportName);

        return JavaScriptRuntimeHostBindingResolver.TryResolveMarshallingPlan(ExecutionState, registrationName, entityName, exportName, out plan);
    }

    internal bool TryPrepareInvocation(
        string registrationName,
        string entityName,
        string exportName,
        out JavaScriptRuntimeInvocationPlan plan)
    {
        ThrowIfDisposed();
        ValidateBindingKey(registrationName, entityName, exportName);
        return JavaScriptRuntimeInvocationOrchestrator.TryPrepareInvocation(ExecutionState, registrationName, entityName, exportName, out plan);
    }

    internal bool TryPrepareEngineEntry(
        string registrationName,
        string entityName,
        string exportName,
        out JavaScriptRuntimeEngineEntry entry)
    {
        ThrowIfDisposed();
        ValidateBindingKey(registrationName, entityName, exportName);
        return JavaScriptRuntimeEngineEntryScaffold.TryPrepareEntry(ExecutionState, registrationName, entityName, exportName, out entry);
    }

    /// <summary>
    /// Регистрирует CLR-сущность для генерации JavaScript-compatible surface.
    /// </summary>
    /// <typeparam name="T">Тип пользовательской сущности.</typeparam>
    /// <returns>Текущий экземпляр runtime.</returns>
    public JavaScriptRuntime Register<T>()
    {
        ThrowIfDisposed();
        ThrowIfHostRegistrationUnsupported();
        ThrowIfRegistrationLocked();
        _ = typeof(T);
        return this;
    }

    /// <summary>
    /// Регистрирует CLR-сущность под заданным именем.
    /// </summary>
    /// <typeparam name="T">Тип пользовательской сущности.</typeparam>
    /// <param name="alias">Публичное имя регистрации.</param>
    /// <returns>Текущий экземпляр runtime.</returns>
    public JavaScriptRuntime Register<T>(string alias)
    {
        ThrowIfDisposed();
        ThrowIfHostRegistrationUnsupported();
        ThrowIfRegistrationLocked();
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        _ = typeof(T);
        return this;
    }

    internal JavaScriptRuntime Register(string registrationName, ImmutableArray<JavaScriptGeneratedTypeMetadata> generatedMetadata)
    {
        ThrowIfDisposed();
        ThrowIfHostRegistrationUnsupported();
        ThrowIfRegistrationLocked();

        var descriptor = JavaScriptRuntimeMetadataReader.Read(registrationName, generatedMetadata);
        ThrowIfRegistrationNameAlreadyRegistered(descriptor.RegistrationName);

        RegistrationDescriptorBuilder!.Add(descriptor);

        return this;
    }

    /// <summary>
    /// Выполняет JavaScript-код в текущей state-сессии.
    /// </summary>
    /// <param name="source">Исходный текст сценария.</param>
    /// <returns>Результат выполнения.</returns>
    /// <exception cref="NotSupportedException">Каркас runtime пока не содержит реализации.</exception>
    public object? Execute(ReadOnlySpan<char> source)
        => ExecuteCore(source);

    /// <summary>
    /// Выполняет JavaScript-код в текущей state-сессии через async entry point.
    /// </summary>
    /// <remarks>
    /// Публичный async surface намеренно не раскрывает dispatch strategy.
    /// Выбор способа запуска выполнения остаётся internal detail до фиксации полноценной execution model.
    /// </remarks>
    /// <param name="source">Исходный текст сценария.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат выполнения.</returns>
    public ValueTask<object?> ExecuteAsync(
        ReadOnlyMemory<char> source,
        CancellationToken cancellationToken = default)
        => ExecuteAsyncCore(source, AsyncDispatchMode.Synchronous, cancellationToken);

    /// <summary>
    /// Выполняет JavaScript-код в текущей state-сессии через internal async dispatch entry point.
    /// </summary>
    /// <remarks>
    /// Этот overload существует для runtime-internal scheduling и тестов.
    /// Он не является частью минимального public API и может меняться вместе с engine architecture.
    /// </remarks>
    /// <param name="source">Исходный текст сценария.</param>
    /// <param name="dispatchMode">Режим dispatch для запуска выполнения. Не эквивалентен ConfigureAwait.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат выполнения.</returns>
    internal ValueTask<object?> ExecuteAsyncCore(
        ReadOnlyMemory<char> source,
        AsyncDispatchMode dispatchMode = AsyncDispatchMode.Synchronous,
        CancellationToken cancellationToken = default)
        => ExecuteAsyncResultCore(source, JavaScriptRuntimeExecutionOperationKind.Execute, dispatchMode, cancellationToken);

    internal ValueTask<object?> ExecuteAsyncWithRunner(
        ReadOnlyMemory<char> source,
        ExecutionRunner executionRunner,
        AsyncDispatchMode dispatchMode = AsyncDispatchMode.Synchronous,
        CancellationToken cancellationToken = default)
        => ExecuteAsyncResultCore(source, JavaScriptRuntimeExecutionOperationKind.Execute, executionRunner, dispatchMode, cancellationToken);

    /// <summary>
    /// Вычисляет сценарий и приводит результат к заданному типу.
    /// </summary>
    /// <typeparam name="T">Ожидаемый тип результата.</typeparam>
    /// <param name="source">Исходный текст сценария.</param>
    /// <returns>Результат вычисления.</returns>
    public T? Evaluate<T>(ReadOnlySpan<char> source)
    {
        var result = RunExecution(source, JavaScriptRuntimeExecutionOperationKind.Evaluate);
        return ConvertResult<T>(JavaScriptRuntimeValueProjection.Project(result.Value, Specification));
    }

    internal T? EvaluateWithRunner<T>(
        ReadOnlySpan<char> source,
        ExecutionRunner executionRunner)
        => ConvertResult<T>(JavaScriptRuntimeValueProjection.Project(
            RunExecution(source, JavaScriptRuntimeExecutionOperationKind.Evaluate, executionRunner).Value,
            Specification));

    /// <summary>
    /// Вычисляет сценарий через async entry point и приводит результат к заданному типу.
    /// </summary>
    /// <remarks>
    /// Публичный async surface намеренно не раскрывает dispatch strategy.
    /// Выбор способа запуска выполнения остаётся internal detail до фиксации полноценной execution model.
    /// </remarks>
    /// <typeparam name="T">Ожидаемый тип результата.</typeparam>
    /// <param name="source">Исходный текст сценария.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат вычисления.</returns>
    public async ValueTask<T?> EvaluateAsync<T>(
        ReadOnlyMemory<char> source,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsyncCore(source, AsyncDispatchMode.Synchronous, cancellationToken).ConfigureAwait(false);
        return ConvertResult<T>(result);
    }

    /// <summary>
    /// Вычисляет сценарий через internal async dispatch entry point и приводит результат к заданному типу.
    /// </summary>
    /// <remarks>
    /// Этот overload существует для runtime-internal scheduling и тестов.
    /// Он не является частью минимального public API и может меняться вместе с engine architecture.
    /// </remarks>
    /// <typeparam name="T">Ожидаемый тип результата.</typeparam>
    /// <param name="source">Исходный текст сценария.</param>
    /// <param name="dispatchMode">Режим dispatch для запуска выполнения. Не эквивалентен ConfigureAwait.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат вычисления.</returns>
    internal async ValueTask<T?> EvaluateAsyncCore<T>(
        ReadOnlyMemory<char> source,
        AsyncDispatchMode dispatchMode = AsyncDispatchMode.Synchronous,
        CancellationToken cancellationToken = default)
    {
        var result = await RunExecutionAsync(source, JavaScriptRuntimeExecutionOperationKind.Evaluate, dispatchMode, cancellationToken).ConfigureAwait(false);
        return ConvertResult<T>(JavaScriptRuntimeValueProjection.Project(result.Value, Specification));
    }

    internal async ValueTask<T?> EvaluateAsyncWithRunner<T>(
        ReadOnlyMemory<char> source,
        ExecutionRunner executionRunner,
        AsyncDispatchMode dispatchMode = AsyncDispatchMode.Synchronous,
        CancellationToken cancellationToken = default)
    {
        var result = await RunExecutionAsync(source, JavaScriptRuntimeExecutionOperationKind.Evaluate, executionRunner, dispatchMode, cancellationToken).ConfigureAwait(false);
        return ConvertResult<T>(JavaScriptRuntimeValueProjection.Project(result.Value, Specification));
    }

    /// <summary>
    /// Очищает пользовательский JavaScript-state, сохраняя compile-time registration model.
    /// </summary>
    /// <exception cref="NotSupportedException">Каркас runtime пока не содержит реализации.</exception>
    public void ResetState()
    {
        ThrowIfDisposed();

        if (State == RuntimeState.Configuring)
            return;

        StateResetCount++;
        ExecutionState = ExecutionState.NextEpoch();
    }

    private object? ExecuteCore(ReadOnlySpan<char> source)
        => JavaScriptRuntimeValueProjection.Project(RunExecution(source, JavaScriptRuntimeExecutionOperationKind.Execute).Value, Specification);

    internal object? ExecuteWithRunner(
        ReadOnlySpan<char> source,
        ExecutionRunner executionRunner)
        => JavaScriptRuntimeValueProjection.Project(
            RunExecution(source, JavaScriptRuntimeExecutionOperationKind.Execute, executionRunner).Value,
            Specification);

    private async ValueTask<object?> ExecuteAsyncResultCore(
        ReadOnlyMemory<char> source,
        JavaScriptRuntimeExecutionOperationKind operation,
        AsyncDispatchMode dispatchMode,
        CancellationToken cancellationToken)
    {
        var result = await RunExecutionAsync(source, operation, dispatchMode, cancellationToken).ConfigureAwait(false);
        return JavaScriptRuntimeValueProjection.Project(result.Value, Specification);
    }

    private async ValueTask<object?> ExecuteAsyncResultCore(
        ReadOnlyMemory<char> source,
        JavaScriptRuntimeExecutionOperationKind operation,
        ExecutionRunner executionRunner,
        AsyncDispatchMode dispatchMode,
        CancellationToken cancellationToken)
    {
        var result = await RunExecutionAsync(source, operation, executionRunner, dispatchMode, cancellationToken).ConfigureAwait(false);
        return JavaScriptRuntimeValueProjection.Project(result.Value, Specification);
    }

    private ValueTask<JavaScriptRuntimeExecutionResult> RunExecutionAsync(
        ReadOnlyMemory<char> source,
        JavaScriptRuntimeExecutionOperationKind operation,
        AsyncDispatchMode dispatchMode,
        CancellationToken cancellationToken)
        => RunExecutionAsync(source, operation, ExecuteWithDefaultRunner, dispatchMode, cancellationToken);

    private ValueTask<JavaScriptRuntimeExecutionResult> RunExecutionAsync(
        ReadOnlyMemory<char> source,
        JavaScriptRuntimeExecutionOperationKind operation,
        ExecutionRunner executionRunner,
        AsyncDispatchMode dispatchMode,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        return dispatchMode switch
        {
            AsyncDispatchMode.Synchronous => ValueTask.FromResult(RunExecution(source.Span, operation, executionRunner)),
            AsyncDispatchMode.WorkerThread => new ValueTask<JavaScriptRuntimeExecutionResult>(Task.Run(() => RunExecution(source.Span, operation, executionRunner), cancellationToken)),
            _ => ValueTask.FromException<JavaScriptRuntimeExecutionResult>(new ArgumentOutOfRangeException(nameof(dispatchMode))),
        };
    }

    private JavaScriptRuntimeExecutionResult RunExecution(ReadOnlySpan<char> source, JavaScriptRuntimeExecutionOperationKind operation)
        => RunExecution(source, operation, ExecuteWithDefaultRunner);

    private JavaScriptRuntimeExecutionResult RunExecution(
        ReadOnlySpan<char> source,
        JavaScriptRuntimeExecutionOperationKind operation,
        ExecutionRunner executionRunner)
    {
        ThrowIfDisposed();

        EnsureExecutionStarted();
        var result = JavaScriptRuntimeExecutionResultPolicy.Apply(
            executionRunner(ExecutionState, source, operation),
            Specification);
        ThrowIfExecutionFailed(result);
        return result;
    }

    private static JavaScriptRuntimeExecutionResult ExecuteWithDefaultRunner(
        JavaScriptRuntimeExecutionState state,
        ReadOnlySpan<char> source,
        JavaScriptRuntimeExecutionOperationKind operation)
        => JavaScriptRuntimeExecutionEngineScaffold.Run(new JavaScriptRuntimeExecutionRequest(state, source, operation));

    private static void ThrowIfExecutionFailed(JavaScriptRuntimeExecutionResult result)
    {
        if (result.Status == JavaScriptRuntimeExecutionStatus.Completed)
            return;

        if (result.Diagnostic is { } diagnostic)
            throw new NotSupportedException(diagnostic.Message);

        throw new InvalidOperationException("JavaScript runtime execution failed without diagnostic information.");
    }

    private static T? ConvertResult<T>(object? result)
    {
        if (result is null)
            return default;

        return (T?)result;
    }

    private static void ValidateRegistrationKey(string registrationName)
        => ArgumentException.ThrowIfNullOrWhiteSpace(registrationName);

    private static void ValidateBindingKey(string registrationName, string entityName)
    {
        ValidateRegistrationKey(registrationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
    }

    private static void ValidateBindingKey(string registrationName, string entityName, string exportName)
    {
        ValidateBindingKey(registrationName, entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportName);
    }

    private void EnsureExecutionStarted()
    {
        if (State == RuntimeState.Running)
            return;

        BootstrapCount++;
        FrozenRegistrationDescriptors = FreezeRegistrations(RegistrationDescriptorBuilder);
        RegistrationDescriptorBuilder = null;
        RegistrationNames = null;
        ExecutionState = JavaScriptRuntimeExecutionStateFactory.Create(FrozenRegistrationDescriptors, Specification, sessionEpoch: 1);
        State = RuntimeState.Running;
        _ = FrozenRegistrationDescriptors.Length;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, typeof(JavaScriptRuntime));

    private void ThrowIfRegistrationLocked()
    {
        if (State != RuntimeState.Configuring)
            throw new InvalidOperationException("Registration is only allowed before the first script execution.");
    }

    private void ThrowIfHostRegistrationUnsupported()
    {
        if (JavaScriptRuntimeSpecificationPolicy.SupportsHostRegistration(Specification))
            return;

        throw new InvalidOperationException("Host registration is only available when JavaScriptRuntimeSpecification.Extended is selected.");
    }

    private void ThrowIfRegistrationNameAlreadyRegistered(string registrationName)
    {
        if (TryRegisterName(registrationName))
            return;

        throw new InvalidOperationException($"Registration name '{registrationName}' is already registered.");
    }

    private static ImmutableArray<JavaScriptRuntimeRegistrationDescriptor> FreezeRegistrations(
        ImmutableArray<JavaScriptRuntimeRegistrationDescriptor>.Builder? builder)
    {
        if (builder is null || builder.Count == 0)
            return [];

        return builder.ToImmutable();
    }

    private bool TryRegisterName(string registrationName)
    {
        if (RegistrationNames is null)
            return false;

        return RegistrationNames.Add(registrationName);
    }

    /// <summary>
    /// Освобождает ресурсы runtime.
    /// </summary>
    /// <returns>Завершённая операция освобождения.</returns>
    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        ExecutionState = default;
        RegistrationDescriptorBuilder = null;
        RegistrationNames = null;
        State = RuntimeState.Disposed;
        return ValueTask.CompletedTask;
    }
}