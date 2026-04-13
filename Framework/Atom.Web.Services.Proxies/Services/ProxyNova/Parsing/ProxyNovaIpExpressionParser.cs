using System.Globalization;
using System.Text;

namespace Atom.Web.Proxies.Services;

internal sealed class ProxyNovaIpExpressionParser(string expression)
{
    private readonly string expression = expression;
    private int index;

    public string Parse()
    {
        var value = ParseExpression();
        SkipWhitespace();
        return value.AsString();
    }

    private ScriptValue ParseExpression()
    {
        var value = ParsePrimary();

        while (true)
        {
            SkipWhitespace();
            if (!TryConsume('.'))
            {
                break;
            }

            var identifier = ParseIdentifier();
            value = identifier switch
            {
                "concat" => ParseConcat(value),
                "substring" => ParseSubstring(value),
                "split" => ParseSplit(value),
                "reverse" => ParseReverse(value),
                "join" => ParseJoin(value),
                "repeat" => ParseRepeat(value),
                "map" => ParseMap(value),
                _ => throw new FormatException($"Unsupported ProxyNova expression method '{identifier}'."),
            };
        }

        return value;
    }

    private ScriptValue ParsePrimary()
    {
        SkipWhitespace();

        if (LookAhead("atob("))
        {
            index += "atob(".Length;
            var encoded = ParseStringLiteral();
            Expect(')');
            return new ScriptValue(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
        }

        if (Current == '"')
        {
            return new ScriptValue(ParseStringLiteral());
        }

        if (Current == '[')
        {
            return new ScriptValue(ParseNumberArray());
        }

        throw new FormatException("Unsupported ProxyNova expression primary.");
    }

    private ScriptValue ParseConcat(ScriptValue value)
    {
        Expect('(');
        using var builder = new Atom.Text.ValueStringBuilder(value.AsString());
        var first = true;
        while (true)
        {
            if (!first)
            {
                Expect(',');
            }

            builder.Append(ParseExpression().AsString());
            first = false;
            SkipWhitespace();
            if (TryConsume(')'))
            {
                break;
            }
        }

        return new ScriptValue(builder.ToString());
    }

    private ScriptValue ParseSubstring(ScriptValue value)
    {
        Expect('(');
        var start = ParseIntegerExpression();
        int? end = null;
        SkipWhitespace();
        if (TryConsume(','))
        {
            end = ParseIntegerExpression();
        }

        Expect(')');

        var text = value.AsString();
        start = Math.Clamp(start, 0, text.Length);
        var boundedEnd = Math.Clamp(end ?? text.Length, start, text.Length);
        return new ScriptValue(text[start..boundedEnd]);
    }

    private ScriptValue ParseSplit(ScriptValue value)
    {
        Expect('(');
        var separator = ParseStringLiteral();
        Expect(')');

        var text = value.AsString();
        return separator.Length == 0
            ? new ScriptValue(SplitCharacters(text))
            : new ScriptValue(SplitString(text, separator));
    }

    private ScriptValue ParseReverse(ScriptValue value)
    {
        Expect('(');
        Expect(')');

        var array = value.AsArray();
        array.Reverse();
        return new ScriptValue(array);
    }

    private ScriptValue ParseJoin(ScriptValue value)
    {
        Expect('(');
        var separator = ParseStringLiteral();
        Expect(')');

        return new ScriptValue(JoinStrings(value.AsArray(), separator));
    }

    private ScriptValue ParseRepeat(ScriptValue value)
    {
        Expect('(');
        var count = ParseIntegerExpression();
        Expect(')');

        if (count <= 0)
        {
            return new ScriptValue(string.Empty);
        }

        var text = value.AsString();
        using var builder = new Atom.Text.ValueStringBuilder(text.Length * count);
        for (var iteration = 0; iteration < count; iteration++)
        {
            builder.Append(text);
        }

        return new ScriptValue(builder.ToString());
    }

    private ScriptValue ParseMap(ScriptValue value)
    {
        Expect('(');
        SkipWhitespace();
        Expect('(');
        var parameter = ParseIdentifier();
        Expect(')');
        SkipWhitespace();
        Expect('=');
        Expect('>');
        SkipWhitespace();

        var offset = ParseCharCodeExpression(parameter);
        Expect(')');

        var numbers = value.AsNumbers();
        var mapped = new List<string>(numbers.Count);
        foreach (var number in numbers)
        {
            mapped.Add(((char)(number + offset)).ToString());
        }

        return new ScriptValue(mapped);
    }

    private static List<string> SplitCharacters(string text)
    {
        var result = new List<string>(text.Length);
        foreach (var character in text)
        {
            result.Add(character.ToString());
        }

        return result;
    }

    private static List<string> SplitString(string text, string separator)
    {
        var result = new List<string>();
        var startIndex = 0;

        while (true)
        {
            var separatorIndex = text.IndexOf(separator, startIndex, StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                result.Add(text[startIndex..]);
                return result;
            }

            result.Add(text[startIndex..separatorIndex]);
            startIndex = separatorIndex + separator.Length;
        }
    }

    private static string JoinStrings(List<string> values, string separator)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        if (values.Count == 1)
        {
            return values[0];
        }

        using var builder = new Atom.Text.ValueStringBuilder();
        builder.Append(values[0]);
        for (var index = 1; index < values.Count; index++)
        {
            builder.Append(separator);
            builder.Append(values[index]);
        }

        return builder.ToString();
    }

