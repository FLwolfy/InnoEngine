#include "RmlUiRenderInterface.hpp"

#include <algorithm>
#include <cstddef>
#include <cstring>
#include <limits>
#include <memory>
#include <new>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

namespace Inno::UI::RmlUiAdapter {
namespace {

template <typename TValue>
Result CopyVector(
    const std::vector<TValue>& source,
    TValue* destination,
    std::uint64_t capacity) noexcept
{
    if (capacity < static_cast<std::uint64_t>(source.size()))
        return Result::BufferTooSmall;
    if (!source.empty() && !destination)
        return Result::InvalidArgument;
    std::copy(source.begin(), source.end(), destination);
    return Result::Success;
}

void AppendKey(std::string& key, float value)
{
    key.append(reinterpret_cast<const char*>(&value), sizeof(value));
}

}

struct RmlUiRenderInterface::State
{
    struct Mesh
    {
        std::uint64_t id = 0;
        std::uint64_t revision = 0;
        std::uint64_t last_used_frame = 0;
        std::vector<Vertex> vertices;
        std::vector<std::uint32_t> indices;
    };

    struct Geometry
    {
        std::vector<Rml::Vertex> vertices;
        std::vector<int> indices;
        std::unordered_map<std::string, std::uint64_t> variants;
    };

    struct Texture
    {
        std::uint64_t id = 0;
        std::uint64_t revision = 0;
        int width = 0;
        int height = 0;
        std::vector<std::uint8_t> pixels;
    };

