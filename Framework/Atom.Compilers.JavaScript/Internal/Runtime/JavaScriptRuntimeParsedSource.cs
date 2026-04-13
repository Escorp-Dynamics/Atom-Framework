using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeParsedSource(
    int SessionEpoch,
    JavaScriptRuntimeSpecification Specification,
    JavaScriptRuntimeParserFeature ParserFeatures,
    JavaScriptRuntimeExecutionOperationKind Operation,
    int SourceLength)
{
    internal bool AllowsExtendedRuntimeFeatures => Specification == JavaScriptRuntimeSpecification.Extended;
    internal bool ContainsIdentifierReference => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsIdentifierReference) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsMemberAccess => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsMemberAccess) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsInvocation => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsInvocation) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsHostBindingCandidate => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsHostBindingCandidate) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsIndexAccess => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsIndexAccess) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsAssignment => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsAssignment) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsStringLiteral => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsStringLiteral) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsNumericLiteral => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsNumericLiteral) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsUnaryOperator => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsUnaryOperator) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsBinaryOperator => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsBinaryOperator) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsComparisonOperator => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsComparisonOperator) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsLogicalOperator => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsLogicalOperator) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsTemplateLiteral => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsTemplateLiteral) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsArrowFunctionCandidate => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsArrowFunctionCandidate) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsNullishCoalescingOperator => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsNullishCoalescingOperator) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsOptionalChainingCandidate => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsOptionalChainingCandidate) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsArrayLiteralCandidate => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsArrayLiteralCandidate) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsObjectLiteralCandidate => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsObjectLiteralCandidate) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsSpreadOrRestCandidate => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsSpreadOrRestCandidate) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsConditionalOperatorCandidate => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsConditionalOperatorCandidate) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsDestructuringPatternCandidate => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsDestructuringPatternCandidate) != JavaScriptRuntimeParserFeature.None;
    internal bool ContainsRegularExpressionLiteralCandidate => (ParserFeatures & JavaScriptRuntimeParserFeature.ContainsRegularExpressionLiteralCandidate) != JavaScriptRuntimeParserFeature.None;
}