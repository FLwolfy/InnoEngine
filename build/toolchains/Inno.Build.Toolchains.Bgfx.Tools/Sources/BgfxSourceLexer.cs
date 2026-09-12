using System;
using System.Collections.Generic;
using Inno.Rendering.Shaders;

namespace Inno.Build.Toolchains.Bgfx.Tools;

internal readonly record struct BgfxSourceToken(string text, ShaderSourcePosition position)
{
    internal bool isIdentifier => text.Length != 0 && (char.IsLetter(text[0]) || text[0] == '_');
}

internal sealed class BgfxSourceSyntaxException(string message, ShaderSourcePosition position) : Exception(message)
{
    internal ShaderSourcePosition position { get; } = position;
}

internal static class BgfxSourceLexer
{
    internal static List<BgfxSourceToken> Tokenize(ShaderSourceFile source)
    {
        var tokens = new List<BgfxSourceToken>();
        string text = source.text;
        int line = 1;
        int column = 1;
        int offset = 0;
        while (offset < text.Length)
        {
            char current = text[offset];
            if (current == '\\' && offset + 1 < text.Length && (text[offset + 1] == '\n' || text[offset + 1] == '\r'))
            {
                Advance();
                if (offset < text.Length && text[offset] == '\r') Advance();
                if (offset < text.Length && text[offset] == '\n') Advance();
                continue;
            }
            if (char.IsWhiteSpace(current))
            {
                if (current == '\n') tokens.Add(new("\n", new(source.assetPath, line, column)));
                Advance();
                continue;
            }
            if (current == '/' && offset + 1 < text.Length && text[offset + 1] == '/')
            {
                while (offset < text.Length && text[offset] != '\n') Advance();
                continue;
            }
            var position = new ShaderSourcePosition(source.assetPath, line, column);
            if (current == '/' && offset + 1 < text.Length && text[offset + 1] == '*')
            {
                Advance(); Advance();
                bool closed = false;
                while (offset < text.Length)
                {
                    if (text[offset] == '*' && offset + 1 < text.Length && text[offset + 1] == '/')
                    {
                        Advance(); Advance(); closed = true; break;
                    }
                    if (text[offset] == '\n') tokens.Add(new("\n", new(source.assetPath, line, column)));
                    Advance();
                }
                if (!closed) throw new BgfxSourceSyntaxException("Unterminated block comment.", position);
                continue;
            }
            int start = offset;
            if (char.IsLetter(current) || current == '_')
            {
                do Advance(); while (offset < text.Length && (char.IsLetterOrDigit(text[offset]) || text[offset] == '_'));
            }
            else if (char.IsDigit(current) || (current == '.' && offset + 1 < text.Length && char.IsDigit(text[offset + 1])))
            {
                bool hexadecimal = current == '0' && offset + 1 < text.Length && text[offset + 1] is 'x' or 'X';
                do
                {
                    char previous = text[offset];
                    Advance();
                    bool exponent = hexadecimal ? previous is 'p' or 'P' : previous is 'e' or 'E';
                    if (exponent && offset < text.Length && text[offset] is '+' or '-') Advance();
                } while (offset < text.Length && (char.IsLetterOrDigit(text[offset]) || text[offset] == '.'));
            }
            else if (current is '"' or '\'')
            {
                Advance();
                bool closed = false;
                while (offset < text.Length)
                {
                    char character = text[offset];
                    Advance();
                    if (character == current) { closed = true; break; }
                    if (character == '\\' && offset < text.Length) Advance();
                    else if (character == '\n') throw new BgfxSourceSyntaxException("Unterminated string literal.", position);
                }
                if (!closed) throw new BgfxSourceSyntaxException("Unterminated string literal.", position);
            }
            else
            {
                Advance();
                if (offset < text.Length && IsPair(current, text[offset]))
                {
                    char second = text[offset];
                    Advance();
                    if (current == second && current is '<' or '>' && offset < text.Length && text[offset] == '=') Advance();
                }
            }
            tokens.Add(new(text[start..offset], position));
        }
        return tokens;

        void Advance()
        {
            if (text[offset++] == '\n') { line++; column = 1; }
            else column++;
        }
    }

    private static bool IsPair(char first, char second)
        => (first, second) is ('&', '&') or ('|', '|') or ('=', '=') or ('!', '=') or ('<', '=') or ('>', '=')
            or ('<', '<') or ('>', '>') or ('#', '#') or ('+', '+') or ('-', '-') or ('+', '=') or ('-', '=')
            or ('*', '=') or ('/', '=') or ('&', '=') or ('|', '=') or ('^', '=') or ('%', '=');
}
