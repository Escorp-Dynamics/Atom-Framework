using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет синтаксический провайдер атрибута для анализатора.
/// </summary>
public abstract class AttributeAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    /// <summary>
    /// Имя атрибута.
    /// </summary>
    public abstract string Attribute { get; }

    /// <inheritdoc/>
    public override ImmutableArray<SyntaxKind> SyntaxKinds => [SyntaxKind.Attribute];

    /// <inheritdoc/>
    public override DiagnosticDescriptor Rule { get; } = DefaultRule;

    /// <summary>
    /// Правило по умолчанию.
    /// </summary>
    public static DiagnosticDescriptor DefaultRule { get; } = new(
        "A0001",
        "Обнаружен атрибут",
        "Обнаружен атрибут '{0}'",
        "Usage",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "Этот анализатор обнаруживает наличие атрибута."
    );

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Execute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not AttributeSyntax node) return;

        if (node.Name.ToString().Equals(Attribute, StringComparison.InvariantCultureIgnoreCase))
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), node.Name));
    }
}