#include "RmlUiRuntime.hpp"

#include "RmlUiEventListener.hpp"
#include "RmlUiProcessHost.hpp"
#include "RmlUiRenderInterface.hpp"

#include <RmlUi/Core.h>
#include <RmlUi/Core/Element.h>
#include <RmlUi/Core/ElementDocument.h>
#include <RmlUi/Core/Event.h>
#include <RmlUi/Core/Input.h>

#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

namespace Inno::UI::RmlUiAdapter {
namespace {

static_assert(sizeof(Byte) == sizeof(std::uint8_t));
static_assert(sizeof(Utf8CodeUnit) == sizeof(char));
static_assert(sizeof(Index) == sizeof(std::uint32_t));
static_assert(sizeof(Handle) == sizeof(std::uint64_t));

constexpr const char* C_CONTEXT_EVENTS[] = {
    "click", "change", "submit", "focus", "blur", "mouseover", "mouseout"
};

struct QueuedEvent
{
    EventType type;
    std::uint64_t document;
    std::string target_id;
};

EventType MapEvent(const Rml::String& type) noexcept
{
    if (type == "change") return EventType::Change;
    if (type == "submit") return EventType::Submit;
    if (type == "focus") return EventType::Focus;
    if (type == "blur") return EventType::Blur;
    if (type == "mouseover") return EventType::MouseEnter;
    if (type == "mouseout") return EventType::MouseLeave;
    return EventType::Click;
}

Rml::Input::KeyIdentifier MapKey(int key) noexcept
{
    using namespace Rml::Input;
    if (key >= 65 && key <= 90)
        return static_cast<KeyIdentifier>(KI_A + key - 65);
    if (key >= 48 && key <= 57)
        return static_cast<KeyIdentifier>(KI_0 + key - 48);
    if (key >= 96 && key <= 105)
        return static_cast<KeyIdentifier>(KI_NUMPAD0 + key - 96);
    if (key >= 112 && key <= 123)
        return static_cast<KeyIdentifier>(KI_F1 + key - 112);
    switch (key)
    {
    case 8: return KI_BACK;
    case 9: return KI_TAB;
    case 13: return KI_RETURN;
    case 20: return KI_CAPITAL;
    case 27: return KI_ESCAPE;
    case 32: return KI_SPACE;
    case 33: return KI_PRIOR;
    case 34: return KI_NEXT;
    case 35: return KI_END;
    case 36: return KI_HOME;
    case 37: return KI_LEFT;
    case 38: return KI_UP;
    case 39: return KI_RIGHT;
    case 40: return KI_DOWN;
    case 45: return KI_INSERT;
    case 46: return KI_DELETE;
    case 91: return KI_LWIN;
    case 92: return KI_RWIN;
    case 144: return KI_NUMLOCK;
    case 145: return KI_SCROLL;
    case 160: return KI_LSHIFT;
    case 161: return KI_RSHIFT;
    case 162: return KI_LCONTROL;
    case 163: return KI_RCONTROL;
    case 164: return KI_LMENU;
    case 165: return KI_RMENU;
    case 186: return KI_OEM_1;
    case 187: return KI_OEM_PLUS;
    case 188: return KI_OEM_COMMA;
    case 189: return KI_OEM_MINUS;
    case 190: return KI_OEM_PERIOD;
    case 191: return KI_OEM_2;
    case 192: return KI_OEM_3;
    case 219: return KI_OEM_4;
    case 220: return KI_OEM_5;
    case 221: return KI_OEM_6;
    case 222: return KI_OEM_7;
    default: return KI_UNKNOWN;
    }
}

int MapModifiers(int modifiers) noexcept
{
    int result = 0;
    if ((modifiers & 2) != 0) result |= Rml::Input::KM_CTRL;
    if ((modifiers & 4) != 0) result |= Rml::Input::KM_SHIFT;
    if ((modifiers & 1) != 0) result |= Rml::Input::KM_ALT;
    if ((modifiers & 8) != 0) result |= Rml::Input::KM_META;
    return result;
}

}

struct RmlUiRuntimeState
{
    struct ContextState final : Inno::UI::RmlUiAdapter::RmlUiEventSink
    {
        std::uint64_t id = 0;
        std::string name;
        Rml::Context* context = nullptr;
        std::unique_ptr<Inno::UI::RmlUiAdapter::RmlUiRenderInterface> renderer;
        std::unique_ptr<Inno::UI::RmlUiAdapter::RmlUiEventListener> listener;
        std::unordered_map<std::uint64_t, Rml::ElementDocument*> documents;
        std::unordered_map<Rml::ElementDocument*, std::uint64_t> document_ids;
        std::vector<QueuedEvent> events;

