namespace Inno.Native.Sdl3
{
    using BGCS.Runtime;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Wraps an opaque native sdlgpubuffer ptr ptr pointer without taking ownership of the referenced allocation.
    /// </summary>
[NativeName(NativeNameType.Typedef, "SDL_GPUBuffer")]
#if NET5_0_OR_GREATER
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
#endif
    public unsafe struct SDLGPUBufferPtrPtr : IEquatable<SDLGPUBufferPtrPtr>
    {
        /// <summary>
        /// Creates a validated sdlgpubuffer ptr ptr instance.
        /// </summary>
        /// <param name="handle">
        /// The opaque handle validated by this operation.
        /// </param>
public SDLGPUBufferPtrPtr(SDLGPUBuffer** handle)
        {
            Handle = handle;
        }

        /// <summary>
        /// The handle value used as part of this type's public representation.
        /// </summary>
public SDLGPUBuffer** Handle;

        /// <summary>
        /// Gets whether the caller-visible condition represented by this property is satisfied.
        /// </summary>
public bool IsNull => Handle == null;

        /// <summary>
        /// Gets an empty native pointer wrapper that references no allocation.
        /// </summary>
public static SDLGPUBufferPtrPtr Null => new SDLGPUBufferPtrPtr(null);

        /// <summary>
        /// Gets or sets the value identified by the supplied index.
        /// </summary>
        /// <param name="index">
        /// The zero-based position of the requested value.
        /// </param>
public SDLGPUBuffer* this[int index] { get => Handle[index]; set => Handle[index] = value; }

        /// <summary>
        /// Converts the supplied value to <see cref="SDLGPUBufferPtrPtr"/>.
        /// </summary>
        /// <param name="handle">
        /// The opaque handle validated by this operation.
        /// </param>
        /// <returns>
        /// The validated sdlgpubuffer ptr ptr that represents the completed operation.
        /// </returns>
public static implicit operator SDLGPUBufferPtrPtr(SDLGPUBuffer** handle) => new SDLGPUBufferPtrPtr(handle);

        /// <summary>
        /// Converts the supplied value to an <c>SDLGPUBuffer**</c> pointer.
        /// </summary>
        /// <param name="handle">
        /// The opaque handle validated by this operation.
        /// </param>
        /// <returns>
        /// The validated sdlgpubuffer** that represents the completed operation.
        /// </returns>
public static implicit operator SDLGPUBuffer**(SDLGPUBufferPtrPtr handle) => handle.Handle;

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
public static bool operator ==(SDLGPUBufferPtrPtr left, SDLGPUBufferPtrPtr right) => left.Handle == right.Handle;

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
public static bool operator !=(SDLGPUBufferPtrPtr left, SDLGPUBufferPtrPtr right) => left.Handle != right.Handle;

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
public static bool operator ==(SDLGPUBufferPtrPtr left, SDLGPUBuffer** right) => left.Handle == right;

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
public static bool operator !=(SDLGPUBufferPtrPtr left, SDLGPUBuffer** right) => left.Handle != right;

        /// <summary>
        /// Determines whether this value and the supplied value represent the same logical state.
        /// </summary>
        /// <param name="other">
        /// The strongly typed value compared with this value.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
public bool Equals(SDLGPUBufferPtrPtr other) => Handle == other.Handle;

        /// <summary>
        /// Determines whether this instance and the supplied value represent the same logical state.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
        /// </returns>
        /// <param name="obj">
        /// The object to compare with this instance.
        /// </param>
        public override bool Equals(object? obj) => obj is SDLGPUBufferPtrPtr handle && Equals(handle);

        /// <summary>
        /// Computes a hash code from the fields that participate in logical equality.
        /// </summary>
        /// <returns>
        /// A hash code consistent with the implemented equality contract.
        /// </returns>
        public override int GetHashCode() => ((nuint)Handle).GetHashCode();

#if NET5_0_OR_GREATER
        private string DebuggerDisplay => string.Format("SDLGPUBufferPtrPtr [0x{0}]", ((nuint)Handle).ToString("X"));
#endif
    }
}
