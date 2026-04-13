namespace Atom.Compilers.JavaScript;

internal static class JavaScriptRuntimeInvocationOrchestrator
{
    internal static bool TryPrepareInvocation(
        JavaScriptRuntimeExecutionState state,
        string registrationName,
        string entityName,
        string exportName,
        out JavaScriptRuntimeInvocationPlan plan)
    {
        plan = default;

        if (!JavaScriptRuntimeHostBindingResolver.TryResolveExecutionMember(
                state,
                registrationName,
                entityName,
                exportName,
                out var bindingPlan,
                out var marshallingPlan))
        {
            return false;
        }

        plan = new JavaScriptRuntimeInvocationPlan(
            state.SessionEpoch,
            bindingPlan.RegistrationIndex,
            bindingPlan.TypeIndex,
            bindingPlan.MemberIndex,
            marshallingPlan.Channel,
            bindingPlan.MemberKind,
            bindingPlan.TypeAttributes,
            bindingPlan.MemberAttributes);

        return true;
    }
}