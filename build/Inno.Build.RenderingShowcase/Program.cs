using System;
using System.IO;
using System.Linq;

using Inno.Assets;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Mathematics;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.ShaderGraph;

namespace Inno.Build.RenderingShowcase;

internal static class Program
{
    private const string C_SCENE_PATH = "RenderingShowcase/Showcase.iscene";
    private const string C_MESH_PATH = "RenderingShowcase/Meshes/ShowcaseCube.obj";
    private const string C_HANDWRITTEN_MATERIAL_PATH =
        "RenderingShowcase/Materials/HandwrittenUnlit.imaterial";
    private const string C_GRAPH_MATERIAL_PATH =
        "RenderingShowcase/Materials/ShaderGraphPbr.imaterial";

    private static int Main(string[] args)
    {
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: Inno.Build.RenderingShowcase <project-directory>");
            return 2;
        }

        string projectRoot = Path.GetFullPath(args[0]);
        string assetRoot = Path.Combine(projectRoot, "Assets");
        if (!Directory.Exists(assetRoot))
        {
            Console.Error.WriteLine($"Project asset directory '{assetRoot}' does not exist.");
            return 2;
        }

        try
        {
            Build(projectRoot, assetRoot);
            Console.WriteLine($"Rendering showcase scene generated at Assets/{C_SCENE_PATH}.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Build(string projectRoot, string assetRoot)
    {
        _ = typeof(SceneAsset);
        _ = typeof(ShaderCompiler);
        _ = typeof(ShaderGraphCompiler);
        _ = typeof(Camera);

        string libraryRoot = Path.Combine(projectRoot, "Library");
        Console.WriteLine("Initializing identity and reflection services...");
        IdentityManager.Initialize();
        try
        {
            AssemblyManager.Initialize(new AssemblyManagerOptions
            {
                cacheDirectory = Path.Combine(libraryRoot, "ShowcaseBuilder", "Assemblies")
            });
            try
            {
                TypeCacheManager.Initialize();
                try
                {
                    Console.WriteLine("Initializing serialization and asset services...");
                    SerializationManager.Initialize();
                    try
                    {
                        AssetManagerOptions options = AssetManagerOptions.Create(assetRoot, libraryRoot) with
                        {
                            enableFileSystemWatcher = false
                        };
                        AssetManager.Initialize(options);
                        try
                        {
                            Console.WriteLine("Importing showcase assets and capturing scene...");
                            BuildScene();
                        }
                        finally
                        {
                            AssetManager.Shutdown();
                        }
                    }
                    finally
                    {
                        SerializationManager.Shutdown();
                    }
                }
                finally
                {
                    TypeCacheManager.Shutdown();
                }
            }
            finally
            {
                AssemblyManager.Shutdown();
            }
        }
        finally
        {
            IdentityManager.Shutdown();
        }
    }

    private static void BuildScene()
    {
        ImportRequired(C_MESH_PATH);
        ImportRequired("RenderingShowcase/Shaders/HandwrittenUnlit.ishader");
        ImportRequired("RenderingShowcase/Graphs/EditorPreview.ishadergraph");
        ImportRequired(C_HANDWRITTEN_MATERIAL_PATH);
        ImportRequired(C_GRAPH_MATERIAL_PATH);
        ImportRequired("RenderingShowcase/Pipelines/ForwardPlus.irenderpipeline");
        ImportRequired("RenderingShowcase/Pipelines/Deferred.irenderpipeline");

        MeshAsset mesh = AssetManager.Load<MeshAsset>(C_MESH_PATH);
        MaterialAsset handwritten = AssetManager.Load<MaterialAsset>(C_HANDWRITTEN_MATERIAL_PATH);
        MaterialAsset generated = AssetManager.Load<MaterialAsset>(C_GRAPH_MATERIAL_PATH);
        var scene = new GameScene("Showcase");

        CreateCamera(scene);
        CreateLights(scene);
        CreateMesh(scene, "Handwritten Shader", new Vector3(-1.6f, 0f, 0f), mesh, handwritten);
        CreateMesh(scene, "ShaderGraph PBR", new Vector3(1.6f, 0f, 0f), mesh, generated);
        GameObject floor = CreateMesh(
            scene,
            "Ground",
            new Vector3(0f, -1.25f, 0f),
            mesh,
            generated);
        floor.transform.localScale = new Vector3(6f, 0.25f, 6f);

        if (AssetManager.TryLoad(C_SCENE_PATH, out SceneAsset? sceneAsset) && sceneAsset is not null)
        {
            sceneAsset.CaptureFrom(scene);
            if (!AssetManager.Save(sceneAsset))
                throw new InvalidOperationException($"Could not update '{C_SCENE_PATH}'.");
        }
        else if (!AssetManager.Save(C_SCENE_PATH, SceneAsset.Capture(scene)))
        {
            throw new InvalidOperationException($"Could not create '{C_SCENE_PATH}'.");
        }

        ValidateSavedScene();
    }

    private static void CreateCamera(GameScene scene)
    {
        GameObject gameObject = scene.CreateObject("Main Camera");
        Vector3 position = new(0f, 3f, -8f);
        gameObject.transform.worldPosition = position;
        gameObject.transform.worldRotation = Quaternion.LookRotation(
            new Vector3(0f, 0f, 0f) - position,
            Vector3.UP);
        Camera camera = gameObject.AddComponent<Camera>();
        camera.renderPath = RenderPath.Automatic;
        camera.clearMode = CameraClearMode.Sky;
    }

    private static void CreateLights(GameScene scene)
    {
        GameObject sunObject = scene.CreateObject("Sun");
        sunObject.transform.worldRotation = Quaternion.CreateFromYawPitchRoll(
            MathHelper.ToRadians(-30f),
            MathHelper.ToRadians(50f),
            0f);
        DirectionalLight sun = sunObject.AddComponent<DirectionalLight>();
        sun.intensity = 3f;
        sun.shadows = true;
        sun.shadowCascadeCount = 4;

        GameObject pointObject = scene.CreateObject("Blue Point Light");
        pointObject.transform.worldPosition = new Vector3(-2f, 2f, -1f);
        PointLight point = pointObject.AddComponent<PointLight>();
        point.color = new Color(0.15f, 0.45f, 1f);
        point.intensity = 8f;
        point.range = 7f;

        GameObject spotObject = scene.CreateObject("Warm Spot Light");
        Vector3 spotPosition = new(3f, 4f, -3f);
        spotObject.transform.worldPosition = spotPosition;
        spotObject.transform.worldRotation = Quaternion.LookRotation(-spotPosition, Vector3.UP);
        SpotLight spot = spotObject.AddComponent<SpotLight>();
        spot.color = new Color(1f, 0.45f, 0.16f);
        spot.intensity = 10f;
        spot.range = 10f;
        spot.innerAngle = 22f;
        spot.outerAngle = 36f;
    }

    private static GameObject CreateMesh(
        GameScene scene,
        string name,
        Vector3 position,
        MeshAsset mesh,
        MaterialAsset material)
    {
        GameObject gameObject = scene.CreateObject(name);
        gameObject.transform.worldPosition = position;
        MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.mesh = mesh;
        renderer.SetMaterial(0, material);
        return gameObject;
    }

    private static void ImportRequired(string path)
    {
        Console.WriteLine($"Importing Assets/{path}...");
        if (!AssetManager.Import(path))
            throw new InvalidOperationException($"No importer accepted required showcase asset '{path}'.");
    }

    private static void ValidateSavedScene()
    {
        GameScene scene = AssetManager.Load<SceneAsset>(C_SCENE_PATH).Instantiate();
        MeshRenderer[] renderers = scene.GetObjects()
            .SelectMany(static gameObject => gameObject.GetComponents<MeshRenderer>())
            .ToArray();
        if (renderers.Length != 3
            || renderers.Any(static renderer => renderer.mesh is null || renderer.materials.Count != 1)
            || scene.GetObjects().Count(static gameObject => gameObject.HasComponent<Camera>()) != 1
            || scene.GetObjects().Count(static gameObject => gameObject.HasComponent<DirectionalLight>()) != 1
            || scene.GetObjects().Count(static gameObject => gameObject.HasComponent<PointLight>()) != 1
            || scene.GetObjects().Count(static gameObject => gameObject.HasComponent<SpotLight>()) != 1)
        {
            throw new InvalidOperationException(
                "The saved showcase scene did not preserve its renderers, materials, camera, or lights.");
        }
    }
}
