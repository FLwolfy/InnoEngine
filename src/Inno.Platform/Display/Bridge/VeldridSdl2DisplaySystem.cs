using System.Collections.Generic;

using Inno.Core.Events;
using Inno.Core.Math;

using Veldrid;
using Veldrid.Sdl2;

namespace Inno.Platform.Display.Bridge;

internal class VeldridSdl2DisplaySystem : IDisplaySystem
{
    // Cursor
    private static readonly Dictionary<Input.MouseCursor, SDL_Cursor> CURSOR_MAP = new()
    {
        [Input.MouseCursor.Arrow] = 
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.Arrow),
        [Input.MouseCursor.TextInput] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.IBeam),
        [Input.MouseCursor.ResizeAll] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeAll),
        [Input.MouseCursor.ResizeNS] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeNS),
        [Input.MouseCursor.ResizeEW] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeWE),
        [Input.MouseCursor.ResizeNESW] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeNESW),
        [Input.MouseCursor.ResizeNWSE] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.SizeNWSE),
        [Input.MouseCursor.Hand] =
            Sdl2Native.SDL_CreateSystemCursor(SDL_SystemCursor.Hand),
    };
    
    // SDL native window delegate
    private unsafe delegate uint SdlGetGlobalMouseStateT(int* x, int* y);
    private SdlGetGlobalMouseStateT? m_pSdlGetGlobalMouseState;
    private unsafe delegate int SdlGetDisplayUsableBoundsT(int displayIndex, Rectangle* rect);
    private SdlGetDisplayUsableBoundsT? m_pSdlGetDisplayUsableBounds;

    internal VeldridSdl2DisplaySystem()
    {
        m_pSdlGetGlobalMouseState ??= Sdl2Native.LoadFunction<SdlGetGlobalMouseStateT>("SDL_GetGlobalMouseState");
        m_pSdlGetDisplayUsableBounds ??= Sdl2Native.LoadFunction<SdlGetDisplayUsableBoundsT>("SDL_GetDisplayUsableBounds");
    }
    
    public int GetDisplayNumber()
    {
        return Sdl2Native.SDL_GetNumVideoDisplays();
    }

    public Rect GetDisplayBounds(int displayIndex)
    {
        Rectangle rect;

        unsafe
        {
            Sdl2Native.SDL_GetDisplayBounds(displayIndex, &rect);
        }
        
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public Rect GetUsableDisplayBounds(int displayIndex)
    {
        Rectangle r = new Rectangle();

        unsafe
        {
            m_pSdlGetDisplayUsableBounds?.Invoke(displayIndex, &r);
        }
        
        return new(r.X, r.Y, r.Width, r.Height);
    }
    
    public void ShowCursor(bool show)
    {
        Sdl2Native.SDL_ShowCursor(show ? 1 : 0);
    }

    public void SetCursor(Input.MouseCursor cursor)
    {
        Sdl2Native.SDL_SetCursor(CURSOR_MAP.TryGetValue(cursor, out var sdlCursor)
            ? sdlCursor
            : CURSOR_MAP[Input.MouseCursor.Arrow]);
    }

    public Vector2Int GetGlobalMousePos()
    {
        int x = 0;
        int y = 0;
        
        unsafe
        {
            m_pSdlGetGlobalMouseState?.Invoke(&x, &y);
        }
        
        return new(x, y);
    }

    public IReadOnlyList<Input.MouseButton> GetGlobalMouseButton()
    {
        List<Input.MouseButton> mouseButtons = [];
        
        unsafe
        {
            int _, __;
            uint? buttons = m_pSdlGetGlobalMouseState?.Invoke(&_, &__);
            
            // SDL: 1=Left, 2=Middle, 4=Right
            var left = (buttons & 0b0001) != 0; // Left
            var right = (buttons & 0b0100) != 0; // Right
            var middle = (buttons & 0b0010) != 0; // Middle
            
            if (left) mouseButtons.Add(Input.MouseButton.Left);
            if (right) mouseButtons.Add(Input.MouseButton.Right);
            if (middle) mouseButtons.Add(Input.MouseButton.Middle);
        }
        
        return mouseButtons;
    }

    public void Dispose()
    {
        m_pSdlGetGlobalMouseState = null;
        m_pSdlGetDisplayUsableBounds = null;
    }
}