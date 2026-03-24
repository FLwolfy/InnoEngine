$input a_position, a_normal, a_texcoord0, a_color0
$output v_color0, v_worldPos, v_worldNormal, v_shadowPos, v_uv0

#include "bgfx_shader.sh"

uniform vec4 u_lightViewProj0_0;
uniform vec4 u_lightViewProj0_1;
uniform vec4 u_lightViewProj0_2;
uniform vec4 u_lightViewProj0_3;

mat4 GetLightViewProj0()
{
    return mat4(u_lightViewProj0_0, u_lightViewProj0_1, u_lightViewProj0_2, u_lightViewProj0_3);
}

void main()
{
    vec4 worldPos = mul(u_model[0], vec4(a_position, 1.0));
    vec3 worldNormal = normalize(mul(u_model[0], vec4(a_normal, 0.0)).xyz);
    gl_Position = mul(u_modelViewProj, vec4(a_position, 1.0));
    v_color0 = a_color0;
    v_worldPos = worldPos.xyz;
    v_worldNormal = worldNormal;
    v_shadowPos = mul(GetLightViewProj0(), worldPos);
    v_uv0 = a_texcoord0;
}
