$input v_color0, v_worldPos, v_worldNormal, v_shadowPos, v_uv0

#include "bgfx_shader.sh"

SAMPLER2D(s_tex0, 0);
SAMPLER2D(s_tex1, 1);

uniform vec4 u_mainLightDir;
uniform vec4 u_globalLight;
uniform vec4 u_ambient;
uniform vec4 u_shadowParams;
uniform vec4 u_shadowReceiver;

float SampleShadow(vec4 shadowPos)
{
    vec3 ndc = shadowPos.xyz / max(shadowPos.w, 1e-5);
    vec2 uv = ndc.xy * 0.5 + 0.5;
    float shadowDepth = ndc.z * 0.5 + 0.5;

    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
    {
        return 1.0;
    }

    uv.y = 1.0 - uv.y;

    float texel = max(1e-6, u_shadowParams.z);
    float radius = u_shadowParams.w;
    float bias = u_shadowParams.x;
    float lit = 0.0;
    float samples = 0.0;
    for (float y = -radius; y <= radius; y += 1.0)
    {
        for (float x = -radius; x <= radius; x += 1.0)
        {
            vec2 offset = vec2(x, y) * texel;
            float depth = texture2D(s_tex1, uv + offset).r;
            lit += (shadowDepth - bias) <= depth ? 1.0 : 0.0;
            samples += 1.0;
        }
    }

    return lit / max(samples, 1.0);
}

void main()
{
    vec4 albedo = texture2D(s_tex0, v_uv0) * v_color0;
    vec3 N = normalize(v_worldNormal);
    vec3 L = normalize(-u_mainLightDir.xyz);
    float ndotl = max(dot(N, L), 0.0);

    float shadow = 1.0;
    if (u_shadowReceiver.x > 0.5)
    {
        shadow = mix(1.0, SampleShadow(v_shadowPos), clamp(u_shadowParams.y, 0.0, 1.0));
    }

    vec3 ambient = u_ambient.rgb * u_ambient.a;
    vec3 direct = u_globalLight.rgb * u_globalLight.a * ndotl * shadow;
    vec3 finalColor = albedo.rgb * (ambient + direct);
    gl_FragColor = vec4(finalColor, albedo.a);
}
