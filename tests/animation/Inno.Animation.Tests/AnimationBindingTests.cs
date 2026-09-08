using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Animation.Runtime;
using Inno.Core.Diagnostics;
using Inno.Core.Identity;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Xunit;

namespace Inno.Animation.Tests;

public sealed class AnimationBindingTests
{
    [Fact]
    public void ProviderIsDiscoveredAndMissingIssuesRecoverOnTheNextBatch()
    {
        string cache = Path.Combine(Path.GetTempPath(), "InnoAnimationBindings", Guid.NewGuid().ToString("N"));
        try
        {
            using var modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = cache });
            using var types = new TypeCatalog(modules);
            var reporter = new Reporter();
            using var bindings = new AnimationBindingRuntime(types, reporter);
            var owner = new IdentityAllocator();
            var destination = new Destination();
            owner.Register(destination);
            var target = new AnimationTarget(destination.identity);
            bindings.Apply([new AnimationSample(target, new AnimationBindingId("test.scalar"), new AnimationValue(AnimationValueKind.Scalar, 3f))]);
            Assert.Equal(3f, destination.value);
            Assert.Empty(reporter.issues);
            bindings.Apply([new AnimationSample(target, new AnimationBindingId("test.missing"), new AnimationValue(AnimationValueKind.Scalar, 3f))]);
            Assert.Equal("animation.binding.unavailable", Assert.Single(reporter.issues).code);
            bindings.Apply([new AnimationSample(target, new AnimationBindingId("test.scalar"), new AnimationValue(AnimationValueKind.Scalar, 4f))]);
            Assert.Empty(reporter.issues);
            types.Rebuild();
            bindings.Apply([new AnimationSample(target, new AnimationBindingId("test.scalar"), new AnimationValue(AnimationValueKind.Scalar, 5f))]);
            Assert.Equal(5f, destination.value);
            owner.Unregister(destination);
            bindings.Apply([new AnimationSample(target, new AnimationBindingId("test.scalar"), new AnimationValue(AnimationValueKind.Scalar, 6f))]);
            Assert.Single(reporter.issues);
            bindings.Apply([new AnimationSample(AnimationTarget.samplingOnly, new AnimationBindingId("test.missing"), default)]);
            Assert.Empty(reporter.issues);
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, true);
        }
    }

    public sealed class Destination : IdentityObject
    {
        public float value { get; set; }
    }

    [AnimationBindingProvider("test.animation.scalar", "test.scalar", AnimationValueKind.Scalar)]
    public sealed class ScalarProvider : AnimationBindingProvider
    {
        public override bool TryApply(AnimationTarget target, AnimationValue value)
        {
            Destination? destination = target.Resolve<Destination>();
            if (destination is null)
                return false;
            destination.value = value.x;
            return true;
        }
    }

    private sealed class Reporter : IDiagnosticReporter
    {
        internal Diagnostic[] issues = [];
        public void Publish(Diagnostic diagnostic) => issues = [diagnostic];
        public void Resolve(string code, string? semanticId = null, Guid? objectId = null) => issues = [];
        public void Replace(IEnumerable<Diagnostic> diagnostics) => issues = diagnostics.ToArray();
    }
}
