using System;
using System.Reflection;
using System.Reflection.Emit;

using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;

using Xunit;

namespace Inno.Extensibility.Types.Tests;

public sealed class AssemblyExtensionsTests
{
    [Fact]
    public void AssemblyClassificationReadsBothMetadataDimensions()
    {
        var asm = BuildDynamicAssembly("InnoPlugin", "Editor");
        Assert.Equal(AssemblyDomain.InnoPlugin, asm.GetInnoAssemblyDomain());
        Assert.Equal(AssemblyScope.Editor, asm.GetInnoAssemblyScope());
    }

    [Fact]
    public void MissingClassificationIsRejected()
    {
        var asm = BuildDynamicAssembly(null, null);
        Assert.Throws<InvalidOperationException>(() => asm.GetInnoAssemblyDomain());
        Assert.Throws<InvalidOperationException>(() => asm.GetInnoAssemblyScope());
    }

    private static Assembly BuildDynamicAssembly(string? domainName, string? scopeName)
    {
        var asmName = new AssemblyName("Inno.Dynamic." + Guid.NewGuid().ToString("N"));
        var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);

        if (domainName is not null && scopeName is not null)
        {
            var ctor = typeof(AssemblyMetadataAttribute).GetConstructor(new[] { typeof(string), typeof(string) })!;
            asmBuilder.SetCustomAttribute(new CustomAttributeBuilder(
                ctor,
                new object[] { "Inno.AssemblyDomain", domainName }));
            asmBuilder.SetCustomAttribute(new CustomAttributeBuilder(
                ctor,
                new object[] { "Inno.AssemblyScope", scopeName }));
        }

        var module = asmBuilder.DefineDynamicModule("main");
        var type = module.DefineType("Inno.Dynamic.Placeholder", TypeAttributes.Public | TypeAttributes.Class);
        _ = type.CreateType();

        return asmBuilder;
    }
}
