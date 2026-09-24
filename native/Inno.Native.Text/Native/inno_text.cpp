#include "inno_text.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <memory>
#include <new>
#include <unordered_map>
#include <vector>

#include <ft2build.h>
#include FT_FREETYPE_H
#include <hb-ft.h>
#include <hb.h>

struct inno_text_face {
    std::vector<uint8_t> bytes;
    FT_Face face = nullptr;
    hb_font_t* font = nullptr;

    ~inno_text_face()
    {
        if (font)
            hb_font_destroy(font);
        if (face)
            FT_Done_Face(face);
    }
};

struct inno_text_context {
    FT_Library library = nullptr;
    uint64_t next_handle = 1;
    std::unordered_map<uint64_t, std::unique_ptr<inno_text_face>> faces;

    ~inno_text_context()
    {
        faces.clear();
        if (library)
            FT_Done_FreeType(library);
    }
};

static inno_text_face* find_face(inno_text_context* context, uint64_t handle)
{
    if (!context || handle == 0)
        return nullptr;
    auto iterator = context->faces.find(handle);
    return iterator == context->faces.end() ? nullptr : iterator->second.get();
}

static bool set_size(inno_text_face* face, float font_size)
{
    if (!face || !std::isfinite(font_size) || font_size <= 0.f)
        return false;
    const auto size_26_6 = static_cast<FT_F26Dot6>(std::lround(font_size * 64.f));
    if (FT_Set_Char_Size(face->face, 0, size_26_6, 72, 72) != 0)
        return false;
    hb_ft_font_changed(face->font);
    return true;
}

static hb_direction_t resolve_direction(inno_text_direction direction)
{
    switch (direction)
    {
    case INNO_TEXT_DIRECTION_LEFT_TO_RIGHT: return HB_DIRECTION_LTR;
    case INNO_TEXT_DIRECTION_RIGHT_TO_LEFT: return HB_DIRECTION_RTL;
    case INNO_TEXT_DIRECTION_TOP_TO_BOTTOM: return HB_DIRECTION_TTB;
    case INNO_TEXT_DIRECTION_BOTTOM_TO_TOP: return HB_DIRECTION_BTT;
    default: return HB_DIRECTION_INVALID;
    }
}

inno_text_result inno_text_create(inno_text_context** context)
{
    if (!context)
        return INNO_TEXT_INVALID_ARGUMENT;
    *context = nullptr;
    std::unique_ptr<inno_text_context> value(new (std::nothrow) inno_text_context());
    if (!value)
        return INNO_TEXT_OUT_OF_MEMORY;
    if (FT_Init_FreeType(&value->library) != 0)
        return INNO_TEXT_BACKEND_ERROR;
    *context = value.release();
    return INNO_TEXT_SUCCESS;
}

void inno_text_destroy(inno_text_context* context)
{
    delete context;
}

inno_text_result inno_text_load_font(
    inno_text_context* context,
    const uint8_t* data,
    size_t length,
    int32_t face_index,
    uint64_t* font_handle)
{
    if (!context || !data || length == 0 || face_index < 0 || !font_handle)
        return INNO_TEXT_INVALID_ARGUMENT;
    *font_handle = 0;
    std::unique_ptr<inno_text_face> value(new (std::nothrow) inno_text_face());
    if (!value)
        return INNO_TEXT_OUT_OF_MEMORY;
    try
    {
        value->bytes.assign(data, data + length);
    }
    catch (...)
    {
        return INNO_TEXT_OUT_OF_MEMORY;
    }
    if (FT_New_Memory_Face(
            context->library,
            value->bytes.data(),
            static_cast<FT_Long>(value->bytes.size()),
            face_index,
            &value->face) != 0)
        return INNO_TEXT_INVALID_FONT;
    value->font = hb_ft_font_create_referenced(value->face);
    if (!value->font)
        return INNO_TEXT_BACKEND_ERROR;
    const uint64_t handle = context->next_handle++;
    try
    {
        context->faces.emplace(handle, std::move(value));
    }
    catch (...)
    {
        return INNO_TEXT_OUT_OF_MEMORY;
    }
    *font_handle = handle;
    return INNO_TEXT_SUCCESS;
}

inno_text_result inno_text_release_font(inno_text_context* context, uint64_t font_handle)
{
    if (!context || font_handle == 0)
        return INNO_TEXT_INVALID_ARGUMENT;
    return context->faces.erase(font_handle) == 1
        ? INNO_TEXT_SUCCESS
        : INNO_TEXT_INVALID_HANDLE;
}

inno_text_result inno_text_get_metrics(
    inno_text_context* context,
    uint64_t font_handle,
    float font_size,
    inno_text_metrics* metrics)
{
    if (!metrics)
        return INNO_TEXT_INVALID_ARGUMENT;
    inno_text_face* face = find_face(context, font_handle);
    if (!face)
        return INNO_TEXT_INVALID_HANDLE;
    if (!set_size(face, font_size))
        return INNO_TEXT_INVALID_ARGUMENT;
    const FT_Size_Metrics& value = face->face->size->metrics;
    metrics->ascender = value.ascender / 64.f;
    metrics->descender = value.descender / 64.f;
    metrics->line_height = value.height / 64.f;
    const float scale = face->face->units_per_EM == 0
        ? 0.f
        : font_size / static_cast<float>(face->face->units_per_EM);
    metrics->underline_position = face->face->underline_position * scale;
    metrics->underline_thickness = std::max(1.f, face->face->underline_thickness * scale);
    return INNO_TEXT_SUCCESS;
}

