using System;

namespace Inno.Runtime.Contracts;

/// <summary>
/// Participates in the ordered lifecycle of one isolated runtime session.
/// </summary>
public interface IRuntimeSubsystem : IDisposable
{
    /// <summary>
    /// Acquires session resources after every dependency has attached.
    /// </summary>
    void Attach();

    /// <summary>
    /// Begins one frame before event dispatch and simulation.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    void BeginFrame(RuntimeFrame frame);

    /// <summary>
    /// Advances one deterministic simulation step.
    /// </summary>
    /// <param name="frame">
    /// The immutable fixed-step state.
    /// </param>
    void FixedUpdate(RuntimeFixedFrame frame);

    /// <summary>
    /// Advances variable simulation state.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    void Update(RuntimeFrame frame);

    /// <summary>
    /// Advances state that depends on completed variable simulation.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    void LateUpdate(RuntimeFrame frame);

    /// <summary>
    /// Prepares frame output after simulation has completed.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    void BeforeRender(RuntimeFrame frame);

    /// <summary>
    /// Produces frame output owned by this subsystem.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    void Render(RuntimeFrame frame);

    /// <summary>
    /// Finalizes frame output even when rendering fails.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    void AfterRender(RuntimeFrame frame);

    /// <summary>
    /// Ends one frame and releases frame-scoped state.
    /// </summary>
    /// <param name="frame">
    /// The immutable variable-frame state.
    /// </param>
    void EndFrame(RuntimeFrame frame);

    /// <summary>
    /// Releases attached session resources before dependencies detach.
    /// </summary>
    void Detach();
}
