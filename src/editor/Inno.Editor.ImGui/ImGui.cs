using System;
using System.Numerics;

using Inno.Native.ImGui;
using RawImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

/// <summary>
/// Exposes a script-safe, pointer-free subset of Dear ImGui for custom Editor tools.
/// Every call is valid only while an Editor drawing callback is active.
/// </summary>
public static class ImGui
{
    /// <summary>
    /// Draws unformatted text.
    /// </summary>
    /// <param name="text">
    /// The text text validated by the text operation.
    /// </param>
    public static void Text(string text) => RawImGui.TextUnformatted(text);

    /// <summary>
    /// Draws a standard button.
    /// </summary>
    /// <param name="label">
    /// The visible label and optional ImGui identity suffix.
    /// </param>
    /// <param name="size">
    /// The requested size, or zero components for automatic sizing.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the button is activated.
    /// </returns>
    public static bool Button(string label, Vector2 size = default) => RawImGui.Button(label, size);

    /// <summary>
    /// Draws a compact button.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the button is activated.
    /// </returns>
    /// <param name="label">
    /// The label text validated by the small button operation.
    /// </param>
    public static bool SmallButton(string label) => RawImGui.SmallButton(label);

    /// <summary>
    /// Draws and edits a Boolean value.
    /// </summary>
    /// <param name="label">
    /// The control label.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value changes.
    /// </returns>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public static bool Checkbox(string label, ref bool value) => RawImGui.Checkbox(label, ref value);

    /// <summary>
    /// Draws and edits a bounded UTF-8 text value.
    /// </summary>
    /// <param name="label">
    /// The control label.
    /// </param>
    /// <param name="value">
    /// The managed string to display and update.
    /// </param>
    /// <param name="capacity">
    /// The positive maximum UTF-8 buffer capacity, including the terminator.
    /// </param>
    /// <param name="flags">
    /// Text editing behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the control reports an edit or submit event.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="capacity"/> is not positive.
    /// </exception>
    public static bool InputText(
        string label,
        ref string value,
        int capacity = 1024,
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        return RawImGui.InputText(label, ref value, (nuint)capacity, flags);
    }

    /// <summary>
    /// Draws and edits an integer value.
    /// </summary>
    /// <param name="label">
    /// The control label.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value changes.
    /// </returns>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public static bool InputInt(string label, ref int value) => RawImGui.InputInt(label, ref value);

    /// <summary>
    /// Draws and edits a floating-point value.
    /// </summary>
    /// <param name="label">
    /// The control label.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value changes.
    /// </returns>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public static bool InputFloat(string label, ref float value) => RawImGui.InputFloat(label, ref value);

    /// <summary>
    /// Draws a floating-point drag control.
    /// </summary>
    /// <param name="label">
    /// The control label.
    /// </param>
    /// <param name="value">
    /// The value to display and update.
    /// </param>
    /// <param name="speed">
    /// The drag speed.
    /// </param>
    /// <param name="minimum">
    /// The inclusive minimum, or zero with a zero maximum for no clamping.
    /// </param>
    /// <param name="maximum">
    /// The inclusive maximum, or zero with a zero minimum for no clamping.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value changes.
    /// </returns>
    public static bool DragFloat(
        string label,
        ref float value,
        float speed = 1f,
        float minimum = 0f,
        float maximum = 0f)
        => RawImGui.DragFloat(label, ref value, speed, minimum, maximum);

    /// <summary>
    /// Draws a bounded floating-point slider.
    /// </summary>
    /// <param name="label">
    /// The control label.
    /// </param>
    /// <param name="value">
    /// The value to display and update.
    /// </param>
    /// <param name="minimum">
    /// The inclusive minimum.
    /// </param>
    /// <param name="maximum">
    /// The inclusive maximum.
    /// </param>
    /// <param name="flags">
    /// Slider behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value changes.
    /// </returns>
    public static bool SliderFloat(
        string label,
        ref float value,
        float minimum,
        float maximum,
        ImGuiSliderFlags flags = ImGuiSliderFlags.None)
        => RawImGui.SliderFloat(label, ref value, minimum, maximum, flags);

