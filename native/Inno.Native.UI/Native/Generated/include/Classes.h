#include "common.h"
#include "enums.h"

typedef struct inno_ui_Runtime inno_ui_Runtime;

typedef struct
{
	unsigned char value;
} inno_ui_Byte;
typedef struct
{
	char value;
} inno_ui_Utf8CodeUnit;
typedef struct
{
	unsigned int value;
} inno_ui_Index;
typedef struct
{
	unsigned long long value;
} inno_ui_Handle;
typedef struct
{
	float x;
	float y;
	float u;
	float v;
	unsigned int color;
} inno_ui_Vertex;
typedef struct
{
	unsigned long long mesh_id;
	unsigned long long texture_id;
	unsigned char scissor_enabled;
	int scissor_x;
	int scissor_y;
	int scissor_width;
	int scissor_height;
} inno_ui_DrawCommand;
typedef struct
{
	unsigned long long mesh_update_count;
	unsigned long long released_mesh_count;
	unsigned long long command_count;
	unsigned long long texture_update_count;
	unsigned long long released_texture_count;
} inno_ui_FrameInfo;
typedef struct
{
	unsigned long long id;
	unsigned long long revision;
	unsigned long long vertex_count;
	unsigned long long index_count;
} inno_ui_MeshInfo;
typedef struct
{
	unsigned long long id;
	unsigned long long revision;
	int width;
	int height;
	unsigned long long byte_length;
} inno_ui_TextureInfo;
typedef struct
{
	inno_ui_EventType type;
	unsigned long long document;
	unsigned long long target_id_length;
} inno_ui_EventInfo;
inno_ui_API(inno_ui_Runtime*) inno_ui_RuntimeCreate(void);
inno_ui_API(void) inno_ui_RuntimeDestroy(inno_ui_Runtime* self);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_CreateContext(inno_ui_Runtime* self, const char* name, int width, int height, float density, unsigned long long* context);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_DestroyContext(inno_ui_Runtime* self, unsigned long long context);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_SetViewport(inno_ui_Runtime* self, unsigned long long context, int width, int height, float density);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_LoadDocument(inno_ui_Runtime* self, unsigned long long context, const char* markup, const char* source_url, unsigned long long* document);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_ShowDocument(inno_ui_Runtime* self, unsigned long long context, unsigned long long document);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_HideDocument(inno_ui_Runtime* self, unsigned long long context, unsigned long long document);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_CloseDocument(inno_ui_Runtime* self, unsigned long long context, unsigned long long document);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_SetInnerMarkup(inno_ui_Runtime* self, unsigned long long context, unsigned long long document, const char* element_id, const char* markup, unsigned char* changed);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_SetAttribute(inno_ui_Runtime* self, unsigned long long context, unsigned long long document, const char* element_id, const char* name, const char* value, unsigned char* changed);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_SetClass(inno_ui_Runtime* self, unsigned long long context, unsigned long long document, const char* element_id, const char* class_name, unsigned char active, unsigned char* changed);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_LoadFont(inno_ui_Runtime* self, inno_ui_Byte* data, size_t data_count, const char* family, int style, int weight, unsigned char fallback);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_RegisterTexture(inno_ui_Runtime* self, unsigned long long context, const char* source, int width, int height, inno_ui_Byte* pixels, size_t pixels_count);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_ProcessMouseMove(inno_ui_Runtime* self, unsigned long long context, int x, int y, int modifiers);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_ProcessMouseButton(inno_ui_Runtime* self, unsigned long long context, int button, unsigned char down, int modifiers);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_ProcessMouseWheel(inno_ui_Runtime* self, unsigned long long context, float x, float y, int modifiers);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_ProcessKey(inno_ui_Runtime* self, unsigned long long context, int key, unsigned char down, int modifiers);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_ProcessText(inno_ui_Runtime* self, unsigned long long context, const char* text);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_Update(inno_ui_Runtime* self, unsigned long long context);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_Render(inno_ui_Runtime* self, unsigned long long context, inno_ui_FrameInfo* frame);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_GetMeshInfo(inno_ui_Runtime* self, unsigned long long context, unsigned long long index, inno_ui_MeshInfo* mesh);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_CopyMeshVertices(inno_ui_Runtime* self, unsigned long long context, unsigned long long mesh, inno_ui_Vertex* vertices, size_t vertices_count);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_CopyMeshIndices(inno_ui_Runtime* self, unsigned long long context, unsigned long long mesh, inno_ui_Index* indices, size_t indices_count);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_CopyCommands(inno_ui_Runtime* self, unsigned long long context, inno_ui_DrawCommand* commands, size_t commands_count);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_GetTextureInfo(inno_ui_Runtime* self, unsigned long long context, unsigned long long index, inno_ui_TextureInfo* texture);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_CopyTexturePixels(inno_ui_Runtime* self, unsigned long long context, unsigned long long texture, inno_ui_Byte* pixels, size_t pixels_count);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_CopyReleasedTextures(inno_ui_Runtime* self, unsigned long long context, inno_ui_Handle* textures, size_t textures_count);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_CopyReleasedMeshes(inno_ui_Runtime* self, unsigned long long context, inno_ui_Handle* meshes, size_t meshes_count);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_FinishFrame(inno_ui_Runtime* self, unsigned long long context);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_GetEventCount(inno_ui_Runtime* self, unsigned long long context, unsigned long long* count);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_GetEventInfo(inno_ui_Runtime* self, unsigned long long context, unsigned long long index, inno_ui_EventInfo* event_info);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_CopyEventTargetId(inno_ui_Runtime* self, unsigned long long context, unsigned long long index, inno_ui_Utf8CodeUnit* target_id, size_t target_id_count);
inno_ui_API(inno_ui_Result) inno_ui_Runtime_ClearEvents(inno_ui_Runtime* self, unsigned long long context);
