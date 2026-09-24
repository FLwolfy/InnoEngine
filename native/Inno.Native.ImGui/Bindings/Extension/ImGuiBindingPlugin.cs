using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BGCS.Core.Extensibility;
using BGCS.Emission;
using BGCS.Intermediate;

namespace Inno.Native.ImGui.Bindings.Extension;

/// <summary>
/// Registers InnoEngine's explicit managed cimgui ABI layouts with BindGen-CS.
/// </summary>
public sealed class ImGuiBindingPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    private static readonly string[] s_sources = ["ImTextureID", "ImVector"];

    /// <summary>
    /// Identifies the InnoEngine cimgui layout extension.
    /// </summary>
    public string Id => "inno.imgui.managed-layouts";

    /// <summary>
    /// Gets the extension implementation version.
    /// </summary>
    public string Version => "1.0.0";

    /// <summary>
    /// Gets the BGCS plugin protocol version implemented by this extension.
    /// </summary>
    public int ContractVersion => BindingPluginContract.CurrentVersion;

    /// <summary>
    /// Registers the managed-layout emitter with the BGCS host.
    /// </summary>
    /// <param name="host">
    /// The BGCS plugin service registry.
    /// </param>
    public void Configure(IBindingPluginHost host) =>
        host.Register<IBindingEmitter>("inno.imgui.managed-layouts", new ManagedLayoutEmitter());

    /// <summary>
    /// Fingerprints source templates so edits invalidate incremental binding output.
    /// </summary>
    /// <returns>
    /// A deterministic hash sequence for the two managed layout templates.
    /// </returns>
    public string GetCacheFingerprint() => string.Join("|", s_sources.Select(source =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(GetLayoutPath(source))))));

    private static string GetLayoutPath(string source)
    {
        string pluginDirectory = Path.GetDirectoryName(typeof(ImGuiBindingPlugin).Assembly.Location)
            ?? throw new InvalidOperationException("ImGui binding plugin has no assembly directory.");
        string path = Path.Combine(pluginDirectory, "Managed", $"{source}.cs.txt");
        return File.Exists(path) ? path : throw new FileNotFoundException("Missing ImGui managed ABI layout.", path);
    }

    private sealed class ManagedLayoutEmitter : IBindingEmitter
    {
        /// <summary>
        /// Identifies the emitted ABI layout contribution.
        /// </summary>
        public string Name => "Inno ImGui managed ABI layouts";

        /// <summary>
        /// Copies the two managed ABI layouts into BGCS's staging directory for single-file composition.
        /// </summary>
        /// <param name="module">
        /// The analyzed binding module.
        /// </param>
        /// <param name="context">
        /// The managed output settings and staging path.
        /// </param>
        /// <returns>
        /// The staged source paths supplied to the BGCS composer.
        /// </returns>
        public IReadOnlyList<string> Emit(BindingModule module, EmissionContext context)
        {
            ArgumentNullException.ThrowIfNull(module);
            ArgumentNullException.ThrowIfNull(context);
            if (!context.SingleFile)
                throw new InvalidOperationException("Inno ImGui bindings require a single-file output.");

            Directory.CreateDirectory(context.OutputPath);
            List<string> emitted = [];
            foreach (string source in s_sources)
            {
                string sourcePath = GetLayoutPath(source);
                string path = Path.Combine(context.OutputPath, $".inno-imgui-{source}.cs");
                File.Copy(sourcePath, path, overwrite: true);
                emitted.Add(path);
            }
            return emitted;
        }
    }
}
