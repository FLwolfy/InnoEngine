using System;

namespace Inno.Platform.Graphics;

public enum SamplerFilter
{
    Point, 
    Linear
}

public enum SamplerAddressMode
{
    Clamp, 
    Repeat
}

public struct SamplerDescription()
{
    public SamplerFilter filter;
    public SamplerAddressMode addressU;
    public SamplerAddressMode addressV;
}

public interface ISampler : IDisposable;