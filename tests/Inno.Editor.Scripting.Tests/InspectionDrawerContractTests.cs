using System;
using System.Reflection;
using System.Runtime.CompilerServices;

using Inno.Editor.Inspection;

using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class InspectionDrawerContractTests
{
    [Fact]
    public void NameBindingUsesOneNamedValueTupleContract()
    {
        MethodInfo interfaceBinding = typeof(IInspectionDrawer).GetMethod(
            nameof(IInspectionDrawer.BindName))!;
        MethodInfo protectedBinding = typeof(InspectionDrawer<object>).GetMethod(
            "BindName",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(typeof(ValueTuple<string, Action<string>>), interfaceBinding.ReturnType);
        Assert.Equal(interfaceBinding.ReturnType, protectedBinding.ReturnType);
        Assert.True(protectedBinding.IsAbstract);
        Assert.True(protectedBinding.IsFamily);
        AssertTupleElementNames(interfaceBinding);
        AssertTupleElementNames(protectedBinding);
        Assert.Null(typeof(IInspectionDrawer).GetMethod("GetName"));
        Assert.Null(typeof(IInspectionDrawer).GetMethod("GetNameSetter"));
    }

    private static void AssertTupleElementNames(MethodInfo method)
        => Assert.Equal(
            ["name", "setter"],
            method.ReturnParameter
                .GetCustomAttribute<TupleElementNamesAttribute>()!
                .TransformNames);
}