        void ProcessRmlUiEvent(Rml::Event& event) override
        {
            Rml::Element* target = event.GetTargetElement();
            if (!target)
                return;
            Rml::ElementDocument* document = target->GetOwnerDocument();
            auto iterator = document_ids.find(document);
            if (iterator == document_ids.end())
                return;
            events.push_back({MapEvent(event.GetType()), iterator->second, target->GetId()});
        }
    };

    std::unordered_map<std::uint64_t, std::unique_ptr<ContextState>> contexts;

    ContextState* Find(std::uint64_t context) noexcept
    {
        auto iterator = contexts.find(context);
        return iterator == contexts.end() ? nullptr : iterator->second.get();
    }

    static Rml::ElementDocument* FindDocument(ContextState* state, std::uint64_t document) noexcept
    {
        if (!state || document == 0)
            return nullptr;
        auto iterator = state->documents.find(document);
        return iterator == state->documents.end() ? nullptr : iterator->second;
    }

    static Rml::Element* FindElement(
        ContextState* state,
        std::uint64_t document,
        const char* id) noexcept
    {
        Rml::ElementDocument* value = FindDocument(state, document);
        return value && id ? value->GetElementById(id) : nullptr;
    }
};

RmlUiRuntimeState* GetRuntimeState(void* state) noexcept
{
    return static_cast<RmlUiRuntimeState*>(state);
}

Runtime::Runtime()
    : m_state(nullptr)
{
    std::unique_ptr<RmlUiRuntimeState> implementation = std::make_unique<RmlUiRuntimeState>();
    if (!Inno::UI::RmlUiAdapter::AcquireProcessHost())
        throw std::runtime_error("The RmlUi process host is already owned by another thread or failed to initialize.");
    m_state = implementation.release();
}

Runtime::~Runtime() noexcept
{
    if (!m_state)
        return;
    auto* state = static_cast<RmlUiRuntimeState*>(m_state);
    while (!state->contexts.empty())
        DestroyContext(state->contexts.begin()->first);
    delete state;
    m_state = nullptr;
    Inno::UI::RmlUiAdapter::ReleaseProcessHost();
}

Result Runtime::CreateContext(
    const char* name,
    int width,
    int height,
    float density,
    std::uint64_t& context)
{
    context = 0;
    auto* implementation = static_cast<RmlUiRuntimeState*>(m_state);
    if (!implementation || !name || name[0] == '\0' || width <= 0 || height <= 0
        || !std::isfinite(density) || density <= 0.f)
    {
        return Result::InvalidArgument;
    }

    auto state = std::make_unique<RmlUiRuntimeState::ContextState>();
    state->renderer = std::make_unique<Inno::UI::RmlUiAdapter::RmlUiRenderInterface>();
    state->id = Inno::UI::RmlUiAdapter::AllocateContextId();
    if (state->id == 0)
        return Result::BackendError;
    state->name = std::string("inno-ui-") + std::to_string(reinterpret_cast<std::uintptr_t>(this))
        + "-" + std::to_string(state->id) + "-" + name;
    state->context = Rml::CreateContext(state->name, {width, height}, state->renderer.get());
    if (!state->context)
        return Result::BackendError;
    state->context->SetDensityIndependentPixelRatio(density);
    state->listener = std::make_unique<Inno::UI::RmlUiAdapter::RmlUiEventListener>(*state);
    for (const char* event_name : C_CONTEXT_EVENTS)
        state->context->AddEventListener(event_name, state->listener.get(), true);

    const std::uint64_t id = state->id;
    implementation->contexts.emplace(id, std::move(state));
    context = id;
    return Result::Success;
}

Result Runtime::DestroyContext(std::uint64_t context)
{
    auto* implementation = static_cast<RmlUiRuntimeState*>(m_state);
    if (!implementation || context == 0)
        return Result::InvalidArgument;
    auto iterator = implementation->contexts.find(context);
    if (iterator == implementation->contexts.end())
        return Result::InvalidHandle;

    RmlUiRuntimeState::ContextState& state = *iterator->second;
    for (const char* event_name : C_CONTEXT_EVENTS)
        state.context->RemoveEventListener(event_name, state.listener.get(), true);
    Rml::RemoveContext(state.name);
    Rml::ReleaseTextures(state.renderer.get());
    Rml::ReleaseCompiledGeometry(state.renderer.get());
    state.renderer->Retire();
    implementation->contexts.erase(iterator);
    return Result::Success;
}

Result Runtime::SetViewport(
    std::uint64_t context,
    int width,
    int height,
    float density)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    if (width <= 0 || height <= 0 || !std::isfinite(density) || density <= 0.f)
        return Result::InvalidArgument;
    state->context->SetDimensions({width, height});
    state->context->SetDensityIndependentPixelRatio(density);
    return Result::Success;
}

