using System;
using System.Collections.Generic;

namespace Inno.UI;

/// <summary>
/// Defines the replaceable document, layout, input, and draw-geometry backend boundary.
/// </summary>
public interface IUiBackend : IDisposable
{
    /// <summary>Gets the stable implementation identity used by imported document artifacts.</summary>
    string implementationId { get; }
    /// <summary>Gets optional behavior and accepted document languages for this generation.</summary>
    UiBackendCapabilities capabilities { get; }
    /// <summary>Creates one independent UI context.</summary>
    UiContextHandle CreateContext(UiContextOptions options);
    /// <summary>Destroys one UI context and all of its documents.</summary>
    void DestroyContext(UiContextHandle context);
    /// <summary>Changes one UI context's pixel dimensions and density.</summary>
    void SetViewport(UiContextHandle context, int width, int height, float density);
    /// <summary>Loads one explicitly tagged in-memory document.</summary>
    UiDocumentHandle LoadDocument(UiContextHandle context, UiDocumentSource source);
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
    /// <summary>Registers an encoded font face in the document engine.</summary>
    void RegisterFont(UiFontRegistration registration);
    /// <summary>Registers a named RGBA8 texture source.</summary>
    void RegisterTexture(UiContextHandle context, string source, UiTextureData texture);
    /// <summary>Processes input and advances document state.</summary>
    void Update(UiContextHandle context, UiInputSnapshot input);
    /// <summary>Builds an immutable frame of incremental resources and ordered draws.</summary>
    UiRenderFrame Render(UiContextHandle context);
    /// <summary>Drains document events queued by the preceding updates.</summary>
    IReadOnlyList<UiEvent> DrainEvents(UiContextHandle context);
}
