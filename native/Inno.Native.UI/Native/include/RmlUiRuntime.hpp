#pragma once

#include <cstdint>
#include <span>

/// Reports the result of an operation performed by the native RmlUi adapter.
enum class Result : std::uint32_t
{
    UnknownError = 0,
    Success = 1,
    InvalidArgument = 2,
    OutOfMemory = 3,
    InvalidHandle = 4,
    BackendError = 5,
    BufferTooSmall = 6,
    NotFound = 7
};

/// Identifies an input event emitted by a retained UI document.
enum class EventType : std::uint32_t
{
    Click = 0,
    Change = 1,
    Submit = 2,
    Focus = 3,
    Blur = 4,
    MouseEnter = 5,
    MouseLeave = 6
};

namespace Inno::UI::RmlUiAdapter {

/// Carries one byte across the generated span ABI.
struct Byte
{
    std::uint8_t value;
};

/// Carries one UTF-8 code unit across the generated span ABI.
struct Utf8CodeUnit
{
    char value;
};

/// Carries one mesh index across the generated span ABI.
struct Index
{
    std::uint32_t value;
};

/// Carries one stable resource handle across the generated span ABI.
struct Handle
{
    std::uint64_t value;
};

/// Stores one premultiplied UI vertex in backend-neutral transfer form.
struct Vertex
{
    float x;
    float y;
    float u;
    float v;
    std::uint32_t color;
};

/// Describes one retained mesh draw submitted during a UI frame.
struct DrawCommand
{
    std::uint64_t mesh_id;
    std::uint64_t texture_id;
    std::uint8_t scissor_enabled;
    std::int32_t scissor_x;
    std::int32_t scissor_y;
    std::int32_t scissor_width;
    std::int32_t scissor_height;
};

/// Summarizes the incremental resources and commands available for one frame.
struct FrameInfo
{
    std::uint64_t mesh_update_count;
    std::uint64_t released_mesh_count;
    std::uint64_t command_count;
    std::uint64_t texture_update_count;
    std::uint64_t released_texture_count;
};

/// Describes one retained mesh update.
struct MeshInfo
{
    std::uint64_t id;
    std::uint64_t revision;
    std::uint64_t vertex_count;
    std::uint64_t index_count;
};

/// Describes one retained RGBA texture update.
struct TextureInfo
{
    std::uint64_t id;
    std::uint64_t revision;
    std::int32_t width;
    std::int32_t height;
    std::uint64_t byte_length;
};

/// Describes one queued UI event without exposing third-party event objects.
struct EventInfo
{
    EventType type;
    std::uint64_t document;
    std::uint64_t target_id_length;
};

/// Owns one isolated RmlUi adapter session and its retained contexts.
class Runtime final
{
public:
    /// Initializes an adapter session on the current thread.
    Runtime();

    /// Releases every retained context and the process-wide RmlUi reference.
    ~Runtime() noexcept;

    Result CreateContext(
        const char* name,
        std::int32_t width,
        std::int32_t height,
        float density,
        std::uint64_t& context);
    Result DestroyContext(std::uint64_t context);
    Result SetViewport(std::uint64_t context, std::int32_t width, std::int32_t height, float density);

    Result LoadDocument(
        std::uint64_t context,
        const char* markup,
        const char* source_url,
        std::uint64_t& document);
    Result ShowDocument(std::uint64_t context, std::uint64_t document);
    Result HideDocument(std::uint64_t context, std::uint64_t document);
    Result CloseDocument(std::uint64_t context, std::uint64_t document);
    Result SetInnerMarkup(
        std::uint64_t context,
        std::uint64_t document,
        const char* element_id,
        const char* markup,
        std::uint8_t& changed);
    Result SetAttribute(
        std::uint64_t context,
        std::uint64_t document,
        const char* element_id,
        const char* name,
        const char* value,
        std::uint8_t& changed);
    Result SetClass(
        std::uint64_t context,
        std::uint64_t document,
        const char* element_id,
        const char* class_name,
        std::uint8_t active,
        std::uint8_t& changed);

    Result LoadFont(
        std::span<Byte> data,
        const char* family,
        std::int32_t style,
        std::int32_t weight,
        std::uint8_t fallback);
    Result RegisterTexture(
        std::uint64_t context,
        const char* source,
        std::int32_t width,
        std::int32_t height,
        std::span<Byte> pixels);

    Result ProcessMouseMove(std::uint64_t context, std::int32_t x, std::int32_t y, std::int32_t modifiers);
    Result HasElementAtPoint(std::uint64_t context, std::int32_t x, std::int32_t y, std::uint8_t& hit);
    Result ProcessMouseButton(
        std::uint64_t context,
        std::int32_t button,
        std::uint8_t down,
        std::int32_t modifiers);
    Result ProcessMouseWheel(std::uint64_t context, float x, float y, std::int32_t modifiers);
    Result ProcessKey(std::uint64_t context, std::int32_t key, std::uint8_t down, std::int32_t modifiers);
    Result ProcessText(std::uint64_t context, const char* text);

    Result Update(std::uint64_t context);
    Result Render(std::uint64_t context, FrameInfo& frame);
    Result GetMeshInfo(std::uint64_t context, std::uint64_t index, MeshInfo& mesh);
    Result CopyMeshVertices(std::uint64_t context, std::uint64_t mesh, std::span<Vertex> vertices);
    Result CopyMeshIndices(std::uint64_t context, std::uint64_t mesh, std::span<Index> indices);
    Result CopyCommands(std::uint64_t context, std::span<DrawCommand> commands);
    Result GetTextureInfo(std::uint64_t context, std::uint64_t index, TextureInfo& texture);
    Result CopyTexturePixels(std::uint64_t context, std::uint64_t texture, std::span<Byte> pixels);
    Result CopyReleasedTextures(std::uint64_t context, std::span<Handle> textures);
    Result CopyReleasedMeshes(std::uint64_t context, std::span<Handle> meshes);
    Result FinishFrame(std::uint64_t context);

    Result GetEventCount(std::uint64_t context, std::uint64_t& count);
    Result GetEventInfo(std::uint64_t context, std::uint64_t index, EventInfo& event_info);
    Result CopyEventTargetId(std::uint64_t context, std::uint64_t index, std::span<Utf8CodeUnit> target_id);
    Result ClearEvents(std::uint64_t context);

private:
    void* m_state;
};

}
