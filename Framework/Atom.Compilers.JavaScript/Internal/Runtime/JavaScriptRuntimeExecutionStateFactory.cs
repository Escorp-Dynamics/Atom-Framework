using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

internal static class JavaScriptRuntimeExecutionStateFactory
{
    private static readonly FrozenDictionary<(string RegistrationName, string EntityName, string ExportName), int> EmptyMemberIndexes
        = new Dictionary<(string RegistrationName, string EntityName, string ExportName), int>(0).ToFrozenDictionary();

    internal static JavaScriptRuntimeExecutionState Create(
        ImmutableArray<JavaScriptRuntimeRegistrationDescriptor> registrations,
        JavaScriptRuntimeSpecification specification,
        int sessionEpoch)
    {
        if (registrations.IsDefaultOrEmpty)
            return new(registrations, specification, default, default, default, default, default, sessionEpoch);

        CountArtifacts(registrations, out var totalTypeCount, out var totalMemberCount, out var totalExportableMemberCount);

        var sessionEntries = new JavaScriptRuntimeSessionTableEntry[registrations.Length];
        var typeEntries = new JavaScriptRuntimeTypeBindingTableEntry[totalTypeCount];
        var memberEntries = new JavaScriptRuntimeMemberBindingTableEntry[totalMemberCount];
        var registrationIndexes = new Dictionary<string, int>(registrations.Length, StringComparer.Ordinal);
        var typeIndexes = new Dictionary<(string RegistrationName, string EntityName), int>(totalTypeCount);
        var memberIndexes = CreateMemberIndexes(totalExportableMemberCount);
        var bindingPlans = new JavaScriptRuntimeBindingPlan[totalMemberCount];
        var marshallingPlans = new JavaScriptRuntimeMarshallingPlan[totalMemberCount];

        BuildArtifacts(
            registrations,
            sessionEntries,
            typeEntries,
            memberEntries,
            registrationIndexes,
            typeIndexes,
            memberIndexes,
            bindingPlans,
            marshallingPlans);

        return new JavaScriptRuntimeExecutionState(
            registrations,
            specification,
            new JavaScriptRuntimeSessionTables(ImmutableCollectionsMarshal.AsImmutableArray(sessionEntries), totalTypeCount, totalMemberCount),
            new JavaScriptRuntimeBindingTables(
                ImmutableCollectionsMarshal.AsImmutableArray(typeEntries),
                ImmutableCollectionsMarshal.AsImmutableArray(memberEntries)),
            new JavaScriptRuntimeLookupCache(
                registrationIndexes.ToFrozenDictionary(StringComparer.Ordinal),
                typeIndexes.ToFrozenDictionary(),
                CreateFrozenMemberIndexes(totalExportableMemberCount, memberIndexes)),
            new JavaScriptRuntimeBindingPlanCache(ImmutableCollectionsMarshal.AsImmutableArray(bindingPlans)),
            new JavaScriptRuntimeMarshallingPlanCache(ImmutableCollectionsMarshal.AsImmutableArray(marshallingPlans)),
            sessionEpoch);
    }

    private static void CountArtifacts(
        ImmutableArray<JavaScriptRuntimeRegistrationDescriptor> registrations,
        out int totalTypeCount,
        out int totalMemberCount,
        out int totalExportableMemberCount)
    {
        totalTypeCount = 0;
        totalMemberCount = 0;
        totalExportableMemberCount = 0;

        for (var registrationIndex = 0; registrationIndex < registrations.Length; registrationIndex++)
        {
            var registration = registrations[registrationIndex];
            totalTypeCount += registration.Types.Length;

            for (var typeIndex = 0; typeIndex < registration.Types.Length; typeIndex++)
            {
                var members = registration.Types[typeIndex].Members;
                totalMemberCount += members.Length;
                totalExportableMemberCount += CountExportableMembers(members);
            }
        }
    }

    private static int CountExportableMembers(ImmutableArray<JavaScriptRuntimeMemberDescriptor> members)
    {
        var exportableMemberCount = 0;

        for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
        {
            if (members[memberIndex].ExportName is not null)
                exportableMemberCount++;
        }

        return exportableMemberCount;
    }

    private static Dictionary<(string RegistrationName, string EntityName, string ExportName), int>? CreateMemberIndexes(int totalExportableMemberCount)
    {
        if (totalExportableMemberCount == 0)
            return null;

        return new Dictionary<(string RegistrationName, string EntityName, string ExportName), int>(totalExportableMemberCount);
    }

    private static FrozenDictionary<(string RegistrationName, string EntityName, string ExportName), int> CreateFrozenMemberIndexes(
        int totalExportableMemberCount,
        Dictionary<(string RegistrationName, string EntityName, string ExportName), int>? memberIndexes)
    {
        if (totalExportableMemberCount == 0)
            return EmptyMemberIndexes;

        return memberIndexes!.ToFrozenDictionary();
    }

