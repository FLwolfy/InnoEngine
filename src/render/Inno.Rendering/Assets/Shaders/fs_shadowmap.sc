#include "bgfx_shader.sh"

void main()
{
    float depth = gl_FragCoord.z;
    gl_FragColor = vec4(depth, depth, depth, 1.0);
}
