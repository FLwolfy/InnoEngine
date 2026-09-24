#pragma once

#include "RmlUiRuntime.hpp"

namespace Inno::UI::RmlUiAdapter {

bool AcquireProcessHost();
void ReleaseProcessHost() noexcept;
std::uint64_t AllocateContextId() noexcept;
std::uint64_t AllocateDocumentId() noexcept;
Result LoadFont(
    const std::uint8_t* data,
    std::uint64_t length,
    const char* family,
    int style,
    int weight,
    bool fallback);

}
