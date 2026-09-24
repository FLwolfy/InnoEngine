#ifndef INNO_TEXT_H
#define INNO_TEXT_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#  if defined(INNO_TEXT_BUILD)
#    define INNO_TEXT_API __declspec(dllexport)
#  else
#    define INNO_TEXT_API __declspec(dllimport)
#  endif
#else
#  define INNO_TEXT_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct inno_text_context inno_text_context;

typedef enum inno_text_result {
    INNO_TEXT_SUCCESS = 0,
    INNO_TEXT_INVALID_ARGUMENT = 1,
    INNO_TEXT_OUT_OF_MEMORY = 2,
    INNO_TEXT_INVALID_FONT = 3,
    INNO_TEXT_INVALID_HANDLE = 4,
    INNO_TEXT_BACKEND_ERROR = 5,
    INNO_TEXT_BUFFER_TOO_SMALL = 6
} inno_text_result;

typedef enum inno_text_direction {
    INNO_TEXT_DIRECTION_AUTOMATIC = 0,
    INNO_TEXT_DIRECTION_LEFT_TO_RIGHT = 1,
    INNO_TEXT_DIRECTION_RIGHT_TO_LEFT = 2,
    INNO_TEXT_DIRECTION_TOP_TO_BOTTOM = 3,
    INNO_TEXT_DIRECTION_BOTTOM_TO_TOP = 4
} inno_text_direction;

typedef struct inno_text_glyph {
    uint32_t glyph_id;
    uint32_t cluster;
    float advance_x;
    float advance_y;
    float offset_x;
    float offset_y;
} inno_text_glyph;

typedef struct inno_text_metrics {
    float ascender;
    float descender;
    float line_height;
    float underline_position;
    float underline_thickness;
} inno_text_metrics;

typedef struct inno_text_bitmap {
    int32_t width;
    int32_t height;
    int32_t bearing_x;
    int32_t bearing_y;
    float advance_x;
    size_t byte_length;
} inno_text_bitmap;

INNO_TEXT_API inno_text_result inno_text_create(inno_text_context** context);
INNO_TEXT_API void inno_text_destroy(inno_text_context* context);
INNO_TEXT_API inno_text_result inno_text_load_font(
    inno_text_context* context,
    const uint8_t* data,
    size_t length,
    int32_t face_index,
    uint64_t* font_handle);
INNO_TEXT_API inno_text_result inno_text_release_font(
    inno_text_context* context,
    uint64_t font_handle);
INNO_TEXT_API inno_text_result inno_text_get_metrics(
    inno_text_context* context,
    uint64_t font_handle,
    float font_size,
    inno_text_metrics* metrics);
INNO_TEXT_API inno_text_result inno_text_shape_utf8(
    inno_text_context* context,
    uint64_t font_handle,
    const char* text,
    size_t text_length,
    float font_size,
    inno_text_direction direction,
    const char* language,
    const char* script,
    inno_text_glyph* glyphs,
    size_t glyph_capacity,
    size_t* glyph_count);
INNO_TEXT_API inno_text_result inno_text_rasterize_glyph(
    inno_text_context* context,
    uint64_t font_handle,
    uint32_t glyph_id,
    float font_size,
    uint8_t* pixels,
    size_t pixel_capacity,
    inno_text_bitmap* bitmap);
INNO_TEXT_API const char* inno_text_result_message(inno_text_result result);

#ifdef __cplusplus
}
#endif

#endif
