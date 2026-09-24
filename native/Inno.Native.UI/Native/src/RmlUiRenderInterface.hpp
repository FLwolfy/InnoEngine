#pragma once

#include "RmlUiRuntime.hpp"

#include <RmlUi/Core/RenderInterface.h>

namespace Inno::UI::RmlUiAdapter {

class RmlUiRenderInterface final : public Rml::RenderInterface
{
public:
    RmlUiRenderInterface();
    ~RmlUiRenderInterface() override;

    RmlUiRenderInterface(const RmlUiRenderInterface&) = delete;
    RmlUiRenderInterface& operator=(const RmlUiRenderInterface&) = delete;

    Rml::CompiledGeometryHandle CompileGeometry(
        Rml::Span<const Rml::Vertex> vertices,
        Rml::Span<const int> indices) override;
    void RenderGeometry(
        Rml::CompiledGeometryHandle geometry,
        Rml::Vector2f translation,
        Rml::TextureHandle texture) override;
    void ReleaseGeometry(Rml::CompiledGeometryHandle geometry) override;
    Rml::TextureHandle LoadTexture(Rml::Vector2i& dimensions, const Rml::String& source) override;
    Rml::TextureHandle GenerateTexture(
        Rml::Span<const Rml::byte> source,
        Rml::Vector2i dimensions) override;
    void ReleaseTexture(Rml::TextureHandle texture) override;
    void EnableScissorRegion(bool enable) override;
    void SetScissorRegion(Rml::Rectanglei region) override;
    void SetTransform(const Rml::Matrix4f* transform) override;

    Result RegisterTexture(
        const char* source,
        int width,
        int height,
        const std::uint8_t* pixels,
        std::uint64_t length);
    void BeginRender();
    void EndRender();
    void FinishFrame() noexcept;
    void Retire() noexcept;

    std::uint64_t MeshUpdateCount() const noexcept;
    std::uint64_t ReleasedMeshCount() const noexcept;
    std::uint64_t CommandCount() const noexcept;
    std::uint64_t TextureUpdateCount() const noexcept;
    std::uint64_t ReleasedTextureCount() const noexcept;
    Result GetMeshInfo(std::uint64_t index, MeshInfo& mesh) const noexcept;
    Result CopyMeshVertices(
        std::uint64_t mesh,
        Vertex* vertices,
        std::uint64_t capacity) const noexcept;
    Result CopyMeshIndices(
        std::uint64_t mesh,
        std::uint32_t* indices,
        std::uint64_t capacity) const noexcept;
    Result CopyCommands(DrawCommand* commands, std::uint64_t capacity) const noexcept;
    Result GetTextureInfo(std::uint64_t index, TextureInfo& texture) const noexcept;
    Result CopyTexturePixels(
        std::uint64_t texture,
        std::uint8_t* pixels,
        std::uint64_t capacity) const noexcept;
    Result CopyReleasedTextures(
        std::uint64_t* textures,
        std::uint64_t capacity) const noexcept;
    Result CopyReleasedMeshes(
        std::uint64_t* meshes,
        std::uint64_t capacity) const noexcept;

private:
    struct State;
    State* m_state;
};

}
