#version 450

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
   gl_Position = u_MVP * vec4(position, 1);
   fsin_Color = u_Color;
}