Result Runtime::LoadDocument(
    std::uint64_t context,
    const char* markup,
    const char* source_url,
    std::uint64_t& document)
{
    document = 0;
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    if (!markup || !source_url)
        return Result::InvalidArgument;
    Rml::ElementDocument* value = state->context->LoadDocumentFromMemory(markup, source_url);
    if (!value)
        return Result::BackendError;
    const std::uint64_t id = Inno::UI::RmlUiAdapter::AllocateDocumentId();
    if (id == 0)
    {
        value->Close();
        return Result::BackendError;
    }
    state->documents.emplace(id, value);
    state->document_ids.emplace(value, id);
    document = id;
    return Result::Success;
}

Result Runtime::ShowDocument(std::uint64_t context, std::uint64_t document)
{
    Rml::ElementDocument* value = GetRuntimeState(m_state)
        ? RmlUiRuntimeState::FindDocument(GetRuntimeState(m_state)->Find(context), document)
        : nullptr;
    if (!value)
        return Result::InvalidHandle;
    value->Show();
    return Result::Success;
}

Result Runtime::HideDocument(std::uint64_t context, std::uint64_t document)
{
    Rml::ElementDocument* value = GetRuntimeState(m_state)
        ? RmlUiRuntimeState::FindDocument(GetRuntimeState(m_state)->Find(context), document)
        : nullptr;
    if (!value)
        return Result::InvalidHandle;
    value->Hide();
    return Result::Success;
}

Result Runtime::CloseDocument(std::uint64_t context, std::uint64_t document)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    Rml::ElementDocument* value = RmlUiRuntimeState::FindDocument(state, document);
    if (!value)
        return Result::InvalidHandle;
    state->document_ids.erase(value);
    state->documents.erase(document);
    value->Close();
    return Result::Success;
}

Result Runtime::SetInnerMarkup(
    std::uint64_t context,
    std::uint64_t document,
    const char* element_id,
    const char* markup,
    std::uint8_t& changed)
{
    changed = 0;
    if (!markup)
        return Result::InvalidArgument;
    Rml::Element* element = GetRuntimeState(m_state)
        ? RmlUiRuntimeState::FindElement(GetRuntimeState(m_state)->Find(context), document, element_id)
        : nullptr;
    if (element)
    {
        element->SetInnerRML(markup);
        changed = 1;
    }
    return Result::Success;
}

Result Runtime::SetAttribute(
    std::uint64_t context,
    std::uint64_t document,
    const char* element_id,
    const char* name,
    const char* value,
    std::uint8_t& changed)
{
    changed = 0;
    if (!name || !value)
        return Result::InvalidArgument;
    Rml::Element* element = GetRuntimeState(m_state)
        ? RmlUiRuntimeState::FindElement(GetRuntimeState(m_state)->Find(context), document, element_id)
        : nullptr;
    if (element)
    {
        element->SetAttribute(name, Rml::String(value));
        changed = 1;
    }
    return Result::Success;
}

