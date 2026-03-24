using Inno.Core.Mathematics;

namespace Inno.Rendering;

internal readonly record struct ShadowCascadeData(
    Matrix view,
    Matrix projection,
    Matrix viewProjection,
    float splitDistance,
    Vector4 atlasScaleBias);