    private int ParseCharCodeExpression(string parameter)
    {
        if (!LookAhead("String.fromCharCode("))
        {
            throw new FormatException("Unsupported ProxyNova map expression.");
        }

        index += "String.fromCharCode(".Length;
        SkipWhitespace();
        var identifier = ParseIdentifier();
        if (!string.Equals(identifier, parameter, StringComparison.Ordinal))
        {
            throw new FormatException("Unsupported ProxyNova map expression parameter.");
        }

        var operation = ParseOperator();
        var offset = ParseIntegerExpression();
        Expect(')');

        return operation switch
        {
            '+' => offset,
            '-' => -offset,
            _ => throw new FormatException("Unsupported ProxyNova map expression operator."),
        };
    }

    private int ParseIntegerExpression()
    {
        SkipWhitespace();
        var value = ParseInteger();

        while (true)
        {
            SkipWhitespace();
            if (!TryConsume('+') && !TryConsume('-'))
            {
                break;
            }

            var operation = expression[index - 1];
            var right = ParseInteger();
            value = operation == '+' ? value + right : value - right;
        }

        return value;
    }

    private List<int> ParseNumberArray()
    {
        Expect('[');
        var numbers = new List<int>();
        SkipWhitespace();
        if (TryConsume(']'))
        {
            return numbers;
        }

        while (true)
        {
            numbers.Add(ParseInteger());
            SkipWhitespace();
            if (TryConsume(']'))
            {
                break;
            }

            Expect(',');
        }

        return numbers;
    }

    private int ParseInteger()
    {
        SkipWhitespace();
        var start = index;
        if (TryConsume('-'))
        {
            start = index - 1;
        }

        while (index < expression.Length && char.IsDigit(expression[index]))
        {
            index++;
        }

        if (start == index)
        {
            throw new FormatException("Expected integer literal.");
        }

        return int.Parse(expression[start..index], CultureInfo.InvariantCulture);
    }

    private string ParseStringLiteral()
    {
        Expect('"');
        using var builder = new Atom.Text.ValueStringBuilder();
        while (index < expression.Length)
        {
            var character = expression[index++];
            if (character == '"')
            {
                return builder.ToString();
            }

            if (character == '\\' && index < expression.Length)
            {
                var escaped = expression[index++];
                builder.Append(escaped switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => escaped,
                });
                continue;
            }

            builder.Append(character);
        }

        throw new FormatException("Unterminated string literal.");
    }

    private string ParseIdentifier()
    {
        SkipWhitespace();
        var start = index;
        while (index < expression.Length && (char.IsLetterOrDigit(expression[index]) || expression[index] is '_' or '$'))
        {
            index++;
        }

        if (start == index)
        {
            throw new FormatException("Expected identifier.");
        }

        return expression[start..index];
    }

    private char ParseOperator()
    {
        SkipWhitespace();
        if (TryConsume('+'))
        {
            return '+';
        }

        if (TryConsume('-'))
        {
            return '-';
        }

        throw new FormatException("Expected operator.");
    }

    private void SkipWhitespace()
    {
        while (index < expression.Length && char.IsWhiteSpace(expression[index]))
        {
            index++;
        }
    }

    private bool LookAhead(string value)
        => expression.AsSpan(index).StartsWith(value, StringComparison.Ordinal);

    private bool TryConsume(char character)
    {
        SkipWhitespace();
        if (index >= expression.Length || expression[index] != character)
        {
            return false;
        }

        index++;
        return true;
    }

    private void Expect(char character)
    {
        if (!TryConsume(character))
        {
            throw new FormatException($"Expected '{character}'.");
        }
    }

    private char Current => index < expression.Length ? expression[index] : '\0';

    private readonly struct ScriptValue
    {
        private readonly string? stringValue;
        private readonly List<string>? arrayValue;
        private readonly List<int>? numbersValue;

        public ScriptValue(string value)
        {
            stringValue = value;
        }

        public ScriptValue(List<string> value) : this()
            => arrayValue = value;

        public ScriptValue(List<int> value) : this()
            => numbersValue = value;

        public string AsString()
        {
            if (stringValue is not null)
            {
                return stringValue;
            }

            if (arrayValue is not null)
            {
                return string.Concat(arrayValue);
            }

            var numbers = numbersValue ?? throw new FormatException("Expected scalar value.");
            using var builder = new Atom.Text.ValueStringBuilder(numbers.Count * 3);
            foreach (var number in numbers)
            {
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        public List<string> AsArray()
            => arrayValue ?? throw new FormatException("Expected string array value.");

        public List<int> AsNumbers()
            => numbersValue ?? throw new FormatException("Expected numeric array value.");
    }
}