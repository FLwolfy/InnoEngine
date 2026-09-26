#ifndef inno_ui_BUILD_SHARED
#define inno_ui_BUILD_SHARED 1
#endif
#include "Classes.h"
#include <algorithm>
#include <exception>
#include <iterator>
#include <stdexcept>
#include <string>
#include <utility>
#include "RmlUiRuntime.hpp"

static thread_local std::string inno_ui_last_error;

inno_ui_API_INTERNAL(const char*) inno_ui_GetLastError(void)
{
	return inno_ui_last_error.c_str();
}
inno_ui_API_INTERNAL(void) inno_ui_ClearLastError(void)
{
	inno_ui_last_error.clear();
}
inno_ui_API_INTERNAL(inno_ui_Runtime*) inno_ui_RuntimeCreate(void)
{
	try
	{
		inno_ui_ClearLastError();
		return reinterpret_cast<inno_ui_Runtime*>(new Inno::UI::RmlUiAdapter::Runtime());
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return nullptr;
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return nullptr;
	}
}
inno_ui_API_INTERNAL(void) inno_ui_RuntimeDestroy(inno_ui_Runtime* self)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		delete ptr;
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return;
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return;
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_CreateContext(inno_ui_Runtime* self, const char* name, int width, int height, float density, unsigned long long* context)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->CreateContext(reinterpret_cast<const char*>(name), width, height, density, *context));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_DestroyContext(inno_ui_Runtime* self, unsigned long long context)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->DestroyContext(context));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_SetViewport(inno_ui_Runtime* self, unsigned long long context, int width, int height, float density)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->SetViewport(context, width, height, density));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_LoadDocument(inno_ui_Runtime* self, unsigned long long context, const char* markup, const char* source_url, unsigned long long* document)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->LoadDocument(context, reinterpret_cast<const char*>(markup), reinterpret_cast<const char*>(source_url), *document));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_ShowDocument(inno_ui_Runtime* self, unsigned long long context, unsigned long long document)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->ShowDocument(context, document));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_HideDocument(inno_ui_Runtime* self, unsigned long long context, unsigned long long document)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->HideDocument(context, document));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_CloseDocument(inno_ui_Runtime* self, unsigned long long context, unsigned long long document)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->CloseDocument(context, document));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_SetInnerMarkup(inno_ui_Runtime* self, unsigned long long context, unsigned long long document, const char* element_id, const char* markup, unsigned char* changed)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->SetInnerMarkup(context, document, reinterpret_cast<const char*>(element_id), reinterpret_cast<const char*>(markup), *changed));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_SetAttribute(inno_ui_Runtime* self, unsigned long long context, unsigned long long document, const char* element_id, const char* name, const char* value, unsigned char* changed)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->SetAttribute(context, document, reinterpret_cast<const char*>(element_id), reinterpret_cast<const char*>(name), reinterpret_cast<const char*>(value), *changed));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_SetClass(inno_ui_Runtime* self, unsigned long long context, unsigned long long document, const char* element_id, const char* class_name, unsigned char active, unsigned char* changed)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->SetClass(context, document, reinterpret_cast<const char*>(element_id), reinterpret_cast<const char*>(class_name), active, *changed));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_LoadFont(inno_ui_Runtime* self, inno_ui_Byte* data, size_t data_count, const char* family, int style, int weight, unsigned char fallback)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->LoadFont(std::span<Inno::UI::RmlUiAdapter::Byte>(reinterpret_cast<Inno::UI::RmlUiAdapter::Byte*>(data), data_count), reinterpret_cast<const char*>(family), style, weight, fallback));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_RegisterTexture(inno_ui_Runtime* self, unsigned long long context, const char* source, int width, int height, inno_ui_Byte* pixels, size_t pixels_count)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->RegisterTexture(context, reinterpret_cast<const char*>(source), width, height, std::span<Inno::UI::RmlUiAdapter::Byte>(reinterpret_cast<Inno::UI::RmlUiAdapter::Byte*>(pixels), pixels_count)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_ProcessMouseMove(inno_ui_Runtime* self, unsigned long long context, int x, int y, int modifiers)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->ProcessMouseMove(context, x, y, modifiers));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_HasElementAtPoint(inno_ui_Runtime* self, unsigned long long context, int x, int y, unsigned char* hit)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->HasElementAtPoint(context, x, y, *hit));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_ProcessMouseButton(inno_ui_Runtime* self, unsigned long long context, int button, unsigned char down, int modifiers)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->ProcessMouseButton(context, button, down, modifiers));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_ProcessMouseWheel(inno_ui_Runtime* self, unsigned long long context, float x, float y, int modifiers)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->ProcessMouseWheel(context, x, y, modifiers));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_ProcessKey(inno_ui_Runtime* self, unsigned long long context, int key, unsigned char down, int modifiers)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->ProcessKey(context, key, down, modifiers));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_ProcessText(inno_ui_Runtime* self, unsigned long long context, const char* text)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->ProcessText(context, reinterpret_cast<const char*>(text)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_Update(inno_ui_Runtime* self, unsigned long long context)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->Update(context));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_Render(inno_ui_Runtime* self, unsigned long long context, inno_ui_FrameInfo* frame)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->Render(context, *reinterpret_cast<Inno::UI::RmlUiAdapter::FrameInfo*>(frame)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_GetMeshInfo(inno_ui_Runtime* self, unsigned long long context, unsigned long long index, inno_ui_MeshInfo* mesh)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->GetMeshInfo(context, index, *reinterpret_cast<Inno::UI::RmlUiAdapter::MeshInfo*>(mesh)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_CopyMeshVertices(inno_ui_Runtime* self, unsigned long long context, unsigned long long mesh, inno_ui_Vertex* vertices, size_t vertices_count)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->CopyMeshVertices(context, mesh, std::span<Inno::UI::RmlUiAdapter::Vertex>(reinterpret_cast<Inno::UI::RmlUiAdapter::Vertex*>(vertices), vertices_count)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_CopyMeshIndices(inno_ui_Runtime* self, unsigned long long context, unsigned long long mesh, inno_ui_Index* indices, size_t indices_count)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->CopyMeshIndices(context, mesh, std::span<Inno::UI::RmlUiAdapter::Index>(reinterpret_cast<Inno::UI::RmlUiAdapter::Index*>(indices), indices_count)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_CopyCommands(inno_ui_Runtime* self, unsigned long long context, inno_ui_DrawCommand* commands, size_t commands_count)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->CopyCommands(context, std::span<Inno::UI::RmlUiAdapter::DrawCommand>(reinterpret_cast<Inno::UI::RmlUiAdapter::DrawCommand*>(commands), commands_count)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_GetTextureInfo(inno_ui_Runtime* self, unsigned long long context, unsigned long long index, inno_ui_TextureInfo* texture)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->GetTextureInfo(context, index, *reinterpret_cast<Inno::UI::RmlUiAdapter::TextureInfo*>(texture)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_CopyTexturePixels(inno_ui_Runtime* self, unsigned long long context, unsigned long long texture, inno_ui_Byte* pixels, size_t pixels_count)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->CopyTexturePixels(context, texture, std::span<Inno::UI::RmlUiAdapter::Byte>(reinterpret_cast<Inno::UI::RmlUiAdapter::Byte*>(pixels), pixels_count)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_CopyReleasedTextures(inno_ui_Runtime* self, unsigned long long context, inno_ui_Handle* textures, size_t textures_count)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->CopyReleasedTextures(context, std::span<Inno::UI::RmlUiAdapter::Handle>(reinterpret_cast<Inno::UI::RmlUiAdapter::Handle*>(textures), textures_count)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_CopyReleasedMeshes(inno_ui_Runtime* self, unsigned long long context, inno_ui_Handle* meshes, size_t meshes_count)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->CopyReleasedMeshes(context, std::span<Inno::UI::RmlUiAdapter::Handle>(reinterpret_cast<Inno::UI::RmlUiAdapter::Handle*>(meshes), meshes_count)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_FinishFrame(inno_ui_Runtime* self, unsigned long long context)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->FinishFrame(context));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_GetEventCount(inno_ui_Runtime* self, unsigned long long context, unsigned long long* count)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->GetEventCount(context, *count));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_GetEventInfo(inno_ui_Runtime* self, unsigned long long context, unsigned long long index, inno_ui_EventInfo* event_info)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->GetEventInfo(context, index, *reinterpret_cast<Inno::UI::RmlUiAdapter::EventInfo*>(event_info)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_CopyEventTargetId(inno_ui_Runtime* self, unsigned long long context, unsigned long long index, inno_ui_Utf8CodeUnit* target_id, size_t target_id_count)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->CopyEventTargetId(context, index, std::span<Inno::UI::RmlUiAdapter::Utf8CodeUnit>(reinterpret_cast<Inno::UI::RmlUiAdapter::Utf8CodeUnit*>(target_id), target_id_count)));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
inno_ui_API_INTERNAL(inno_ui_Result) inno_ui_Runtime_ClearEvents(inno_ui_Runtime* self, unsigned long long context)
{
	try
	{
		inno_ui_ClearLastError();
		auto* ptr = reinterpret_cast<Inno::UI::RmlUiAdapter::Runtime*>(self);
		return static_cast<inno_ui_Result>(ptr->ClearEvents(context));
	}
	catch (const std::exception& exception)
	{
		inno_ui_last_error = exception.what();
		return {};
	}
	catch (...)
	{
		inno_ui_last_error = "Unknown C++ exception";
		return {};
	}
}
