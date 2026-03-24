using Inno.Graphics;
using Inno.Native.Bgfx;
using System.Runtime.InteropServices;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxResourceSet : DisposableGraphicsResource, IGraphicsResourceSet
{
    private readonly List<TextureBinding> m_textureBindings = [];
    private readonly HashSet<int> m_boundTextureSlots = [];
    private readonly HashSet<ushort> m_boundUniformIds = [];

    public BgfxResourceSet(ResourceSetDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);

        foreach (var binding in description.bindings)
        {
            if (binding.bindingType != GraphicsBindingType.Texture)
            {
                continue;
            }

            if (binding.resource is not BgfxTexture texture)
            {
                continue;
            }

            if (!m_boundTextureSlots.Add(binding.slot))
            {
                continue;
            }

            var uniform = bgfx.create_uniform($"s_tex{binding.slot}", bgfx.UniformType.Sampler, 1);
            if (!m_boundUniformIds.Add(uniform.idx))
            {
                if (uniform.Valid)
                {
                    bgfx.destroy_uniform(uniform);
                }

                continue;
            }

            m_textureBindings.Add(new TextureBinding(binding.slot, uniform, texture));
        }
    }

    internal IReadOnlyList<TextureBinding> textureBindings => m_textureBindings;

    protected override void Dispose(bool disposing)
    {
        foreach (var binding in m_textureBindings)
        {
            if (binding.uniform.Valid)
            {
                bgfx.destroy_uniform(binding.uniform);
            }
        }

        m_textureBindings.Clear();
        m_boundTextureSlots.Clear();
        m_boundUniformIds.Clear();
    }

}
