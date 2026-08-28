using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Mathematics;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Layers;

namespace Inno.Rendering;

/// <summary>
/// Stores an axis-aligned world-space bounding box used by render culling.
/// </summary>
public readonly record struct RenderBounds
{
    /// <summary>
    /// Creates world-space bounds.
    /// </summary>
    /// <param name="center">World-space center.</param>
    /// <param name="extents">Non-negative half-extents.</param>
    public RenderBounds(Vector3 center, Vector3 extents)
    {
        if (extents.x < 0f || extents.y < 0f || extents.z < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(extents), "Render bounds extents cannot be negative.");
        }

        this.center = center;
        this.extents = extents;
    }

    /// <summary>Gets the world-space center.</summary>
    public Vector3 center { get; }

    /// <summary>Gets the non-negative half-extents.</summary>
    public Vector3 extents { get; }
}

/// <summary>
/// Identifies the lighting model represented by one immutable render-world light.
/// </summary>
public enum RenderLightKind
{
    /// <summary>Parallel directional lighting.</summary>
    Directional,
    /// <summary>Omnidirectional local lighting.</summary>
    Point,
    /// <summary>Conical local lighting.</summary>
    Spot
}

/// <summary>
/// Contains immutable backend-neutral light data for one render frame.
/// </summary>
public sealed class RenderLightData
{
    /// <summary>
    /// Creates a frame-scoped light snapshot.
    /// </summary>
    /// <param name="persistentId">Persistent component identity.</param>
    /// <param name="kind">Light model.</param>
    /// <param name="position">World-space position for local lights.</param>
    /// <param name="direction">Normalized world-space emission direction.</param>
    /// <param name="color">Linear light color.</param>
    /// <param name="intensity">Non-negative radiometric intensity.</param>
    /// <param name="range">Positive local-light range, or zero for directional lights.</param>
    /// <param name="innerConeCosine">Spot inner-cone cosine, or one for other lights.</param>
    /// <param name="outerConeCosine">Spot outer-cone cosine, or one for other lights.</param>
    /// <param name="castsShadows">Whether the light requests supported shadows.</param>
    /// <param name="shadowStrength">Normalized shadow contribution.</param>
    /// <param name="shadowCascadeCount">Directional cascade count, or zero for local lights.</param>
    public RenderLightData(
        Guid persistentId,
        RenderLightKind kind,
        Vector3 position,
        Vector3 direction,
        Color color,
        float intensity,
        float range,
        float innerConeCosine,
        float outerConeCosine,
        bool castsShadows,
        float shadowStrength,
        int shadowCascadeCount)
    {
        if (persistentId == Guid.Empty)
        {
            throw new ArgumentException("A render light requires a persistent identity.", nameof(persistentId));
        }

        this.persistentId = persistentId;
        this.kind = kind;
        this.position = position;
        this.direction = direction;
        this.color = color;
        this.intensity = Math.Max(0f, intensity);
        this.range = Math.Max(0f, range);
        this.innerConeCosine = innerConeCosine;
        this.outerConeCosine = outerConeCosine;
        this.castsShadows = castsShadows;
        this.shadowStrength = Math.Clamp(shadowStrength, 0f, 1f);
        this.shadowCascadeCount = Math.Clamp(shadowCascadeCount, 0, 4);
    }

    /// <summary>Gets the persistent component identity.</summary>
    public Guid persistentId { get; }

    /// <summary>Gets the light model.</summary>
    public RenderLightKind kind { get; }

    /// <summary>Gets the world-space position for local lights.</summary>
    public Vector3 position { get; }

    /// <summary>Gets the normalized world-space emission direction.</summary>
    public Vector3 direction { get; }

    /// <summary>Gets the linear light color.</summary>
    public Color color { get; }

    /// <summary>Gets non-negative radiometric intensity.</summary>
    public float intensity { get; }

    /// <summary>Gets local-light range, or zero for directional lights.</summary>
    public float range { get; }

