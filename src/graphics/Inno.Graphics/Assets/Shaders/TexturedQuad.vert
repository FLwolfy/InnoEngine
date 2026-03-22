#version 450

layout(location = 0) in vec3 position;
layout(location = 1) in vec2 uv;

layout(set = 0, binding = 0) uniform MVP
{
    mat4 u_MVP;
};

layout(set = 0, binding = 1) uniform Color
{
    vec4 u_Color;
};

layout(set = 0, binding = 2) uniform UVRect
{
    vec4 u_UVRect; // (u0, v0, u1, v1)
};

layout(location = 0) out vec4 fsin_Color;
layout(location = 1) out vec2 fsin_UV;

void main()
{
    gl_Position = u_MVP * vec4(position, 1);
    fsin_Color = u_Color;

    // uv in [0,1], map to rect
    fsin_UV = mix(u_UVRect.xy, u_UVRect.zw, uv);
}
