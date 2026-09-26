using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Inno.UI;

/// <summary>
/// Describes one vertex in backend-neutral UI draw geometry.
/// </summary>
/// <param name="x">
/// The horizontal or first component.
/// </param>
/// <param name="y">
/// The vertical or second component.
/// </param>
/// <param name="u">
/// The float value used to initialize this instance.
/// </param>
/// <param name="v">
/// The concrete value read or transformed by this operation.
/// </param>
/// <param name="color">
/// The uint value used to initialize this instance.
/// </param>
public readonly record struct UiVertex(float x, float y, float u, float v, uint color);

/// <summary>
/// Defines an integer clipping rectangle in UI surface coordinates.
/// </summary>
/// <param name="x">
/// The horizontal or first component.
/// </param>
/// <param name="y">
/// The vertical or second component.
/// </param>
/// <param name="width">
/// The width in logical units or pixels required by this operation.
/// </param>
/// <param name="height">
/// The height in logical units or pixels required by this operation.
/// </param>
public readonly record struct UiClipRectangle(int x, int y, int width, int height);

/// <summary>
/// Publishes one immutable mesh generation to a rendering plugin.
/// </summary>
public sealed class UiMeshUpdate
{
    private readonly IReadOnlyList<UiVertex> m_vertices;
    private readonly IReadOnlyList<uint> m_indices;

    internal UiMeshUpdate(UiMeshHandle mesh, ulong revision, UiVertex[] vertices, uint[] indices)
    {
        this.mesh = mesh;
        this.revision = revision;
        m_vertices = new ReadOnlyCollection<UiVertex>(vertices);
        m_indices = new ReadOnlyCollection<uint>(indices);
    }

    /// <summary>
    /// Gets the generation-scoped mesh handle.
    /// </summary>
    public UiMeshHandle mesh { get; }
    /// <summary>
    /// Gets the monotonically increasing mesh content revision.
    /// </summary>
    public ulong revision { get; }
    /// <summary>
    /// Gets immutable local-space vertices.
    /// </summary>
    public IReadOnlyList<UiVertex> vertices => m_vertices;
    /// <summary>
    /// Gets immutable triangle indices.
    /// </summary>
    public IReadOnlyList<uint> indices => m_indices;
}

/// <summary>
/// Publishes one immutable premultiplied RGBA8 texture generation.
/// </summary>
public sealed class UiTextureUpdate
{
    private readonly byte[] m_pixels;

    internal UiTextureUpdate(UiTextureHandle texture, ulong revision, int width, int height, byte[] pixels)
    {
        this.texture = texture;
        this.revision = revision;
        this.width = width;
        this.height = height;
        m_pixels = pixels;
    }

    /// <summary>
    /// Gets the generation-scoped texture handle.
    /// </summary>
    public UiTextureHandle texture { get; }
    /// <summary>
    /// Gets the monotonically increasing content revision.
    /// </summary>
    public ulong revision { get; }
    /// <summary>
    /// Gets the texture width.
    /// </summary>
    public int width { get; }
    /// <summary>
    /// Gets the texture height.
    /// </summary>
    public int height { get; }
    /// <summary>
    /// Gets immutable tightly packed premultiplied RGBA8 pixels.
    /// </summary>
    public ReadOnlyMemory<byte> pixels => m_pixels;
}

/// <summary>
/// Describes one draw of a previously published mesh.
/// </summary>
/// <param name="mesh">
/// The ui mesh handle value used to initialize this instance.
/// </param>
/// <param name="texture">
/// The ui texture handle value used to initialize this instance.
/// </param>
/// <param name="scissorEnabled">
/// The bool value used to initialize this instance.
/// </param>
/// <param name="scissor">
/// The ui clip rectangle value used to initialize this instance.
/// </param>
public readonly record struct UiDrawCommand(
    UiMeshHandle mesh,
    UiTextureHandle texture,
    bool scissorEnabled,
    UiClipRectangle scissor);