    std::vector<DrawCommand> commands;
    std::unordered_map<std::uint64_t, Mesh> meshes;
    std::vector<std::uint64_t> mesh_updates;
    std::vector<std::uint64_t> released_meshes;
    std::unordered_map<std::uint64_t, Texture> textures;
    std::vector<std::uint64_t> texture_updates;
    std::vector<std::uint64_t> released_textures;
    std::unordered_map<std::string, Texture> source_textures;
    std::unordered_set<Geometry*> geometries;
    std::uint64_t next_texture = 1;
    std::uint64_t next_mesh = 1;
    std::uint64_t next_revision = 1;
    std::uint64_t frame_number = 0;
    bool scissor_enabled = false;
    Rml::Rectanglei scissor = Rml::Rectanglei::FromPositionSize({0, 0}, {0, 0});
    bool has_transform = false;
    Rml::Matrix4f transform;
};

RmlUiRenderInterface::RmlUiRenderInterface()
    : m_state(new State())
{
}

RmlUiRenderInterface::~RmlUiRenderInterface()
{
    Retire();
    delete m_state;
}

Rml::CompiledGeometryHandle RmlUiRenderInterface::CompileGeometry(
    Rml::Span<const Rml::Vertex> vertices,
    Rml::Span<const int> indices)
{
    std::unique_ptr<State::Geometry> geometry(new (std::nothrow) State::Geometry());
    if (!geometry)
        return 0;
    try
    {
        geometry->vertices.assign(vertices.begin(), vertices.end());
        geometry->indices.assign(indices.begin(), indices.end());
        State::Geometry* result = geometry.release();
        m_state->geometries.insert(result);
        return reinterpret_cast<Rml::CompiledGeometryHandle>(result);
    }
    catch (...)
    {
        return 0;
    }
}

void RmlUiRenderInterface::RenderGeometry(
    Rml::CompiledGeometryHandle handle,
    Rml::Vector2f translation,
    Rml::TextureHandle texture)
{
    auto* geometry = reinterpret_cast<State::Geometry*>(handle);
    if (!geometry)
        return;

    std::string key;
    key.reserve(sizeof(float) * 18 + 1);
    AppendKey(key, translation.x);
    AppendKey(key, translation.y);
    key.push_back(m_state->has_transform ? '\1' : '\0');
    if (m_state->has_transform)
    {
        for (std::size_t row = 0; row < 4; ++row)
            for (std::size_t column = 0; column < 4; ++column)
                AppendKey(key, m_state->transform[row][column]);
    }

    auto variant = geometry->variants.find(key);
    std::uint64_t mesh_id = 0;
    if (variant == geometry->variants.end() || m_state->meshes.find(variant->second) == m_state->meshes.end())
    {
        State::Mesh mesh;
        mesh.id = m_state->next_mesh++;
        mesh.revision = m_state->next_revision++;
        mesh.last_used_frame = m_state->frame_number;
        mesh.vertices.reserve(geometry->vertices.size());
        mesh.indices.reserve(geometry->indices.size());
        for (const Rml::Vertex& source : geometry->vertices)
        {
            float x = source.position.x + translation.x;
            float y = source.position.y + translation.y;
            if (m_state->has_transform)
            {
                const Rml::Vector4f value = m_state->transform * Rml::Vector4f(x, y, 0.f, 1.f);
                if (value.w != 0.f)
                {
                    x = value.x / value.w;
                    y = value.y / value.w;
                }
            }
            const Rml::ColourbPremultiplied& color = source.colour;
            const std::uint32_t packed = static_cast<std::uint32_t>(color.red)
                | (static_cast<std::uint32_t>(color.green) << 8)
                | (static_cast<std::uint32_t>(color.blue) << 16)
                | (static_cast<std::uint32_t>(color.alpha) << 24);
            mesh.vertices.push_back({x, y, source.tex_coord.x, source.tex_coord.y, packed});
        }
        for (int index : geometry->indices)
            mesh.indices.push_back(static_cast<std::uint32_t>(index));

        mesh_id = mesh.id;
        m_state->meshes.emplace(mesh.id, std::move(mesh));
        if (variant == geometry->variants.end())
            geometry->variants.emplace(std::move(key), mesh_id);
        else
            variant->second = mesh_id;
        m_state->mesh_updates.push_back(mesh_id);
    }
    else
    {
        mesh_id = variant->second;
        m_state->meshes.at(mesh_id).last_used_frame = m_state->frame_number;
    }

    m_state->commands.push_back({
        mesh_id,
        static_cast<std::uint64_t>(texture),
        static_cast<std::uint8_t>(m_state->scissor_enabled ? 1 : 0),
        m_state->scissor.Left(),
        m_state->scissor.Top(),
        m_state->scissor.Width(),
        m_state->scissor.Height()
    });
}

void RmlUiRenderInterface::ReleaseGeometry(Rml::CompiledGeometryHandle handle)
{
    auto* geometry = reinterpret_cast<State::Geometry*>(handle);
    if (!geometry)
        return;
    for (const auto& variant : geometry->variants)
    {
        if (m_state->meshes.erase(variant.second) == 1)
            m_state->released_meshes.push_back(variant.second);
    }
    m_state->geometries.erase(geometry);
    delete geometry;
}

Rml::TextureHandle RmlUiRenderInterface::LoadTexture(Rml::Vector2i& dimensions, const Rml::String& source)
{
    auto iterator = m_state->source_textures.find(source);
    if (iterator == m_state->source_textures.end())
    {
        for (auto candidate = m_state->source_textures.begin(); candidate != m_state->source_textures.end(); ++candidate)
        {
            if (source.size() >= candidate->first.size()
                && source.compare(source.size() - candidate->first.size(), candidate->first.size(), candidate->first) == 0)
            {
                iterator = candidate;
                break;
            }
        }
    }
    if (iterator == m_state->source_textures.end())
        return 0;

    State::Texture texture = iterator->second;
    texture.id = m_state->next_texture++;
    texture.revision = m_state->next_revision++;
    dimensions = Rml::Vector2i(texture.width, texture.height);
    const std::uint64_t id = texture.id;
    m_state->textures.emplace(id, std::move(texture));
    m_state->texture_updates.push_back(id);
    return static_cast<Rml::TextureHandle>(id);
}

Rml::TextureHandle RmlUiRenderInterface::GenerateTexture(
    Rml::Span<const Rml::byte> source,
    Rml::Vector2i dimensions)
{
    if (dimensions.x <= 0 || dimensions.y <= 0
        || source.size() != static_cast<std::size_t>(dimensions.x) * dimensions.y * 4)
    {
        return 0;
    }

    State::Texture texture;
    texture.id = m_state->next_texture++;
    texture.revision = m_state->next_revision++;
    texture.width = dimensions.x;
    texture.height = dimensions.y;
    texture.pixels.assign(source.begin(), source.end());
    const std::uint64_t id = texture.id;
    m_state->textures.emplace(id, std::move(texture));
    m_state->texture_updates.push_back(id);
    return static_cast<Rml::TextureHandle>(id);
}

void RmlUiRenderInterface::ReleaseTexture(Rml::TextureHandle handle)
{
    const std::uint64_t id = static_cast<std::uint64_t>(handle);
    if (m_state->textures.erase(id) == 1)
        m_state->released_textures.push_back(id);
}

void RmlUiRenderInterface::EnableScissorRegion(bool enable)
{
    m_state->scissor_enabled = enable;
}

void RmlUiRenderInterface::SetScissorRegion(Rml::Rectanglei region)
{
    m_state->scissor = region;
}

void RmlUiRenderInterface::SetTransform(const Rml::Matrix4f* transform)
{
    m_state->has_transform = transform != nullptr;
    if (transform)
        m_state->transform = *transform;
}

Result RmlUiRenderInterface::RegisterTexture(
    const char* source,
    int width,
    int height,
    const std::uint8_t* pixels,
    std::uint64_t length)
{
    if (!source || source[0] == '\0' || width <= 0 || height <= 0 || !pixels)
        return Result::InvalidArgument;
    const std::uint64_t expected = static_cast<std::uint64_t>(width)
        * static_cast<std::uint64_t>(height) * 4ull;
    if (length != expected || length > static_cast<std::uint64_t>(std::numeric_limits<std::size_t>::max()))
        return Result::InvalidArgument;

    State::Texture texture;
    texture.width = width;
    texture.height = height;
    texture.pixels.assign(pixels, pixels + static_cast<std::size_t>(length));
    for (std::size_t index = 0; index < texture.pixels.size(); index += 4)
    {
        const std::uint32_t alpha = texture.pixels[index + 3];
        texture.pixels[index] = static_cast<std::uint8_t>((texture.pixels[index] * alpha + 127) / 255);
        texture.pixels[index + 1] = static_cast<std::uint8_t>((texture.pixels[index + 1] * alpha + 127) / 255);
        texture.pixels[index + 2] = static_cast<std::uint8_t>((texture.pixels[index + 2] * alpha + 127) / 255);
    }
    m_state->source_textures[source] = std::move(texture);
    return Result::Success;
}

void RmlUiRenderInterface::BeginRender()
{
    m_state->commands.clear();
    ++m_state->frame_number;
    m_state->scissor_enabled = false;
    m_state->has_transform = false;
}

void RmlUiRenderInterface::EndRender()
{
    for (State::Geometry* geometry : m_state->geometries)
    {
        for (auto iterator = geometry->variants.begin(); iterator != geometry->variants.end();)
        {
            auto mesh = m_state->meshes.find(iterator->second);
            if (mesh != m_state->meshes.end() && mesh->second.last_used_frame != m_state->frame_number)
            {
                m_state->released_meshes.push_back(mesh->first);
                m_state->meshes.erase(mesh);
                iterator = geometry->variants.erase(iterator);
            }
            else
            {
                ++iterator;
            }
        }
    }
}

void RmlUiRenderInterface::FinishFrame() noexcept
{
    m_state->mesh_updates.clear();
    m_state->released_meshes.clear();
    m_state->texture_updates.clear();
    m_state->released_textures.clear();
}

void RmlUiRenderInterface::Retire() noexcept
{
    for (State::Geometry* geometry : m_state->geometries)
        delete geometry;
    m_state->geometries.clear();
    std::vector<DrawCommand>().swap(m_state->commands);
    std::vector<std::uint64_t>().swap(m_state->mesh_updates);
    std::vector<std::uint64_t>().swap(m_state->released_meshes);
    std::vector<std::uint64_t>().swap(m_state->texture_updates);
    std::vector<std::uint64_t>().swap(m_state->released_textures);
    std::unordered_map<std::uint64_t, State::Mesh>().swap(m_state->meshes);
    std::unordered_map<std::uint64_t, State::Texture>().swap(m_state->textures);
    std::unordered_map<std::string, State::Texture>().swap(m_state->source_textures);
}

std::uint64_t RmlUiRenderInterface::MeshUpdateCount() const noexcept
{
    return static_cast<std::uint64_t>(m_state->mesh_updates.size());
}

std::uint64_t RmlUiRenderInterface::ReleasedMeshCount() const noexcept
{
    return static_cast<std::uint64_t>(m_state->released_meshes.size());
}

std::uint64_t RmlUiRenderInterface::CommandCount() const noexcept
{
    return static_cast<std::uint64_t>(m_state->commands.size());
}

std::uint64_t RmlUiRenderInterface::TextureUpdateCount() const noexcept
{
    return static_cast<std::uint64_t>(m_state->texture_updates.size());
}

std::uint64_t RmlUiRenderInterface::ReleasedTextureCount() const noexcept
{
    return static_cast<std::uint64_t>(m_state->released_textures.size());
}

Result RmlUiRenderInterface::GetMeshInfo(std::uint64_t index, MeshInfo& mesh) const noexcept
{
    if (index >= static_cast<std::uint64_t>(m_state->mesh_updates.size()))
        return Result::NotFound;
    const std::uint64_t id = m_state->mesh_updates[static_cast<std::size_t>(index)];
    auto iterator = m_state->meshes.find(id);
    if (iterator == m_state->meshes.end())
        return Result::NotFound;
    const State::Mesh& value = iterator->second;
    mesh = {
        value.id,
        value.revision,
        static_cast<std::uint64_t>(value.vertices.size()),
        static_cast<std::uint64_t>(value.indices.size())
    };
    return Result::Success;
}

Result RmlUiRenderInterface::CopyMeshVertices(
    std::uint64_t mesh,
    Vertex* vertices,
    std::uint64_t capacity) const noexcept
{
    auto iterator = m_state->meshes.find(mesh);
    return iterator == m_state->meshes.end()
        ? Result::NotFound
        : CopyVector(iterator->second.vertices, vertices, capacity);
}

Result RmlUiRenderInterface::CopyMeshIndices(
    std::uint64_t mesh,
    std::uint32_t* indices,
    std::uint64_t capacity) const noexcept
{
    auto iterator = m_state->meshes.find(mesh);
    return iterator == m_state->meshes.end()
        ? Result::NotFound
        : CopyVector(iterator->second.indices, indices, capacity);
}

Result RmlUiRenderInterface::CopyCommands(
    DrawCommand* commands,
    std::uint64_t capacity) const noexcept
{
    return CopyVector(m_state->commands, commands, capacity);
}

Result RmlUiRenderInterface::GetTextureInfo(
    std::uint64_t index,
    TextureInfo& texture) const noexcept
{
    if (index >= static_cast<std::uint64_t>(m_state->texture_updates.size()))
        return Result::NotFound;
    auto iterator = m_state->textures.find(m_state->texture_updates[static_cast<std::size_t>(index)]);
    if (iterator == m_state->textures.end())
        return Result::NotFound;
    const State::Texture& value = iterator->second;
    texture = {
        value.id,
        value.revision,
        value.width,
        value.height,
        static_cast<std::uint64_t>(value.pixels.size())
    };
    return Result::Success;
}

Result RmlUiRenderInterface::CopyTexturePixels(
    std::uint64_t texture,
    std::uint8_t* pixels,
    std::uint64_t capacity) const noexcept
{
    auto iterator = m_state->textures.find(texture);
    return iterator == m_state->textures.end()
        ? Result::NotFound
        : CopyVector(iterator->second.pixels, pixels, capacity);
}

Result RmlUiRenderInterface::CopyReleasedTextures(
    std::uint64_t* textures,
    std::uint64_t capacity) const noexcept
{
    return CopyVector(m_state->released_textures, textures, capacity);
}

Result RmlUiRenderInterface::CopyReleasedMeshes(
    std::uint64_t* meshes,
    std::uint64_t capacity) const noexcept
{
    return CopyVector(m_state->released_meshes, meshes, capacity);
}

}
