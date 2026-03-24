$input a_position, a_normal, a_color0

#include "bgfx_shader.sh"

uniform vec4 u_shadowViewProj0;
uniform vec4 u_shadowViewProj1;
uniform vec4 u_shadowViewProj2;
uniform vec4 u_shadowViewProj3;

mat4 GetShadowViewProj()
{
    return mat4(u_shadowViewProj0, u_shadowViewProj1, u_shadowViewProj2, u_shadowViewProj3);
}

void main()
{
    // Keep attribute mapping stable across backends by consuming normal/color too.
    vec3 shadowPos = a_position + a_normal * 1e-7 + a_color0.xyz * 1e-7;
    vec4 worldPos = mul(u_model[0], vec4(shadowPos, 1.0));
    gl_Position = mul(GetShadowViewProj(), worldPos);
}
