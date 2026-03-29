using System;

namespace Inno.Core.Reflection;

/// <summary>
/// Marks a static method to be invoked automatically
/// once when TypeCacheManager.Initialize() is called.
/// 
/// Constraints:
/// - Method MUST be static
/// - Method MUST return void
/// - Method MUST take no parameters
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TypeCacheInitializeAttribute : Attribute;

/// <summary>
/// Marks a static method to be invoked automatically
/// whenever the TypeCache is refreshed.
/// (TypeCache is always refreshed once after initialization)
/// 
/// <p>
/// Constraints: 
/// - Method MUST be static <br/>
/// - Method MUST return void <br/>
/// - Method MUST take no parameters
/// </p>
/// 
/// <code>
///     [TypeCacheRefresh]
///     static void OnTypeCacheRefreshed() { ... }
/// </code>
/// 
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TypeCacheRefreshAttribute : Attribute;