using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Rendering.Shaders;

/// <summary>Declares value flow across a source function parameter.</summary>
public enum ShaderSourceParameterDirection
{
    /// <summary>The caller supplies a value.</summary>
    Input,
    /// <summary>The function produces a value.</summary>
    Output,
    /// <summary>The caller supplies a value and the function produces its replacement.</summary>
    InputOutput
}

/// <summary>Describes one parameter parsed from a public source declaration.</summary>
public sealed class ShaderSourceParameter
{
    /// <summary>Creates a validated function parameter.</summary>
    /// <param name="name">Public parameter name, used for semantic port matching rather than ordinal matching.</param>
    /// <param name="type">Canonical value type.</param>
    /// <param name="direction">Declared value flow.</param>
    /// <exception cref="ArgumentException">The parameter has void type.</exception>
    public ShaderSourceParameter(string name, ShaderSourceType type, ShaderSourceParameterDirection direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (type?.id == "void") throw new ArgumentException("A parameter cannot have void type.", nameof(type));
        this.name = name;
        this.type = type ?? throw new ArgumentNullException(nameof(type));
        this.direction = direction;
    }
    /// <summary>Gets the public parameter name.</summary>
    public string name { get; }
    /// <summary>Gets the complete canonical value type.</summary>
    public ShaderSourceType type { get; }
    /// <summary>Gets the declared value flow.</summary>
    public ShaderSourceParameterDirection direction { get; }
}

/// <summary>Contains the single callable interface exported by a source asset.</summary>
public sealed class ShaderSourceFunction
{
    /// <summary>Captures a source function declaration without retaining parser or provider objects.</summary>
    /// <param name="name">Implementation function name selected by import settings.</param>
    /// <param name="returnType">Canonical result type; void has no result port.</param>
    /// <param name="parameters">Unique named parameters in call order.</param>
    /// <param name="location">Original declaration location.</param>
    /// <exception cref="ArgumentException">The name is reserved or parameters have duplicate names.</exception>
    public ShaderSourceFunction(string name, ShaderSourceType returnType,
        IEnumerable<ShaderSourceParameter> parameters, ShaderSourcePosition location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(parameters);
        if (name == "main") throw new ArgumentException("A source module exports a function, not a shader main entry point.", nameof(name));
        ShaderSourceParameter[] snapshot = parameters.ToArray();
        if (snapshot.Any(static parameter => parameter is null) ||
            snapshot.Select(static parameter => parameter.name).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("A function requires unique parameter names.", nameof(parameters));
        this.name = name;
        this.returnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
        this.parameters = Array.AsReadOnly(snapshot);
        this.location = location;
    }
    /// <summary>Gets the implementation function name.</summary>
    public string name { get; }
    /// <summary>Gets the canonical result type.</summary>
    public ShaderSourceType returnType { get; }
    /// <summary>Gets the immutable function parameters.</summary>
    public IReadOnlyList<ShaderSourceParameter> parameters { get; }
    /// <summary>Gets the original declaration position.</summary>
    public ShaderSourcePosition location { get; }

    /// <summary>Validates alternative implementations by semantic names, types, direction, and call order.</summary>
    /// <param name="other">Candidate interface, whose private implementation function name may differ.</param>
    /// <returns>True when both implementations expose the same graph ports and calling interface.</returns>
    public bool HasSameInterface(ShaderSourceFunction? other)
    {
        if (other is null || !returnType.IsEquivalentTo(other.returnType) || parameters.Count != other.parameters.Count)
            return false;
        for (int i = 0; i < parameters.Count; i++)
            if (parameters[i].name != other.parameters[i].name || parameters[i].direction != other.parameters[i].direction ||
                !parameters[i].type.IsEquivalentTo(other.parameters[i].type)) return false;
        return true;
    }
}