/// <summary>
/// Contains incremental resource changes and ordered draw commands for one UI context.
/// </summary>
public sealed class UiRenderFrame
{
    private readonly IReadOnlyList<UiMeshUpdate> m_meshUpdates;
    private readonly IReadOnlyList<UiMeshHandle> m_releasedMeshes;
    private readonly IReadOnlyList<UiTextureUpdate> m_textureUpdates;
    private readonly IReadOnlyList<UiTextureHandle> m_releasedTextures;
    private readonly IReadOnlyList<UiDrawCommand> m_commands;

    internal UiRenderFrame(
        UiMeshUpdate[] meshUpdates,
        UiMeshHandle[] releasedMeshes,
        UiTextureUpdate[] textureUpdates,
        UiTextureHandle[] releasedTextures,
        UiDrawCommand[] commands)
    {
        m_meshUpdates = new ReadOnlyCollection<UiMeshUpdate>(meshUpdates);
        m_releasedMeshes = new ReadOnlyCollection<UiMeshHandle>(releasedMeshes);
        m_textureUpdates = new ReadOnlyCollection<UiTextureUpdate>(textureUpdates);
        m_releasedTextures = new ReadOnlyCollection<UiTextureHandle>(releasedTextures);
        m_commands = new ReadOnlyCollection<UiDrawCommand>(commands);
    }

    /// <summary>
    /// Gets an empty resource-delta frame.
    /// </summary>
    public static UiRenderFrame empty { get; } = new([], [], [], [], []);

    /// <summary>
    /// Creates a command-free frame that deterministically retires plugin-owned resources.
    /// </summary>
    /// <param name="meshes">
    /// Assigned mesh handles to retire.
    /// </param>
    /// <param name="textures">
    /// Assigned texture handles to retire.
    /// </param>
    /// <returns>
    /// An immutable retirement frame.
    /// </returns>
    public static UiRenderFrame CreateRetirement(UiMeshHandle[] meshes, UiTextureHandle[] textures)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(textures);
        if (Array.Exists(meshes, static value => !value.isValid))
            throw new ArgumentException("Retirement meshes must be assigned.", nameof(meshes));
        if (Array.Exists(textures, static value => !value.isValid))
            throw new ArgumentException("Retirement textures must be assigned.", nameof(textures));
        return new([], (UiMeshHandle[])meshes.Clone(), [], (UiTextureHandle[])textures.Clone(), []);
    }
    /// <summary>
    /// Gets created or replaced mesh generations.
    /// </summary>
    public IReadOnlyList<UiMeshUpdate> meshUpdates => m_meshUpdates;
    /// <summary>
    /// Gets mesh generations that must be retired.
    /// </summary>
    public IReadOnlyList<UiMeshHandle> releasedMeshes => m_releasedMeshes;
    /// <summary>
    /// Gets created or replaced texture generations.
    /// </summary>
    public IReadOnlyList<UiTextureUpdate> textureUpdates => m_textureUpdates;
    /// <summary>
    /// Gets texture generations that must be retired.
    /// </summary>
    public IReadOnlyList<UiTextureHandle> releasedTextures => m_releasedTextures;
    /// <summary>
    /// Gets ordered draws referencing published resources.
    /// </summary>
    public IReadOnlyList<UiDrawCommand> commands => m_commands;
}

/// <summary>
/// Builds one immutable backend-neutral frame while transferring ownership of update arrays.
/// </summary>
/// <remarks>
/// This backend SPI is intentionally not exported to ordinary game scripts.
/// </remarks>
public sealed class UiRenderFrameBuilder
{
    private readonly List<UiMeshUpdate> m_meshUpdates = [];
    private readonly List<UiMeshHandle> m_releasedMeshes = [];
    private readonly List<UiTextureUpdate> m_textureUpdates = [];
    private readonly List<UiTextureHandle> m_releasedTextures = [];
    private readonly List<UiDrawCommand> m_commands = [];
    private bool m_built;

