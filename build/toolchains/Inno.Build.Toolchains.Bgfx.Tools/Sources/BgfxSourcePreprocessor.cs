using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Inno.Core.Execution;
using Inno.Rendering.Shaders;

namespace Inno.Build.Toolchains.Bgfx.Tools;

internal sealed class BgfxSourcePreprocessor(ShaderSourceRequest request)
{
    private const int C_MAX_DEPTH = 128;
    private const int C_MAX_TOKENS = 1000000;
    private readonly Dictionary<string, Macro> m_macros = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_dependencies = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_once = new(StringComparer.Ordinal);
    private int m_emittedTokens;

    internal IEnumerable<string> dependencies => m_dependencies;

    internal List<BgfxSourceToken> Process()
    {
        foreach ((string name, string value) in request.defines)
        {
            List<BgfxSourceToken> replacement = BgfxSourceLexer.Tokenize(new(request.source.assetPath, value));
            m_macros.Add(name, new(null, replacement.Where(static token => token.text != "\n").ToArray()));
        }
        return ProcessFile(request.source, 0);
    }

    private List<BgfxSourceToken> ProcessFile(ShaderSourceFile source, int depth)
    {
        if (m_once.Contains(source.assetPath)) return [];
        if (depth >= C_MAX_DEPTH)
            throw new BgfxSourceSyntaxException("Shader include depth exceeded; check for an unguarded include cycle.", new(source.assetPath, 1, 1));
        List<BgfxSourceToken> input = BgfxSourceLexer.Tokenize(source);
        var output = new List<BgfxSourceToken>();
        var pending = new List<BgfxSourceToken>();
        var conditions = new Stack<Conditional>();
        bool active = true;
        for (int index = 0; index < input.Count;)
        {
            int start = index;
            while (index < input.Count && input[index].text != "\n") index++;
            List<BgfxSourceToken> line = input.GetRange(start, index - start);
            if (index < input.Count) index++;
            if (line.Count == 0) continue;
            if (line[0].text != "#")
            {
                if (active) pending.AddRange(line);
                continue;
            }
            Flush();
            if (line.Count < 2) continue;
            BgfxSourceToken directive = line[1];
            List<BgfxSourceToken> arguments = line.GetRange(2, line.Count - 2);
            switch (directive.text)
            {
                case "if":
                case "ifdef":
                case "ifndef":
                {
                    if (directive.text != "if" && (arguments.Count != 1 || !arguments[0].isIdentifier))
                        Fail("Expected one macro name after conditional directive.", directive);
                    bool selected = active && (directive.text == "if" ? Evaluate(arguments, directive) :
                        m_macros.ContainsKey(arguments[0].text) == (directive.text == "ifdef"));
                    conditions.Push(new(active, selected, false));
                    active = selected;
                    break;
                }
                case "elif":
                {
                    if (conditions.Count == 0 || conditions.Peek().hasElse) Fail("Unexpected #elif.", directive);
                    Conditional branch = conditions.Pop();
                    active = branch.parentActive && !branch.taken && Evaluate(arguments, directive);
                    conditions.Push(branch with { taken = branch.taken || active });
                    break;
                }
                case "else":
                {
                    if (arguments.Count != 0 || conditions.Count == 0 || conditions.Peek().hasElse) Fail("Unexpected #else.", directive);
                    Conditional branch = conditions.Pop();
                    active = branch.parentActive && !branch.taken;
                    conditions.Push(branch with { taken = true, hasElse = true });
                    break;
                }
                case "endif":
                    if (arguments.Count != 0 || conditions.Count == 0) Fail("Unexpected #endif.", directive);
                    active = conditions.Pop().parentActive;
                    break;
                default:
                    if (active) ApplyDirective();
                    break;
            }

            void ApplyDirective()
            {
                switch (directive.text)
                {
                    case "define": Define(arguments, directive); break;
                    case "undef":
                        if (arguments.Count != 1 || !arguments[0].isIdentifier) Fail("Expected one macro name after #undef.", directive);
                        m_macros.Remove(arguments[0].text);
                        break;
                    case "include":
                    {
                        List<BgfxSourceToken> expanded = Expand(arguments, [], 0);
                        string include;
                        if (expanded.Count == 1 && expanded[0].text.StartsWith('"') && expanded[0].text.EndsWith('"'))
                            include = expanded[0].text[1..^1];
                        else if (expanded.Count > 2 && expanded[0].text == "<" && expanded[^1].text == ">")
                            include = string.Concat(expanded.Skip(1).Take(expanded.Count - 2).Select(static token => token.text));
                        else { Fail("Expected a quoted or bracketed include path.", directive); return; }
                        ShaderSourceFile dependency;
                        try { dependency = request.resolver.ReadInclude(source.assetPath, include); }
                        catch (Exception failure) when (
                            failure is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
                            && RetirementPendingException.Find(failure) is null)
                        {
                            throw new BgfxSourceSyntaxException($"Cannot resolve include '{include}': {failure.Message}", directive.position);
                        }
                        ArgumentNullException.ThrowIfNull(dependency);
                        ArgumentException.ThrowIfNullOrWhiteSpace(dependency.assetPath);
                        ArgumentNullException.ThrowIfNull(dependency.text);
                        m_dependencies.Add(dependency.assetPath);
                        output.AddRange(ProcessFile(dependency, depth + 1));
                        break;
                    }
                    case "pragma":
                        if (arguments.Count == 1 && arguments[0].text == "once") m_once.Add(source.assetPath);
                        else Fail("Unsupported module pragma; stage configuration belongs to the graph target.", directive);
                        break;
                    case "error": Fail(string.Join(" ", arguments.Select(static token => token.text)), directive); break;
                    default: Fail($"Unsupported module preprocessor directive '#{directive.text}'.", directive); break;
                }
            }
        }
        Flush();
        if (conditions.Count != 0)
            throw new BgfxSourceSyntaxException("Unterminated preprocessor conditional.", new(source.assetPath, 1, 1));
        return output;

        void Flush()
        {
            if (pending.Count == 0) return;
            output.AddRange(Expand(pending, [], 0));
            pending.Clear();
        }
    }