    /// <summary>Gets the spot inner-cone cosine.</summary>
    public float innerConeCosine { get; }

    /// <summary>Gets the spot outer-cone cosine.</summary>
    public float outerConeCosine { get; }

    /// <summary>Gets whether the light requests supported shadows.</summary>
    public bool castsShadows { get; }

    /// <summary>Gets normalized shadow contribution.</summary>
    public float shadowStrength { get; }

    /// <summary>Gets directional cascade count, or zero for local lights.</summary>
    public int shadowCascadeCount { get; }
}

/// <summary>
/// Contains immutable renderer state consumed by culling, sorting and pass execution.
/// </summary>
public sealed class RenderObjectData
{
    private readonly IReadOnlyList<MaterialAsset> m_materials;

    /// <summary>
    /// Creates one frame-scoped renderer snapshot.
    /// </summary>
    /// <param name="persistentId">Persistent component identity.</param>
    /// <param name="layer">Scene layer used for camera filtering.</param>
    /// <param name="localToWorld">Object-to-world matrix.</param>
    /// <param name="bounds">Conservative world-space bounds.</param>
    /// <param name="mesh">Imported mesh asset.</param>
    /// <param name="materials">Ordered shared materials.</param>
    /// <param name="propertyBlock">Optional frame-visible property overrides.</param>
    /// <param name="renderQueue">Resolved material render queue.</param>
    /// <param name="transparent">Whether this renderer belongs to the transparent list.</param>
    /// <param name="shadowCastingMode">Directional shadow casting behavior.</param>
    /// <param name="receiveShadows">Whether lighting may sample received shadows.</param>
    /// <param name="enableInstancing">Whether compatible draws may be instanced.</param>
    public RenderObjectData(
        Guid persistentId,
        GameLayer layer,
        Matrix localToWorld,
        RenderBounds bounds,
        MeshAsset mesh,
        IReadOnlyList<MaterialAsset> materials,
        MaterialPropertyBlock? propertyBlock,
        int renderQueue,
        bool transparent,
        ShadowCastingMode shadowCastingMode,
        bool receiveShadows,
        bool enableInstancing)
    {
        if (persistentId == Guid.Empty)
        {
            throw new ArgumentException("A render object requires a persistent identity.", nameof(persistentId));
        }

        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(materials);
        this.persistentId = persistentId;
        this.layer = layer;
        this.localToWorld = localToWorld;
        this.bounds = bounds;
        this.mesh = mesh;
        m_materials = materials.ToArray();
        this.propertyBlock = propertyBlock;
        this.renderQueue = renderQueue;
        this.transparent = transparent;
        this.shadowCastingMode = shadowCastingMode;
        this.receiveShadows = receiveShadows;
        this.enableInstancing = enableInstancing;
    }

    /// <summary>Gets the persistent component identity.</summary>
    public Guid persistentId { get; }

    /// <summary>Gets the scene layer.</summary>
    public GameLayer layer { get; }

    /// <summary>Gets the object-to-world matrix.</summary>
    public Matrix localToWorld { get; }

    /// <summary>Gets conservative world-space bounds.</summary>
    public RenderBounds bounds { get; }

    /// <summary>Gets imported mesh geometry.</summary>
    public MeshAsset mesh { get; }

    /// <summary>Gets ordered shared materials.</summary>
    public IReadOnlyList<MaterialAsset> materials => m_materials;

    /// <summary>Gets optional frame-visible material overrides.</summary>
    public MaterialPropertyBlock? propertyBlock { get; }

    /// <summary>Gets the resolved render queue.</summary>
    public int renderQueue { get; }

    /// <summary>Gets whether the renderer belongs to the transparent list.</summary>
    public bool transparent { get; }

    /// <summary>Gets directional shadow casting behavior.</summary>
    public ShadowCastingMode shadowCastingMode { get; }

    /// <summary>Gets whether lighting may sample received shadows.</summary>
    public bool receiveShadows { get; }