    private static void BuildArtifacts(
        ImmutableArray<JavaScriptRuntimeRegistrationDescriptor> registrations,
        JavaScriptRuntimeSessionTableEntry[] sessionEntries,
        JavaScriptRuntimeTypeBindingTableEntry[] typeEntries,
        JavaScriptRuntimeMemberBindingTableEntry[] memberEntries,
        Dictionary<string, int> registrationIndexes,
        Dictionary<(string RegistrationName, string EntityName), int> typeIndexes,
        Dictionary<(string RegistrationName, string EntityName, string ExportName), int>? memberIndexes,
        JavaScriptRuntimeBindingPlan[] bindingPlans,
        JavaScriptRuntimeMarshallingPlan[] marshallingPlans)
    {
        var typeStart = 0;
        var memberStart = 0;

        for (var registrationIndex = 0; registrationIndex < registrations.Length; registrationIndex++)
        {
            var registration = registrations[registrationIndex];
            var registrationName = registration.RegistrationName;
            registrationIndexes.Add(registrationName, registrationIndex);
            var registrationMemberStart = memberStart;

            for (var typeOffset = 0; typeOffset < registration.Types.Length; typeOffset++)
            {
                var type = registration.Types[typeOffset];
                typeIndexes.Add((registrationName, type.EntityName), typeStart);
                var typeMemberStart = memberStart;

                PopulateMemberArtifacts(
                    registrationName,
                    registrationIndex,
                    typeStart,
                    type,
                    memberStart,
                    memberEntries,
                    memberIndexes,
                    bindingPlans,
                    marshallingPlans);

                typeEntries[typeStart] = new JavaScriptRuntimeTypeBindingTableEntry(
                    registrationIndex,
                    type.EntityName,
                    type.Generator,
                    typeMemberStart,
                    type.Members.Length,
                    type.Attributes);

                typeStart++;
                memberStart += type.Members.Length;
            }

            sessionEntries[registrationIndex] = new JavaScriptRuntimeSessionTableEntry(
                registrationName,
                typeStart - registration.Types.Length,
                registration.Types.Length,
                registrationMemberStart,
                memberStart - registrationMemberStart);
        }
    }

    private static void PopulateMemberArtifacts(
        string registrationName,
        int registrationIndex,
        int typeIndex,
        JavaScriptRuntimeTypeDescriptor type,
        int memberStart,
        JavaScriptRuntimeMemberBindingTableEntry[] memberEntries,
        Dictionary<(string RegistrationName, string EntityName, string ExportName), int>? memberIndexes,
        JavaScriptRuntimeBindingPlan[] bindingPlans,
        JavaScriptRuntimeMarshallingPlan[] marshallingPlans)
    {
        for (var memberOffset = 0; memberOffset < type.Members.Length; memberOffset++)
        {
            var member = type.Members[memberOffset];
            var memberIndex = memberStart + memberOffset;

            memberEntries[memberIndex] = new JavaScriptRuntimeMemberBindingTableEntry(typeIndex, member.Name, member.ExportName, member.Kind, member.Attributes);

            if (member.ExportName is null)
                continue;

            var memberKey = (registrationName, type.EntityName, member.ExportName);
            memberIndexes!.Add(memberKey, memberIndex);

            var bindingPlan = new JavaScriptRuntimeBindingPlan(
                registrationIndex,
                typeIndex,
                memberIndex,
                type.Attributes,
                member.Kind,
                member.Attributes);

            bindingPlans[memberIndex] = bindingPlan;
            marshallingPlans[memberIndex] = CreateMarshallingPlan(bindingPlan);
        }
    }

    private static JavaScriptRuntimeMarshallingPlan CreateMarshallingPlan(JavaScriptRuntimeBindingPlan plan)
    {
        var channel = GetMarshallingChannel(plan.MemberKind);

        return new JavaScriptRuntimeMarshallingPlan(
            plan.RegistrationIndex,
            plan.TypeIndex,
            plan.MemberIndex,
            channel,
            (plan.MemberAttributes & JavaScriptRuntimeMemberAttributes.Pure) != JavaScriptRuntimeMemberAttributes.None,
            (plan.MemberAttributes & JavaScriptRuntimeMemberAttributes.Inline) != JavaScriptRuntimeMemberAttributes.None,
            (plan.MemberAttributes & JavaScriptRuntimeMemberAttributes.ReadOnly) != JavaScriptRuntimeMemberAttributes.None,
            (plan.MemberAttributes & JavaScriptRuntimeMemberAttributes.Required) != JavaScriptRuntimeMemberAttributes.None);
    }

    private static JavaScriptRuntimeMarshallingChannel GetMarshallingChannel(JavaScriptGeneratedMemberKind memberKind)
    {
        if (memberKind == JavaScriptGeneratedMemberKind.Property)
            return JavaScriptRuntimeMarshallingChannel.PropertyAccess;

        return JavaScriptRuntimeMarshallingChannel.MethodCall;
    }
}