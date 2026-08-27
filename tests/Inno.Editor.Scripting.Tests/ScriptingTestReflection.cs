using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

using Inno.Core.Assemblies;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Editor.Scripting;
using Inno.Engine.Scene;

namespace Inno.Editor.Scripting.Tests;

internal static class ScriptingTestReflection
{
    internal static object Create(
        Assembly assembly,
        string typeName,
        params object?[] arguments)
        => Create(assembly.GetType(typeName, throwOnError: true)!, arguments);

    internal static object Create(Type type, params object?[] arguments)
    {
        try
        {
            return Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: arguments,
                culture: null)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            Rethrow(exception.InnerException);
            throw;
        }
    }

    internal static ScriptManager CreateScriptManager(
        ScriptManagerOptions options,
        Func<CancellationToken, ValueTask>? compileGateProbe)
        => (ScriptManager)Create(typeof(ScriptManager), options, compileGateProbe);

    internal static T Get<T>(object target, string propertyName)
        => (T)target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;

    internal static T Invoke<T>(object target, string methodName, params object?[] arguments)
    {
        MethodInfo method = FindMethod(target.GetType(), methodName, arguments.Length, isStatic: false);
        return InvokeMethod<T>(target, method, arguments);
    }

    internal static void Invoke(object target, string methodName, params object?[] arguments)
        => _ = Invoke<object?>(target, methodName, arguments);

    internal static T InvokeStatic<T>(Type type, string methodName, params object?[] arguments)
    {
        MethodInfo method = FindMethod(type, methodName, arguments.Length, isStatic: true);
        return InvokeMethod<T>(target: null, method, arguments);
    }

    internal static MethodInfo FindMethod(
        Type type,
        string methodName,
        int parameterCount,
        bool isStatic = false)
        => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance))
            .Single(candidate =>
                candidate.Name == methodName &&
                candidate.GetParameters().Length == parameterCount);

    internal static T InvokeMethod<T>(object? target, MethodInfo method, params object?[] arguments)
    {
        try
        {
            return (T)method.Invoke(target, arguments)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            Rethrow(exception.InnerException);
            throw;
        }
    }

    private static void Rethrow(Exception exception)
        => ExceptionDispatchInfo.Capture(exception).Throw();
}

internal static class ScriptManagerTestExtensions
{
    internal static bool TryCompilePending(
        this ScriptManager manager,
        out Task<ScriptCompilationResult>? compilation)
    {
        object?[] arguments = [null];
        bool result = ScriptingTestReflection.InvokeMethod<bool>(
            manager,
            ScriptingTestReflection.FindMethod(
                typeof(ScriptManager),
                "TryCompilePending",
                parameterCount: 1),
            arguments);
        compilation = (Task<ScriptCompilationResult>?)arguments[0];
        return result;
    }

    internal static ValueTask<ScriptCompilationResult> CompileAsync(
        this ScriptManager manager,
        CancellationToken cancellationToken = default)
        => ScriptingTestReflection.InvokeMethod<ValueTask<ScriptCompilationResult>>(
            manager,
            ScriptingTestReflection.FindMethod(
                typeof(ScriptManager),
                "CompileAsync",
                parameterCount: 1),
            cancellationToken);

    internal static bool ApplyPendingReload(this ScriptManager manager)
        => ScriptingTestReflection.Invoke<bool>(manager, "ApplyPendingReload");

    internal static bool AdvanceUnloadVerification(
        this ScriptManager manager,
        out Exception? failure)
    {
        object?[] arguments = [null];
        bool result = ScriptingTestReflection.InvokeMethod<bool>(
            manager,
            ScriptingTestReflection.FindMethod(
                typeof(ScriptManager),
                "AdvanceUnloadVerification",
                parameterCount: 1),
            arguments);
        failure = (Exception?)arguments[0];
        return result;
    }

    internal static bool IsUnloadVerificationPendingForTest(this ScriptManager manager)
        => ScriptingTestReflection.Get<bool>(manager, "isUnloadVerificationPending");

    internal static IReadOnlyList<string> CompiledAssembliesForTest(
        this ScriptCompilationResult result)
        => ScriptingTestReflection.Get<IReadOnlyList<string>>(result, "compiledAssemblies");

    internal static IReadOnlyList<string> ReusedAssembliesForTest(
        this ScriptCompilationResult result)
        => ScriptingTestReflection.Get<IReadOnlyList<string>>(result, "reusedAssemblies");

    internal static IReadOnlyList<AssemblyLoadRequest> ReloadRequestsForTest(
        this ScriptCompilationResult result)
        => ScriptingTestReflection.Get<IReadOnlyList<AssemblyLoadRequest>>(result, "reloadRequests");
}

internal sealed class ReflectedSceneWorkspace
{
    internal ReflectedSceneWorkspace(EditorInteractions? interactions = null)
    {
        Type workspaceType = typeof(SceneEdits).Assembly.GetType(
            "Inno.Editor.Scene.EditorSceneWorkspace",
            throwOnError: true)!;
        instance = ScriptingTestReflection.Create(workspaceType, interactions);
    }

    internal object instance { get; }

    internal EditorModule module => (EditorModule)instance;

    internal IReadOnlyList<GameScene> scenes
        => ScriptingTestReflection.Get<IReadOnlyList<GameScene>>(instance, "scenes");

    internal GameScene? activeScene
        => ScriptingTestReflection.Get<GameScene?>(instance, "activeScene");

    internal GameScene CreateScene()
        => ScriptingTestReflection.Invoke<GameScene>(instance, "CreateScene");

    internal string Save(GameScene scene, string directory)
        => ScriptingTestReflection.Invoke<string>(instance, "Save", scene, directory);

    internal bool IsDirty(GameScene scene)
        => ScriptingTestReflection.Invoke<bool>(instance, "IsDirty", scene);

    internal bool CloseScene(GameScene scene)
        => ScriptingTestReflection.Invoke<bool>(instance, "CloseScene", scene);

    internal GameScene Open(string path)
        => ScriptingTestReflection.Invoke<GameScene>(instance, "Open", path);

    internal bool TryGetSourcePath(GameScene scene, out string path)
    {
        object?[] arguments = [scene, null];
        bool result = ScriptingTestReflection.InvokeMethod<bool>(
            instance,
            ScriptingTestReflection.FindMethod(
                instance.GetType(),
                "TryGetSourcePath",
                parameterCount: 2),
            arguments);
        path = (string?)arguments[1] ?? string.Empty;
        return result;
    }

    internal void Start(EditorContext context) => module.Start(context);

    internal void Update(EditorContext context) => module.Update(context);

    internal void Stop(EditorContext context) => module.Stop(context);
}