    /// <summary>Gets whether compatible draws may be instanced.</summary>
    public bool enableInstancing { get; }
}

/// <summary>
/// Contains camera-filtered and deterministically sorted frame data.
/// </summary>
public sealed class RenderCullingResults
{
    internal RenderCullingResults(
        IReadOnlyList<RenderObjectData> opaqueObjects,
        IReadOnlyList<RenderObjectData> transparentObjects,
        IReadOnlyList<RenderObjectData> shadowCasters,
        IReadOnlyList<RenderLightData> lights)
    {
        this.opaqueObjects = opaqueObjects;
        this.transparentObjects = transparentObjects;
        this.shadowCasters = shadowCasters;
        this.lights = lights;
    }

    /// <summary>Gets opaque objects sorted front-to-back.</summary>
    public IReadOnlyList<RenderObjectData> opaqueObjects { get; }

    /// <summary>Gets transparent objects sorted back-to-front.</summary>
    public IReadOnlyList<RenderObjectData> transparentObjects { get; }

    /// <summary>Gets visible directional shadow casters.</summary>
    public IReadOnlyList<RenderObjectData> shadowCasters { get; }

    /// <summary>Gets active lights in stable scene order.</summary>
    public IReadOnlyList<RenderLightData> lights { get; }
}

/// <summary>
/// Freezes loaded scene rendering state for one frame without retaining runtime extension types.
/// </summary>
public sealed class RenderWorldSnapshot
{
    private readonly IReadOnlyList<RenderObjectData> m_objects;
    private readonly IReadOnlyList<RenderLightData> m_lights;

    private RenderWorldSnapshot(
        IReadOnlyList<RenderObjectData> objects,
        IReadOnlyList<RenderLightData> lights)
    {
        m_objects = objects;
        m_lights = lights;
    }

    /// <summary>Gets all active render objects in stable scene order.</summary>
    public IReadOnlyList<RenderObjectData> objects => m_objects;

    /// <summary>Gets all active lights in stable scene order.</summary>
    public IReadOnlyList<RenderLightData> lights => m_lights;

    /// <summary>
    /// Captures every currently loaded scene.
    /// </summary>
    /// <returns>An immutable frame snapshot.</returns>
    public static RenderWorldSnapshot CaptureLoadedScenes() => Capture(SceneManager.loadedScenes);

