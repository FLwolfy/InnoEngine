using System;
using System.Collections.Generic;
using System.Text;

using Inno.Core.Math;
using Inno.ImGui;

using ImGuiNET;
using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.GUI;

/// <summary>
/// Minimal editor layout helpers built on top of ImGui.
/// </summary>
public static class EditorGUILayout
{
    public enum LayoutAlign { Front, Center, Back }
    [Flags] public enum FontStyle { None, Bold, Italic }

    private static readonly Stack<FontStyle> FONT_STYLE_STACK = new();
    private static readonly Stack<int> SCOPE_STACK = new();
    private static readonly Stack<LayoutAlign> ALIGN_STACK = new();
    private static readonly Stack<bool> COLUMN_DIRTY_STACK = new();
    private static readonly Stack<int> COLUMN_KEY_STACK = new();

    private static readonly Dictionary<int, int> COLUMN_COUNT_MAP = new();
    private static readonly Dictionary<int, float> COLUMN_TOTAL_WEIGHT_MAP = new();
    private static readonly Dictionary<int, List<float>> COLUMN_WEIGHT_MAP = new();
    private static readonly Stack<bool> COLUMN_PAD_STACK = new();

    private static int m_columnDepth = 0;
    private static float m_nextIndentWidth = 0;
    private static bool m_frameBegin = false;

    #region Lifecycles

    /// <summary>
    /// Begins a UI frame for EditorGUILayout validation.
    /// </summary>
    public static void BeginFrame()
    {
        if (m_frameBegin) throw new InvalidOperationException("BeginFrame() can only be called once.");
        m_frameBegin = true;
    }

    /// <summary>
    /// Ends a UI frame and validates stack state.
    /// </summary>
    public static void EndFrame()
    {
        if (ALIGN_STACK.Count != 0 || SCOPE_STACK.Count != 0 || m_nextIndentWidth != 0 || !m_frameBegin)
            throw new InvalidOperationException("EndFrame() is called improperly.");

        m_frameBegin = false;
    }

    /// <summary>
    /// Begins an ImGui ID scope.
    /// </summary>
    public static void BeginScope(int id)
    {
        ImGuiNet.PushID(id);
        SCOPE_STACK.Push(id);
    }

    /// <summary>
    /// Ends the current ImGui ID scope.
    /// </summary>
    public static void EndScope()
    {
        ImGuiNet.PopID();
        SCOPE_STACK.Pop();
    }

    #endregion

    #region Layouts

    /// <summary>
    /// Begins a weighted column layout.
    /// </summary>
    public static void BeginColumns(float firstColumnWeight = 1.0f, bool bordered = false)
    {
        var flags = ImGuiTableFlags.SizingStretchProp;
        if (bordered) flags |= ImGuiTableFlags.BordersInner | ImGuiTableFlags.BordersOuter;

        m_columnDepth++;

        int layoutKey = (int)ImGuiNet.GetID($"__EditorGUILayout_Columns__{m_columnDepth}");
        COLUMN_KEY_STACK.Push(layoutKey);

        bool dirty = !COLUMN_COUNT_MAP.ContainsKey(layoutKey);
        COLUMN_DIRTY_STACK.Push(dirty);

        // Track whether we push style var for this table instance
        bool pushedPad = false;

        if (!dirty)
        {
            // If this is a nested table (e.g. Vector2/3/4 fields inside a property row),
            // remove vertical cell padding so the row height matches normal widgets.
            if (m_columnDepth > 1)
            {
                var style = ImGuiNet.GetStyle();
                ImGuiNet.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(style.CellPadding.X, 0f));
                pushedPad = true;
            }

            int columnCount = COLUMN_COUNT_MAP[layoutKey];

            ImGuiNet.BeginTable($"EditorLayout##{layoutKey}", columnCount, flags);

            var weights = COLUMN_WEIGHT_MAP[layoutKey];
            for (int i = 0; i < columnCount; i++)
                ImGuiNet.TableSetupColumn($"Column {i}", ImGuiTableColumnFlags.None, weights[i]);

            float rowH = ImGuiNet.GetFrameHeight();
            ImGuiNet.TableNextRow(ImGuiTableRowFlags.None, rowH);
            ImGuiNet.TableSetColumnIndex(0);
        }
        else
        {
            COLUMN_COUNT_MAP[layoutKey] = 1;
            COLUMN_TOTAL_WEIGHT_MAP[layoutKey] = firstColumnWeight;
            COLUMN_WEIGHT_MAP[layoutKey] = new List<float> { firstColumnWeight };
        }

