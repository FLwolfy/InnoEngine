cmake_minimum_required(VERSION 3.15)
cmake_policy(SET CMP0091 NEW)
project(InnoCimgui)

file(TO_CMAKE_PATH "${INNO_CIMGUI_SOURCE_DIR}" INNO_CIMGUI_SOURCE_DIR)
if(NOT IS_DIRECTORY "${INNO_CIMGUI_SOURCE_DIR}")
    message(FATAL_ERROR "INNO_CIMGUI_SOURCE_DIR must point to the validated cimgui checkout")
endif()

# Preserve upstream build options, exports and compile definitions. Only the
# window implementation is replaced by the validated, generated source overlay.
add_subdirectory("${INNO_CIMGUI_SOURCE_DIR}" upstream)
get_target_property(cimgui_sources cimgui SOURCES)
set(original_window_source "${INNO_CIMGUI_SOURCE_DIR}/imgui/imgui.cpp")
list(FIND cimgui_sources "${original_window_source}" original_window_index)
if(original_window_index EQUAL -1)
    message(FATAL_ERROR "Upstream cimgui no longer compiles the expected imgui.cpp")
endif()
list(REMOVE_ITEM cimgui_sources "${original_window_source}")
set_property(TARGET cimgui PROPERTY SOURCES "${cimgui_sources}")
target_sources(cimgui PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/imgui.cpp")