    private bool Evaluate(List<BgfxSourceToken> input, BgfxSourceToken directive)
    {
        if (input.Count == 0) Fail("Expected a preprocessor expression.", directive);
        var replaced = new List<BgfxSourceToken>();
        for (int i = 0; i < input.Count; i++)
        {
            BgfxSourceToken token = input[i];
            if (token.text != "defined") { replaced.Add(token); continue; }
            bool parenthesized = i + 1 < input.Count && input[i + 1].text == "(";
            if (parenthesized) i++;
            if (++i >= input.Count || !input[i].isIdentifier) Fail("Expected a macro identifier after defined.", token);
            bool defined = m_macros.ContainsKey(input[i].text);
            if (parenthesized && (++i >= input.Count || input[i].text != ")")) Fail("Expected ')' after defined.", token);
            replaced.Add(new(defined ? "1" : "0", token.position));
        }
        List<BgfxSourceToken> expanded = Expand(replaced, [], 0);
        if (expanded.Count == 0) Fail("Preprocessor expression expanded to no tokens.", directive);
        return new BgfxPreprocessorExpression(expanded).Evaluate() != 0;
    }

    private void Define(List<BgfxSourceToken> arguments, BgfxSourceToken directive)
    {
        if (arguments.Count == 0 || !arguments[0].isIdentifier) Fail("Expected a macro identifier after #define.", directive);
        BgfxSourceToken name = arguments[0];
        int offset = 1;
        string[]? parameters = null;
        if (offset < arguments.Count && arguments[offset].text == "(" &&
            arguments[offset].position.line == name.position.line &&
            arguments[offset].position.column == name.position.column + name.text.Length)
        {
            offset++;
            var names = new List<string>();
            while (offset < arguments.Count && arguments[offset].text != ")")
            {
                if (!arguments[offset].isIdentifier) Fail("Expected a named macro parameter.", arguments[offset]);
                string parameter = arguments[offset++].text;
                if (names.Contains(parameter, StringComparer.Ordinal)) Fail("Duplicate macro parameter.", name);
                names.Add(parameter);
                if (offset < arguments.Count && arguments[offset].text == ",")
                {
                    offset++;
                    if (offset >= arguments.Count || arguments[offset].text == ")")
                        Fail("A macro parameter is missing after ','.", name);
                }
                else if (offset < arguments.Count && arguments[offset].text != ")") Fail("Expected ',' or ')' in macro declaration.", arguments[offset]);
            }
            if (offset >= arguments.Count) Fail("Unterminated macro parameter list.", name);
            offset++;
            parameters = names.ToArray();
        }
        var macro = new Macro(parameters, arguments.Skip(offset).ToArray());
        if (m_macros.TryGetValue(name.text, out Macro? previous) &&
            (!SameParameters(previous.parameters, parameters) ||
             !previous.replacement.Select(static token => token.text).SequenceEqual(macro.replacement.Select(static token => token.text))))
            Fail($"Conflicting macro definition '{name.text}'.", name);
        m_macros[name.text] = macro;
    }

