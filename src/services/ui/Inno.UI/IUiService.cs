using System.Collections.Generic;

using Inno.Text;

namespace Inno.UI;

/// <summary>
/// Provides retained-mode documents and backend-neutral UI frames to scripts and rendering plugins.
/// </summary>
public interface IUiService
{
    /// <summary>Creates one independent UI context.</summary>
    UiContextHandle CreateContext(UiContextOptions options);
    /// <summary>Destroys one UI context and all owned documents.</summary>
    void DestroyContext(UiContextHandle context);
    /// <summary>Changes one context's pixel dimensions and density.</summary>
    void SetViewport(UiContextHandle context, int width, int height, float density = 1f);
    /// <summary>Loads one explicitly tagged in-memory document.</summary>
    UiDocumentHandle LoadDocument(UiContextHandle context, UiDocumentSource source);
    /// <summary>Loads one imported document compatible with the selected backend.</summary>
    UiDocumentHandle LoadDocument(UiContextHandle context, UiDocumentAsset document);
    /// <summary>Shows one document.</summary>
    void ShowDocument(UiContextHandle context, UiDocumentHandle document);
    /// <summary>Hides one document.</summary>
    void HideDocument(UiContextHandle context, UiDocumentHandle document);
    /// <summary>Closes one document.</summary>
    void CloseDocument(UiContextHandle context, UiDocumentHandle document);
    /// <summary>Replaces an element's children with plain Unicode text.</summary>
    bool SetText(UiContextHandle context, UiDocumentHandle document, string elementId, string text);
    /// <summary>Replaces an element's children with an explicitly tagged source-language fragment.</summary>
    bool SetContent(UiContextHandle context, UiDocumentHandle document, string elementId, UiDocumentFragment fragment);
    /// <summary>Sets one element attribute.</summary>
    bool SetAttribute(UiContextHandle context, UiDocumentHandle document, string elementId, string name, string value);
    /// <summary>Activates or deactivates one element class.</summary>
    bool SetClass(UiContextHandle context, UiDocumentHandle document, string elementId, string className, bool active);
    /// <summary>Registers one imported font face.</summary>
    void RegisterFont(FontAsset font, string family, int faceIndex = 0, TextFontStyle style = TextFontStyle.Normal, int weight = 400, bool fallback = false);
    /// <summary>Registers one named RGBA8 texture source.</summary>
    void RegisterTexture(UiContextHandle context, string source, UiTextureData texture);
    /// <summary>Processes current-frame input and advances one context.</summary>
    void Update(UiContextHandle context);
    /// <summary>Builds an immutable frame of incremental resources and ordered draws.</summary>
    UiRenderFrame Render(UiContextHandle context);
    /// <summary>Drains document events queued by preceding updates.</summary>
    IReadOnlyList<UiEvent> DrainEvents(UiContextHandle context);
}
