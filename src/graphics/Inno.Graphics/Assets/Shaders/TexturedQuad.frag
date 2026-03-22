#version 450

layout(location = 0) in vec4 fsin_Color;
layout(location = 1) in vec2 fsin_UV;

layout(set = 1, binding = 0) uniform texture2D Texture0;
layout(set = 1, binding = 1) uniform sampler Sampler0;

layout(location = 0) out vec4 fsout_Color;

void main()
{
    vec4 tex = texture(sampler2D(Texture0, Sampler0), fsin_UV);
    fsout_Color = tex * fsin_Color;
}
