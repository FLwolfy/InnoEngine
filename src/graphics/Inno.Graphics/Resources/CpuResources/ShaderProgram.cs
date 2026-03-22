using System.Collections.Generic;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Resources.CpuResources;

public class ShaderProgram
{
    private readonly Dictionary<ShaderStage, Dictionary<string, Shader>> m_shaders = new();

    public void Add(Shader shader)
    {
        if (!m_shaders.TryGetValue(shader.stage, out var stageDict))
        {
            stageDict = new Dictionary<string, Shader>();
            m_shaders[shader.stage] = stageDict;
        }
        stageDict[shader.name] = shader;
    }

    public IReadOnlyDictionary<string, Shader> GetShadersByStage(ShaderStage stage)
        => m_shaders.TryGetValue(stage, out var stageDict) ? stageDict : new Dictionary<string, Shader>();
}