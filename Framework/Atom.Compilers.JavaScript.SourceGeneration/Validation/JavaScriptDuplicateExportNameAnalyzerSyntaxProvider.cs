using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptDuplicateExportNameAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS103",
        "Дублирующееся JavaScript export name",
        "Exported JavaScript name '{0}' дублируется внутри типа '{1}'",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Один тип не должен публиковать несколько членов с одинаковым JavaScript export name."
    );

    public override string Id => RuleDescriptor.Id;

    public override ImmutableArray<SyntaxKind> SyntaxKinds =>
    [
        SyntaxKind.ClassDeclaration,
        SyntaxKind.StructDeclaration,
        SyntaxKind.InterfaceDeclaration,
    ];

    public override DiagnosticDescriptor Rule => RuleDescriptor;

    public override void Execute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not TypeDeclarationSyntax typeNode) return;
        if (context.SemanticModel.GetDeclaredSymbol(typeNode) is not INamedTypeSymbol typeSymbol) return;
        if (!IsPrimaryDeclaration(typeSymbol, typeNode)) return;
        if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(typeSymbol)) return;

        var exportNames = new Dictionary<string, List<Location>>(StringComparer.Ordinal);

        foreach (var member in typeSymbol.GetMembers())
            AddMemberExports(exportNames, member);

        foreach (var duplicate in exportNames.Where(static x => x.Value.Count > 1))
        {
            foreach (var location in duplicate.Value)
                context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, location, duplicate.Key, typeNode.Identifier.Text));
        }
    }

    private static void AddMemberExports(Dictionary<string, List<Location>> exportNames, ISymbol member)
    {
        switch (member)
        {
            case IMethodSymbol methodSymbol when methodSymbol.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation:
                AddMethodExport(exportNames, methodSymbol);
                return;

            case IPropertySymbol propertySymbol:
                AddPropertyExport(exportNames, propertySymbol);
                return;

            case IFieldSymbol fieldSymbol:
                AddFieldExports(exportNames, fieldSymbol);
                return;
        }
    }

    private static void AddMethodExport(Dictionary<string, List<Location>> exportNames, IMethodSymbol methodSymbol)
    {
        if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(methodSymbol)) return;
        if (!JavaScriptAnalyzerSyntaxHelpers.TryGetExportName(methodSymbol, JavaScriptAttributeNames.Function, methodSymbol.Name, out var functionName)) return;

        AddExport(exportNames, functionName, methodSymbol.Locations[0]);
    }

    private static void AddPropertyExport(Dictionary<string, List<Location>> exportNames, IPropertySymbol propertySymbol)
    {
        if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(propertySymbol)) return;
        if (!JavaScriptAnalyzerSyntaxHelpers.TryGetExportName(propertySymbol, JavaScriptAttributeNames.Property, propertySymbol.Name, out var propertyName)) return;

        AddExport(exportNames, propertyName, propertySymbol.Locations[0]);
    }

    private static void AddFieldExports(Dictionary<string, List<Location>> exportNames, IFieldSymbol fieldSymbol)
    {
        if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(fieldSymbol)) return;
        if (!JavaScriptAnalyzerSyntaxHelpers.TryGetExportName(fieldSymbol, JavaScriptAttributeNames.Property, fieldSymbol.Name, out var fieldName)) return;

        AddExport(exportNames, fieldName, fieldSymbol.Locations[0]);
    }

    private static void AddExport(Dictionary<string, List<Location>> exports, string exportName, Location location)
    {
        if (!exports.TryGetValue(exportName, out var locations))
        {
            locations = [];
            exports[exportName] = locations;
        }

        locations.Add(location);
    }

    private static bool IsPrimaryDeclaration(INamedTypeSymbol symbol, TypeDeclarationSyntax node)
        => symbol.DeclaringSyntaxReferences.Select(static reference => reference.Span.Start).Min() == node.SpanStart;
}