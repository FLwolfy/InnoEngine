namespace Inno.Native.Sdl3
{
    using System;

    /// <summary>
    /// Carries a native platform message through the SDL3 event ABI.
    /// </summary>
public struct Msg : IEquatable<Msg>
    {
        /// <summary>
        /// The hwnd value used as part of this type's public representation.
        /// </summary>
public nint Hwnd;
        /// <summary>
        /// The message value used as part of this type's public representation.
        /// </summary>
public uint Message;
        /// <summary>
        /// The wparam value used as part of this type's public representation.
        /// </summary>
public nuint WParam;
        /// <summary>
        /// The lparam value used as part of this type's public representation.
        /// </summary>
public nint LParam;
        /// <summary>
        /// The time value used as part of this type's public representation.
        /// </summary>
public uint Time;
        /// <summary>
        /// The pt value used as part of this type's public representation.
        /// </summary>
public Point32 Pt;

        /// <summary>
        /// Creates a validated msg instance.
        /// </summary>
        /// <param name="hwnd">
        /// The hwnd consumed by msg; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="message">
        /// The message consumed by msg; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="wParam">
        /// The w param consumed by msg; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="lParam">
        /// The l param consumed by msg; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="time">
        /// The time consumed by msg; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="pt">
        /// The pt consumed by msg; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
public Msg(nint hwnd, uint message, nuint wParam, nint lParam, uint time, Point32 pt)
        {
            Hwnd = hwnd;
            Message = message;
            WParam = wParam;
            LParam = lParam;
            Time = time;
            Pt = pt;
        }

        /// <summary>
        /// Determines whether this value and the supplied value represent the same logical state.
        /// </summary>
        /// <param name="obj">
        /// The object compared with this value.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
public override readonly bool Equals(object? obj)
        {
            return obj is Msg msg && Equals(msg);
        }

        /// <summary>
        /// Determines whether this value and the supplied value represent the same logical state.
        /// </summary>
        /// <param name="other">
        /// The strongly typed value compared with this value.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
public readonly bool Equals(Msg other)
        {
            return Hwnd.Equals(other.Hwnd) &&
                   Message == other.Message &&
                   WParam.Equals(other.WParam) &&
                   LParam.Equals(other.LParam) &&
                   Time == other.Time &&
                   Pt.Equals(other.Pt);
        }

        /// <summary>
        /// Computes a hash code consistent with the implemented equality contract.
        /// </summary>
        /// <returns>
        /// The scalar result calculated from the supplied inputs.
        /// </returns>
public override readonly int GetHashCode()
        {
            return HashCode.Combine(Hwnd, Message, WParam, LParam, Time, Pt);
        }

        /// <summary>
        /// Determines whether the supplied values are equal under the type's equality tolerance.
        /// </summary>
        /// <param name="left">
        /// The left consumed by ==; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="right">
        /// The right consumed by ==; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
public static bool operator ==(Msg left, Msg right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether the supplied values differ under the type's equality tolerance.
        /// </summary>
        /// <param name="left">
        /// The left consumed by !=; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="right">
        /// The right consumed by !=; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
public static bool operator !=(Msg left, Msg right)
        {
            return !(left == right);
        }
    }
}