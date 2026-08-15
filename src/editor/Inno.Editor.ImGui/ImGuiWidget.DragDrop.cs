using System;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

public static partial class ImGuiWidget
{
    /// <summary>
    /// Publishes an unmanaged drag payload for the most recently submitted item.
    /// </summary>
    /// <typeparam name="TPayload">Unmanaged payload type.</typeparam>
    /// <param name="payloadType">Stable ImGui payload type identifier.</param>
    /// <param name="payload">Payload value.</param>
    /// <param name="drawPreview">Optional preview drawing callback.</param>
    /// <returns><see langword="true"/> while the item is an active drag source.</returns>
    public static unsafe bool DragDropSource<TPayload>(
        string payloadType,
        in TPayload payload,
        Action? drawPreview = null)
        where TPayload : unmanaged
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadType);
        if (!NativeImGui.BeginDragDropSource())
        {
            return false;
        }

        TPayload payloadCopy = payload;
        _ = NativeImGui.SetDragDropPayload(payloadType, &payloadCopy, (nuint)sizeof(TPayload));
        drawPreview?.Invoke();
        NativeImGui.EndDragDropSource();
        return true;
    }

    /// <summary>
    /// Publishes a lazily created unmanaged drag payload for the most recently submitted item.
    /// </summary>
    /// <typeparam name="TPayload">Unmanaged payload type.</typeparam>
    /// <param name="payloadType">Stable ImGui payload type identifier.</param>
    /// <param name="payloadFactory">Creates the payload only after dragging starts.</param>
    /// <param name="drawPreview">Optional preview drawing callback.</param>
    /// <returns><see langword="true"/> while the item is an active drag source.</returns>
    public static unsafe bool DragDropSource<TPayload>(
        string payloadType,
        Func<TPayload> payloadFactory,
        Action? drawPreview = null)
        where TPayload : unmanaged
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadType);
        ArgumentNullException.ThrowIfNull(payloadFactory);
        if (!NativeImGui.BeginDragDropSource())
        {
            return false;
        }

        TPayload payload = payloadFactory();
        _ = NativeImGui.SetDragDropPayload(payloadType, &payload, (nuint)sizeof(TPayload));
        drawPreview?.Invoke();
        NativeImGui.EndDragDropSource();
        return true;
    }

    /// <summary>
    /// Accepts an unmanaged payload on the most recently submitted item.
    /// </summary>
    /// <typeparam name="TPayload">Unmanaged payload type.</typeparam>
    /// <param name="payloadType">Stable ImGui payload type identifier.</param>
    /// <param name="payload">Delivered payload value.</param>
    /// <returns><see langword="true"/> only when a compatible payload is delivered.</returns>
    public static unsafe bool DragDropTarget<TPayload>(string payloadType, out TPayload payload)
        where TPayload : unmanaged
    {
        return DragDropTarget(payloadType, out payload, out _);
    }

    /// <summary>
    /// Accepts an unmanaged payload and reports its preview state on the most recently submitted item.
    /// </summary>
    /// <typeparam name="TPayload">Unmanaged payload type.</typeparam>
    /// <param name="payloadType">Stable ImGui payload type identifier.</param>
    /// <param name="payload">Previewed or delivered payload value.</param>
    /// <param name="isPreviewing">Whether a compatible payload is hovering over the target.</param>
    /// <returns><see langword="true"/> only when a compatible payload is delivered.</returns>
    public static unsafe bool DragDropTarget<TPayload>(
        string payloadType,
        out TPayload payload,
        out bool isPreviewing)
        where TPayload : unmanaged
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadType);
        payload = default;
        isPreviewing = false;
        if (!NativeImGui.BeginDragDropTarget())
        {
            return false;
        }

        ImGuiPayloadPtr nativePayload = NativeImGui.AcceptDragDropPayload(
            payloadType,
            ImGuiDragDropFlags.AcceptBeforeDelivery);
        bool compatible = !nativePayload.IsNull
            && nativePayload.DataSize == sizeof(TPayload)
            && nativePayload.Data != null;
        if (compatible)
        {
            payload = *(TPayload*)nativePayload.Data;
            isPreviewing = nativePayload.Preview;
        }

        bool delivered = compatible && nativePayload.Delivery;
        NativeImGui.EndDragDropTarget();
        return delivered;
    }
}
