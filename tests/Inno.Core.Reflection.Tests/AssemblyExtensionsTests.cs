using System;
using System.Reflection;
using System.Reflection.Emit;

using Inno.Core.Assemblies;
using Inno.Core.Reflection;

using Xunit;

namespace Inno.Core.Reflection.Tests;

public sealed class AssemblyExtensionsTests
{
    [Fact]
    public void GetInnoAssemblyGroup_ReadsMetadata()
    {
        var asm = BuildDynamicAssembly("Core");
        Assert.Equal(AssemblyGroup.Core, asm.GetInnoAssemblyGroup());
    }

    [Fact]
    public void GetInnoAssemblyGroup_DefaultsToNone()
    {
        var asm = BuildDynamicAssembly(null);
        Assert.Equal(AssemblyGroup.None, asm.GetInnoAssemblyGroup());
    }

    private static Assembly BuildDynamicAssembly(string? groupName)
    {
        var asmName = new AssemblyName("Inno.Dynamic." + Guid.NewGuid().ToString("N"));
        var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);

        if (groupName != null)
        {
            var ctor = typeof(AssemblyMetadataAttribute).GetConstructor(new[] { typeof(string), typeof(string) })!;
            var attr = new CustomAttributeBuilder(ctor, new object[] { "Inno.AssemblyGroup", groupName });
            asmBuilder.SetCustomAttribute(attr);
        }

        var module = asmBuilder.DefineDynamicModule("main");
        var type = module.DefineType("Inno.Dynamic.Placeholder", TypeAttributes.Public | TypeAttributes.Class);
        _ = type.CreateType();

        return asmBuilder;
    }
}
