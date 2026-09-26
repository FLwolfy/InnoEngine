using System;
using System.Collections.Generic;

using Inno.Assets;
using Inno.Core.Input;
using Inno.Core.Mathematics;
using Inno.Input;
using Inno.Runtime.Contracts;
using Inno.Text;
using Inno.UI.Runtime;
using Xunit;

namespace Inno.UI.Tests;

public sealed class UiRuntimeTests
{
    [Fact]
    public void FrameScopeCapturesInputAndBindsTheScriptFacade()
    {
        var backend = new CapturingBackend();
        using var runtime = new UiRuntime(backend, new RejectingArtifacts());
        InputSnapshot snapshot = new(
            7,
            keysPressed: [KeyCode.Enter],
            mouseButtonsReleased: [MouseButton.Left],
            mousePosition: new Vector2(12f, 34f),
            scrollDelta: new Vector2(0f, 2f),
            modifiers: KeyModifier.Control,
            textInput: ["界"]);
        using IDisposable input = InputExecutionContext.EnterScope(new FixedInput(snapshot));
        var frame = new RuntimeFrame(7, 1f, 1f, 0.016f, 0.016f, 1f, false);
        runtime.Attach();

        runtime.BeginFrame(frame);
        Inno.UI.UI.Update(new UiContextHandle(10));

        UiInputSnapshot captured = Assert.IsType<UiInputSnapshot>(backend.lastInput);
        Assert.Equal(new Vector2(12f, 34f), captured.mousePosition);
        Assert.Contains(KeyCode.Enter, captured.keysPressed);
        Assert.Contains(MouseButton.Left, captured.buttonsReleased);
        Assert.Equal("界", Assert.Single(captured.textInput));
        runtime.EndFrame(frame);
        Assert.Throws<InvalidOperationException>(() => _ = UiExecutionContext.current);
    }

    private sealed class FixedInput(InputSnapshot snapshot) : IInputService
    {
        public InputSnapshot snapshot { get; } = snapshot;
    }

    private sealed class RejectingArtifacts : IAssetArtifactLookup
    {
        public ArtifactLease AcquireArtifact(Guid persistentId, string outputName)
            => throw new InvalidOperationException("No asset access was expected.");

        public bool TryGetArtifact(Guid persistentId, string outputName, out AssetArtifactInfo? artifact)
        {
            artifact = null;
            return false;
        }
    }

    private sealed class CapturingBackend : IUiBackend
    {
        internal UiInputSnapshot? lastInput { get; private set; }

        public string implementationId => "tests.ui";
        public UiBackendCapabilities capabilities { get; } = new([new("tests.ui-language")], true);

        public UiContextHandle CreateContext(UiContextOptions options) => new(1);
        public void DestroyContext(UiContextHandle context) { }
        public void SetViewport(UiContextHandle context, int width, int height, float density) { }
        public UiDocumentHandle LoadDocument(UiContextHandle context, UiDocumentSource source) => new(1);
        public void ShowDocument(UiContextHandle context, UiDocumentHandle document) { }
        public void HideDocument(UiContextHandle context, UiDocumentHandle document) { }
        public void CloseDocument(UiContextHandle context, UiDocumentHandle document) { }
        public bool SetText(UiContextHandle context, UiDocumentHandle document, string elementId, string text) => true;
        public bool SetContent(UiContextHandle context, UiDocumentHandle document, string elementId, UiDocumentFragment content) => true;
        public bool SetAttribute(UiContextHandle context, UiDocumentHandle document, string elementId, string name, string value) => true;
        public bool SetClass(UiContextHandle context, UiDocumentHandle document, string elementId, string className, bool active) => true;
        public void RegisterFont(UiFontRegistration registration) { }
        public void RegisterTexture(UiContextHandle context, string source, UiTextureData texture) { }
        public bool HasElementAtPoint(UiContextHandle context, Inno.Core.Mathematics.Vector2 position) => false;
        public void Update(UiContextHandle context, UiInputSnapshot input) => lastInput = input;
        public UiRenderFrame Render(UiContextHandle context) => UiRenderFrame.empty;
        public IReadOnlyList<UiEvent> DrainEvents(UiContextHandle context) => [];
        public void Dispose() { }
    }
}