Result Runtime::SetClass(
    std::uint64_t context,
    std::uint64_t document,
    const char* element_id,
    const char* class_name,
    std::uint8_t active,
    std::uint8_t& changed)
{
    changed = 0;
    if (!class_name)
        return Result::InvalidArgument;
    Rml::Element* element = GetRuntimeState(m_state)
        ? RmlUiRuntimeState::FindElement(GetRuntimeState(m_state)->Find(context), document, element_id)
        : nullptr;
    if (element)
    {
        element->SetClass(class_name, active != 0);
        changed = 1;
    }
    return Result::Success;
}

Result Runtime::LoadFont(
    std::span<Byte> data,
    const char* family,
    int style,
    int weight,
    std::uint8_t fallback)
{
    if (!m_state)
        return Result::InvalidHandle;
    return Inno::UI::RmlUiAdapter::LoadFont(
        reinterpret_cast<const std::uint8_t*>(data.data()),
        static_cast<std::uint64_t>(data.size()),
        family,
        style,
        weight,
        fallback != 0);
}

Result Runtime::RegisterTexture(
    std::uint64_t context,
    const char* source,
    int width,
    int height,
    std::span<Byte> pixels)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    return state
        ? state->renderer->RegisterTexture(
            source,
            width,
            height,
            reinterpret_cast<const std::uint8_t*>(pixels.data()),
            static_cast<std::uint64_t>(pixels.size()))
        : Result::InvalidHandle;
}

Result Runtime::ProcessMouseMove(
    std::uint64_t context,
    int x,
    int y,
    int modifiers)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    state->context->ProcessMouseMove(x, y, MapModifiers(modifiers));
    return Result::Success;
}

Result Runtime::ProcessMouseButton(
    std::uint64_t context,
    int button,
    std::uint8_t down,
    int modifiers)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    if (button < 0 || button > 4)
        return Result::InvalidArgument;
    if (down != 0)
        state->context->ProcessMouseButtonDown(button, MapModifiers(modifiers));
    else
        state->context->ProcessMouseButtonUp(button, MapModifiers(modifiers));
    return Result::Success;
}

Result Runtime::ProcessMouseWheel(
    std::uint64_t context,
    float x,
    float y,
    int modifiers)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    state->context->ProcessMouseWheel({x, y}, MapModifiers(modifiers));
    return Result::Success;
}

Result Runtime::ProcessKey(
    std::uint64_t context,
    int key,
    std::uint8_t down,
    int modifiers)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    const Rml::Input::KeyIdentifier identifier = MapKey(key);
    if (down != 0)
        state->context->ProcessKeyDown(identifier, MapModifiers(modifiers));
    else
        state->context->ProcessKeyUp(identifier, MapModifiers(modifiers));
    return Result::Success;
}

Result Runtime::ProcessText(std::uint64_t context, const char* text)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    if (!text)
        return Result::InvalidArgument;
    state->context->ProcessTextInput(text);
    return Result::Success;
}

Result Runtime::Update(std::uint64_t context)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    return state->context->Update() ? Result::Success : Result::BackendError;
}

Result Runtime::Render(std::uint64_t context, FrameInfo& frame)
{
    frame = {};
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    state->renderer->BeginRender();
    const bool rendered = state->context->Render();
    state->renderer->EndRender();
    if (!rendered)
        return Result::BackendError;
    frame = {
        state->renderer->MeshUpdateCount(),
        state->renderer->ReleasedMeshCount(),
        state->renderer->CommandCount(),
        state->renderer->TextureUpdateCount(),
        state->renderer->ReleasedTextureCount()
    };
    return Result::Success;
}

Result Runtime::GetMeshInfo(
    std::uint64_t context,
    std::uint64_t index,
    MeshInfo& mesh)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    return state ? state->renderer->GetMeshInfo(index, mesh) : Result::InvalidHandle;
}