    /// <summary>
    /// Draws and edits a four-component color.
    /// </summary>
    /// <param name="label">
    /// The control label.
    /// </param>
    /// <param name="value">
    /// The RGBA value to display and update.
    /// </param>
    /// <param name="flags">
    /// Color editing behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value changes.
    /// </returns>
    public static bool ColorEdit4(
        string label,
        ref Vector4 value,
        ImGuiColorEditFlags flags = ImGuiColorEditFlags.None)
        => RawImGui.ColorEdit4(label, ref value, flags);

    /// <summary>
    /// Begins a child region.
    /// </summary>
    /// <param name="id">
    /// The child identity.
    /// </param>
    /// <param name="size">
    /// The requested child size.
    /// </param>
    /// <param name="flags">
    /// Child-region behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when child contents should be submitted.
    /// </returns>
    public static bool BeginChild(
        string id,
        Vector2 size = default,
        ImGuiChildFlags flags = ImGuiChildFlags.None)
        => RawImGui.BeginChild(id, size, flags);

    /// <summary>
    /// Ends the current child region.
    /// </summary>
    public static void EndChild() => RawImGui.EndChild();

    /// <summary>
    /// Begins a table.
    /// </summary>
    /// <param name="id">
    /// The table identity.
    /// </param>
    /// <param name="columns">
    /// The positive column count.
    /// </param>
    /// <param name="flags">
    /// Table behavior.
    /// </param>
    /// <param name="size">
    /// The requested outer size.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when table contents should be submitted.
    /// </returns>
    public static bool BeginTable(
        string id,
        int columns,
        ImGuiTableFlags flags = ImGuiTableFlags.None,
        Vector2 size = default)
        => RawImGui.BeginTable(id, columns, flags, size);

    /// <summary>
    /// Ends the current table.
    /// </summary>
    public static void EndTable() => RawImGui.EndTable();

    /// <summary>
    /// Declares one table column.
    /// </summary>
    /// <param name="label">
    /// The column label.
    /// </param>
    /// <param name="flags">
    /// Column behavior.
    /// </param>
    /// <param name="widthOrWeight">
    /// The initial fixed width or stretch weight.
    /// </param>
    public static void TableSetupColumn(
        string label,
        ImGuiTableColumnFlags flags = ImGuiTableColumnFlags.None,
        float widthOrWeight = 0f)
        => RawImGui.TableSetupColumn(label, flags, widthOrWeight);

    /// <summary>
    /// Draws the table header row from configured column labels.
    /// </summary>
    public static void TableHeadersRow() => RawImGui.TableHeadersRow();

    /// <summary>
    /// Advances to the next table row.
    /// </summary>
    public static void TableNextRow() => RawImGui.TableNextRow();

    /// <summary>
    /// Advances to the next table column.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the column is visible.
    /// </returns>
    public static bool TableNextColumn() => RawImGui.TableNextColumn();

    /// <summary>
    /// Begins a combo popup.
    /// </summary>
    /// <param name="label">
    /// The control label.
    /// </param>
    /// <param name="preview">
    /// The preview text.
    /// </param>
    /// <param name="flags">
    /// Combo behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when combo contents should be submitted.
    /// </returns>
    public static bool BeginCombo(
        string label,
        string preview,
        ImGuiComboFlags flags = ImGuiComboFlags.None)
        => RawImGui.BeginCombo(label, preview, flags);

    /// <summary>
    /// Ends the current combo popup.
    /// </summary>
    public static void EndCombo() => RawImGui.EndCombo();

    /// <summary>
    /// Draws one selectable item.
    /// </summary>
    /// <param name="label">
    /// The visible label and identity.
    /// </param>
    /// <param name="selected">
    /// Whether the item is currently selected.
    /// </param>
    /// <param name="flags">
    /// Selectable behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the item is activated.
    /// </returns>
    public static bool Selectable(
        string label,
        bool selected = false,
        ImGuiSelectableFlags flags = ImGuiSelectableFlags.None)
        => RawImGui.Selectable(label, selected, flags);

    /// <summary>
    /// Draws a collapsible section header.
    /// </summary>
    /// <param name="label">
    /// The visible label and identity.
    /// </param>
    /// <param name="flags">
    /// Tree-node behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the section is open.
    /// </returns>
    public static bool CollapsingHeader(
        string label,
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None)
        => RawImGui.CollapsingHeader(label, flags);