    /// <summary>
    /// Captures active renderers and lights from an explicit scene set.
    /// </summary>
    /// <param name="scenes">Scenes to traverse in stable order.</param>
    /// <returns>An immutable frame snapshot.</returns>
    public static RenderWorldSnapshot Capture(IEnumerable<GameScene> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        List<RenderObjectData> objects = [];
        List<RenderLightData> lights = [];
        foreach (GameScene scene in scenes)
        {
            ArgumentNullException.ThrowIfNull(scene);
            foreach (GameObject gameObject in scene.GetObjects())
            {
                if (!gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (gameObject.TryGetComponent(out MeshRenderer? renderer)
                    && renderer is not null
                    && renderer.mesh is MeshAsset mesh
                    && renderer.materials.Count != 0)
                {
                    objects.Add(CaptureObject(gameObject, renderer, mesh));
                }

                CaptureLights(gameObject, lights);
            }
        }

        return new RenderWorldSnapshot(objects.ToArray(), lights.ToArray());
    }

    /// <summary>
    /// Filters and sorts this snapshot for one camera view.
    /// </summary>
    /// <param name="view">Camera view and culling mask.</param>
    /// <returns>Deterministic opaque, transparent, shadow and light lists.</returns>
    public RenderCullingResults Cull(RenderView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Frustum frustum = Frustum.FromMatrix(view.projectionMatrix * view.viewMatrix);
        List<RenderObjectData> opaque = [];
        List<RenderObjectData> transparent = [];
        List<RenderObjectData> shadowCasters = [];
        foreach (RenderObjectData renderObject in m_objects)
        {
            if (!view.cullingMask.Contains(renderObject.layer)
                || !frustum.Intersects(renderObject.bounds))
            {
                continue;
            }

            if (renderObject.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
            {
                (renderObject.transparent ? transparent : opaque).Add(renderObject);
            }

            if (renderObject.shadowCastingMode != ShadowCastingMode.Off)
            {
                shadowCasters.Add(renderObject);
            }
        }

        opaque.Sort((left, right) => CompareVisible(left, right, view.worldPosition, transparent: false));
        transparent.Sort((left, right) => CompareVisible(left, right, view.worldPosition, transparent: true));
        shadowCasters.Sort(static (left, right) => left.persistentId.CompareTo(right.persistentId));
        return new RenderCullingResults(opaque, transparent, shadowCasters, m_lights);
    }

    private static RenderObjectData CaptureObject(
        GameObject gameObject,
        MeshRenderer renderer,
        MeshAsset mesh)
    {
        Matrix localToWorld = Matrix.CreateTranslation(renderer.transform.worldPosition)
            * Matrix.CreateFromQuaternion(renderer.transform.worldRotation)
            * Matrix.CreateScale(renderer.transform.worldScale);
        RenderBounds bounds = TransformBounds(mesh.boundsCenter, mesh.boundsExtents, localToWorld);
        bool transparent = false;
        int renderQueue = int.MaxValue;
        foreach (MaterialAsset material in renderer.materials)
        {
            bool materialTransparent = IsTransparent(material);
            transparent |= materialTransparent;
            renderQueue = Math.Min(renderQueue, material.renderQueue ?? (materialTransparent ? 3000 : 2000));
        }

        return new RenderObjectData(
            renderer.identity.persistentId,
            gameObject.layer,
            localToWorld,
            bounds,
            mesh,
            renderer.materials,
            renderer.GetPropertyBlock()?.Snapshot(),
            renderQueue == int.MaxValue ? 2000 : renderQueue,
            transparent,
            renderer.shadowCastingMode,
            renderer.receiveShadows,
            renderer.enableInstancing);
    }

    private static void CaptureLights(GameObject gameObject, List<RenderLightData> lights)
    {
        Vector3 position = gameObject.transform.worldPosition;
        Vector3 direction = Vector3.Transform(Vector3.FORWARD, gameObject.transform.worldRotation).normalized;
        foreach (Light light in gameObject.GetComponents<Light>())
        {
            switch (light)
            {
                case DirectionalLight directional:
                    lights.Add(CreateLight(
                        directional,
                        RenderLightKind.Directional,
                        position,
                        direction,
                        0f,
                        1f,
                        1f,
                        directional.shadowCascadeCount));
                    break;
                case PointLight point:
                    lights.Add(CreateLight(
                        point,
                        RenderLightKind.Point,
                        position,
                        direction,
                        point.range,
                        1f,
                        1f,
                        0));
                    break;
                case SpotLight spot:
                    lights.Add(CreateLight(
                        spot,
                        RenderLightKind.Spot,
                        position,
                        direction,
                        spot.range,
                        MathF.Cos(MathHelper.ToRadians(spot.innerAngle * 0.5f)),
                        MathF.Cos(MathHelper.ToRadians(spot.outerAngle * 0.5f)),
                        0));
                    break;
            }
        }
    }

    private static RenderLightData CreateLight(
        Light source,
        RenderLightKind kind,
        Vector3 position,
        Vector3 direction,
        float range,
        float innerConeCosine,
        float outerConeCosine,
        int cascadeCount)
        => new(
            source.identity.persistentId,
            kind,
            position,
            direction,
            source.color,
            source.intensity,
            range,
            innerConeCosine,
            outerConeCosine,
            source.shadows,
            source.shadowStrength,
            cascadeCount);

    private static bool IsTransparent(MaterialAsset material)
    {
        if (material.renderQueue is >= 3000)
        {
            return true;
        }

        return material.shader?.definition?.passes.Any(
            static pass => pass.renderState.blend != ShaderBlendMode.Opaque) == true;
    }

    private static RenderBounds TransformBounds(Vector3 center, Vector3 extents, Matrix transform)
    {
        Vector3 worldCenter = Vector3.Transform(center, transform);
        Vector3 worldExtents = new(
            MathF.Abs(transform.m11) * extents.x
                + MathF.Abs(transform.m12) * extents.y
                + MathF.Abs(transform.m13) * extents.z,
            MathF.Abs(transform.m21) * extents.x
                + MathF.Abs(transform.m22) * extents.y
                + MathF.Abs(transform.m23) * extents.z,
            MathF.Abs(transform.m31) * extents.x
                + MathF.Abs(transform.m32) * extents.y
                + MathF.Abs(transform.m33) * extents.z);
        return new RenderBounds(worldCenter, worldExtents);
    }

    private static int CompareVisible(
        RenderObjectData left,
        RenderObjectData right,
        Vector3 cameraPosition,
        bool transparent)
    {
        int queue = left.renderQueue.CompareTo(right.renderQueue);
        if (queue != 0)
        {
            return queue;
        }

        float leftDistance = (left.bounds.center - cameraPosition).LengthSquared();
        float rightDistance = (right.bounds.center - cameraPosition).LengthSquared();
        int distance = transparent
            ? rightDistance.CompareTo(leftDistance)
            : leftDistance.CompareTo(rightDistance);
        return distance != 0 ? distance : left.persistentId.CompareTo(right.persistentId);
    }

    private readonly record struct Frustum(Plane left, Plane right, Plane bottom, Plane top, Plane near, Plane far)
    {
        public static Frustum FromMatrix(Matrix matrix)
            => new(
                Plane.Normalize(matrix.m41 + matrix.m11, matrix.m42 + matrix.m12, matrix.m43 + matrix.m13, matrix.m44 + matrix.m14),
                Plane.Normalize(matrix.m41 - matrix.m11, matrix.m42 - matrix.m12, matrix.m43 - matrix.m13, matrix.m44 - matrix.m14),
                Plane.Normalize(matrix.m41 + matrix.m21, matrix.m42 + matrix.m22, matrix.m43 + matrix.m23, matrix.m44 + matrix.m24),
                Plane.Normalize(matrix.m41 - matrix.m21, matrix.m42 - matrix.m22, matrix.m43 - matrix.m23, matrix.m44 - matrix.m24),
                Plane.Normalize(matrix.m31, matrix.m32, matrix.m33, matrix.m34),
                Plane.Normalize(matrix.m41 - matrix.m31, matrix.m42 - matrix.m32, matrix.m43 - matrix.m33, matrix.m44 - matrix.m34));

        public bool Intersects(RenderBounds bounds)
            => Intersects(left, bounds)
                && Intersects(right, bounds)
                && Intersects(bottom, bounds)
                && Intersects(top, bounds)
                && Intersects(near, bounds)
                && Intersects(far, bounds);

        private static bool Intersects(Plane plane, RenderBounds bounds)
        {
            float radius = MathF.Abs(plane.x) * bounds.extents.x
                + MathF.Abs(plane.y) * bounds.extents.y
                + MathF.Abs(plane.z) * bounds.extents.z;
            float distance = plane.x * bounds.center.x
                + plane.y * bounds.center.y
                + plane.z * bounds.center.z
                + plane.w;
            return distance + radius >= 0f;
        }
    }

    private readonly record struct Plane(float x, float y, float z, float w)
    {
        public static Plane Normalize(float x, float y, float z, float w)
        {
            float length = MathF.Sqrt(x * x + y * y + z * z);
            return length > MathHelper.C_TOLERANCE
                ? new Plane(x / length, y / length, z / length, w / length)
                : new Plane(0f, 0f, 0f, float.PositiveInfinity);
        }
    }
}
