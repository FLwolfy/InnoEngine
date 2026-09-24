#pragma once

#include <RmlUi/Core/EventListener.h>

namespace Rml {
class Event;
}

namespace Inno::UI::RmlUiAdapter {

class RmlUiEventSink
{
public:
    virtual void ProcessRmlUiEvent(Rml::Event& event) = 0;

protected:
    ~RmlUiEventSink() = default;
};

class RmlUiEventListener final : public Rml::EventListener
{
public:
    explicit RmlUiEventListener(RmlUiEventSink& sink) noexcept;
    void ProcessEvent(Rml::Event& event) override;

private:
    RmlUiEventSink& m_sink;
};

}