    /// <summary>
    /// Adds one mesh update and takes exclusive ownership of both arrays.
    /// </summary>
    /// <param name="mesh">
    /// Assigned mesh handle.
    /// </param>
    /// <param name="revision">
    /// Positive content revision.
    /// </param>
    /// <param name="vertices">
    /// Owned vertex array; the caller must not mutate it afterward.
    /// </param>
    /// <param name="indices">
    /// Owned index array; the caller must not mutate it afterward.
    /// </param>
    public void AddMeshUpdate(UiMeshHandle mesh, ulong revision, UiVertex[] vertices, uint[] indices)
    {
        EnsureMutable();
        if (!mesh.isValid) throw new ArgumentException("A mesh update requires an assigned handle.", nameof(mesh));
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        if (vertices.Length == 0 || indices.Length == 0)
            throw new ArgumentException("A mesh update requires nonempty geometry.");
        foreach (uint index in indices)
            if (index >= vertices.Length)
                throw new ArgumentException("A mesh index references a vertex outside the update.", nameof(indices));
        m_meshUpdates.Add(new(mesh, revision, vertices, indices));
    }

    /// <summary>
    /// Adds one mesh retirement.
    /// </summary>
    /// <param name="mesh">
    /// Assigned mesh handle.
    /// </param>
    public void AddReleasedMesh(UiMeshHandle mesh)
    {
        EnsureMutable();
        if (!mesh.isValid) throw new ArgumentException("A released mesh requires an assigned handle.", nameof(mesh));
        m_releasedMeshes.Add(mesh);
    }

    /// <summary>
    /// Adds one texture update and takes exclusive ownership of its pixel array.
    /// </summary>
    /// <param name="texture">
    /// Assigned texture handle.
    /// </param>
    /// <param name="revision">
    /// Positive content revision.
    /// </param>
    /// <param name="width">
    /// Positive pixel width.
    /// </param>
    /// <param name="height">
    /// Positive pixel height.
    /// </param>
    /// <param name="pixels">
    /// Owned tightly packed premultiplied RGBA8 pixels.
    /// </param>
    public void AddTextureUpdate(UiTextureHandle texture, ulong revision, int width, int height, byte[] pixels)
    {
        EnsureMutable();
        if (!texture.isValid) throw new ArgumentException("A texture update requires an assigned handle.", nameof(texture));
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("Texture pixels must be tightly packed RGBA8.", nameof(pixels));
        m_textureUpdates.Add(new(texture, revision, width, height, pixels));
    }

    /// <summary>
    /// Adds one texture retirement.
    /// </summary>
    /// <param name="texture">
    /// Assigned texture handle.
    /// </param>
    public void AddReleasedTexture(UiTextureHandle texture)
    {
        EnsureMutable();
        if (!texture.isValid) throw new ArgumentException("A released texture requires an assigned handle.", nameof(texture));
        m_releasedTextures.Add(texture);
    }

    /// <summary>
    /// Adds one ordered mesh draw.
    /// </summary>
    /// <param name="command">
    /// Draw command referencing an assigned mesh.
    /// </param>
    public void AddCommand(UiDrawCommand command)
    {
        EnsureMutable();
        if (!command.mesh.isValid) throw new ArgumentException("A draw command requires an assigned mesh.", nameof(command));
        m_commands.Add(command);
    }

    /// <summary>
    /// Freezes the accumulated frame. The builder cannot be reused.
    /// </summary>
    /// <returns>
    /// An immutable frame owning every supplied array.
    /// </returns>
    public UiRenderFrame Build()
    {
        EnsureMutable();
        m_built = true;
        return new(
            m_meshUpdates.ToArray(),
            m_releasedMeshes.ToArray(),
            m_textureUpdates.ToArray(),
            m_releasedTextures.ToArray(),
            m_commands.ToArray());
    }

    private void EnsureMutable()
    {
        if (m_built)
            throw new InvalidOperationException("A UI render-frame builder can only build one frame.");
    }
}
