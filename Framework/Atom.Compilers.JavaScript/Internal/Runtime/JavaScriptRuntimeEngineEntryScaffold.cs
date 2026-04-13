namespace Atom.Compilers.JavaScript;

internal static class JavaScriptRuntimeEngineEntryScaffold
{
    internal static bool TryPrepareEntry(
        JavaScriptRuntimeExecutionState state,
        string registrationName,
        string entityName,
        string exportName,
        out JavaScriptRuntimeEngineEntry entry)
    {
        entry = default;

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

        entry = new JavaScriptRuntimeEngineEntry(
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