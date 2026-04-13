namespace Atom.Compilers.JavaScript;

[System.Flags]
internal enum JavaScriptRuntimeParserFeature
{
    None = 0,
    ContainsIdentifierReference = 1 << 0,
    ContainsMemberAccess = 1 << 1,
    ContainsInvocation = 1 << 2,
    ContainsHostBindingCandidate = 1 << 3,
    ContainsIndexAccess = 1 << 4,
    ContainsAssignment = 1 << 5,
    ContainsStringLiteral = 1 << 6,
    ContainsNumericLiteral = 1 << 7,
    ContainsUnaryOperator = 1 << 8,
    ContainsBinaryOperator = 1 << 9,
    ContainsComparisonOperator = 1 << 10,
    ContainsLogicalOperator = 1 << 11,
    ContainsTemplateLiteral = 1 << 12,
    ContainsArrowFunctionCandidate = 1 << 13,
    ContainsNullishCoalescingOperator = 1 << 14,
    ContainsOptionalChainingCandidate = 1 << 15,
    ContainsArrayLiteralCandidate = 1 << 16,
    ContainsObjectLiteralCandidate = 1 << 17,
    ContainsSpreadOrRestCandidate = 1 << 18,
    ContainsConditionalOperatorCandidate = 1 << 19,
    ContainsDestructuringPatternCandidate = 1 << 20,
    ContainsRegularExpressionLiteralCandidate = 1 << 21,
}