    /// <summary>
    /// Draws a tree node and pushes its tree scope when it is open.
    /// </summary>
    /// <param name="label">
    /// The visible label and identity.
    /// </param>
    /// <param name="flags">
    /// Tree-node behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when child contents should be submitted.
    /// </returns>
    public static bool TreeNode(
        string label,
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None)
        => RawImGui.TreeNodeEx(label, flags);

    /// <summary>
    /// Ends the current open tree-node scope.
    /// </summary>
    public static void TreePop() => RawImGui.TreePop();

    /// <summary>
    /// Begins a tab bar.
    /// </summary>
    /// <param name="id">
    /// The tab bar identity.
    /// </param>
    /// <param name="flags">
    /// Tab-bar behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when tab contents should be submitted.
    /// </returns>
    public static bool BeginTabBar(string id, ImGuiTabBarFlags flags = ImGuiTabBarFlags.None)
        => RawImGui.BeginTabBar(id, flags);

    /// <summary>
    /// Ends the current tab bar.
    /// </summary>
    public static void EndTabBar() => RawImGui.EndTabBar();

    /// <summary>
    /// Begins one tab item.
    /// </summary>
    /// <param name="label">
    /// The tab label and identity.
    /// </param>
    /// <param name="flags">
    /// Tab-item behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when tab contents should be submitted.
    /// </returns>
    public static bool BeginTabItem(string label, ImGuiTabItemFlags flags = ImGuiTabItemFlags.None)
        => RawImGui.BeginTabItem(label, flags);

    /// <summary>
    /// Ends the current tab item.
    /// </summary>
    public static void EndTabItem() => RawImGui.EndTabItem();

    /// <summary>
    /// Opens a named popup.
    /// </summary>
    /// <param name="id">
    /// The popup identity.
    /// </param>
    /// <param name="flags">
    /// Popup opening behavior.
    /// </param>
    public static void OpenPopup(string id, ImGuiPopupFlags flags = ImGuiPopupFlags.None)
        => RawImGui.OpenPopup(id, flags);