    private List<BgfxSourceToken> Expand(IReadOnlyList<BgfxSourceToken> tokens, HashSet<string> disabled, int depth)
    {
        if (depth >= C_MAX_DEPTH && tokens.Count != 0) Fail("Shader macro expansion exceeded its depth budget.", tokens[0]);
        var pending = tokens.Select(token => new ExpansionToken(token, disabled)).ToList();
        var result = new List<BgfxSourceToken>();
        for (int i = 0; i < pending.Count; i++)
        {
            ExpansionToken current = pending[i];
            BgfxSourceToken token = current.token;
            if (++m_emittedTokens > C_MAX_TOKENS) Fail("Shader source expansion exceeded its token budget.", token);
            if (token.text == "__LINE__") { result.Add(new(token.position.line.ToString(CultureInfo.InvariantCulture), token.position)); continue; }
            if (token.text == "__FILE__") { result.Add(new('"' + token.position.assetPath + '"', token.position)); continue; }
            if (!m_macros.TryGetValue(token.text, out Macro? macro) || current.disabled.Contains(token.text))
            { result.Add(token); continue; }
            List<BgfxSourceToken> replacement = macro.replacement.ToList();
            int start = i;
            if (macro.parameters is not null)
            {
                if (i + 1 >= pending.Count || pending[i + 1].token.text != "(") { result.Add(token); continue; }
                i += 2;
                var arguments = new List<List<BgfxSourceToken>>();
                var argument = new List<BgfxSourceToken>();
                int nesting = 0;
                for (; i < pending.Count; i++)
                {
                    BgfxSourceToken next = pending[i].token;
                    if (next.text == ")" && nesting == 0) break;
                    if (next.text == "," && nesting == 0) { arguments.Add(argument); argument = []; continue; }
                    if (next.text == "(") nesting++;
                    if (next.text == ")") nesting--;
                    argument.Add(next);
                }
                if (i >= pending.Count) Fail("Unterminated function-like macro invocation.", token);
                if (argument.Count != 0 || arguments.Count != 0 || macro.parameters.Length != 0) arguments.Add(argument);
                if (arguments.Count != macro.parameters.Length) Fail($"Macro '{token.text}' has an incorrect argument count.", token);
                replacement = Substitute(macro, arguments, token, current.disabled, depth);
            }
            else replacement = Paste(replacement, token);
            // Rescan replacement tokens together with the following input. An alias may expand to
            // a function-like macro whose '(' was not part of the alias replacement list.
            var nestedDisabled = new HashSet<string>(current.disabled, StringComparer.Ordinal) { token.text };
            if (nestedDisabled.Count >= C_MAX_DEPTH) Fail("Shader macro expansion exceeded its depth budget.", token);
            pending.RemoveRange(start, i - start + 1);
            pending.InsertRange(start, replacement.Select(value => new ExpansionToken(value, nestedDisabled)));
            i = start - 1;
        }
        return result;
    }

    private List<BgfxSourceToken> Substitute(Macro macro, List<List<BgfxSourceToken>> arguments,
        BgfxSourceToken invocation, HashSet<string> disabled, int depth)
    {
        var substituted = new List<BgfxSourceToken>();
        for (int index = 0; index < macro.replacement.Length; index++)
        {
            BgfxSourceToken item = macro.replacement[index];
            if (item.text == "#" && index + 1 < macro.replacement.Length)
            {
                int parameter = Array.IndexOf(macro.parameters!, macro.replacement[++index].text);
                if (parameter < 0) Fail("Stringification requires a macro parameter.", item);
                string literal = string.Join(" ", arguments[parameter].Select(static value => value.text))
                    .Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
                substituted.Add(new('"' + literal + '"', invocation.position));
                continue;
            }
            int parameterIndex = Array.IndexOf(macro.parameters!, item.text);
            if (parameterIndex < 0) { substituted.Add(item); continue; }
            bool pasted = index > 0 && macro.replacement[index - 1].text == "##" ||
                          index + 1 < macro.replacement.Length && macro.replacement[index + 1].text == "##";
            if (pasted && arguments[parameterIndex].Count == 0)
                substituted.Add(new(string.Empty, invocation.position));
            else substituted.AddRange(pasted ? arguments[parameterIndex] : Expand(arguments[parameterIndex], disabled, depth + 1));
        }
        return Paste(substituted, invocation);
    }

    private static List<BgfxSourceToken> Paste(List<BgfxSourceToken> substituted, BgfxSourceToken invocation)
    {
        var output = new List<BgfxSourceToken>();
        for (int i = 0; i < substituted.Count; i++)
        {
            if (substituted[i].text != "##") { output.Add(substituted[i]); continue; }
            if (output.Count == 0 || i + 1 >= substituted.Count) Fail("Token pasting requires two nonempty operands.", invocation);
            string combined = output[^1].text + substituted[++i].text;
            List<BgfxSourceToken> joined = BgfxSourceLexer.Tokenize(new(invocation.position.assetPath, combined));
            if (combined.Length > 0 && joined.Count != 1) Fail("Token pasting did not produce one preprocessing token.", invocation);
            output[^1] = new(combined, invocation.position);
        }
        output.RemoveAll(static token => token.text.Length == 0);
        return output;
    }

    private static bool SameParameters(string[]? first, string[]? second)
        => first is null ? second is null : second is not null && first.SequenceEqual(second);

    private static void Fail(string message, BgfxSourceToken token)
        => throw new BgfxSourceSyntaxException(message, token.position);

    private sealed record Macro(string[]? parameters, BgfxSourceToken[] replacement);
    private sealed record ExpansionToken(BgfxSourceToken token, HashSet<string> disabled);
    private readonly record struct Conditional(bool parentActive, bool taken, bool hasElse);
}
