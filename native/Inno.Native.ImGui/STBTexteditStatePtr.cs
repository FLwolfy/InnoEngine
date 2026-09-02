#nullable disable

namespace Inno.Native.ImGui
{
    using System;

    /// <summary>
    /// Wraps an opaque native stbtextedit state ptr pointer without taking ownership of the referenced allocation.
    /// </summary>
public unsafe struct STBTexteditStatePtr : IEquatable<STBTexteditStatePtr>
    {
        /// <summary>
        /// The handle value used as part of this type's public representation.
        /// </summary>
public STBTexteditState* Handle;

        /// <summary>
        /// Creates a validated stbtextedit state ptr instance.
        /// </summary>
        /// <param name="handle">
        /// The opaque handle validated by this operation.
        /// </param>
public unsafe STBTexteditStatePtr(STBTexteditState* handle)
        {
            Handle = handle;
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
public override readonly bool Equals(object obj)
        {
            return obj is STBTexteditStatePtr ptr && Equals(ptr);
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
public readonly bool Equals(STBTexteditStatePtr other)
        {
            return Handle == other.Handle;
        }

        /// <summary>
        /// Computes a hash code consistent with the implemented equality contract.
        /// </summary>
        /// <returns>
        /// The scalar result calculated from the supplied inputs.
        /// </returns>
public override readonly int GetHashCode()
        {
            return ((nint)Handle).GetHashCode();
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
public static bool operator ==(STBTexteditStatePtr left, STBTexteditStatePtr right)
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
public static bool operator !=(STBTexteditStatePtr left, STBTexteditStatePtr right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Converts the supplied value to an <c>STBTexteditState*</c> pointer.
        /// </summary>
        /// <param name="handle">
        /// The opaque handle validated by this operation.
        /// </param>
        /// <returns>
        /// The validated stbtextedit state* that represents the completed operation.
        /// </returns>
public static implicit operator STBTexteditState*(STBTexteditStatePtr handle) => handle.Handle;

        /// <summary>
        /// Converts the supplied value to <see cref="STBTexteditStatePtr"/>.
        /// </summary>
        /// <param name="handle">
        /// The opaque handle validated by this operation.
        /// </param>
        /// <returns>
        /// The validated stbtextedit state ptr that represents the completed operation.
        /// </returns>
public static implicit operator STBTexteditStatePtr(STBTexteditState* handle) => new(handle);
    }
}
