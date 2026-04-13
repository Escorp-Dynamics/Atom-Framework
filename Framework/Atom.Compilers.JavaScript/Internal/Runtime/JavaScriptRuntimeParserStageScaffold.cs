using System.Buffers;

namespace Atom.Compilers.JavaScript;

internal static class JavaScriptRuntimeParserStageScaffold
{
    private enum PatternScanState
    {
        None,
        SingleQuotedString,
        DoubleQuotedString,
        TemplateLiteral,
        RegularExpressionLiteral,
        LineComment,
        BlockComment,
    }

    internal static bool TryParse(
        JavaScriptRuntimeExecutionRequest request,
        out JavaScriptRuntimeParsedSource parsedSource)
    {
        var parserFeatures = GetParserFeatures(request.Source);

        parsedSource = new JavaScriptRuntimeParsedSource(
            request.SessionEpoch,
            request.Specification,
            parserFeatures,
            request.Operation,
            request.Source.Length);

        return true;
    }

    private static JavaScriptRuntimeParserFeature GetParserFeatures(ReadOnlySpan<char> source)
    {
        if (!RequiresSanitizedPatternScan(source))
            return GetUnsanitizedSourceFeatures(source);

        var rentedBuffer = ArrayPool<char>.Shared.Rent(source.Length == 0 ? 1 : source.Length);

        try
        {
            var sanitizedSpan = rentedBuffer.AsSpan(0, source.Length);
            CreatePatternScanBuffer(source, sanitizedSpan);
            return GetSanitizedSourceFeatures(sanitizedSpan);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rentedBuffer);
        }
    }

    private static bool RequiresSanitizedPatternScan(ReadOnlySpan<char> source)
        => source.IndexOfAny("'\"`/".AsSpan()) >= 0;

    private static bool IsParserWhiteSpace(char value)
        => char.IsWhiteSpace(value);

    private static JavaScriptRuntimeParserFeature GetUnsanitizedSourceFeatures(ReadOnlySpan<char> source)
    {
        var parserFeatures = GetIterativeCharacterFeatures(source);

        if (ContainsHostBindingCandidate(source))
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsHostBindingCandidate;

        if (ContainsConditionalOperatorCandidate(source))
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsConditionalOperatorCandidate;

        if (ContainsDestructuringPatternCandidate(source))
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsDestructuringPatternCandidate;

        return parserFeatures;
    }

    private static JavaScriptRuntimeParserFeature GetSanitizedSourceFeatures(Span<char> sanitizedSpan)
    {
        var parserFeatures = JavaScriptRuntimeParserFeature.None;

        if (AnalyzeAndMaskRegularExpressionLiterals(sanitizedSpan))
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsRegularExpressionLiteralCandidate;

        parserFeatures |= GetIterativeCharacterFeatures(sanitizedSpan);

        if (ContainsHostBindingCandidate(sanitizedSpan))
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsHostBindingCandidate;

        if (ContainsConditionalOperatorCandidate(sanitizedSpan))
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsConditionalOperatorCandidate;

        if (ContainsDestructuringPatternCandidate(sanitizedSpan))
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsDestructuringPatternCandidate;

        return parserFeatures;
    }

    private static JavaScriptRuntimeParserFeature GetIterativeCharacterFeatures(ReadOnlySpan<char> sanitizedSpan)
    {
        var parserFeatures = JavaScriptRuntimeParserFeature.None;
        var previousSignificantIndex = -1;
        var previousSignificant = '\0';

        for (var index = 0; index < sanitizedSpan.Length; index++)
        {
            var current = sanitizedSpan[index];

            if (TrySkipResolvedDelimiter(current, index, parserFeatures, ref previousSignificant, ref previousSignificantIndex))
                continue;

            if (TryConsumeSimpleCharacterRun(sanitizedSpan, ref index, current, ref parserFeatures, ref previousSignificant, ref previousSignificantIndex))
                continue;

            if (TryGetNonContextualCharacterFeatures(current, out var characterFeatures))
            {
                parserFeatures |= characterFeatures;
                UpdatePreviousSignificant(ref previousSignificant, ref previousSignificantIndex, current, index);
                continue;
            }

            var previous = index > 0 ? sanitizedSpan[index - 1] : '\0';
            var next = index + 1 < sanitizedSpan.Length ? sanitizedSpan[index + 1] : '\0';
            var next2 = index + 2 < sanitizedSpan.Length ? sanitizedSpan[index + 2] : '\0';

            parserFeatures |= GetCharacterFeatures(sanitizedSpan, index, current, previous, previousSignificantIndex, previousSignificant, next, next2);

            UpdatePreviousSignificant(ref previousSignificant, ref previousSignificantIndex, current, index);
        }

        return parserFeatures;
    }

    private static bool TrySkipResolvedDelimiter(
        char current,
        int index,
        JavaScriptRuntimeParserFeature parserFeatures,
        ref char previousSignificant,
        ref int previousSignificantIndex)
    {
        if (current == '{'
            && (parserFeatures & JavaScriptRuntimeParserFeature.ContainsObjectLiteralCandidate) != JavaScriptRuntimeParserFeature.None)
        {
            previousSignificant = current;
            previousSignificantIndex = index;
            return true;
        }

        if (current != '[' || !HasResolvedBracketFeatures(parserFeatures))
            return false;

        previousSignificant = current;
        previousSignificantIndex = index;
        return true;
    }

    private static bool HasResolvedBracketFeatures(JavaScriptRuntimeParserFeature parserFeatures)
        => (parserFeatures & JavaScriptRuntimeParserFeature.ContainsArrayLiteralCandidate) != JavaScriptRuntimeParserFeature.None
            && (parserFeatures & JavaScriptRuntimeParserFeature.ContainsIndexAccess) != JavaScriptRuntimeParserFeature.None;

    private static bool TryConsumeSimpleCharacterRun(
        ReadOnlySpan<char> source,
        ref int index,
        char current,
        ref JavaScriptRuntimeParserFeature parserFeatures,
        ref char previousSignificant,
        ref int previousSignificantIndex)
    {
        if (IsParserWhiteSpace(current))
        {
            index = ConsumeWhiteSpaceRun(source, index);
            return true;
        }

        if (IsIdentifierStart(current))
        {
            if (TryConsumeObjectLiteralPropertyKeyRun(source, ref index, ref previousSignificant, ref previousSignificantIndex, parserFeatures))
                return true;

            if (TryConsumeArrayLiteralElementRun(source, ref index, ref previousSignificant, ref previousSignificantIndex, parserFeatures))
                return true;

            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsIdentifierReference;
            index = ConsumeIdentifierRun(source, index);
            previousSignificantIndex = index;
            previousSignificant = source[index];
            return true;
        }

        if (char.IsDigit(current))
        {
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsNumericLiteral;
            index = ConsumeDigitRun(source, index);
            previousSignificantIndex = index;
            previousSignificant = source[index];
            return true;
        }

        return false;
    }

    private static bool TryConsumeObjectLiteralPropertyKeyRun(
        ReadOnlySpan<char> source,
        ref int index,
        ref char previousSignificant,
        ref int previousSignificantIndex,
        JavaScriptRuntimeParserFeature parserFeatures)
    {
        if ((parserFeatures & JavaScriptRuntimeParserFeature.ContainsObjectLiteralCandidate) == JavaScriptRuntimeParserFeature.None
            || (parserFeatures & JavaScriptRuntimeParserFeature.ContainsIdentifierReference) == JavaScriptRuntimeParserFeature.None
            || previousSignificant is not '{' and not ',')
        {
            return false;
        }

        if (!TryGetObjectLiteralPropertyTerminator(source, index + 1, out var terminatorIndex)
            || source[terminatorIndex] is not ':' and not ',' and not '}')
        {
            return false;
        }

        previousSignificant = source[terminatorIndex];
        previousSignificantIndex = terminatorIndex;
        index = source[terminatorIndex] is ':' or ','
            ? ConsumeWhiteSpaceRun(source, terminatorIndex)
            : terminatorIndex;
        return true;
    }

    private static bool TryConsumeArrayLiteralElementRun(
        ReadOnlySpan<char> source,
        ref int index,
        ref char previousSignificant,
        ref int previousSignificantIndex,
        JavaScriptRuntimeParserFeature parserFeatures)
    {
        if ((parserFeatures & JavaScriptRuntimeParserFeature.ContainsArrayLiteralCandidate) == JavaScriptRuntimeParserFeature.None
            || previousSignificant is not '[' and not ','
            || (parserFeatures & JavaScriptRuntimeParserFeature.ContainsIdentifierReference) == JavaScriptRuntimeParserFeature.None)
        {
            return false;
        }

        if (!TryGetArrayLiteralElementTerminator(source, index + 1, out var terminatorIndex))
            return false;

        previousSignificant = source[terminatorIndex];
        previousSignificantIndex = terminatorIndex;
        index = source[terminatorIndex] == ','
            ? ConsumeWhiteSpaceRun(source, terminatorIndex)
            : terminatorIndex;
        return true;
    }

    private static int ConsumeIdentifierRun(ReadOnlySpan<char> source, int index)
    {
        while (index + 1 < source.Length && IsIdentifierPart(source[index + 1]))
            index++;

        return index;
    }

    private static int ConsumeDigitRun(ReadOnlySpan<char> source, int index)
    {
        while (index + 1 < source.Length && char.IsDigit(source[index + 1]))
            index++;

        return index;
    }

    private static int ConsumeWhiteSpaceRun(ReadOnlySpan<char> source, int index)
    {
        while (index + 1 < source.Length && IsParserWhiteSpace(source[index + 1]))
            index++;

        return index;
    }

    private static bool TryGetNonContextualCharacterFeatures(char current, out JavaScriptRuntimeParserFeature parserFeatures)
    {
        if (RequiresContextualCharacterAnalysis(current))
        {
            parserFeatures = JavaScriptRuntimeParserFeature.None;
            return false;
        }

        parserFeatures = GetLexicalFeatures(current);
        return true;
    }

    private static void UpdatePreviousSignificant(ref char previousSignificant, ref int previousSignificantIndex, char current, int index)
    {
        if (IsParserWhiteSpace(current) || current == '`')
            return;

        previousSignificant = current;
        previousSignificantIndex = index;
    }

    private static bool RequiresContextualCharacterAnalysis(char current)
        => current is '.' or '(' or ')' or '[' or '{' or '!' or '~' or '+' or '-' or '*' or '%' or '/' or '<' or '>' or '=' or '&' or '|' or '?';

    private static void CreatePatternScanBuffer(ReadOnlySpan<char> source, Span<char> buffer)
    {
        source.CopyTo(buffer);
        var state = PatternScanState.None;
        var isEscaped = false;
        var isRegexCharacterClass = false;
        var templateExpressionDepth = 0;

        for (var index = 0; index < buffer.Length; index++)
        {
            if (state != PatternScanState.None)
            {
                HandleActivePatternScanState(buffer, index, ref state, ref isEscaped, ref isRegexCharacterClass, ref templateExpressionDepth);
                continue;
            }

            if (templateExpressionDepth > 0
                && HandleTemplateExpressionBoundary(buffer, index, ref state, ref templateExpressionDepth))
            {
                continue;
            }

            if (TryStartComment(buffer, index, ref state))
                continue;

            if (templateExpressionDepth > 0 && TryStartRegularExpressionLiteral(buffer, index, ref state, ref isEscaped, ref isRegexCharacterClass))
                continue;

            TryStartString(buffer[index], ref state);
        }
    }

    private static bool AnalyzeAndMaskRegularExpressionLiterals(Span<char> buffer)
    {
        var containsRegularExpressionLiteralCandidate = false;
        var previousSignificant = '\0';
        var index = 0;

        while (index < buffer.Length)
        {
            var current = buffer[index];

            if (IsParserWhiteSpace(current) || current == '`')
            {
                index++;
                continue;
            }

            if (current == '/'
                && IsRegularExpressionLiteralStart(buffer, index, previousSignificant, index + 1 < buffer.Length ? buffer[index + 1] : '\0')
                && TryFindRegexClosingDelimiter(buffer, index + 1, out var closingIndex))
            {
                containsRegularExpressionLiteralCandidate = true;
                MaskRegularExpressionSegment(buffer, index + 1, closingIndex);
                index = MaskRegularExpressionFlags(buffer, closingIndex + 1);
                previousSignificant = '/';
                continue;
            }

            previousSignificant = current;
            index++;
        }

        return containsRegularExpressionLiteralCandidate;
    }

    private static bool TryFindRegexClosingDelimiter(ReadOnlySpan<char> source, int startIndex, out int closingIndex)
    {
        var isEscaped = false;
        var isInCharacterClass = false;

        for (var index = startIndex; index < source.Length; index++)
        {
            var current = source[index];

            if (isEscaped)
            {
                isEscaped = false;
                continue;
            }

            if (current == '\\')
            {
                isEscaped = true;
                continue;
            }

            if (current == '[')
            {
                isInCharacterClass = true;
                continue;
            }

            if (current == ']' && isInCharacterClass)
            {
                isInCharacterClass = false;
                continue;
            }

            if (current == '/' && !isInCharacterClass)
            {
                closingIndex = index;
                return true;
            }
        }

        closingIndex = -1;
        return false;
    }

    private static void MaskRegularExpressionSegment(Span<char> buffer, int startIndex, int closingIndex)
    {
        for (var index = startIndex; index <= closingIndex; index++)
            buffer[index] = ' ';
    }

    private static int MaskRegularExpressionFlags(Span<char> buffer, int startIndex)
    {
        var index = startIndex;

        while (index < buffer.Length && IsIdentifierPart(buffer[index]))
        {
            buffer[index] = ' ';
            index++;
        }

        return index;
    }

    private static void HandleActivePatternScanState(Span<char> buffer, int index, ref PatternScanState state, ref bool isEscaped, ref bool isRegexCharacterClass, ref int templateExpressionDepth)
    {
        switch (state)
        {
            case PatternScanState.LineComment:
                HandleLineCommentState(buffer, index, ref state);
                break;
            case PatternScanState.BlockComment:
                HandleBlockCommentState(buffer, index, ref state);
                break;
            case PatternScanState.SingleQuotedString:
                HandleQuotedStringState(buffer, index, '\'', ref state, ref isEscaped);
                break;
            case PatternScanState.DoubleQuotedString:
                HandleQuotedStringState(buffer, index, '"', ref state, ref isEscaped);
                break;
            case PatternScanState.TemplateLiteral:
                HandleTemplateLiteralState(buffer, index, ref state, ref isEscaped, ref templateExpressionDepth);
                break;
            case PatternScanState.RegularExpressionLiteral:
                HandleRegularExpressionLiteralState(buffer, index, ref state, ref isEscaped, ref isRegexCharacterClass);
                break;
        }
    }

    private static bool TryStartRegularExpressionLiteral(Span<char> buffer, int index, ref PatternScanState state, ref bool isEscaped, ref bool isRegexCharacterClass)
    {
        if (buffer[index] != '/')
            return false;

        var previousSignificantIndex = GetPreviousSignificantIndex(buffer, index - 1);
        var previousSignificant = previousSignificantIndex >= 0 ? buffer[previousSignificantIndex] : '\0';
        var next = index + 1 < buffer.Length ? buffer[index + 1] : '\0';

        if (!IsRegularExpressionLiteralStart(buffer, index, previousSignificant, next)
            || !TryFindRegexClosingDelimiter(buffer, index + 1, out _))
        {
            return false;
        }

        isEscaped = false;
        isRegexCharacterClass = false;
        state = PatternScanState.RegularExpressionLiteral;
        return true;
    }

    private static void HandleLineCommentState(Span<char> buffer, int index, ref PatternScanState state)
    {
        if (buffer[index] is '\n' or '\r')
        {
            state = PatternScanState.None;
            return;
        }

        buffer[index] = ' ';
    }

    private static void HandleBlockCommentState(Span<char> buffer, int index, ref PatternScanState state)
    {
        var current = buffer[index];
        var next = index + 1 < buffer.Length ? buffer[index + 1] : '\0';

        buffer[index] = ' ';

        if (current == '*' && next == '/')
        {
            buffer[index + 1] = ' ';
            state = PatternScanState.None;
        }
    }

    private static void HandleQuotedStringState(Span<char> buffer, int index, char terminator, ref PatternScanState state, ref bool isEscaped)
    {
        var current = buffer[index];

        if (isEscaped)
        {
            isEscaped = false;
            buffer[index] = ' ';
            return;
        }

        if (current == '\\')
        {
            isEscaped = true;
            buffer[index] = ' ';
            return;
        }

        buffer[index] = ' ';

        if (current == terminator)
            state = PatternScanState.None;
    }

    private static void HandleTemplateLiteralState(Span<char> buffer, int index, ref PatternScanState state, ref bool isEscaped, ref int templateExpressionDepth)
    {
        var current = buffer[index];
        var next = index + 1 < buffer.Length ? buffer[index + 1] : '\0';

        if (isEscaped)
        {
            isEscaped = false;
            buffer[index] = ' ';
            return;
        }

        if (current == '\\')
        {
            isEscaped = true;
            buffer[index] = ' ';
            return;
        }

        if (current == '`')
        {
            state = PatternScanState.None;
            return;
        }

        if (current == '$' && next == '{')
        {
            buffer[index] = ' ';
            buffer[index + 1] = ' ';
            state = PatternScanState.None;
            templateExpressionDepth = 1;
            return;
        }

        buffer[index] = ' ';
    }

    private static void HandleRegularExpressionLiteralState(Span<char> buffer, int index, ref PatternScanState state, ref bool isEscaped, ref bool isRegexCharacterClass)
    {
        var current = buffer[index];

        if (isEscaped)
        {
            isEscaped = false;
            return;
        }

        if (current == '\\')
        {
            isEscaped = true;
            return;
        }

        if (current == '[')
        {
            isRegexCharacterClass = true;
            return;
        }

        if (current == ']' && isRegexCharacterClass)
        {
            isRegexCharacterClass = false;
            return;
        }

        if (current == '/' && !isRegexCharacterClass)
            state = PatternScanState.None;
    }

    private static bool HandleTemplateExpressionBoundary(Span<char> buffer, int index, ref PatternScanState state, ref int templateExpressionDepth)
    {
        var current = buffer[index];

        if (current == '{')
        {
            templateExpressionDepth++;
            return false;
        }

        if (current != '}')
            return false;

        templateExpressionDepth--;

        if (templateExpressionDepth != 0)
            return false;

        buffer[index] = ' ';
        state = PatternScanState.TemplateLiteral;
        return true;
    }

    private static bool TryStartComment(Span<char> buffer, int index, ref PatternScanState state)
    {
        var current = buffer[index];
        var next = index + 1 < buffer.Length ? buffer[index + 1] : '\0';

        if (current == '/' && next == '/')
        {
            buffer[index] = ' ';
            buffer[index + 1] = ' ';
            state = PatternScanState.LineComment;
            return true;
        }

        if (current == '/' && next == '*')
        {
            buffer[index] = ' ';
            buffer[index + 1] = ' ';
            state = PatternScanState.BlockComment;
            return true;
        }

        return false;
    }

    private static void TryStartString(char current, ref PatternScanState state)
    {
        if (current == '\'')
        {
            state = PatternScanState.SingleQuotedString;
            return;
        }

        if (current == '"')
        {
            state = PatternScanState.DoubleQuotedString;
            return;
        }

        if (current == '`')
            state = PatternScanState.TemplateLiteral;
    }

    private static bool ContainsHostBindingCandidate(ReadOnlySpan<char> source)
    {
        const string hostBindingPrefix = "host.";

        return source.IndexOf(hostBindingPrefix, System.StringComparison.Ordinal) >= 0;
    }

    private static bool ContainsConditionalOperatorCandidate(ReadOnlySpan<char> source)
    {
        if (source.IndexOf('?') < 0 || source.IndexOf(':') < 0)
            return false;

        var hasConditionalQuestion = false;

        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (current == '?' && next is not '?' and not '.')
            {
                hasConditionalQuestion = true;
                continue;
            }

            if (hasConditionalQuestion && current == ':')
                return true;
        }

        return false;
    }

    private static bool ContainsDestructuringPatternCandidate(ReadOnlySpan<char> source)
    {
        var trimmed = TrimLeadingWhiteSpace(source);

        if (!StartsWith(trimmed, "const {")
            && !StartsWith(trimmed, "let {")
            && !StartsWith(trimmed, "var {")
            && !StartsWith(trimmed, "const [")
            && !StartsWith(trimmed, "let [")
            && !StartsWith(trimmed, "var [")
            && !StartsWith(trimmed, "({")
            && !StartsWith(trimmed, "(["))
        {
            return false;
        }

        if (trimmed.IndexOf('=') < 0)
            return false;

        return true;
    }

    private static bool IsObjectLiteralOpeningContext(ReadOnlySpan<char> source, int previousSignificantIndex, char previousSignificant)
    {
        if (previousSignificantIndex < 0)
            return true;

        if (previousSignificant is '=' or '(' or '[' or ',' or ':' or '?' or '+' or '-' or '*' or '%' or '<' or '>' or '&' or '|' or '^' or '!')
            return true;

        if (IsLogicalExpressionBoundary(source, previousSignificantIndex))
            return true;

        return HasExpressionLeadingKeywordContext(source[..(previousSignificantIndex + 1)]);
    }

    private static bool HasExpressionLeadingKeywordContext(ReadOnlySpan<char> source)
    {
        var token = GetPreviousIdentifierToken(source, source.Length);

        return token.SequenceEqual("return")
            || token.SequenceEqual("throw")
            || token.SequenceEqual("case")
            || token.SequenceEqual("await")
            || token.SequenceEqual("yield")
            || token.SequenceEqual("in")
            || token.SequenceEqual("instanceof")
            || token.SequenceEqual("delete")
            || token.SequenceEqual("typeof")
            || token.SequenceEqual("void");
    }

    private static bool IsLogicalExpressionBoundary(ReadOnlySpan<char> source, int previousSignificantIndex)
    {
        var previousSignificant = source[previousSignificantIndex];
        var beforePreviousIndex = GetPreviousSignificantIndex(source, previousSignificantIndex - 1);

        if (beforePreviousIndex < 0)
            return false;

        var beforePrevious = source[beforePreviousIndex];

        return (previousSignificant == '&' && beforePrevious == '&')
            || (previousSignificant == '|' && beforePrevious == '|')
            || (previousSignificant == '?' && beforePrevious == '?');
    }

    private static bool HasObjectLiteralBody(ReadOnlySpan<char> source)
    {
        var index = 0;

        while (index < source.Length)
        {
            var current = source[index];

            if (IsParserWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '}')
                return true;

            if (current == ':' || (current == '.' && index + 2 < source.Length && source[index + 1] == '.' && source[index + 2] == '.'))
                return true;

            if (!IsIdentifierStart(current))
            {
                index++;
                continue;
            }

            if (TryGetObjectLiteralPropertyTerminator(source, index + 1, out var terminatorIndex)
                && source[terminatorIndex] is ':' or ',' or '}')
            {
                return true;
            }

            index = terminatorIndex;
        }

        return false;
    }

    private static bool TryGetObjectLiteralPropertyTerminator(ReadOnlySpan<char> source, int index, out int terminatorIndex)
    {
        while (index < source.Length && IsIdentifierPart(source[index]))
            index++;

        while (index < source.Length && IsParserWhiteSpace(source[index]))
            index++;

        terminatorIndex = index;
        return index < source.Length;
    }

    private static bool TryGetArrayLiteralElementTerminator(ReadOnlySpan<char> source, int index, out int terminatorIndex)
    {
        while (index < source.Length && IsIdentifierPart(source[index]))
            index++;

        while (index < source.Length && IsParserWhiteSpace(source[index]))
            index++;

        terminatorIndex = index;
        return index < source.Length && source[index] is ',' or ']';
    }

    private static ReadOnlySpan<char> TrimLeadingWhiteSpace(ReadOnlySpan<char> source)
    {
        var index = 0;

        while (index < source.Length && IsParserWhiteSpace(source[index]))
            index++;

        return source[index..];
    }

    private static bool StartsWith(ReadOnlySpan<char> source, string value)
        => source.StartsWith(value, System.StringComparison.Ordinal);

    private static int GetPreviousSignificantIndex(ReadOnlySpan<char> source, int index)
    {
        while (index >= 0)
        {
            if (!IsParserWhiteSpace(source[index]) && source[index] != '`')
                return index;

            index--;
        }

        return -1;
    }

    private static bool IsIdentifierStart(char value)
        => value is '$' or '_'
            || char.IsLetter(value);

    private static bool IsIdentifierPart(char value)
        => value is '$' or '_'
            || char.IsLetterOrDigit(value);

    private static JavaScriptRuntimeParserFeature GetCharacterFeatures(ReadOnlySpan<char> source, int index, char current, char previous, int previousSignificantIndex, char previousSignificant, char next, char next2)
    {
        return GetLexicalFeatures(current)
            | GetStructuralFeatures(source, index, current, previousSignificantIndex, previousSignificant)
            | GetOperatorFeatures(source, index, current, previous, previousSignificant, next, next2);
    }

    private static JavaScriptRuntimeParserFeature GetLexicalFeatures(char current)
    {
        var parserFeatures = JavaScriptRuntimeParserFeature.None;

        if (IsIdentifierStart(current))
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsIdentifierReference;

        if (current is '\'' or '"')
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsStringLiteral;

        if (current == '`')
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsTemplateLiteral;

        if (char.IsDigit(current))
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsNumericLiteral;

        return parserFeatures;
    }

    private static JavaScriptRuntimeParserFeature GetStructuralFeatures(ReadOnlySpan<char> source, int index, char current, int previousSignificantIndex, char previousSignificant)
    {
        var parserFeatures = JavaScriptRuntimeParserFeature.None;

        if (current == '{'
            && IsObjectLiteralOpeningContext(source, previousSignificantIndex, previousSignificant)
            && HasObjectLiteralBody(source[(index + 1)..]))
        {
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsObjectLiteralCandidate;
        }

        if (current == '.' && !IsSpreadDot(source, index))
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsMemberAccess;

        if (current is '(' or ')')
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsInvocation;

        if (current != '[')
            return parserFeatures;

        if (IsArrayLiteralCandidate(source, previousSignificantIndex, previousSignificant))
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsArrayLiteralCandidate;
        else
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsIndexAccess;

        return parserFeatures;
    }

    private static bool IsSpreadDot(ReadOnlySpan<char> source, int index)
        => source[index] == '.'
            && ((index > 0 && source[index - 1] == '.')
                || (index + 1 < source.Length && source[index + 1] == '.'));

    private static JavaScriptRuntimeParserFeature GetOperatorFeatures(ReadOnlySpan<char> source, int index, char current, char previous, char previousSignificant, char next, char next2)
        => current switch
        {
            '!' => GetBangFeatures(next),
            '~' => JavaScriptRuntimeParserFeature.ContainsUnaryOperator,
            '+' or '-' or '*' or '%' => JavaScriptRuntimeParserFeature.ContainsBinaryOperator,
            '/' => GetSlashFeatures(source, index, previousSignificant, next),
            '<' or '>' => GetAngleBracketFeatures(previous),
            '=' => GetEqualsFeatures(previous, next),
            '&' or '|' => GetLogicalPairFeatures(current, next),
            '?' => GetQuestionMarkFeatures(next),
            '.' => GetDotFeatures(next, next2),
            _ => JavaScriptRuntimeParserFeature.None,
        };

    private static JavaScriptRuntimeParserFeature GetBangFeatures(char next)
    {
        var parserFeatures = JavaScriptRuntimeParserFeature.ContainsUnaryOperator;

        if (next == '=')
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsComparisonOperator;

        return parserFeatures;
    }

    private static JavaScriptRuntimeParserFeature GetSlashFeatures(ReadOnlySpan<char> source, int index, char previousSignificant, char next)
        => IsRegularExpressionLiteralStart(source, index, previousSignificant, next)
            ? JavaScriptRuntimeParserFeature.None
            : JavaScriptRuntimeParserFeature.ContainsBinaryOperator;

    private static JavaScriptRuntimeParserFeature GetAngleBracketFeatures(char previous)
        => previous == '='
            ? JavaScriptRuntimeParserFeature.None
            : JavaScriptRuntimeParserFeature.ContainsComparisonOperator;

    private static JavaScriptRuntimeParserFeature GetEqualsFeatures(char previous, char next)
    {
        if (next == '=')
            return JavaScriptRuntimeParserFeature.ContainsComparisonOperator;

        var parserFeatures = JavaScriptRuntimeParserFeature.None;

        if (previous != '=' && next != '=' && next != '>')
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsAssignment;

        if (next == '>')
            parserFeatures |= JavaScriptRuntimeParserFeature.ContainsArrowFunctionCandidate;

        return parserFeatures;
    }

    private static JavaScriptRuntimeParserFeature GetLogicalPairFeatures(char current, char next)
        => next == current
            ? JavaScriptRuntimeParserFeature.ContainsLogicalOperator
            : JavaScriptRuntimeParserFeature.None;

    private static JavaScriptRuntimeParserFeature GetQuestionMarkFeatures(char next)
    {
        if (next == '?')
        {
            return JavaScriptRuntimeParserFeature.ContainsLogicalOperator
                | JavaScriptRuntimeParserFeature.ContainsNullishCoalescingOperator;
        }

        return next == '.'
            ? JavaScriptRuntimeParserFeature.ContainsOptionalChainingCandidate
            : JavaScriptRuntimeParserFeature.None;
    }

    private static JavaScriptRuntimeParserFeature GetDotFeatures(char next, char next2)
        => next == '.' && next2 == '.'
            ? JavaScriptRuntimeParserFeature.ContainsSpreadOrRestCandidate
            : JavaScriptRuntimeParserFeature.None;

    private static bool IsRegularExpressionLiteralStart(ReadOnlySpan<char> source, int slashIndex, char previousSignificant, char next)
    {
        if (next is '\0' or '/' or '*')
            return false;

        if (previousSignificant is '\0' or '=' or '(' or '[' or '{' or ',' or ':' or ';' or '!' or '?' or '&' or '|' or '+' or '-' or '*' or '%' or '<' or '>' or '^' or '~')
            return true;

        if (!IsIdentifierPart(previousSignificant))
            return false;

        return HasRegularExpressionKeywordContext(source, slashIndex);
    }

    private static bool HasRegularExpressionKeywordContext(ReadOnlySpan<char> source, int slashIndex)
    {
        var token = GetPreviousIdentifierToken(source, slashIndex);

        return token.SequenceEqual("return")
            || token.SequenceEqual("throw")
            || token.SequenceEqual("yield")
            || token.SequenceEqual("await")
            || token.SequenceEqual("typeof")
            || token.SequenceEqual("delete")
            || token.SequenceEqual("void")
            || token.SequenceEqual("case")
            || token.SequenceEqual("in")
            || token.SequenceEqual("instanceof");
    }

    private static ReadOnlySpan<char> GetPreviousIdentifierToken(ReadOnlySpan<char> source, int index)
    {
        var cursor = index - 1;

        while (cursor >= 0 && IsParserWhiteSpace(source[cursor]))
            cursor--;

        var end = cursor;

        while (cursor >= 0 && IsIdentifierPart(source[cursor]))
            cursor--;

        if (end < 0 || end == cursor)
            return [];

        return source[(cursor + 1)..(end + 1)];
    }


    private static bool IsArrayLiteralCandidate(ReadOnlySpan<char> source, int previousSignificantIndex, char previousSignificant)
    {
        if (previousSignificantIndex < 0)
            return true;

        if (previousSignificant is '=' or '(' or ',' or ':' or '?' or '+' or '-' or '*' or '%' or '<' or '>' or '&' or '|' or '^')
            return true;

        if (IsLogicalExpressionBoundary(source, previousSignificantIndex))
            return true;

        return HasExpressionLeadingKeywordContext(source[..(previousSignificantIndex + 1)]);
    }
}