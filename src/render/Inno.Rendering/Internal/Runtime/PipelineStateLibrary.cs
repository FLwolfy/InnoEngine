using Inno.Graphics;

namespace Inno.Rendering;

internal sealed class PipelineStateLibrary : IDisposable
{
    private readonly IGraphicsDevice m_device;
    private readonly string m_shaderProfile;
    private readonly string m_shaderAssetRoot;
    private readonly MaterialShaderResolverRegistry m_shaderResolvers = new();
    private readonly Dictionary<string, IGraphicsProgram> m_programCache = new(StringComparer.Ordinal);
    private readonly Dictionary<RuntimePipelineKey, IGraphicsRenderPipeline> m_pipelineCache = new();
    private readonly List<IGraphicsShader> m_shaderCache = [];
    private IRenderPipelineDescriptorFactory m_pipelineDescriptorFactory = new DefaultRenderPipelineDescriptorFactory();

    public PipelineStateLibrary(IGraphicsDevice device, string shaderProfile, string shaderAssetRoot)
    {
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_shaderProfile = shaderProfile ?? throw new ArgumentNullException(nameof(shaderProfile));
        m_shaderAssetRoot = shaderAssetRoot ?? throw new ArgumentNullException(nameof(shaderAssetRoot));
    }

    public void RegisterShaderResolver(IMaterialShaderResolver resolver)
    {
        m_shaderResolvers.Register(resolver);
    }

    public void SetPipelineDescriptorFactory(IRenderPipelineDescriptorFactory factory)
    {
        m_pipelineDescriptorFactory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public IGraphicsRenderPipeline GetOrCreatePipeline(Material material, IGraphicsInputLayout inputLayout, RenderItemFilter filter)
    {
        var shaderName = m_shaderResolvers.Resolve(material);
        if (filter == RenderItemFilter.ShadowCasters)
        {
            shaderName = "shadowmap";
        }

        var key = new RuntimePipelineKey(shaderName, material.surfaceType, material.blendMode, material.cullMode, material.depthMode, inputLayout, filter);
        if (m_pipelineCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var program = GetOrCreateProgram(shaderName);
        var descriptor = m_pipelineDescriptorFactory.Create(material, filter, program, inputLayout);
        var pipeline = m_device.CreateRenderPipeline(descriptor);

        m_pipelineCache.Add(key, pipeline);
        return pipeline;
    }

    public void Dispose()
    {
        foreach (var pipeline in m_pipelineCache.Values)
        {
            pipeline.Dispose();
        }

        foreach (var program in m_programCache.Values)
        {
            program.Dispose();
        }

        foreach (var shader in m_shaderCache)
        {
            shader.Dispose();
        }
    }

    private IGraphicsProgram GetOrCreateProgram(string shaderName)
    {
        if (m_programCache.TryGetValue(shaderName, out var cached))
        {
            return cached;
        }

        var language = m_shaderProfile switch
        {
            "metal" => ShaderLanguage.Metal,
            "dxbc" or "dxil" => ShaderLanguage.Hlsl,
            "spirv" => ShaderLanguage.SpirV,
            _ => ShaderLanguage.Glsl
        };

        var vsBytes = LoadShader("vs", shaderName);
        var fsBytes = LoadShader("fs", shaderName);
        var vs = m_device.CreateShader(new ShaderDescription
        {
            stage = ShaderStage.Vertex,
            language = language,
            bytecode = vsBytes
        });
        var fs = m_device.CreateShader(new ShaderDescription
        {
            stage = ShaderStage.Fragment,
            language = language,
            bytecode = fsBytes
        });

        var program = m_device.CreateProgram(new GraphicsProgramDescription
        {
            shaders = [vs, fs]
        });

        m_shaderCache.Add(vs);
        m_shaderCache.Add(fs);
        m_programCache.Add(shaderName, program);
        return program;
    }

    private ReadOnlyMemory<byte> LoadShader(string stagePrefix, string shaderName)
    {
        TryCompileRuntime(shaderName);
        var filePath = Path.Combine(m_shaderAssetRoot, m_shaderProfile, $"{stagePrefix}_{shaderName}.bin");

        if (!File.Exists(filePath))
        {
            TryCompileRuntime("cubes");
            filePath = Path.Combine(m_shaderAssetRoot, m_shaderProfile, $"{stagePrefix}_cubes.bin");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Cannot find shader bytecode for '{shaderName}' in profile '{m_shaderProfile}'.", filePath);
        }

        return File.ReadAllBytes(filePath);
    }

    private void TryCompileRuntime(string shaderName)
    {
        try
        {
            RuntimeShaderCompiler.EnsureCompiled(m_shaderAssetRoot, m_shaderProfile, shaderName);
        }
        catch (FileNotFoundException)
        {
            // Allow fallback shader resolution when custom shader source is not provided.
        }
    }
}