        COLUMN_PAD_STACK.Push(pushedPad);
    }

    /// <summary>
    /// Ends the current column layout.
    /// </summary>
    public static void EndColumns()
    {
        if (COLUMN_DIRTY_STACK.Count == 0 || COLUMN_KEY_STACK.Count == 0)
            throw new InvalidOperationException("EndColumns() called without BeginColumns().");

        bool dirty = COLUMN_DIRTY_STACK.Pop();
        int layoutKey = COLUMN_KEY_STACK.Pop();

        // Must pop style var even if dirty (we always pushed a marker)
        bool pushedPad = COLUMN_PAD_STACK.Pop();

        if (!dirty)
        {
            ImGuiNet.EndTable();

            if (pushedPad)
                ImGuiNet.PopStyleVar();
        }
        else
        {
            float totalWeight = COLUMN_TOTAL_WEIGHT_MAP[layoutKey];
            if (totalWeight != 0)
            {
                var weights = COLUMN_WEIGHT_MAP[layoutKey];
                int count = COLUMN_COUNT_MAP[layoutKey];
                for (int i = 0; i < count; i++)
                    weights[i] /= totalWeight;
            }
        }

        m_columnDepth--;
    }

    /// <summary>
    /// Adds the next column in the current layout.
    /// </summary>
    public static void SplitColumns(float nextColumnWeight = 1.0f)
    {
        if (COLUMN_DIRTY_STACK.Count == 0 || COLUMN_KEY_STACK.Count == 0)
            throw new InvalidOperationException("SplitColumns() called without BeginColumns().");

        if (!COLUMN_DIRTY_STACK.Peek())
        {
            ImGuiNet.TableNextColumn();
        }
        else
        {
            int layoutKey = COLUMN_KEY_STACK.Peek();

            COLUMN_COUNT_MAP[layoutKey]++;
            COLUMN_TOTAL_WEIGHT_MAP[layoutKey] += nextColumnWeight;
            COLUMN_WEIGHT_MAP[layoutKey].Add(nextColumnWeight);
        }
    }

    /// <summary>
    /// Inserts vertical spacing.
    /// </summary>
    public static void Space(float pixels = 8f) => ImGuiNet.Dummy(new Vector2(1, pixels));

    /// <summary>
    /// Sets a one-shot indentation for the next property row.
    /// </summary>
    public static void Indent(float pixels = 8f)
    {
        m_nextIndentWidth = pixels;
    }

    /// <summary>
    /// Pushes a font style for subsequent widgets.
    /// </summary>
    public static void BeginFont(FontStyle style)
    {
        FONT_STYLE_STACK.Push(style);

        FontStyle result = FontStyle.None;
        foreach (var s in FONT_STYLE_STACK) result |= s;

        if (result == FontStyle.Bold) ImGuiHost.UseFont(ImGuiFontStyle.Bold);
        else if (result == FontStyle.Italic) ImGuiHost.UseFont(ImGuiFontStyle.Italic);
        else if (result == (FontStyle.Bold | FontStyle.Italic)) ImGuiHost.UseFont(ImGuiFontStyle.BoldItalic);
        else ImGuiHost.UseFont(ImGuiFontStyle.Regular);
    }

    /// <summary>
    /// Pops the current font style.
    /// </summary>
    public static void EndFont()
    {
        FONT_STYLE_STACK.Pop();

        FontStyle result = FontStyle.None;
        foreach (var s in FONT_STYLE_STACK) result |= s;

        if (result == FontStyle.Bold) ImGuiHost.UseFont(ImGuiFontStyle.Bold);
        else if (result == FontStyle.Italic) ImGuiHost.UseFont(ImGuiFontStyle.Italic);
        else if (result == (FontStyle.Bold | FontStyle.Italic)) ImGuiHost.UseFont(ImGuiFontStyle.BoldItalic);
        else ImGuiHost.UseFont(ImGuiFontStyle.Regular);
    }

    /// <summary>
    /// Begins an alignment group.
    /// </summary>
    public static void BeginAlignment(LayoutAlign align)
    {
        ALIGN_STACK.Push(align);
        ImGuiNet.BeginGroup();
    }

    /// <summary>
    /// Ends the current alignment group.
    /// </summary>
    public static void EndAlignment()
    {
        if (ALIGN_STACK.Count == 0)
            throw new InvalidOperationException("EditorLayout.End called without matching Begin");

        ALIGN_STACK.Pop();
        ImGuiNet.EndGroup();
    }

    #endregion

    #region Widgets

    /// <summary>
    /// Draws a frame-height label.
    /// </summary>
    public static void Label(string text, bool enabled = true)
    {
        var labelText = ToInspectorLabel(text);
        
        // Keep using frame height so each property row aligns to standard widgets.
        float rowH = ImGuiNet.GetFrameHeight();

        Vector2 p0 = ImGuiNet.GetCursorScreenPos();
        float availW = ImGuiNet.GetContentRegionAvail().X;

        // Reserve the full row height in the layout
        ImGuiNet.Dummy(new Vector2(1, rowH));

        // Horizontal alignment (your existing alignment stack)
        Vector2 textSize = ImGuiNet.CalcTextSize(labelText);
        float offsetX = GetAlignedOffsetX(textSize.x, availW);

        // Vertical center inside the reserved row height
        float textY = p0.y + (rowH - textSize.y) * 0.5f;

        uint col = ImGuiNet.GetColorU32(enabled ? ImGuiCol.Text : ImGuiCol.TextDisabled);

        ImGuiNet.GetWindowDrawList().AddText(
            new Vector2(p0.x + offsetX, textY),
            col,
            labelText);
    }

    /// <summary>
    /// Draws a button with optional disable.
    /// </summary>
    public static bool Button(string label, bool enabled = true)
    {
        float width = MeasureWidth(() => ImGuiNet.Button(label));
        SetAlignedCursorPosX(width);
        using (new DrawScope(enabled)) { return ImGuiNet.Button(label); }
    }

    /// <summary>
    /// Draws an int input field.
    /// </summary>
    public static bool IntField(string label, ref int value, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);
            ImGuiNet.SetNextItemWidth(ImGuiNet.GetContentRegionAvail().X);
            bool result = ImGuiNet.InputInt("##value", ref value);
            EndPropertyRow();
            return result;
        }
    }

    /// <summary>
    /// Draws a float input field.
    /// </summary>
    public static bool FloatField(string label, ref float value, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);
            ImGuiNet.SetNextItemWidth(ImGuiNet.GetContentRegionAvail().X);
            bool result = ImGuiNet.InputFloat("##value", ref value);
            EndPropertyRow();
            return result;
        }
    }

    /// <summary>
    /// Draws a Vector2 field with per-axis drags.
    /// </summary>
    public static bool Vector2Field(string label, ref Vector2 value, bool enabled = true)
    {
        bool changed = false;

        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);

            float x = value.x;
            float y = value.y;

            BeginColumns();

            changed |= DrawAxisDrag("X", ref x, ImGuiNet.GetColumnWidth(), new Color(0.75f, 0.20f, 0.20f));
            SplitColumns();
            changed |= DrawAxisDrag("Y", ref y, ImGuiNet.GetColumnWidth(), new Color(0.20f, 0.65f, 0.25f));
            SplitColumns();
            
            EndColumns();

            if (changed) value = new Vector2(x, y);

            EndPropertyRow();
        }

        return changed;
    }

    /// <summary>
    /// Draws a Vector3 field with per-axis drags.
    /// </summary>
    public static bool Vector3Field(string label, ref Vector3 value, bool enabled = true)
    {
        bool changed = false;

        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);

            float x = value.x;
            float y = value.y;
            float z = value.z;

            BeginColumns();

            changed |= DrawAxisDrag("X", ref x, ImGuiNet.GetColumnWidth(), new Color(0.75f, 0.20f, 0.20f));
            SplitColumns();
            changed |= DrawAxisDrag("Y", ref y, ImGuiNet.GetColumnWidth(), new Color(0.20f, 0.65f, 0.25f));
            SplitColumns();
            changed |= DrawAxisDrag("Z", ref z, ImGuiNet.GetColumnWidth(), new Color(0.25f, 0.35f, 0.80f));

            EndColumns();

            if (changed) value = new Vector3(x, y, z);

            EndPropertyRow();
        }

        return changed;
    }
    
    /// <summary>
    /// Draws a Vector4 field with per-axis drags.
    /// </summary>
    public static bool Vector4Field(string label, ref Vector4 value, bool enabled = true)
    {
        bool changed = false;

        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);

            float x = value.x;
            float y = value.y;
            float z = value.z;
            float w = value.w;

            BeginColumns();
            
            changed |= DrawAxisDrag("X", ref x, ImGuiNet.GetColumnWidth(), new Color(0.75f, 0.20f, 0.20f)); // Red
            SplitColumns();
            changed |= DrawAxisDrag("Z", ref z, ImGuiNet.GetColumnWidth(), new Color(0.25f, 0.35f, 0.80f)); // Blue
            SplitColumns();
            changed |= DrawAxisDrag("Y", ref y, ImGuiNet.GetColumnWidth(), new Color(0.20f, 0.65f, 0.25f)); // Green
            SplitColumns();
            changed |= DrawAxisDrag("W", ref w, ImGuiNet.GetColumnWidth(), new Color(0.65f, 0.65f, 0.65f)); // Gray
            
            EndColumns();

            if (changed) value = new Vector4(x, y, z, w);

            EndPropertyRow();
        }

        return changed;
    }

    /// <summary>
    /// Draws a Quaternion field as Euler angles (degrees) with stable UI editing.
    /// </summary>
    /// <remarks>
    /// Quaternion remains the source of truth. The editor shows XYZ degrees (Vector3-like),
    /// while caching euler values in ImGui state storage to keep continuity (unwrap) and avoid jumps.
    /// </remarks>
    public static bool QuaternionField(string label, ref Quaternion value, bool enabled = true)
    {
        bool changed = false;

        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);

            // Per-widget cache keyed by ImGui ID
            uint id = ImGuiNet.GetID("##quat_euler_cache");
            var storage = ImGuiNet.GetStateStorage();

            uint kHas = id ^ 0xA13F_001u;
            uint kQx  = id ^ 0xA13F_010u;
            uint kQy  = id ^ 0xA13F_011u;
            uint kQz  = id ^ 0xA13F_012u;
            uint kQw  = id ^ 0xA13F_013u;
            uint kEx  = id ^ 0xA13F_020u;
            uint kEy  = id ^ 0xA13F_021u;
            uint kEz  = id ^ 0xA13F_022u;

            // Pack/unpack float into ImGui storage int slots
            int Pack(float f) => BitConverter.SingleToInt32Bits(f);
            float Unpack(int i) => BitConverter.Int32BitsToSingle(i);

            bool hasCache = storage.GetBool(kHas, false);

            Quaternion lastQ = hasCache
                ? new Quaternion(
                    Unpack(storage.GetInt(kQx, 0)),
                    Unpack(storage.GetInt(kQy, 0)),
                    Unpack(storage.GetInt(kQz, 0)),
                    Unpack(storage.GetInt(kQw, Pack(1f))))
                : Quaternion.identity;

            Vector3 cachedEuler = hasCache
                ? new Vector3(
                    Unpack(storage.GetInt(kEx, 0)),
                    Unpack(storage.GetInt(kEy, 0)),
                    Unpack(storage.GetInt(kEz, 0)))
                : Vector3.ZERO;

            // If the quaternion changed externally, refresh the cached euler from quaternion
            bool externalChanged = !hasCache || !value.Equals(lastQ);

            Vector3 euler;
            if (externalChanged)
            {
                // quaternion -> euler
                euler = value.normalized.ToEulerAnglesXYZDegrees();

                // normalize to [-180, 180]
                float nx = euler.x % 360f; if (nx > 180f) nx -= 360f; if (nx < -180f) nx += 360f;
                float ny = euler.y % 360f; if (ny > 180f) ny -= 360f; if (ny < -180f) ny += 360f;
                float nz = euler.z % 360f; if (nz > 180f) nz -= 360f; if (nz < -180f) nz += 360f;

                if (hasCache)
                {
                    // unwrap each axis to be closest to cachedEuler
                    float rx = cachedEuler.x % 360f; if (rx > 180f) rx -= 360f; if (rx < -180f) rx += 360f;
                    float ry = cachedEuler.y % 360f; if (ry > 180f) ry -= 360f; if (ry < -180f) ry += 360f;
                    float rz = cachedEuler.z % 360f; if (rz > 180f) rz -= 360f; if (rz < -180f) rz += 360f;

                    float dx = nx - rx; if (dx > 180f) nx -= 360f; else if (dx < -180f) nx += 360f;
                    float dy = ny - ry; if (dy > 180f) ny -= 360f; else if (dy < -180f) ny += 360f;
                    float dz = nz - rz; if (dz > 180f) nz -= 360f; else if (dz < -180f) nz += 360f;
                }

                euler = new Vector3(nx, ny, nz);
                cachedEuler = euler;

                // store cache (no helper method)
                storage.SetBool(kHas, true);
                storage.SetInt(kQx, Pack(value.x));
                storage.SetInt(kQy, Pack(value.y));
                storage.SetInt(kQz, Pack(value.z));
                storage.SetInt(kQw, Pack(value.w));
                storage.SetInt(kEx, Pack(cachedEuler.x));
                storage.SetInt(kEy, Pack(cachedEuler.y));
                storage.SetInt(kEz, Pack(cachedEuler.z));

                hasCache = true;
            }
            else
            {
                euler = cachedEuler;
            }

            float x = euler.x;
            float y = euler.y;
            float z = euler.z;

            BeginColumns();

            changed |= DrawAxisDrag("X", ref x, ImGuiNet.GetColumnWidth(), new Color(0.75f, 0.20f, 0.20f));
            SplitColumns();
            changed |= DrawAxisDrag("Y", ref y, ImGuiNet.GetColumnWidth(), new Color(0.20f, 0.65f, 0.25f));
            SplitColumns();
            changed |= DrawAxisDrag("Z", ref z, ImGuiNet.GetColumnWidth(), new Color(0.25f, 0.35f, 0.80f));

            EndColumns();

            if (changed)
            {
                // normalize input to [-180, 180]
                float nx = x % 360f; if (nx > 180f) nx -= 360f; if (nx < -180f) nx += 360f;
                float ny = y % 360f; if (ny > 180f) ny -= 360f; if (ny < -180f) ny += 360f;
                float nz = z % 360f; if (nz > 180f) nz -= 360f; if (nz < -180f) nz += 360f;

                // unwrap relative to cachedEuler (keep continuity)
                float rx = cachedEuler.x % 360f; if (rx > 180f) rx -= 360f; if (rx < -180f) rx += 360f;
                float ry = cachedEuler.y % 360f; if (ry > 180f) ry -= 360f; if (ry < -180f) ry += 360f;
                float rz = cachedEuler.z % 360f; if (rz > 180f) rz -= 360f; if (rz < -180f) rz += 360f;

                float dx = nx - rx; if (dx > 180f) nx -= 360f; else if (dx < -180f) nx += 360f;
                float dy = ny - ry; if (dy > 180f) ny -= 360f; else if (dy < -180f) ny += 360f;
                float dz = nz - rz; if (dz > 180f) nz -= 360f; else if (dz < -180f) nz += 360f;

                var newEuler = new Vector3(nx, ny, nz);

                // euler -> quaternion
                var newQ = Quaternion.FromEulerAnglesXYZDegrees(newEuler).normalized;
                value = newQ;

                // update cache
                cachedEuler = newEuler;
                storage.SetBool(kHas, true);
                storage.SetInt(kQx, Pack(value.x));
                storage.SetInt(kQy, Pack(value.y));
                storage.SetInt(kQz, Pack(value.z));
                storage.SetInt(kQw, Pack(value.w));
                storage.SetInt(kEx, Pack(cachedEuler.x));
                storage.SetInt(kEy, Pack(cachedEuler.y));
                storage.SetInt(kEz, Pack(cachedEuler.z));
            }

            EndPropertyRow();
        }

        return changed;
    }

    /// <summary>
    /// Draws an input text field.
    /// </summary>
    public static bool TextField(string label, ref string value, uint maxLength = 256, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);
            ImGuiNet.SetNextItemWidth(ImGuiNet.GetContentRegionAvail().X);
            bool result = ImGuiNet.InputText("##value", ref value, maxLength);
            EndPropertyRow();
            return result;
        }
    }

    /// <summary>
    /// Draws a checkbox field.
    /// </summary>
    public static bool Checkbox(string label, ref bool value, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);
            bool result = ImGuiNet.Checkbox("##value", ref value);
            EndPropertyRow();
            return result;
        }
    }

    /// <summary>
    /// Draws a ColorEdit4 field.
    /// </summary>
    public static bool ColorField(string label, in Color input, out Color output, bool enabled = true)
    {
        bool result;

        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);

            var v = new System.Numerics.Vector4(input.r, input.g, input.b, input.a);
            ImGuiNet.SetNextItemWidth(ImGuiNet.GetContentRegionAvail().X);
            result = ImGuiNet.ColorEdit4("##value", ref v);

            output = new Color(v.X, v.Y, v.Z, v.W);

            EndPropertyRow();
        }

        return result;
    }

    /// <summary>
    /// Draws a combo box field.
    /// </summary>
    public static bool Combo(string label, string[] list, ref int selectedIndex, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);
            ImGuiNet.SetNextItemWidth(ImGuiNet.GetContentRegionAvail().X);
            bool result = ImGuiNet.Combo("##value", ref selectedIndex, list, list.Length);
            EndPropertyRow();
            return result;
        }
    }

    /// <summary>
    /// Draws a button-driven popup menu.
    /// </summary>
    public static bool PopupMenu(string label, string emptyMsg, string[] itemNameList, out int? selectedIndex, bool enabled = true)
    {
        bool changed = false;
        selectedIndex = null;

        using (new DrawScope(enabled))
        {
            if (Button(label, enabled))
            {
                ImGuiNet.OpenPopup(label);
            }

            if (ImGuiNet.BeginPopup(label))
            {
                if (itemNameList.Length == 0)
                {
                    ImGuiNet.Text(emptyMsg);
                }

                for (int i = 0; i < itemNameList.Length; i++)
                {
                    if (ImGuiNet.MenuItem(itemNameList[i]))
                    {
                        selectedIndex = i;
                        changed = true;
                        ImGuiNet.CloseCurrentPopup();
                    }
                }

                ImGuiNet.EndPopup();
            }
        }

        return changed;
    }

    /// <summary>
    /// Draws a collapsing header (optional closable).
    /// </summary>
    public static bool CollapsingHeader(string label, Action? onClose = null, bool defaultOpen = true, bool enabled = true)
    {
        ImGuiNet.SetNextItemOpen(defaultOpen, ImGuiCond.Once);

        bool visibility = true;
        bool result;

        if (onClose == null)
        {
            BeginFont(FontStyle.Bold);
            result = ImGuiNet.CollapsingHeader(label);
            EndFont();
        }
        else
        {
            using (new DrawScope(enabled))
            {
                BeginFont(FontStyle.Bold);
                result = ImGuiNet.CollapsingHeader(label, ref visibility);
                EndFont();
            }

            if (!visibility) onClose.Invoke();
        }

        return result;
    }

    /// <summary>
    /// Draws a custom collapsing label row.
    /// </summary>
    public static bool CollapsingLabel(string label, bool defaultOpen = true, bool enabled = true)
    {
        using (new DrawScope(enabled))
        {
            ImGuiNet.PushID(label);

            var storage = ImGuiNet.GetStateStorage();
            uint openId = ImGuiNet.GetID("##open");
            bool open = storage.GetBool(openId, defaultOpen);
            float rowH = ImGuiNet.GetFrameHeight();

            Vector2 p0 = ImGuiNet.GetCursorScreenPos();

            var prevFont = ImGuiHost.GetCurrentFont();
            ImGuiHost.UseFont(ImGuiFontStyle.Bold);

            float labelW = ImGuiNet.CalcTextSize(label).X;

            ImGuiNet.InvisibleButton("##lbl_hit", new Vector2(labelW, rowH));
            bool lblHovered = ImGuiNet.IsItemHovered();
            bool lblClicked = ImGuiNet.IsItemClicked(ImGuiMouseButton.Left);

            ImGuiNet.SetCursorScreenPos(p0);
            Label(label);

            ImGuiHost.UseFont(prevFont);

            Vector2 triP0 = new Vector2(p0.x + labelW, p0.y);
            Vector2 triSize = new Vector2(rowH, rowH);

            ImGuiNet.SetCursorScreenPos(triP0);
            ImGuiNet.InvisibleButton("##tri_hit", triSize);
            bool triHovered = ImGuiNet.IsItemHovered();
            bool triClicked = ImGuiNet.IsItemClicked(ImGuiMouseButton.Left);

            bool hovered = lblHovered || triHovered;

            if (hovered)
                ImGuiNet.SetMouseCursor(ImGuiMouseCursor.Hand);

            var dl = ImGuiNet.GetWindowDrawList();
            float rounding = ImGuiNet.GetStyle().FrameRounding;

            if (hovered)
            {
                uint bg = ImGuiNet.GetColorU32(ImGuiCol.HeaderHovered);
                bg = (bg & 0x00FFFFFFu) | (50u << 24);
                dl.AddRectFilled(triP0, triP0 + triSize, bg, rounding);
            }

            uint col = ImGuiNet.GetColorU32(ImGuiCol.Text);
            if (hovered) col = (col & 0x00FFFFFFu) | (255u << 24);

            float triSizePx = rowH * 0.14f;
            Vector2 center = new Vector2(triP0.x + rowH * 0.5f, triP0.y + rowH * 0.5f);

            if (open)
            {
                dl.AddTriangleFilled(
                    new Vector2(center.x - triSizePx, center.y - triSizePx * 0.6f),
                    new Vector2(center.x + triSizePx, center.y - triSizePx * 0.6f),
                    new Vector2(center.x,            center.y + triSizePx),
                    col
                );
            }
            else
            {
                dl.AddTriangleFilled(
                    new Vector2(center.x - triSizePx * 0.6f, center.y - triSizePx),
                    new Vector2(center.x - triSizePx * 0.6f, center.y + triSizePx),
                    new Vector2(center.x + triSizePx,        center.y),
                    col
                );
            }

            if (lblClicked || triClicked)
            {
                open = !open;
                storage.SetBool(openId, open);
            }

            ImGuiNet.PopID();
            return open;
        }
    }

    /// <summary>
    /// Draws a drop reference target field.
    /// </summary>
    public static bool DropRef<T>(
        string label,
        string displayText,
        string payloadType,
        ref T? value,
        Predicate<T>? condition = null,
        bool enabled = true)
    {
        bool changed = false;

        using (new DrawScope(enabled))
        {
            BeginPropertyRow(label);

            float w = ImGuiNet.GetContentRegionAvail().X;
            float h = ImGuiNet.GetFrameHeight();

            Vector2 p0 = ImGuiNet.GetCursorScreenPos();
            Vector2 size = new Vector2(w, h);

            ImGuiNet.InvisibleButton("##guid_drop", size);

            bool hovered = ImGuiNet.IsItemHovered();
            bool active = ImGuiNet.IsItemActive();

            var dl = ImGuiNet.GetWindowDrawList();

            uint bgCol = ImGuiNet.GetColorU32(
                active ? ImGuiCol.FrameBgActive :
                hovered ? ImGuiCol.FrameBgHovered :
                          ImGuiCol.FrameBg);

            uint borderCol = ImGuiNet.GetColorU32(ImGuiCol.Border);

            float rounding = ImGuiNet.GetStyle().FrameRounding;
            dl.AddRectFilled(p0, p0 + size, bgCol, rounding);
            dl.AddRect(p0, p0 + size, borderCol, rounding);

            if (enabled && ImGuiNet.BeginDragDropTarget())
            {
                if (ImGuiHost.TryAcceptDragPayload(payloadType, out T incoming, condition))
                {
                    value = incoming;
                    changed = true;
                }

                ImGuiNet.EndDragDropTarget();
            }

            if (enabled && ImGuiNet.BeginPopupContextItem("##guid_drop_ctx"))
            {
                if (ImGuiNet.MenuItem("Clear"))
                {
                    value = default;
                    changed = true;
                }
                ImGuiNet.EndPopup();
            }

            Vector2 textSize = ImGuiNet.CalcTextSize(displayText);
            Vector2 pad = ImGuiNet.GetStyle().FramePadding;

            float textX = p0.x + pad.x;
            float textY = p0.y + (h - textSize.y) * 0.5f;

            uint textCol = ImGuiNet.GetColorU32(ImGuiCol.Text);
            dl.AddText(new Vector2(textX, textY), textCol, displayText);

            EndPropertyRow();
        }

        return changed;
    }

    #endregion
    
    #region Helpers
    
        private readonly struct DrawScope : IDisposable
    {
        private readonly bool m_enabled;

        public DrawScope(bool enabled)
        {
            m_enabled = enabled;
            if (!enabled) ImGuiNet.BeginDisabled();
        }

        public void Dispose()
        {
            if (!m_enabled) ImGuiNet.EndDisabled();
        }
    }

    private static float GetAlignedOffsetX(float itemWidth, float availWidth)
    {
        if (ALIGN_STACK.Count == 0) return 0f;

        var align = ALIGN_STACK.Peek();
        return align switch
        {
            LayoutAlign.Center => (availWidth - itemWidth) * 0.5f,
            LayoutAlign.Back => (availWidth - itemWidth),
            _ => 0f
        };
    }

    private static void SetAlignedCursorPosX(float itemWidth)
    {
        if (ALIGN_STACK.Count == 0) return;

        var align = ALIGN_STACK.Peek();
        var cursorPos = ImGuiNet.GetCursorPos();
        var avail = ImGuiNet.GetContentRegionAvail();

        float offsetX = align switch
        {
            LayoutAlign.Center => (avail.X - itemWidth) * 0.5f,
            LayoutAlign.Back => (avail.X - itemWidth),
            _ => 0f
        };

        ImGuiNet.SetCursorPosX(cursorPos.X + offsetX);
    }

    private static float MeasureWidth(Action onMeasure)
    {
        ImGuiHost.BeginInvisible();
        ImGuiNet.PushID("__measure__");
        onMeasure.Invoke();
        ImGuiNet.PopID();
        ImGuiHost.EndInvisible();

        return ImGuiHost.GetInvisibleItemSize().x;
    }

    private static void BeginPropertyRow(string label)
    {
        ImGuiNet.PushID(label);

        BeginColumns(2f);
        if (m_nextIndentWidth != 0)
        {
            ImGuiNet.Dummy(new Vector2(m_nextIndentWidth, 0));
            ImGuiNet.SameLine();
            m_nextIndentWidth = 0;
        }
        Label(label);

        SplitColumns(3f);

        if (!COLUMN_DIRTY_STACK.Peek())
            ImGuiNet.TableSetColumnIndex(1);
    }

    private static void EndPropertyRow()
    {
        EndColumns();
        ImGuiNet.PopID();
    }

    private static bool DrawAxisDrag(string axis, ref float value, float fieldW, Color tagColor)
    {
        bool changed = false;
        float gap = ImGuiNet.GetStyle().ItemSpacing.X;
        float h = ImGuiNet.GetFrameHeight();
        var tagSize = new Vector2(h, h);

        var dl = ImGuiNet.GetWindowDrawList();
        Vector2 p0 = ImGuiNet.GetCursorScreenPos();
        Vector2 p1 = new Vector2(p0.x + tagSize.x, p0.y + tagSize.y);

        ImGuiNet.InvisibleButton($"##tag_{axis}", tagSize);
        bool hovered = ImGuiNet.IsItemHovered();
        bool held = ImGuiNet.IsItemActive();

        Vector4 bg = new Vector4(tagColor.r, tagColor.g, tagColor.b, tagColor.a);
        if (held)
        {
            bg = new Vector4(tagColor.r * 0.90f, tagColor.g * 0.90f, tagColor.b * 0.90f, tagColor.a);
        }
        else if (hovered)
        {
            bg = new Vector4(
                MathF.Min(1f, tagColor.r + 0.10f),
                MathF.Min(1f, tagColor.g + 0.10f),
                MathF.Min(1f, tagColor.b + 0.10f),
                tagColor.a);
        }

        float rounding = ImGuiNet.GetStyle().FrameRounding;
        dl.AddRectFilled(p0, p1, ImGuiNet.ColorConvertFloat4ToU32(bg), rounding);

        Vector2 textSize = ImGuiNet.CalcTextSize(axis);
        Vector2 textPos = new Vector2(
            p0.x + (tagSize.x - textSize.x) * 0.5f,
            p0.y + (tagSize.y - textSize.y) * 0.5f);
        dl.AddText(textPos, ImGuiNet.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), axis);

        if (held)
        {
            float speed = 0.02f;
            var io = ImGuiNet.GetIO();
            if (io.KeyShift) speed *= 0.2f;
            if (io.KeyCtrl) speed *= 5.0f;

            float delta = io.MouseDelta.X * speed;
            if (delta != 0f)
            {
                value += delta;
                changed = true;
            }

            ImGuiNet.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        }

        ImGuiNet.SameLine(0f, gap);
        ImGuiNet.SetNextItemWidth(fieldW);
        changed |= ImGuiNet.InputFloat($"##{axis}", ref value);

        return changed;
    }
    
    private static string ToInspectorLabel(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var sb = new StringBuilder(name.Length * 2);

        sb.Append(char.ToUpper(name[0]));

        for (int i = 1; i < name.Length; i++)
        {
            char curr = name[i];
            char prev = name[i - 1];
            char prevPrev = i > 1 ? name[i - 2] : '\0';

            bool currUpper = char.IsUpper(curr);
            bool prevUpper = char.IsUpper(prev);
            bool currLower = char.IsLower(curr);
            bool currDigit = char.IsDigit(curr);
            bool prevDigit = char.IsDigit(prev);

            bool needSpace =
                // lower -> upper (positionX)
                (!prevUpper && currUpper)

                // acronym end: URLValue
                || (prevUpper && currLower && i > 1 && char.IsUpper(prevPrev))

                // letter <-> digit
                || (!prevDigit && currDigit)
                || (prevDigit && !currDigit);

            if (needSpace)
                sb.Append(' ');

            sb.Append(curr);
        }

        return sb.ToString();
    }
    
    #endregion
}
