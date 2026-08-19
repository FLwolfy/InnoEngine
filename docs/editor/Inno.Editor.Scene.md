# Inno.Editor.Scene

[Editor 索引](README.md) · [Engine Scene](../engine/Inno.Engine.Scene.md) · [Editor Assets](Inno.Editor.Assets.md)

`Inno.Editor.Scene` 是 Scene 领域的 Editor feature：Workspace、Hierarchy、Inspector、Scene command/menu、Scene drag/drop，以及 SceneAsset 与 Asset Browser 的集成适配器。

## EditorSceneWorkspace

| API | 说明 |
| --- | --- |
| `scenes` / `activeScene` | 当前 additive scene 集合和 active scene。 |
| `CreateScene()` | 创建并加载唯一命名的未保存 Scene。 |
| `OpenScene(path)` | additive 打开 SceneAsset，已打开时只激活。 |
| `CloseScene(scene)` | 关闭指定 Scene；最后一个 Scene 不允许删除。 |
| `SaveScene` / `SaveSceneToDirectory` | 保存并在名称变化时事务重命名对应 SceneAsset。 |
| `SavePrefab` | 把 GameObject 保存到当前 Asset 目录。 |
| `IsDirty` / `TryGetSourcePath` | 查询 Scene 文档状态。 |
| `Refresh()` | 消费已提交 Asset move/change 并同步 Scene/Prefab 名称和 dirty 状态。 |
| Module lifecycle | `EditorRuntime` 自动 Start/Update/Stop，无需手工注册。 |

需要 Workspace 的 Panel、Action、Drop 或 Inspection module 直接在唯一构造函数中声明 `EditorSceneWorkspace`，runtime 自动注入。

## Hierarchy 与顺序

`HierarchyPanel` 绘制 additive Scene 与 GameObject tree。Scene、GameObject 的 reorder/reparent 由独立 Drop Handler 执行；ancestor 拖入 descendant 时继续使用 child promotion 防止循环。Drop 成功后选择移动对象，并向视图返回 reveal/expand 请求。

Component 在 Inspector 中通过按钮上下移动；Transform 始终置顶。GameSystem 同样提供上下移动和删除，但其运行调度仍以明确 `order` 为主。最后一个加载 Scene 的 Delete command 返回 disabled。

## Inspection

| API | 说明 |
| --- | --- |
| `InspectorDrawerAttribute` / `IInspectorDrawer` | 为目标对象类型注册 Inspector。 |
| `PropertyDrawerAttribute` / `IPropertyDrawer` | 为 SerializedProperty 类型注册字段绘制。 |
| `InspectorDrawContext` | EditorContext 与 target。 |
| `PropertyDrawContext` | path、label、type、visibility、getter/setter 与 child drawing。 |
| `SerializedPropertyRenderer` | 解析 drawer、隔离异常并统一属性行布局。 |
| `SceneInspectionModule` | 拥有当前 TypeCache generation 的两个 drawer registry 和 renderer。 |

```csharp
[PropertyDrawer(typeof(Angle))]
public sealed class AngleDrawer : IPropertyDrawer
{
    public void Draw(PropertyDrawContext context)
    {
        Angle value = (Angle)(context.GetValue() ?? default(Angle));
        float degrees = value.degrees;
        if (NativeImGui.DragFloat($"##{context.path}", ref degrees))
            context.SetValue(Angle.FromDegrees(degrees));
    }
}
```

Drawer snapshot 会随 TypeCache generation 刷新；冲突或构造失败会保留旧 snapshot。

## Scene command/menu/drop

`SceneSurface` 以 marker `Type` 区分 Hierarchy Scene/Object/Blank、Component/System、Add Component/System 和 EngineObjectReference。`SceneActionIds` 提供 Create Scene、Create GameObject、Create Child、Set Active、Add Component、Add System。

所有静态 context menu 都使用 Action 上的 `[EditorMenu]`。动态 Add Component/System 菜单使用 `EditorMenuSource` 从当前 TypeCache 生成，Panel 不直接枚举类型。

Scene feature 提供 `HierarchyObjectDropTarget`、`HierarchySceneDropTarget` 与 `EngineObjectReferenceDropTarget`。Scene/Prefab 拖到 Asset 目录的保存 handler 也位于这里，从而保持 `Inno.Editor.Assets` 不依赖 Scene。

## Scripting facade

EditorScripts 可显式使用 `InnoEditor.Scene`、`InnoEditor.Inspection` 和共享的 `InnoEditor.DragDrop`。没有 global using；内建 Registry 实现和具体内建 Panel 不属于 facade。