Result Runtime::CopyMeshVertices(
    std::uint64_t context,
    std::uint64_t mesh,
    std::span<Vertex> vertices)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    return state
        ? state->renderer->CopyMeshVertices(
            mesh,
            vertices.data(),
            static_cast<std::uint64_t>(vertices.size()))
        : Result::InvalidHandle;
}

Result Runtime::CopyMeshIndices(
    std::uint64_t context,
    std::uint64_t mesh,
    std::span<Index> indices)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    return state
        ? state->renderer->CopyMeshIndices(
            mesh,
            reinterpret_cast<std::uint32_t*>(indices.data()),
            static_cast<std::uint64_t>(indices.size()))
        : Result::InvalidHandle;
}

Result Runtime::CopyCommands(
    std::uint64_t context,
    std::span<DrawCommand> commands)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    return state
        ? state->renderer->CopyCommands(
            commands.data(),
            static_cast<std::uint64_t>(commands.size()))
        : Result::InvalidHandle;
}

Result Runtime::GetTextureInfo(
    std::uint64_t context,
    std::uint64_t index,
    TextureInfo& texture)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    return state ? state->renderer->GetTextureInfo(index, texture) : Result::InvalidHandle;
}

Result Runtime::CopyTexturePixels(
    std::uint64_t context,
    std::uint64_t texture,
    std::span<Byte> pixels)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    return state
        ? state->renderer->CopyTexturePixels(
            texture,
            reinterpret_cast<std::uint8_t*>(pixels.data()),
            static_cast<std::uint64_t>(pixels.size()))
        : Result::InvalidHandle;
}

Result Runtime::CopyReleasedTextures(
    std::uint64_t context,
    std::span<Handle> textures)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    return state
        ? state->renderer->CopyReleasedTextures(
            reinterpret_cast<std::uint64_t*>(textures.data()),
            static_cast<std::uint64_t>(textures.size()))
        : Result::InvalidHandle;
}

Result Runtime::CopyReleasedMeshes(
    std::uint64_t context,
    std::span<Handle> meshes)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    return state
        ? state->renderer->CopyReleasedMeshes(
            reinterpret_cast<std::uint64_t*>(meshes.data()),
            static_cast<std::uint64_t>(meshes.size()))
        : Result::InvalidHandle;
}

Result Runtime::FinishFrame(std::uint64_t context)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    state->renderer->FinishFrame();
    return Result::Success;
}

Result Runtime::GetEventCount(std::uint64_t context, std::uint64_t& count)
{
    count = 0;
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    count = static_cast<std::uint64_t>(state->events.size());
    return Result::Success;
}

Result Runtime::GetEventInfo(
    std::uint64_t context,
    std::uint64_t index,
    EventInfo& event_info)
{
    event_info = {};
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    if (index >= static_cast<std::uint64_t>(state->events.size()))
        return Result::NotFound;
    const QueuedEvent& value = state->events[static_cast<std::size_t>(index)];
    event_info = {
        value.type,
        value.document,
        static_cast<std::uint64_t>(value.target_id.size())
    };
    return Result::Success;
}

Result Runtime::CopyEventTargetId(
    std::uint64_t context,
    std::uint64_t index,
    std::span<Utf8CodeUnit> target_id)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    if (index >= static_cast<std::uint64_t>(state->events.size()))
        return Result::NotFound;
    const std::string& value = state->events[static_cast<std::size_t>(index)].target_id;
    if (target_id.size() < value.size() + 1)
        return Result::BufferTooSmall;
    std::memcpy(target_id.data(), value.data(), value.size());
    target_id[value.size()].value = '\0';
    return Result::Success;
}

Result Runtime::ClearEvents(std::uint64_t context)
{
    RmlUiRuntimeState::ContextState* state = GetRuntimeState(m_state)
        ? GetRuntimeState(m_state)->Find(context)
        : nullptr;
    if (!state)
        return Result::InvalidHandle;
    state->events.clear();
    return Result::Success;
}

}