inno_text_result inno_text_shape_utf8(
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
    size_t* glyph_count)
{
    if (!text || !glyph_count || (glyph_capacity > 0 && !glyphs))
        return INNO_TEXT_INVALID_ARGUMENT;
    inno_text_face* face = find_face(context, font_handle);
    if (!face)
        return INNO_TEXT_INVALID_HANDLE;
    if (!set_size(face, font_size))
        return INNO_TEXT_INVALID_ARGUMENT;

    hb_buffer_t* buffer = hb_buffer_create();
    if (!buffer)
        return INNO_TEXT_OUT_OF_MEMORY;
    hb_buffer_add_utf8(buffer, text, static_cast<int>(text_length), 0, static_cast<int>(text_length));
    hb_direction_t resolved = resolve_direction(direction);
    if (resolved != HB_DIRECTION_INVALID)
        hb_buffer_set_direction(buffer, resolved);
    if (language && language[0] != '\0')
        hb_buffer_set_language(buffer, hb_language_from_string(language, -1));
    if (script && script[0] != '\0')
        hb_buffer_set_script(buffer, hb_script_from_string(script, -1));
    hb_buffer_guess_segment_properties(buffer);
    hb_shape(face->font, buffer, nullptr, 0);

    unsigned int count = 0;
    hb_glyph_info_t* infos = hb_buffer_get_glyph_infos(buffer, &count);
    hb_glyph_position_t* positions = hb_buffer_get_glyph_positions(buffer, &count);
    *glyph_count = count;
    if (glyph_capacity < count)
    {
        hb_buffer_destroy(buffer);
        return glyphs ? INNO_TEXT_BUFFER_TOO_SMALL : INNO_TEXT_SUCCESS;
    }
    for (unsigned int index = 0; index < count; ++index)
    {
        glyphs[index] = {
            infos[index].codepoint,
            infos[index].cluster,
            positions[index].x_advance / 64.f,
            positions[index].y_advance / 64.f,
            positions[index].x_offset / 64.f,
            positions[index].y_offset / 64.f
        };
    }
    hb_buffer_destroy(buffer);
    return INNO_TEXT_SUCCESS;
}

inno_text_result inno_text_rasterize_glyph(
    inno_text_context* context,
    uint64_t font_handle,
    uint32_t glyph_id,
    float font_size,
    uint8_t* pixels,
    size_t pixel_capacity,
    inno_text_bitmap* bitmap)
{
    if (!bitmap || (pixel_capacity > 0 && !pixels))
        return INNO_TEXT_INVALID_ARGUMENT;
    inno_text_face* face = find_face(context, font_handle);
    if (!face)
        return INNO_TEXT_INVALID_HANDLE;
    if (!set_size(face, font_size))
        return INNO_TEXT_INVALID_ARGUMENT;
    if (FT_Load_Glyph(face->face, glyph_id, FT_LOAD_DEFAULT) != 0
        || FT_Render_Glyph(face->face->glyph, FT_RENDER_MODE_NORMAL) != 0)
        return INNO_TEXT_BACKEND_ERROR;

    const FT_GlyphSlot slot = face->face->glyph;
    const FT_Bitmap& source = slot->bitmap;
    const size_t length = static_cast<size_t>(source.width) * source.rows;
    bitmap->width = static_cast<int32_t>(source.width);
    bitmap->height = static_cast<int32_t>(source.rows);
    bitmap->bearing_x = slot->bitmap_left;
    bitmap->bearing_y = slot->bitmap_top;
    bitmap->advance_x = slot->advance.x / 64.f;
    bitmap->byte_length = length;
    if (!pixels)
        return INNO_TEXT_SUCCESS;
    if (pixel_capacity < length)
        return INNO_TEXT_BUFFER_TOO_SMALL;

    for (unsigned int row = 0; row < source.rows; ++row)
    {
        const uint8_t* source_row = source.pitch >= 0
            ? source.buffer + static_cast<size_t>(row) * source.pitch
            : source.buffer + static_cast<size_t>(source.rows - row - 1) * static_cast<size_t>(-source.pitch);
        uint8_t* target_row = pixels + static_cast<size_t>(row) * source.width;
        if (source.pixel_mode == FT_PIXEL_MODE_GRAY)
        {
            std::memcpy(target_row, source_row, source.width);
        }
        else if (source.pixel_mode == FT_PIXEL_MODE_MONO)
        {
            for (unsigned int column = 0; column < source.width; ++column)
                target_row[column] = (source_row[column >> 3] & (0x80 >> (column & 7))) ? 255 : 0;
        }
        else
        {
            std::fill(target_row, target_row + source.width, 0);
        }
    }
    return INNO_TEXT_SUCCESS;
}

const char* inno_text_result_message(inno_text_result result)
{
    switch (result)
    {
    case INNO_TEXT_SUCCESS: return "success";
    case INNO_TEXT_INVALID_ARGUMENT: return "invalid argument";
    case INNO_TEXT_OUT_OF_MEMORY: return "out of memory";
    case INNO_TEXT_INVALID_FONT: return "invalid font";
    case INNO_TEXT_INVALID_HANDLE: return "invalid handle";
    case INNO_TEXT_BACKEND_ERROR: return "backend error";
    case INNO_TEXT_BUFFER_TOO_SMALL: return "buffer too small";
    default: return "unknown error";
    }
}
