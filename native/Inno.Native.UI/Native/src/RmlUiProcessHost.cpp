#include "RmlUiProcessHost.hpp"

#include "FontEngineInterfaceHarfBuzz.h"

#include <RmlUi/Core.h>

#include <atomic>
#include <limits>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

namespace Inno::UI::RmlUiAdapter {
namespace {

struct ProcessHost
{
    std::mutex mutex;
    std::atomic<std::uint64_t> next_context{1};
    std::atomic<std::uint64_t> next_document{1};
    std::uint32_t references = 0;
    std::thread::id owner_thread;
    std::unique_ptr<FontEngineInterfaceHarfBuzz> font_engine;
    std::vector<std::unique_ptr<std::vector<std::uint8_t>>> font_memory;
    std::unordered_map<std::string, std::uint64_t> font_registrations;
};

ProcessHost& GetProcessHost()
{
    static ProcessHost host;
    return host;
}

std::uint64_t HashBytes(const std::uint8_t* data, std::uint64_t length) noexcept
{
    std::uint64_t hash = 1469598103934665603ull;
    for (std::uint64_t index = 0; index < length; ++index)
    {
        hash ^= data[index];
        hash *= 1099511628211ull;
    }
    return hash;
}

}

bool AcquireProcessHost()
{
    ProcessHost& host = GetProcessHost();
    std::lock_guard<std::mutex> lock(host.mutex);
    if (host.references > 0)
    {
        if (host.owner_thread != std::this_thread::get_id())
            return false;
        ++host.references;
        return true;
    }

    host.owner_thread = std::this_thread::get_id();
    host.font_engine = std::make_unique<FontEngineInterfaceHarfBuzz>();
    Rml::SetFontEngineInterface(host.font_engine.get());
    if (!Rml::Initialise())
    {
        Rml::SetFontEngineInterface(nullptr);
        host.font_engine.reset();
        host.owner_thread = {};
        return false;
    }

    host.references = 1;
    host.font_engine->RegisterLanguage("en", "Latn", TextFlowDirection::LeftToRight);
    host.font_engine->RegisterLanguage("zh", "Hani", TextFlowDirection::LeftToRight);
    host.font_engine->RegisterLanguage("ja", "Jpan", TextFlowDirection::LeftToRight);
    host.font_engine->RegisterLanguage("ko", "Kore", TextFlowDirection::LeftToRight);
    host.font_engine->RegisterLanguage("ar", "Arab", TextFlowDirection::RightToLeft);
    host.font_engine->RegisterLanguage("he", "Hebr", TextFlowDirection::RightToLeft);
    return true;
}

void ReleaseProcessHost() noexcept
{
    ProcessHost& host = GetProcessHost();
    std::lock_guard<std::mutex> lock(host.mutex);
    if (host.references == 0 || --host.references != 0)
        return;

    Rml::Shutdown();
    host.font_memory.clear();
    host.font_registrations.clear();
    Rml::SetFontEngineInterface(nullptr);
    host.font_engine.reset();
    host.owner_thread = {};
}

std::uint64_t AllocateContextId() noexcept
{
    return GetProcessHost().next_context.fetch_add(1, std::memory_order_relaxed);
}

std::uint64_t AllocateDocumentId() noexcept
{
    return GetProcessHost().next_document.fetch_add(1, std::memory_order_relaxed);
}

Result LoadFont(
    const std::uint8_t* data,
    std::uint64_t length,
    const char* family,
    int style,
    int weight,
    bool fallback)
{
    if (!data || length == 0 || !family || family[0] == '\0' || style < 0 || style > 2 || weight < 1 || weight > 1000)
        return Result::InvalidArgument;
    if (length > static_cast<std::uint64_t>(std::numeric_limits<std::size_t>::max()))
        return Result::InvalidArgument;

    const std::string registration_key = std::string(family) + "\x1f" + std::to_string(style)
        + "\x1f" + std::to_string(weight) + "\x1f" + std::to_string(fallback);
    const std::uint64_t content_hash = HashBytes(data, length);
    ProcessHost& host = GetProcessHost();
    std::lock_guard<std::mutex> lock(host.mutex);
    auto existing = host.font_registrations.find(registration_key);
    if (existing != host.font_registrations.end())
        return existing->second == content_hash ? Result::Success : Result::BackendError;

    const auto native_length = static_cast<std::size_t>(length);
    auto memory = std::make_unique<std::vector<std::uint8_t>>(data, data + native_length);
    const Rml::Style::FontStyle font_style = style == 0
        ? Rml::Style::FontStyle::Normal
        : Rml::Style::FontStyle::Italic;
    if (!Rml::LoadFontFace(
            {memory->data(), memory->size()},
            family,
            font_style,
            static_cast<Rml::Style::FontWeight>(weight),
            fallback))
    {
        return Result::BackendError;
    }

    host.font_memory.push_back(std::move(memory));
    host.font_registrations.emplace(registration_key, content_hash);
    return Result::Success;
}

}
