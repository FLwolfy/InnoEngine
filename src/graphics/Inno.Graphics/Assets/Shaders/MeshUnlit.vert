#version 450

// 注意：你的 VeldridPipelineState 會用 attr0/attr1... 對應 location 0/1...
// 我們只用 location=0 的 position，其他 vertex attributes（normal/uv）就算存在也可以不宣告、不使用。

layout(location = 0) in vec3 position;

layout(set = 0, binding = 0) uniform MVP
{
   mat4 u_MVP;
};

layout(set = 0, binding = 1) uniform Color
{
   vec4 u_Color;
};

layout(location = 0) out vec4 fsin_Color;

void main()
{
   gl_Position = u_MVP * vec4(position, 1.0);
   fsin_Color = u_Color;
}
