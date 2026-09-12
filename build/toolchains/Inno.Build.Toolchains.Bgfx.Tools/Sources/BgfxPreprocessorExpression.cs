using System;
using System.Collections.Generic;
using System.Globalization;

namespace Inno.Build.Toolchains.Bgfx.Tools;

internal sealed class BgfxPreprocessorExpression(IReadOnlyList<BgfxSourceToken> tokens)
{
    private int m_offset;

    internal long Evaluate()
    {
        if (tokens.Count == 0) throw new InvalidOperationException("A preprocessing expression is empty.");
        long value = Conditional(true);
        if (m_offset != tokens.Count) Fail("Unexpected token in integer constant expression.");
        return value;
    }

    private long Conditional(bool evaluate)
    {
        long condition = Binary(1, evaluate);
        if (!Match("?")) return condition;
        long whenTrue = Conditional(evaluate && condition != 0);
        if (!Match(":")) Fail("Expected ':' in conditional expression.");
        long whenFalse = Conditional(evaluate && condition == 0);
        return condition != 0 ? whenTrue : whenFalse;
    }

    private long Binary(int minimum, bool evaluate)
    {
        long left = Unary(evaluate);
        while (m_offset < tokens.Count)
        {
            string operation = tokens[m_offset].text;
            int precedence = Precedence(operation);
            if (precedence < minimum) break;
            m_offset++;
            bool evaluateRight = evaluate && !(operation == "&&" && left == 0 || operation == "||" && left != 0);
            long right = Binary(precedence + 1, evaluateRight);
            if (!evaluate) { left = 0; continue; }
            if (operation is "/" or "%" && right == 0) Fail("Division by zero in integer constant expression.");
            left = operation switch
            {
                "||" => left != 0 || right != 0 ? 1 : 0,
                "&&" => left != 0 && right != 0 ? 1 : 0,
                "|" => left | right, "^" => left ^ right, "&" => left & right,
                "==" => left == right ? 1 : 0, "!=" => left != right ? 1 : 0,
                "<" => left < right ? 1 : 0, ">" => left > right ? 1 : 0,
                "<=" => left <= right ? 1 : 0, ">=" => left >= right ? 1 : 0,
                "<<" => left << (int)right, ">>" => left >> (int)right,
                "+" => unchecked(left + right), "-" => unchecked(left - right), "*" => unchecked(left * right),
                "/" => left == long.MinValue && right == -1 ? long.MinValue : left / right,
                "%" => left == long.MinValue && right == -1 ? 0 : left % right,
                _ => throw new InvalidOperationException("Unknown preprocessing operation.")
            };
        }
        return left;
    }

    private long Unary(bool evaluate)
    {
        if (Match("!")) return Unary(evaluate) == 0 ? 1 : 0;
        if (Match("~")) return ~Unary(evaluate);
        if (Match("-")) return unchecked(-Unary(evaluate));
        if (Match("+")) return Unary(evaluate);
        if (Match("("))
        {
            long value = Conditional(evaluate);
            if (!Match(")")) Fail("Expected ')' in constant expression.");
            return value;
        }
        if (m_offset >= tokens.Count) Fail("Expected an integer expression.");
        BgfxSourceToken token = tokens[m_offset++];
        if (token.isIdentifier) return 0;
        string number = token.text.TrimEnd('u', 'U', 'l', 'L');
        try
        {
            if (number.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return unchecked((long)ulong.Parse(number.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
            if (number.Length > 1 && number[0] == '0') return Convert.ToInt64(number, 8);
            if (long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)) return parsed;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException) { }
        throw new BgfxSourceSyntaxException($"'{token.text}' is not an integer constant.", token.position);
    }

    private bool Match(string value)
    {
        if (m_offset >= tokens.Count || tokens[m_offset].text != value) return false;
        m_offset++;
        return true;
    }

    private void Fail(string message)
        => throw new BgfxSourceSyntaxException(message, tokens[Math.Clamp(m_offset, 0, tokens.Count - 1)].position);

    private static int Precedence(string operation) => operation switch
    {
        "||" => 1, "&&" => 2, "|" => 3, "^" => 4, "&" => 5,
        "==" or "!=" => 6, "<" or ">" or "<=" or ">=" => 7,
        "<<" or ">>" => 8, "+" or "-" => 9, "*" or "/" or "%" => 10,
        _ => 0
    };
}
