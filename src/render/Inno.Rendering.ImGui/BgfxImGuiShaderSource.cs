namespace Inno.Rendering.ImGui;

/// <summary>Provides shaderc-compatible sources for the built-in BGFX ImGui pipeline.</summary>
public static class BgfxImGuiShaderSource
{
    /// <summary>Gets the vertex and fragment interface definition.</summary>
    public const string varying = """
        vec2 a_position  : POSITION;
        vec2 a_texcoord0 : TEXCOORD0;
        vec4 a_color0    : COLOR0;
        vec2 v_texcoord0 : TEXCOORD0;
        vec4 v_color0    : COLOR0;
        """;

    /// <summary>Gets the shaderc-compatible ImGui vertex stage.</summary>
    public const string vertex = """
        $input a_position, a_texcoord0, a_color0
        $output v_texcoord0, v_color0
        #include <bgfx_shader.sh>

        void main()
        {
            gl_Position = mul(u_viewProj, vec4(a_position, 0.0, 1.0));
            v_texcoord0 = a_texcoord0;
            v_color0 = a_color0;
        }
        """;

    /// <summary>Gets the shaderc-compatible alpha-blended ImGui fragment stage.</summary>
    public const string fragment = """
        $input v_texcoord0, v_color0
        #include <bgfx_shader.sh>
        SAMPLER2D(s_tex, 0);

        void main()
        {
            gl_FragColor = texture2D(s_tex, v_texcoord0) * v_color0;
        }
        """;
}
