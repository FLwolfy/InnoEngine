using System;
using System.Collections.Generic;

using Inno.Core.Execution;
using Inno.Text;

namespace Inno.UI;

/// <summary>
/// Binds one UI service to the current asynchronous execution context.
/// </summary>
public static class UiExecutionContext
{
    private static readonly ExecutionSlot<IUiService> S_CURRENT_SCOPE = new("ui");

    /// <summary>Gets the UI service bound to the current execution context.</summary>
    public static IUiService current => S_CURRENT_SCOPE.current;

    /// <summary>Binds a UI service until the returned strict last-in-first-out scope is disposed.</summary>
    /// <param name="ui">The host-owned UI service.</param>
    /// <returns>The caller-owned binding scope.</returns>
    public static IDisposable EnterScope(IUiService ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        return S_CURRENT_SCOPE.Enter(ui);
    }
}

/// <summary>
/// Provides script-friendly retained-mode UI operations.
/// </summary>
public static class UI
{
    /// <summary>Creates one independent UI context.</summary>
    public static UiContextHandle CreateContext(UiContextOptions options) => UiExecutionContext.current.CreateContext(options);
    /// <summary>Destroys one UI context and all owned documents.</summary>
    public static void DestroyContext(UiContextHandle context) => UiExecutionContext.current.DestroyContext(context);
    /// <summary>Changes one context's pixel dimensions and density.</summary>
    public static void SetViewport(UiContextHandle context, int width, int height, float density = 1f)
        => UiExecutionContext.current.SetViewport(context, width, height, density);
    /// <summary>Loads one explicitly tagged in-memory document.</summary>
    public static UiDocumentHandle LoadDocument(UiContextHandle context, UiDocumentSource source)
        => UiExecutionContext.current.LoadDocument(context, source);
    /// <summary>Loads one imported document compatible with the selected backend.</summary>
    public static UiDocumentHandle LoadDocument(UiContextHandle context, UiDocumentAsset document)
        => UiExecutionContext.current.LoadDocument(context, document);
    /// <summary>Shows one document.</summary>
    public static void ShowDocument(UiContextHandle context, UiDocumentHandle document)
        => UiExecutionContext.current.ShowDocument(context, document);
    /// <summary>Hides one document.</summary>
    public static void HideDocument(UiContextHandle context, UiDocumentHandle document)
        => UiExecutionContext.current.HideDocument(context, document);
    /// <summary>Closes one document.</summary>
    public static void CloseDocument(UiContextHandle context, UiDocumentHandle document)
        => UiExecutionContext.current.CloseDocument(context, document);
    /// <summary>Replaces an element's children with plain Unicode text.</summary>
    public static bool SetText(UiContextHandle context, UiDocumentHandle document, string elementId, string text)
        => UiExecutionContext.current.SetText(context, document, elementId, text);
    /// <summary>Replaces an element's children with an explicitly tagged source-language fragment.</summary>
    public static bool SetContent(UiContextHandle context, UiDocumentHandle document, string elementId, UiDocumentFragment fragment)
        => UiExecutionContext.current.SetContent(context, document, elementId, fragment);
    /// <summary>Sets one element attribute.</summary>
    public static bool SetAttribute(UiContextHandle context, UiDocumentHandle document, string elementId, string name, string value)
        => UiExecutionContext.current.SetAttribute(context, document, elementId, name, value);
    /// <summary>Activates or deactivates one element class.</summary>
    public static bool SetClass(UiContextHandle context, UiDocumentHandle document, string elementId, string className, bool active)
        => UiExecutionContext.current.SetClass(context, document, elementId, className, active);
    /// <summary>Registers one imported font face.</summary>
    public static void RegisterFont(FontAsset font, string family, int faceIndex = 0, TextFontStyle style = TextFontStyle.Normal, int weight = 400, bool fallback = false)
        => UiExecutionContext.current.RegisterFont(font, family, faceIndex, style, weight, fallback);
    /// <summary>Registers one named RGBA8 texture source.</summary>
    public static void RegisterTexture(UiContextHandle context, string source, UiTextureData texture)
        => UiExecutionContext.current.RegisterTexture(context, source, texture);
    /// <summary>Processes current-frame input and advances one context.</summary>
    public static void Update(UiContextHandle context) => UiExecutionContext.current.Update(context);
    /// <summary>Builds an immutable frame of incremental resources and ordered draws.</summary>
    public static UiRenderFrame Render(UiContextHandle context) => UiExecutionContext.current.Render(context);
    /// <summary>Drains document events queued by preceding updates.</summary>
    public static IReadOnlyList<UiEvent> DrainEvents(UiContextHandle context)
        => UiExecutionContext.current.DrainEvents(context);
}
