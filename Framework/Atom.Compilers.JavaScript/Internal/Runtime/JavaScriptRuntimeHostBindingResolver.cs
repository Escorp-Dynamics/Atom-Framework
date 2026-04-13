namespace Atom.Compilers.JavaScript;

internal static class JavaScriptRuntimeHostBindingResolver
{
    internal static bool TryResolveMemberIndex(
        JavaScriptRuntimeExecutionState state,
        string registrationName,
        string entityName,
        string exportName,
        out int memberIndex)
    {
        memberIndex = -1;

        return state.IsInitialized
            && state.LookupCache.MemberIndexes is not null
            && state.LookupCache.MemberIndexes.TryGetValue((registrationName, entityName, exportName), out memberIndex);
    }

    internal static bool TryResolveRegistration(JavaScriptRuntimeExecutionState state, string registrationName, out JavaScriptRuntimeSessionTableEntry registration)
    {
        registration = default;

        if (!state.IsInitialized
            || state.LookupCache.RegistrationIndexes is null
            || !state.LookupCache.RegistrationIndexes.TryGetValue(registrationName, out var registrationIndex))
        {
            return false;
        }

        registration = state.Tables.Registrations[registrationIndex];
        return true;
    }

    internal static bool TryResolveType(JavaScriptRuntimeExecutionState state, string registrationName, string entityName, out JavaScriptRuntimeTypeBindingTableEntry type)
    {
        type = default;

        if (!state.IsInitialized
            || state.LookupCache.TypeIndexes is null
            || !state.LookupCache.TypeIndexes.TryGetValue((registrationName, entityName), out var typeIndex))
        {
            return false;
        }

        type = state.BindingTables.Types[typeIndex];
        return true;
    }

    internal static bool TryResolveMember(
        JavaScriptRuntimeExecutionState state,
        string registrationName,
        string entityName,
        string exportName,
        out JavaScriptRuntimeMemberBindingTableEntry member)
    {
        member = default;

        if (!TryResolveMemberIndex(state, registrationName, entityName, exportName, out var memberIndex))
        {
            return false;
        }

        member = state.BindingTables.Members[memberIndex];
        return true;
    }

    internal static bool TryResolveBindingPlan(
        JavaScriptRuntimeExecutionState state,
        string registrationName,
        string entityName,
        string exportName,
        out JavaScriptRuntimeBindingPlan plan)
    {
        plan = default;

        if (!TryResolveMemberIndex(state, registrationName, entityName, exportName, out var memberIndex)
            || memberIndex < 0
            || memberIndex >= state.BindingPlanCache.MemberPlansByMemberIndex.Length)
        {
            return false;
        }

        plan = state.BindingPlanCache.MemberPlansByMemberIndex[memberIndex];
        return true;
    }

    internal static bool TryResolveMarshallingPlan(
        JavaScriptRuntimeExecutionState state,
        int memberIndex,
        out JavaScriptRuntimeMarshallingPlan plan)
    {
        plan = default;

        if (!state.IsInitialized
            || memberIndex < 0
            || memberIndex >= state.MarshallingPlanCache.MemberPlansByMemberIndex.Length)
        {
            return false;
        }

        plan = state.MarshallingPlanCache.MemberPlansByMemberIndex[memberIndex];
        return true;
    }

    internal static bool TryResolveMarshallingPlan(
        JavaScriptRuntimeExecutionState state,
        string registrationName,
        string entityName,
        string exportName,
        out JavaScriptRuntimeMarshallingPlan plan)
    {
        plan = default;

        return TryResolveMemberIndex(state, registrationName, entityName, exportName, out var memberIndex)
            && TryResolveMarshallingPlan(state, memberIndex, out plan);
    }

    internal static bool TryResolveExecutionMember(
        JavaScriptRuntimeExecutionState state,
        string registrationName,
        string entityName,
        string exportName,
        out JavaScriptRuntimeBindingPlan bindingPlan,
        out JavaScriptRuntimeMarshallingPlan marshallingPlan)
    {
        marshallingPlan = default;

        return TryResolveBindingPlan(state, registrationName, entityName, exportName, out bindingPlan)
            && TryResolveMarshallingPlan(state, bindingPlan.MemberIndex, out marshallingPlan);
    }
}