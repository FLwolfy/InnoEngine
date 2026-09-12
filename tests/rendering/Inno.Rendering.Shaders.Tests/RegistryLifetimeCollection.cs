using Xunit;

namespace Inno.Rendering.Shaders.Tests;

// These fixtures deliberately change process-visible extension declarations. Other tests discover
// the same declarations, so fault injection must not overlap another registry's construction.
[CollectionDefinition("Shader registry fault injection", DisableParallelization = true)]
public sealed class RegistryLifetimeCollection { }