    /// <summary>
    /// Begins a named popup.
    /// </summary>
    /// <param name="id">
    /// The popup identity.
    /// </param>
    /// <param name="flags">
    /// Popup window behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when popup contents should be submitted.
    /// </returns>
    public static bool BeginPopup(string id, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
        => RawImGui.BeginPopup(id, flags);

    /// <summary>
    /// Ends the current popup.
    /// </summary>
    public static void EndPopup() => RawImGui.EndPopup();

    /// <summary>
    /// Closes the current popup.
    /// </summary>
    public static void CloseCurrentPopup() => RawImGui.CloseCurrentPopup();

    /// <summary>
    /// Begins a disabled block.
    /// </summary>
    /// <param name="disabled">
    /// Whether controls in the block are disabled.
    /// </param>
    public static void BeginDisabled(bool disabled = true) => RawImGui.BeginDisabled(disabled);

    /// <summary>
    /// Ends the current disabled block.
    /// </summary>
    public static void EndDisabled() => RawImGui.EndDisabled();

    /// <summary>
    /// Begins an item group.
    /// </summary>
    public static void BeginGroup() => RawImGui.BeginGroup();

    /// <summary>
    /// Ends the current item group.
    /// </summary>
    public static void EndGroup() => RawImGui.EndGroup();

    /// <summary>
    /// Pushes a string identity onto the ImGui ID stack.
    /// </summary>
    /// <param name="id">
    /// The stable identity used to locate the requested value.
    /// </param>
    public static void PushId(string id) => RawImGui.PushID(id);

    /// <summary>
    /// Pushes an integer identity onto the ImGui ID stack.
    /// </summary>
    /// <param name="id">
    /// The stable identity used to locate the requested value.
    /// </param>
    public static void PushId(int id) => RawImGui.PushID(id);

    /// <summary>
    /// Pops one identity from the ImGui ID stack.
    /// </summary>
    public static void PopId() => RawImGui.PopID();

    /// <summary>
    /// Pushes one color override.
    /// </summary>
    /// <param name="color">
    /// The style color slot.
    /// </param>
    /// <param name="value">
    /// The linear RGBA value.
    /// </param>
    public static void PushStyleColor(ImGuiCol color, Vector4 value)
        => RawImGui.PushStyleColor(color, value);

    /// <summary>
    /// Pops color overrides.
    /// </summary>
    /// <param name="count">
    /// The positive number of overrides to pop.
    /// </param>
    public static void PopStyleColor(int count = 1) => RawImGui.PopStyleColor(count);

    /// <summary>
    /// Pushes one scalar style override.
    /// </summary>
    /// <param name="style">
    /// The style variable.
    /// </param>
    /// <param name="value">
    /// The scalar value.
    /// </param>
    public static void PushStyleVar(ImGuiStyleVar style, float value)
        => RawImGui.PushStyleVar(style, value);

    /// <summary>
    /// Pushes one two-component style override.
    /// </summary>
    /// <param name="style">
    /// The style variable.
    /// </param>
    /// <param name="value">
    /// The vector value.
    /// </param>
    public static void PushStyleVar(ImGuiStyleVar style, Vector2 value)
        => RawImGui.PushStyleVar(style, value);

    /// <summary>
    /// Pops style-variable overrides.
    /// </summary>
    /// <param name="count">
    /// The positive number of overrides to pop.
    /// </param>
    public static void PopStyleVar(int count = 1) => RawImGui.PopStyleVar(count);

    /// <summary>
    /// Places the next item on the same line.
    /// </summary>
    /// <param name="offset">
    /// The horizontal offset from the line start, or zero for automatic placement.
    /// </param>
    /// <param name="spacing">
    /// The explicit spacing, or a negative value for the current style spacing.
    /// </param>
    public static void SameLine(float offset = 0f, float spacing = -1f)
        => RawImGui.SameLine(offset, spacing);

    /// <summary>
    /// Draws a horizontal separator.
    /// </summary>
    public static void Separator() => RawImGui.Separator();

    /// <summary>
    /// Draws a labeled horizontal separator.
    /// </summary>
    /// <param name="label">
    /// The label text validated by the separator text operation.
    /// </param>
    public static void SeparatorText(string label) => RawImGui.SeparatorText(label);

    /// <summary>
    /// Adds one standard vertical spacing unit.
    /// </summary>
    public static void Spacing() => RawImGui.Spacing();

    /// <summary>
    /// Advances layout by an invisible size.
    /// </summary>
    /// <param name="size">
    /// The size consumed by dummy; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public static void Dummy(Vector2 size) => RawImGui.Dummy(size);

    /// <summary>
    /// Sets the width of the next item.
    /// </summary>
    /// <param name="width">
    /// The width in logical units or pixels required by this operation.
    /// </param>
    public static void SetNextItemWidth(float width) => RawImGui.SetNextItemWidth(width);

    /// <summary>
    /// Gets the remaining content size in the current region.
    /// </summary>
    /// <returns>
    /// The remaining local width and height.
    /// </returns>
    public static Vector2 GetContentRegionAvailable() => RawImGui.GetContentRegionAvail();

    /// <summary>
    /// Gets the current local cursor position.
    /// </summary>
    /// <returns>
    /// The local cursor position.
    /// </returns>
    public static Vector2 GetCursorPosition() => RawImGui.GetCursorPos();

    /// <summary>
    /// Sets the current local cursor position.
    /// </summary>
    /// <param name="position">
    /// The position consumed by set cursor position; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public static void SetCursorPosition(Vector2 position) => RawImGui.SetCursorPos(position);

    /// <summary>
    /// Gets the current screen-space cursor position.
    /// </summary>
    /// <returns>
    /// The screen-space cursor position.
    /// </returns>
    public static Vector2 GetCursorScreenPosition() => RawImGui.GetCursorScreenPos();

    /// <summary>
    /// Gets the minimum screen-space corner of the previous item.
    /// </summary>
    /// <returns>
    /// The item minimum corner.
    /// </returns>
    public static Vector2 GetItemMinimum() => RawImGui.GetItemRectMin();

    /// <summary>
    /// Gets the maximum screen-space corner of the previous item.
    /// </summary>
    /// <returns>
    /// The item maximum corner.
    /// </returns>
    public static Vector2 GetItemMaximum() => RawImGui.GetItemRectMax();

    /// <summary>
    /// Measures text using the current font.
    /// </summary>
    /// <returns>
    /// The measured width and height.
    /// </returns>
    /// <param name="text">
    /// The text text validated by the measure text operation.
    /// </param>
    public static Vector2 MeasureText(string text) => RawImGui.CalcTextSize(text);

    /// <summary>
    /// Gets whether the previous item is hovered.
    /// </summary>
    /// <param name="flags">
    /// Hovered query behavior.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the query succeeds.
    /// </returns>
    public static bool IsItemHovered(ImGuiHoveredFlags flags = ImGuiHoveredFlags.None)
        => RawImGui.IsItemHovered(flags);

    /// <summary>
    /// Gets whether the previous item is active.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> while the item owns an active interaction.
    /// </returns>
    public static bool IsItemActive() => RawImGui.IsItemActive();

    /// <summary>
    /// Gets whether the previous item was clicked with a mouse button.
    /// </summary>
    /// <param name="button">
    /// The mouse button to query.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the item was clicked.
    /// </returns>
    public static bool IsItemClicked(ImGuiMouseButton button = ImGuiMouseButton.Left)
        => RawImGui.IsItemClicked(button);

    /// <summary>
    /// Converts a floating-point RGBA color to Dear ImGui packed color order.
    /// </summary>
    /// <returns>
    /// The packed color value.
    /// </returns>
    /// <param name="color">
    /// The color consumed by to packed color; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public static uint ToPackedColor(Vector4 color) => RawImGui.ColorConvertFloat4ToU32(color);

    /// <summary>
    /// Adds a line to the current window draw list.
    /// </summary>
    /// <param name="start">
    /// The screen-space start point.
    /// </param>
    /// <param name="end">
    /// The screen-space end point.
    /// </param>
    /// <param name="color">
    /// The packed ImGui color.
    /// </param>
    /// <param name="thickness">
    /// The line thickness.
    /// </param>
    public static void DrawLine(Vector2 start, Vector2 end, uint color, float thickness = 1f)
        => RawImGui.GetWindowDrawList().AddLine(start, end, color, thickness);

    /// <summary>
    /// Adds a rectangle outline to the current window draw list.
    /// </summary>
    /// <param name="minimum">
    /// The screen-space minimum corner.
    /// </param>
    /// <param name="maximum">
    /// The screen-space maximum corner.
    /// </param>
    /// <param name="color">
    /// The packed ImGui color.
    /// </param>
    /// <param name="rounding">
    /// The corner rounding radius.
    /// </param>
    /// <param name="thickness">
    /// The outline thickness.
    /// </param>
    public static void DrawRectangle(
        Vector2 minimum,
        Vector2 maximum,
        uint color,
        float rounding = 0f,
        float thickness = 1f)
        => RawImGui.GetWindowDrawList().AddRect(minimum, maximum, color, rounding, thickness);

    /// <summary>
    /// Adds a filled rectangle to the current window draw list.
    /// </summary>
    /// <param name="minimum">
    /// The screen-space minimum corner.
    /// </param>
    /// <param name="maximum">
    /// The screen-space maximum corner.
    /// </param>
    /// <param name="color">
    /// The packed ImGui color.
    /// </param>
    /// <param name="rounding">
    /// The corner rounding radius.
    /// </param>
    public static void DrawFilledRectangle(
        Vector2 minimum,
        Vector2 maximum,
        uint color,
        float rounding = 0f)
        => RawImGui.GetWindowDrawList().AddRectFilled(minimum, maximum, color, rounding);

    /// <summary>
    /// Adds text to the current window draw list.
    /// </summary>
    /// <param name="position">
    /// The screen-space text origin.
    /// </param>
    /// <param name="color">
    /// The packed ImGui color.
    /// </param>
    /// <param name="text">
    /// The text to draw.
    /// </param>
    public static void DrawText(Vector2 position, uint color, string text)
        => RawImGui.GetWindowDrawList().AddText(position, color, text);
}
