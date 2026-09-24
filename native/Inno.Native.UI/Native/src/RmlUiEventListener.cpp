#include "RmlUiEventListener.hpp"

#include <RmlUi/Core/Event.h>

namespace Inno::UI::RmlUiAdapter {

RmlUiEventListener::RmlUiEventListener(RmlUiEventSink& sink) noexcept
    : m_sink(sink)
{
}

void RmlUiEventListener::ProcessEvent(Rml::Event& event)
{
    m_sink.ProcessRmlUiEvent(event);
}

}
