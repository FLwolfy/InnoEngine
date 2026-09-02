namespace Inno.Scripting.Compiler;

/// <summary>
/// Describes monotonic progress through one script compilation.
/// </summary>
/// <param name="fraction">
/// The completed fraction in the inclusive range from zero to one.
/// </param>
/// <param name="stage">
/// The current human-readable compiler stage.
/// </param>
public readonly record struct ScriptCompilationProgress(float fraction, string stage);
