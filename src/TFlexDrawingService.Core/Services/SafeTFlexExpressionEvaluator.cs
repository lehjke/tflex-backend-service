using System.Globalization;
using System.Text;

namespace TFlexDrawingService.Core.Services;

/// <summary>
/// Evaluates the deliberately small, side-effect-free subset of T-FLEX expressions
/// used by calculated variables and validation rules. Unsupported syntax and missing
/// variables produce an indeterminate result; callers must treat an indeterminate
/// validation rule as a catalogue error rather than accepting the request.
/// </summary>
internal static class SafeTFlexExpressionEvaluator
{
    private const int MaxExpressionLength = 8192;
    private const int MaxEvaluatedStringLength = 64 * 1024;
    private const int MaxStringAllocationWork = 256 * 1024;

    public static bool TryEvaluateExpression(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        out object? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaxExpressionLength)
        {
            return false;
        }

        try
        {
            var value = new Parser(TruncateTopLevelSemicolon(expression), variables).Parse();
            if (!value.IsKnown)
            {
                return false;
            }

            if (value.Raw is string text && text.Length > MaxEvaluatedStringLength)
            {
                return false;
            }

            result = value.Raw;
            return true;
        }
        catch (ExpressionException)
        {
            return false;
        }
        catch (ArithmeticException)
        {
            return false;
        }
    }

    public static bool TryEvaluateRule(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        out bool passed)
    {
        return TryEvaluateRuleCore(expression, variables, null, out passed);
    }

    internal static bool TryEvaluateRule(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        IReadOnlyDictionary<string, object?> overrides,
        out bool passed)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return TryEvaluateRuleCore(expression, variables, overrides, out passed);
    }

    private static bool TryEvaluateRuleCore(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        IReadOnlyDictionary<string, object?>? overrides,
        out bool passed)
    {
        passed = false;
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaxExpressionLength)
        {
            return false;
        }

        try
        {
            var value = new Parser(
                TruncateTopLevelSemicolon(expression),
                variables,
                overrides).Parse();
            if (value.Raw is string text && text.Length > MaxEvaluatedStringLength)
            {
                return false;
            }

            return TryGetRuleTruth(value, out passed);
        }
        catch (ExpressionException)
        {
            return false;
        }
        catch (ArithmeticException)
        {
            return false;
        }
    }

    public static bool TryValidateSupportedSyntax(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaxExpressionLength)
        {
            return false;
        }

        try
        {
            var parser = new Parser(
                TruncateTopLevelSemicolon(expression),
                new Dictionary<string, object?>());
            parser.Parse();
            return !parser.HasUnsupportedFunction;
        }
        catch (ExpressionException)
        {
            return false;
        }
        catch (ArithmeticException)
        {
            return false;
        }
    }

    private static string TruncateTopLevelSemicolon(string expression)
    {
        var depth = 0;
        var quote = '\0';

        for (var index = 0; index < expression.Length; index++)
        {
            var current = expression[index];
            var previous = index == 0 ? '\0' : expression[index - 1];
            if (quote != '\0')
            {
                if (current == quote && previous != '\\')
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
            }
            else if (current == '(')
            {
                depth++;
            }
            else if (current == ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (current == ';' && depth == 0)
            {
                return expression[..index].Trim();
            }
        }

        return expression;
    }

    private static bool TryGetRuleTruth(Value value, out bool result)
    {
        result = false;
        if (!value.IsKnown)
        {
            return false;
        }

        switch (value.Raw)
        {
            case bool boolean:
                result = boolean;
                return true;
            case string text:
                var normalized = text.Trim();
                result = normalized.Length > 0
                    && !string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalized, "нет", StringComparison.OrdinalIgnoreCase);
                return true;
            default:
                if (TryGetNumber(value, out var number))
                {
                    result = number != 0;
                    return true;
                }

                return false;
        }
    }

    private static bool TryGetCondition(Value value, out bool result)
    {
        result = false;
        if (!value.IsKnown)
        {
            return false;
        }

        if (value.Raw is bool boolean)
        {
            result = boolean;
            return true;
        }

        if (TryGetNumber(value, out var number))
        {
            result = number != 0;
            return true;
        }

        if (value.Raw is string text)
        {
            result = text.Length > 0;
            return true;
        }

        return false;
    }

    private static bool TryGetNumber(Value value, out decimal number)
    {
        number = default;
        if (!value.IsKnown)
        {
            return false;
        }

        try
        {
            switch (value.Raw)
            {
                case decimal decimalValue:
                    number = decimalValue;
                    return true;
                case bool boolean:
                    number = boolean ? 1m : 0m;
                    return true;
                case byte or sbyte or short or ushort or int or uint or long or ulong:
                    number = Convert.ToDecimal(value.Raw, CultureInfo.InvariantCulture);
                    return true;
                case float floatValue when float.IsFinite(floatValue):
                    number = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
                    return true;
                case double doubleValue when double.IsFinite(doubleValue):
                    number = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                    return true;
                case string text:
                    return decimal.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out number);
                default:
                    return false;
            }
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string ToInvariantText(Value value)
    {
        return value.Raw switch
        {
            null => string.Empty,
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.Raw.ToString() ?? string.Empty
        };
    }

    private readonly record struct Value(bool IsKnown, object? Raw)
    {
        public static Value Unknown => new(false, null);

        public static Value Known(object value) => new(true, value);
    }

    private sealed class Parser
    {
        private const int MaxTokens = 4096;
        private const int MaxDepth = 64;

        private readonly Lexer _lexer;
        private readonly IReadOnlyDictionary<string, object?> _variables;
        private readonly IReadOnlyDictionary<string, object?>? _overrides;
        private Token _current;
        private int _tokenCount;
        private int _depth;
        private int _stringAllocationWork;

        public bool HasUnsupportedFunction { get; private set; }

        public Parser(
            string expression,
            IReadOnlyDictionary<string, object?> variables,
            IReadOnlyDictionary<string, object?>? overrides = null)
        {
            _lexer = new Lexer(expression);
            _variables = variables;
            _overrides = overrides;

            Advance();
        }

        public Value Parse()
        {
            var result = ParseConditional();
            Expect(TokenKind.End);
            return result;
        }

        private Value ParseConditional()
        {
            var condition = ParseOr();
            if (!Match(TokenKind.Question))
            {
                return condition;
            }

            EnterNestedExpression();
            try
            {
                var whenTrue = ParseConditional();
                Expect(TokenKind.Colon);
                var whenFalse = ParseConditional();

                if (TryGetCondition(condition, out var conditionValue))
                {
                    return conditionValue ? whenTrue : whenFalse;
                }

                return AreEquivalent(whenTrue, whenFalse) ? whenTrue : Value.Unknown;
            }
            finally
            {
                _depth--;
            }
        }

        private Value ParseOr()
        {
            var left = ParseAnd();
            while (Match(TokenKind.OrOr))
            {
                left = ApplyOr(left, ParseAnd());
            }

            return left;
        }

        private Value ParseAnd()
        {
            var left = ParseEquality();
            while (Match(TokenKind.AndAnd))
            {
                left = ApplyAnd(left, ParseEquality());
            }

            return left;
        }

        private Value ParseEquality()
        {
            var left = ParseRelational();
            while (_current.Kind is TokenKind.EqualEqual or TokenKind.BangEqual)
            {
                var operation = _current.Kind;
                Advance();
                var equal = ApplyEquality(left, ParseRelational());
                left = operation == TokenKind.BangEqual ? ApplyNot(equal) : equal;
            }

            return left;
        }

        private Value ParseRelational()
        {
            var left = ParseAdditive();
            while (_current.Kind is TokenKind.Less
                or TokenKind.LessEqual
                or TokenKind.Greater
                or TokenKind.GreaterEqual)
            {
                var operation = _current.Kind;
                Advance();
                left = ApplyRelational(operation, left, ParseAdditive());
            }

            return left;
        }

        private Value ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (_current.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var operation = _current.Kind;
                Advance();
                left = ApplyArithmetic(operation, left, ParseMultiplicative());
            }

            return left;
        }

        private Value ParseMultiplicative()
        {
            var left = ParsePower();
            while (_current.Kind is TokenKind.Star
                or TokenKind.DoubleStar
                or TokenKind.Slash
                or TokenKind.Percent)
            {
                var operation = _current.Kind;
                Advance();
                left = ApplyArithmetic(operation, left, ParsePower());
            }

            return left;
        }

        private Value ParsePower()
        {
            var left = ParseUnary();
            if (!Match(TokenKind.Caret))
            {
                return left;
            }

            return ApplyPower(left, ParsePower());
        }

        private Value ParseUnary()
        {
            if (_current.Kind is not (TokenKind.Bang or TokenKind.Plus or TokenKind.Minus))
            {
                return ParsePrimary();
            }

            var operation = _current.Kind;
            Advance();
            EnterNestedExpression();
            try
            {
                var operand = ParseUnary();
                return operation switch
                {
                    TokenKind.Bang => ApplyNot(operand),
                    TokenKind.Plus => TryGetNumber(operand, out var positive)
                        ? Value.Known(positive)
                        : Value.Unknown,
                    TokenKind.Minus => TryGetNumber(operand, out var negative)
                        ? Negate(negative)
                        : Value.Unknown,
                    _ => Value.Unknown
                };
            }
            finally
            {
                _depth--;
            }
        }

        private Value ParsePrimary()
        {
            if (_current.Kind == TokenKind.Number)
            {
                var number = _current.Number;
                Advance();
                return Value.Known(number);
            }

            if (_current.Kind == TokenKind.String)
            {
                var text = _current.Text;
                Advance();
                return Value.Known(text);
            }

            if (_current.Kind == TokenKind.Identifier)
            {
                var name = _current.Text;
                Advance();
                return Match(TokenKind.LeftParenthesis)
                    ? ParseFunction(name)
                    : ResolveIdentifier(name);
            }

            if (Match(TokenKind.LeftParenthesis))
            {
                EnterNestedExpression();
                try
                {
                    var value = ParseConditional();
                    Expect(TokenKind.RightParenthesis);
                    return value;
                }
                finally
                {
                    _depth--;
                }
            }

            throw new ExpressionException();
        }

        private Value ParseFunction(string name)
        {
            EnterNestedExpression();
            try
            {
                var arguments = new List<Value>();
                if (!Match(TokenKind.RightParenthesis))
                {
                    do
                    {
                        arguments.Add(ParseConditional());
                    }
                    while (Match(TokenKind.Comma));

                    Expect(TokenKind.RightParenthesis);
                }

                if (!IsSupportedFunction(name, arguments.Count))
                {
                    HasUnsupportedFunction = true;
                }

                return EvaluateFunction(name, arguments);
            }
            finally
            {
                _depth--;
            }
        }

        private static bool IsSupportedFunction(string name, int argumentCount)
        {
            return name.ToLowerInvariant() switch
            {
                "error" or "warn" => true,
                "select" => argumentCount >= 1,
                "switch" => argumentCount >= 2,
                "atof" or "ftoa" => argumentCount == 1,
                "val" => argumentCount is 1 or 2,
                "tpart" => argumentCount is 2 or 3,
                "ltot" => argumentCount is >= 1 and <= 4,
                "abs" or "floor" or "sin" or "cos" or "tan" => argumentCount == 1,
                "ceil" or "round" => argumentCount is 1 or 2,
                "min" or "max" => argumentCount >= 1,
                "ch_level" or "ch_front" or "ch_rear" => argumentCount == 2,
                "ch_lvl" or "ch_hf" or "ch_maxhf" => argumentCount == 3,
                "ch_thf" or "ch_maxthf" => argumentCount == 2,
                _ => false
            };
        }

        private Value ResolveIdentifier(string name)
        {
            if (string.Equals(name, "true", StringComparison.OrdinalIgnoreCase))
            {
                return Value.Known(true);
            }

            if (string.Equals(name, "false", StringComparison.OrdinalIgnoreCase))
            {
                return Value.Known(false);
            }

            var resolution = ResolveVariable(name, out var value);
            if (resolution == VariableResolution.Found)
            {
                return Value.Known(value);
            }

            if (resolution == VariableResolution.Ambiguous)
            {
                return Value.Unknown;
            }

            var alias = name.StartsWith('$') ? name[1..] : $"${name}";
            return ResolveVariable(alias, out value) == VariableResolution.Found
                ? Value.Known(value)
                : Value.Unknown;
        }

        private Value EvaluateFunction(string name, IReadOnlyList<Value> arguments)
        {
            if (string.Equals(name, "error", StringComparison.OrdinalIgnoreCase))
            {
                return Value.Known(0m);
            }

            if (string.Equals(name, "warn", StringComparison.OrdinalIgnoreCase))
            {
                return Value.Known(1m);
            }

            if (string.Equals(name, "select", StringComparison.OrdinalIgnoreCase))
            {
                return EvaluateSelect(arguments);
            }

            if (string.Equals(name, "switch", StringComparison.OrdinalIgnoreCase))
            {
                return EvaluateSwitch(arguments);
            }

            if (arguments.Count == 1
                && (string.Equals(name, "atof", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "val", StringComparison.OrdinalIgnoreCase)))
            {
                return TryGetNumber(arguments[0], out var parsed)
                    ? Value.Known(parsed)
                    : arguments[0].IsKnown
                        ? Value.Known(0m)
                        : Value.Unknown;
            }

            if (string.Equals(name, "val", StringComparison.OrdinalIgnoreCase)
                && arguments.Count == 2)
            {
                return arguments[1].IsKnown && TryGetNumber(arguments[1], out var lookupValue)
                    ? Value.Known(lookupValue)
                    : TryGetNumber(arguments[0], out var fallback)
                        ? Value.Known(fallback)
                        : Value.Unknown;
            }

            if (string.Equals(name, "ftoa", StringComparison.OrdinalIgnoreCase)
                && arguments.Count == 1
                && arguments[0].IsKnown)
            {
                return Value.Known(ToInvariantText(arguments[0]));
            }

            if (string.Equals(name, "tpart", StringComparison.OrdinalIgnoreCase)
                && arguments.Count is 2 or 3
                && arguments[0].IsKnown
                && TryGetNumber(arguments[1], out var start))
            {
                var text = ToInvariantText(arguments[0]);
                var offset = Math.Max(0, decimal.ToInt32(decimal.Truncate(start)) - 1);
                if (offset >= text.Length)
                {
                    return Value.Known(string.Empty);
                }

                if (arguments.Count == 2)
                {
                    if (text.Length - offset > MaxEvaluatedStringLength)
                    {
                        return Value.Unknown;
                    }

                    return Value.Known(text[offset..].Trim());
                }

                if (!TryGetNumber(arguments[2], out var length))
                {
                    return Value.Unknown;
                }

                var count = decimal.ToInt32(decimal.Truncate(length));
                var selectedLength = count <= 0
                    ? text.Length - offset
                    : Math.Min(count, text.Length - offset);
                if (selectedLength > MaxEvaluatedStringLength)
                {
                    return Value.Unknown;
                }

                return Value.Known(
                    count <= 0
                        ? text[offset..].Trim()
                        : text.Substring(offset, Math.Min(count, text.Length - offset)).Trim());
            }

            if (string.Equals(name, "ltot", StringComparison.OrdinalIgnoreCase)
                && arguments.Count is >= 1 and <= 4
                && TryGetNumber(arguments[0], out var textNumber))
            {
                var precision = 3;
                if (arguments.Count >= 4 && TryGetNumber(arguments[3], out var precisionValue))
                {
                    precision = Math.Clamp(decimal.ToInt32(decimal.Truncate(precisionValue)), 0, 28);
                }

                return Value.Known(textNumber.ToString($"F{precision}", CultureInfo.InvariantCulture));
            }

            if (arguments.Count == 1 && TryGetNumber(arguments[0], out var number))
            {
                try
                {
                    if (string.Equals(name, "abs", StringComparison.OrdinalIgnoreCase))
                    {
                        return Value.Known(decimal.Abs(number));
                    }

                    if (string.Equals(name, "floor", StringComparison.OrdinalIgnoreCase))
                    {
                        return Value.Known(decimal.Floor(number));
                    }

                    if (string.Equals(name, "ceil", StringComparison.OrdinalIgnoreCase))
                    {
                        return Value.Known(decimal.Ceiling(number));
                    }

                    if (string.Equals(name, "sin", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "cos", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "tan", StringComparison.OrdinalIgnoreCase))
                    {
                        var radians = (double)number * Math.PI / 180d;
                        var result = string.Equals(name, "sin", StringComparison.OrdinalIgnoreCase)
                            ? Math.Sin(radians)
                            : string.Equals(name, "cos", StringComparison.OrdinalIgnoreCase)
                                ? Math.Cos(radians)
                                : Math.Tan(radians);
                        return double.IsFinite(result)
                            ? Value.Known(Convert.ToDecimal(result, CultureInfo.InvariantCulture))
                            : Value.Unknown;
                    }

                    if (string.Equals(name, "round", StringComparison.OrdinalIgnoreCase))
                    {
                        return Value.Known(decimal.Round(number, 0, MidpointRounding.AwayFromZero));
                    }
                }
                catch (OverflowException)
                {
                    return Value.Unknown;
                }
            }

            if (string.Equals(name, "ceil", StringComparison.OrdinalIgnoreCase)
                && arguments.Count == 2
                && TryGetNumber(arguments[0], out var ceilValue)
                && TryGetNumber(arguments[1], out var ceilStep)
                && ceilStep != 0)
            {
                var increment = decimal.Abs(ceilStep);
                return Value.Known(decimal.Ceiling(ceilValue / increment) * increment);
            }

            if (string.Equals(name, "round", StringComparison.OrdinalIgnoreCase)
                && arguments.Count == 2
                && TryGetNumber(arguments[0], out var roundValue)
                && TryGetNumber(arguments[1], out var step)
                && step != 0)
            {
                try
                {
                    var increment = decimal.Abs(step);
                    return Value.Known(
                        decimal.Round(roundValue / increment, 0, MidpointRounding.AwayFromZero) * increment);
                }
                catch (OverflowException)
                {
                    return Value.Unknown;
                }
            }

            if ((string.Equals(name, "min", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "max", StringComparison.OrdinalIgnoreCase))
                && arguments.Count > 0)
            {
                decimal? result = null;
                foreach (var argument in arguments)
                {
                    if (!TryGetNumber(argument, out var argumentValue))
                    {
                        return Value.Unknown;
                    }

                    result = result is null
                        ? argumentValue
                        : string.Equals(name, "min", StringComparison.OrdinalIgnoreCase)
                            ? decimal.Min(result.Value, argumentValue)
                            : decimal.Max(result.Value, argumentValue);
                }

                return Value.Known(result!.Value);
            }

            var customResult = EvaluateTemplateHelper(name, arguments);
            if (customResult.IsKnown)
            {
                return customResult;
            }

            return Value.Unknown;
        }

        private static Value EvaluateSwitch(IReadOnlyList<Value> arguments)
        {
            if (arguments.Count < 2 || !arguments[0].IsKnown)
            {
                return Value.Unknown;
            }

            for (var index = 1; index + 1 < arguments.Count; index += 2)
            {
                var equal = ApplyEquality(arguments[0], arguments[index]);
                if (!equal.IsKnown)
                {
                    return Value.Unknown;
                }

                if (equal.Raw is true)
                {
                    return arguments[index + 1];
                }
            }

            return arguments.Count % 2 == 0 ? arguments[^1] : Value.Unknown;
        }

        private static Value EvaluateSelect(IReadOnlyList<Value> arguments)
        {
            var unknownConditionSeen = false;
            for (var index = 0; index + 1 < arguments.Count; index += 2)
            {
                if (!TryGetCondition(arguments[index], out var condition))
                {
                    unknownConditionSeen = true;
                    continue;
                }

                if (condition)
                {
                    return unknownConditionSeen ? Value.Unknown : arguments[index + 1];
                }
            }

            if (unknownConditionSeen)
            {
                return Value.Unknown;
            }

            return arguments.Count % 2 == 1 ? arguments[^1] : Value.Unknown;
        }

        private Value EvaluateTemplateHelper(string name, IReadOnlyList<Value> arguments)
        {
            if (string.Equals(name, "ch_level", StringComparison.OrdinalIgnoreCase)
                && arguments.Count == 2
                && TryGetNumber(arguments[0], out var level)
                && TryGetNumber(arguments[1], out var levelIndex)
                && TryGetVariableNumber("stops", out var stops))
            {
                var index = decimal.ToInt32(decimal.Truncate(levelIndex));
                if (index < stops)
                {
                    return Value.Known(level);
                }

                if (index == stops && TryGetVariableNumber("s_top_level_1", out var topLevel))
                {
                    return Value.Known(topLevel);
                }

                return Value.Known(0m);
            }

            if ((string.Equals(name, "ch_front", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "ch_rear", StringComparison.OrdinalIgnoreCase))
                && arguments.Count == 2
                && TryGetNumber(arguments[0], out var stopIndex)
                && TryGetNumber(arguments[1], out var enabled)
                && TryGetVariableNumber("stops", out var stopCount))
            {
                var index = decimal.ToInt32(decimal.Truncate(stopIndex));
                if (index < stopCount)
                {
                    return Value.Known(enabled == 0 ? 0m : 1m);
                }

                if (index == stopCount)
                {
                    if (string.Equals(name, "ch_front", StringComparison.OrdinalIgnoreCase))
                    {
                        // Every configured top landing has a front opening.
                        return Value.Known(1m);
                    }

                    const string topVariable = "s_top_rear";
                    return TryGetVariableNumber(topVariable, out var topEnabled)
                        ? Value.Known(topEnabled == 0 ? 0m : 1m)
                        : Value.Unknown;
                }

                return Value.Known(0m);
            }

            if (string.Equals(name, "ch_LVL", StringComparison.OrdinalIgnoreCase)
                && arguments.Count == 3
                && TryGetNumber(arguments[0], out var front)
                && TryGetNumber(arguments[1], out var rear)
                && TryGetNumber(arguments[2], out var exitIndex)
                && TryGetVariableNumber("stops", out var exitStopCount))
            {
                var index = decimal.ToInt32(decimal.Truncate(exitIndex));
                return Value.Known(index <= exitStopCount && front == 0 && rear == 0 ? 0m : 1m);
            }

            if ((string.Equals(name, "ch_HF", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "ch_maxHF", StringComparison.OrdinalIgnoreCase))
                && arguments.Count == 3
                && TryGetNumber(arguments[0], out var currentLevel)
                && TryGetNumber(arguments[1], out var nextLevel)
                && TryGetNumber(arguments[2], out var nextIndex)
                && TryGetVariableNumber("stops", out var floorStopCount))
            {
                var index = decimal.ToInt32(decimal.Truncate(nextIndex));
                if (index > floorStopCount)
                {
                    return Value.Known(1m);
                }

                var distance = nextLevel - currentLevel;
                if (string.Equals(name, "ch_maxHF", StringComparison.OrdinalIgnoreCase))
                {
                    return Value.Known(distance <= 11000m ? 1m : 0m);
                }

                return TryGetVariableNumber("S_HF", out var minimumHeight)
                    ? Value.Known(distance >= minimumHeight ? 1m : 0m)
                    : Value.Unknown;
            }

            if ((string.Equals(name, "ch_THF", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "ch_maxTHF", StringComparison.OrdinalIgnoreCase))
                && arguments.Count == 2
                && TryGetNumber(arguments[0], out var candidateLevel)
                && TryGetNumber(arguments[1], out var candidateIndex)
                && TryGetVariableNumber("stops", out var topStopCount)
                && TryGetVariableNumber("s_top_level_1", out var finalLevel))
            {
                var index = decimal.ToInt32(decimal.Truncate(candidateIndex));
                if (index != topStopCount - 1)
                {
                    return Value.Known(1m);
                }

                var distance = finalLevel - candidateLevel;
                if (string.Equals(name, "ch_maxTHF", StringComparison.OrdinalIgnoreCase))
                {
                    return Value.Known(distance <= 11000m ? 1m : 0m);
                }

                return TryGetVariableNumber("S_HF", out var minimumHeight)
                    ? Value.Known(distance >= minimumHeight ? 1m : 0m)
                    : Value.Unknown;
            }

            return Value.Unknown;
        }

        private bool TryGetVariableNumber(string name, out decimal result)
        {
            result = default;
            var resolution = ResolveVariable(name, out var value);
            if (resolution == VariableResolution.Ambiguous)
            {
                return false;
            }

            if (resolution == VariableResolution.Missing)
            {
                var alias = name.StartsWith('$') ? name[1..] : $"${name}";
                if (ResolveVariable(alias, out value) != VariableResolution.Found)
                {
                    return false;
                }
            }

            return TryGetNumber(Value.Known(value), out result);
        }

        private VariableResolution ResolveVariable(string name, out object value)
        {
            if (_overrides is not null
                && TryGetExact(_overrides, name, out value))
            {
                return VariableResolution.Found;
            }

            if (TryGetExact(_variables, name, out value))
            {
                return VariableResolution.Found;
            }

            if (_overrides is not null)
            {
                var overrideResolution = TryGetUniqueCaseInsensitive(
                    _overrides,
                    name,
                    out value);
                if (overrideResolution != VariableResolution.Missing)
                {
                    return overrideResolution;
                }
            }

            return TryGetUniqueCaseInsensitive(_variables, name, out value);
        }

        private static bool TryGetExact(
            IReadOnlyDictionary<string, object?> source,
            string name,
            out object value)
        {
            foreach (var pair in source)
            {
                if (string.Equals(pair.Key, name, StringComparison.Ordinal)
                    && pair.Value is not null)
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null!;
            return false;
        }

        private static VariableResolution TryGetUniqueCaseInsensitive(
            IReadOnlyDictionary<string, object?> source,
            string name,
            out object value)
        {
            var found = false;
            value = null!;

            foreach (var pair in source)
            {
                if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)
                    || pair.Value is null)
                {
                    continue;
                }

                if (found)
                {
                    value = null!;
                    return VariableResolution.Ambiguous;
                }

                found = true;
                value = pair.Value;
            }

            return found ? VariableResolution.Found : VariableResolution.Missing;
        }

        private enum VariableResolution
        {
            Missing,
            Found,
            Ambiguous
        }

        private static Value ApplyAnd(Value left, Value right)
        {
            var leftKnown = TryGetCondition(left, out var leftValue);
            var rightKnown = TryGetCondition(right, out var rightValue);

            if ((leftKnown && !leftValue) || (rightKnown && !rightValue))
            {
                return Value.Known(false);
            }

            return leftKnown && rightKnown ? Value.Known(true) : Value.Unknown;
        }

        private static Value ApplyOr(Value left, Value right)
        {
            var leftKnown = TryGetCondition(left, out var leftValue);
            var rightKnown = TryGetCondition(right, out var rightValue);

            if ((leftKnown && leftValue) || (rightKnown && rightValue))
            {
                return Value.Known(true);
            }

            return leftKnown && rightKnown ? Value.Known(false) : Value.Unknown;
        }

        private static Value ApplyNot(Value value)
        {
            return TryGetCondition(value, out var condition)
                ? Value.Known(!condition)
                : Value.Unknown;
        }

        private static Value ApplyEquality(Value left, Value right)
        {
            if (!left.IsKnown || !right.IsKnown)
            {
                return Value.Unknown;
            }

            if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
            {
                return Value.Known(leftNumber == rightNumber);
            }

            if (left.Raw is bool leftBoolean && right.Raw is bool rightBoolean)
            {
                return Value.Known(leftBoolean == rightBoolean);
            }

            if (left.Raw is string leftText && right.Raw is string rightText)
            {
                return Value.Known(string.Equals(leftText, rightText, StringComparison.Ordinal));
            }

            return Value.Known(false);
        }

        private static Value ApplyRelational(TokenKind operation, Value left, Value right)
        {
            int comparison;
            if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
            {
                comparison = leftNumber.CompareTo(rightNumber);
            }
            else if (left.IsKnown && right.IsKnown && left.Raw is string leftText && right.Raw is string rightText)
            {
                comparison = string.Compare(leftText, rightText, StringComparison.Ordinal);
            }
            else
            {
                return Value.Unknown;
            }

            return Value.Known(operation switch
            {
                TokenKind.Less => comparison < 0,
                TokenKind.LessEqual => comparison <= 0,
                TokenKind.Greater => comparison > 0,
                TokenKind.GreaterEqual => comparison >= 0,
                _ => false
            });
        }

        private Value ApplyArithmetic(TokenKind operation, Value left, Value right)
        {
            if (operation == TokenKind.Plus
                && left.IsKnown
                && right.IsKnown
                && (left.Raw is string || right.Raw is string))
            {
                var leftText = ToInvariantText(left);
                var rightText = ToInvariantText(right);
                if (leftText.Length > MaxEvaluatedStringLength
                    || rightText.Length > MaxEvaluatedStringLength
                    || leftText.Length > MaxEvaluatedStringLength - rightText.Length)
                {
                    throw new ExpressionException();
                }

                var resultLength = leftText.Length + rightText.Length;
                if (_stringAllocationWork > MaxStringAllocationWork - resultLength)
                {
                    throw new ExpressionException();
                }

                _stringAllocationWork += resultLength;
                return Value.Known(string.Concat(leftText, rightText));
            }

            if (!TryGetNumber(left, out var leftNumber) || !TryGetNumber(right, out var rightNumber))
            {
                return Value.Unknown;
            }

            try
            {
                return operation switch
                {
                    TokenKind.Plus => Value.Known(leftNumber + rightNumber),
                    TokenKind.Minus => Value.Known(leftNumber - rightNumber),
                    TokenKind.Star or TokenKind.DoubleStar => Value.Known(leftNumber * rightNumber),
                    TokenKind.Slash when rightNumber != 0 => Value.Known(leftNumber / rightNumber),
                    TokenKind.Percent when rightNumber != 0 => Value.Known(leftNumber % rightNumber),
                    _ => Value.Unknown
                };
            }
            catch (ArithmeticException)
            {
                return Value.Unknown;
            }
        }

        private static Value ApplyPower(Value left, Value right)
        {
            if (!TryGetNumber(left, out var leftNumber) || !TryGetNumber(right, out var rightNumber))
            {
                return Value.Unknown;
            }

            var result = Math.Pow((double)leftNumber, (double)rightNumber);
            if (!double.IsFinite(result))
            {
                return Value.Unknown;
            }

            try
            {
                return Value.Known((decimal)result);
            }
            catch (OverflowException)
            {
                return Value.Unknown;
            }
        }

        private static Value Negate(decimal number)
        {
            try
            {
                return Value.Known(-number);
            }
            catch (OverflowException)
            {
                return Value.Unknown;
            }
        }

        private static bool AreEquivalent(Value left, Value right)
        {
            if (!left.IsKnown || !right.IsKnown)
            {
                return false;
            }

            var comparison = ApplyEquality(left, right);
            return comparison.IsKnown && comparison.Raw is true;
        }

        private bool Match(TokenKind kind)
        {
            if (_current.Kind != kind)
            {
                return false;
            }

            Advance();
            return true;
        }

        private void Expect(TokenKind kind)
        {
            if (!Match(kind))
            {
                throw new ExpressionException();
            }
        }

        private void Advance()
        {
            if (++_tokenCount > MaxTokens)
            {
                throw new ExpressionException();
            }

            _current = _lexer.NextToken();
            if (_current.Kind == TokenKind.Invalid)
            {
                throw new ExpressionException();
            }
        }

        private void EnterNestedExpression()
        {
            if (++_depth > MaxDepth)
            {
                throw new ExpressionException();
            }
        }
    }

    private sealed class Lexer(string expression)
    {
        private int _position;

        public Token NextToken()
        {
            while (_position < expression.Length && char.IsWhiteSpace(expression[_position]))
            {
                _position++;
            }

            if (_position >= expression.Length)
            {
                return new Token(TokenKind.End);
            }

            var current = expression[_position];
            if (char.IsDigit(current) || (current == '.' && PeekIsDigit()))
            {
                return ReadNumber();
            }

            if (current is '\'' or '"')
            {
                return ReadString();
            }

            if (current == '$' || current == '_' || char.IsLetter(current))
            {
                return ReadIdentifier();
            }

            _position++;
            return current switch
            {
                '+' => new Token(TokenKind.Plus),
                '-' => new Token(TokenKind.Minus),
                '*' when Consume('*') => new Token(TokenKind.DoubleStar),
                '*' => new Token(TokenKind.Star),
                '/' => new Token(TokenKind.Slash),
                '%' => new Token(TokenKind.Percent),
                '^' => new Token(TokenKind.Caret),
                '!' when Consume('=') => new Token(TokenKind.BangEqual),
                '!' => new Token(TokenKind.Bang),
                '&' when Consume('&') => new Token(TokenKind.AndAnd),
                '|' when Consume('|') => new Token(TokenKind.OrOr),
                '=' when Consume('=') => new Token(TokenKind.EqualEqual),
                '<' when Consume('=') => new Token(TokenKind.LessEqual),
                '<' => new Token(TokenKind.Less),
                '>' when Consume('=') => new Token(TokenKind.GreaterEqual),
                '>' => new Token(TokenKind.Greater),
                '?' => new Token(TokenKind.Question),
                ':' => new Token(TokenKind.Colon),
                '(' => new Token(TokenKind.LeftParenthesis),
                ')' => new Token(TokenKind.RightParenthesis),
                ',' => new Token(TokenKind.Comma),
                _ => new Token(TokenKind.Invalid)
            };
        }

        private Token ReadNumber()
        {
            var start = _position;
            while (_position < expression.Length && char.IsDigit(expression[_position]))
            {
                _position++;
            }

            if (_position < expression.Length && expression[_position] == '.')
            {
                _position++;
                while (_position < expression.Length && char.IsDigit(expression[_position]))
                {
                    _position++;
                }
            }

            if (_position < expression.Length && expression[_position] is 'e' or 'E')
            {
                _position++;
                if (_position < expression.Length && expression[_position] is '+' or '-')
                {
                    _position++;
                }

                var exponentStart = _position;
                while (_position < expression.Length && char.IsDigit(expression[_position]))
                {
                    _position++;
                }

                if (exponentStart == _position)
                {
                    return new Token(TokenKind.Invalid);
                }
            }

            var text = expression[start.._position];
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? new Token(TokenKind.Number, Number: number)
                : new Token(TokenKind.Invalid);
        }

        private Token ReadString()
        {
            var quote = expression[_position++];
            var builder = new StringBuilder();
            while (_position < expression.Length)
            {
                var current = expression[_position++];
                if (current == quote)
                {
                    return new Token(TokenKind.String, builder.ToString());
                }

                if (current != '\\' || _position >= expression.Length)
                {
                    builder.Append(current);
                    continue;
                }

                var escaped = expression[_position++];
                builder.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => escaped
                });
            }

            return new Token(TokenKind.Invalid);
        }

        private Token ReadIdentifier()
        {
            var start = _position++;
            while (_position < expression.Length)
            {
                var current = expression[_position];
                if (current != '_' && current != '$' && !char.IsLetterOrDigit(current))
                {
                    break;
                }

                _position++;
            }

            return new Token(TokenKind.Identifier, expression[start.._position]);
        }

        private bool PeekIsDigit()
        {
            return _position + 1 < expression.Length && char.IsDigit(expression[_position + 1]);
        }

        private bool Consume(char expected)
        {
            if (_position >= expression.Length || expression[_position] != expected)
            {
                return false;
            }

            _position++;
            return true;
        }
    }

    private readonly record struct Token(TokenKind Kind, string Text = "", decimal Number = default);

    private enum TokenKind
    {
        Invalid,
        End,
        Number,
        String,
        Identifier,
        Plus,
        Minus,
        Star,
        DoubleStar,
        Slash,
        Percent,
        Caret,
        Bang,
        AndAnd,
        OrOr,
        EqualEqual,
        BangEqual,
        Less,
        LessEqual,
        Greater,
        GreaterEqual,
        Question,
        Colon,
        LeftParenthesis,
        RightParenthesis,
        Comma
    }

    private sealed class ExpressionException : Exception;
}
