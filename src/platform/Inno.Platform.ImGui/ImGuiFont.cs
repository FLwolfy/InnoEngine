using System;
using System.Collections.Generic;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Platform.ImGui;

/// <summary>
/// Provides scoped access to font faces registered for the current ImGui context.
/// </summary>
public static class ImGuiFont
{
    private static readonly object s_sync = new();
    private static readonly Dictionary<nuint, Dictionary<ImGuiFontStyle, ImFontPtr>> s_fontsByContext = [];

    /// <summary>
    /// Gets whether an exact font style is available in the current ImGui context.
    /// </summary>
    /// <param name="style">The composable style to query.</param>
    /// <returns><see langword="true"/> when the exact face is registered.</returns>
    public static bool IsAvailable(ImGuiFontStyle style)
    {
        ValidateStyle(style);
        ImGuiContextPtr context = NativeImGui.GetCurrentContext();
        if (context.IsNull)
            return false;

        lock (s_sync)
        {
            return s_fontsByContext.TryGetValue(GetContextKey(context), out Dictionary<ImGuiFontStyle, ImFontPtr>? fonts) &&
                   fonts.ContainsKey(style);
        }
    }

    /// <summary>
    /// Pushes a font style for the current ImGui context and returns a scope that restores it.
    /// </summary>
    /// <param name="style">The composable style to use.</param>
    /// <returns>A disposable scope. Missing faces gracefully fall back to the nearest registered face.</returns>
    public static ImGuiFontScope PushStyle(ImGuiFontStyle style)
    {
        ValidateStyle(style);
        ImGuiContextPtr context = NativeImGui.GetCurrentContext();
        if (context.IsNull)
            return default;

        nuint contextKey = GetContextKey(context);
        ImFontPtr font;
        lock (s_sync)
        {
            if (!s_fontsByContext.TryGetValue(contextKey, out Dictionary<ImGuiFontStyle, ImFontPtr>? fonts) ||
                !TryResolve(fonts, style, out font))
            {
                return default;
            }
        }

        NativeImGui.PushFont(font, 0f);
        return new ImGuiFontScope(contextKey, true);
    }

    internal static void RegisterContext(
        ImGuiContextPtr context,
        IReadOnlyDictionary<ImGuiFontStyle, ImFontPtr> fonts)
    {
        if (context.IsNull)
            throw new ArgumentException("An ImGui context is required.", nameof(context));
        ArgumentNullException.ThrowIfNull(fonts);

        var registered = new Dictionary<ImGuiFontStyle, ImFontPtr>();
        foreach ((ImGuiFontStyle style, ImFontPtr font) in fonts)
        {
            ValidateStyle(style);
            if (!font.IsNull)
                registered[style] = font;
        }

        lock (s_sync)
            s_fontsByContext[GetContextKey(context)] = registered;
    }

    internal static void RegisterStyle(
        ImGuiContextPtr context,
        ImGuiFontStyle style,
        ImFontPtr font)
    {
        if (context.IsNull)
            throw new ArgumentException("An ImGui context is required.", nameof(context));
        ValidateStyle(style);
        if (font.IsNull)
            throw new ArgumentException("A loaded font is required.", nameof(font));

        lock (s_sync)
        {
            nuint contextKey = GetContextKey(context);
            if (!s_fontsByContext.TryGetValue(contextKey, out Dictionary<ImGuiFontStyle, ImFontPtr>? fonts))
            {
                fonts = [];
                s_fontsByContext.Add(contextKey, fonts);
            }
            fonts[style] = font;
        }
    }

    internal static void UnregisterContext(ImGuiContextPtr context)
    {
        if (context.IsNull)
            return;
        lock (s_sync)
            s_fontsByContext.Remove(GetContextKey(context));
    }

    internal static void ValidateStyle(ImGuiFontStyle style)
    {
        const ImGuiFontStyle supported = ImGuiFontStyle.Bold | ImGuiFontStyle.Italic;
        if ((style & ~supported) != 0)
            throw new ArgumentOutOfRangeException(nameof(style), style, "The font style contains unsupported flags.");
    }

    internal static void PopStyle(nuint contextKey)
    {
        ImGuiContextPtr context = NativeImGui.GetCurrentContext();
        if (context.IsNull || GetContextKey(context) != contextKey)
            throw new InvalidOperationException("The font style scope must be disposed in the context where it was created.");
        NativeImGui.PopFont();
    }

    private static unsafe nuint GetContextKey(ImGuiContextPtr context) => (nuint)context.Handle;

    private static bool TryResolve(
        IReadOnlyDictionary<ImGuiFontStyle, ImFontPtr> fonts,
        ImGuiFontStyle style,
        out ImFontPtr font)
    {
        if (fonts.TryGetValue(style, out font))
            return true;
        if ((style & ImGuiFontStyle.Bold) != 0 && fonts.TryGetValue(ImGuiFontStyle.Bold, out font))
            return true;
        if ((style & ImGuiFontStyle.Italic) != 0 && fonts.TryGetValue(ImGuiFontStyle.Italic, out font))
            return true;
        return fonts.TryGetValue(ImGuiFontStyle.Regular, out font);
    }
}

/// <summary>
/// Restores the previous ImGui font when a scoped style ends.
/// </summary>
public ref struct ImGuiFontScope
{
    private readonly nuint m_contextKey;
    private bool m_isPushed;

    internal ImGuiFontScope(nuint contextKey, bool isPushed)
    {
        m_contextKey = contextKey;
        m_isPushed = isPushed;
    }

    /// <summary>Restores the font that was active before this scope.</summary>
    public void Dispose()
    {
        if (!m_isPushed)
            return;
        ImGuiFont.PopStyle(m_contextKey);
        m_isPushed = false;
    }
}
