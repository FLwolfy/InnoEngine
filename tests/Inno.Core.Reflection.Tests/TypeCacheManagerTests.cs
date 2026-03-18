using System;

using Inno.Core.Reflection;

using Xunit;

namespace Inno.Core.Reflection.Tests;

public sealed class TypeCacheManagerTests
{
    [Fact]
    public void GetSubTypesOf_ReturnsDerivedTypes()
    {
        TypeCacheManager.Refresh();

        var types = TypeCacheManager.GetSubTypesOf<TestBase>();
        Assert.Contains(typeof(TestDerived), types);
    }

    [Fact]
    public void GetTypesImplementing_ReturnsImplementations()
    {
        TypeCacheManager.Refresh();

        var types = TypeCacheManager.GetTypesImplementing<ITestContract>();
        Assert.Contains(typeof(TestContractImpl), types);
    }

    [Fact]
    public void GetTypesWithAttribute_ReturnsAttributedTypes()
    {
        TypeCacheManager.Refresh();

        var types = TypeCacheManager.GetTypesWithAttribute<TestMarkerAttribute>();
        Assert.Contains(typeof(AttributedType), types);
    }
}

public class TestBase;
public sealed class TestDerived : TestBase;

public interface ITestContract;
public sealed class TestContractImpl : ITestContract;

[AttributeUsage(AttributeTargets.Class)]
public sealed class TestMarkerAttribute : Attribute;

[TestMarker]
public sealed class AttributedType;
