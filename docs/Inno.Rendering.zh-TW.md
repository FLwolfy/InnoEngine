# Inno.Rendering 完整中文說明（不含 Inno.Rendering.Renderer）

本文對應 `src/render/Inno.Rendering`，目標是把整個設計框架、總體使用流程、各板塊 API 與 member 的用途與互相依賴關係講清楚，並提供可落地的使用案例。

## 1. 模組定位與設計框架

`Inno.Rendering` 是「高階渲染描述層」，它不直接下 GPU 指令，而是：

1. 用 `RenderScene + RenderView + RenderTarget` 描述「畫什麼、怎麼看、畫到哪裡」。
2. 用 `RenderPipeline` 決定「以哪些 Pass 順序處理」。
3. 由 `RenderSystem` 組裝每幀上下文並執行。

依賴：

- `Inno.Core.Mathematics`：向量、矩陣、顏色、四元數。
- `Inno.Graphics`：底層圖形抽象（本專案這層在 `Inno.Rendering` 僅作模型依賴，非直接 API 主角）。

分層關係（由上到下）：

1. System：`RenderSystem`, `RenderRequest`, `RenderFrame`。
2. Pipeline/Pass：`RenderPipeline`, `ForwardPipeline`, `RenderPass`。
3. Scene/View：`RenderScene`, `Renderable`, `Light`, `RenderView`, `Camera`。
4. Resource：`Mesh`, `Material`, `Texture`, `RenderTarget`。
5. Internal（執行期）：`RenderList`, `RenderQueue`, `RenderResourceCache`。

## 2. 總體使用流程（完整）

典型每幀流程：

1. 建立或更新場景資料：`RenderScene` 內的 `renderables` / `lights`。
2. 建立或更新相機與視圖：`Camera` + `RenderView`。
3. 準備輸出目標：`RenderTarget.Backbuffer(...)` 或 `RenderTarget.Texture2D(...)`。
4. 決定管線：`ForwardPipeline.Create(builder => ...)`。
5. 呼叫 `RenderSystem.Render(...)`。
6. 讀取 `RenderFrameStatistics` 做 HUD/分析。

簡化端到端範例：

```csharp
var scene = new RenderScene();
scene.environment.ambientColor = Color.BLACK;
scene.settings.enableShadows = true;

var mesh = new MeshBuilder()
    .SetVertices<StandardVertex>(vertices)
    .SetIndices(indices)
    .AddSurface(new MeshSurface(0, indices.Length, 0, MeshTopology.Triangles))
    .Build("Cube");

var material = new StandardMaterial
{
    name = "PBR",
    baseColor = Color.WHITE,
    metallic = 0.1f,
    roughness = 0.8f
};

scene.Add(new MeshRenderable
{
    name = "Cube",
    mesh = mesh,
    material = material,
    transform = Transform.identity
});

scene.Add(new DirectionalLight
{
    intensity = 1.5f,
    direction = Vector3.NormalizeSafe(new Vector3(-0.5f, -1f, -0.2f))
});

var camera = new PerspectiveCamera
{
    fieldOfViewDegrees = 60f,
    transform = new CameraTransform
    {
        position = new Vector3(0, 2, 6),
        rotation = Quaternion.identity
    }
};

var view = RenderView.ForCamera(camera)
    .WithViewport(0, 0, 1280, 720)
    .WithClear(ClearSettings.Solid(new Color(0.1f, 0.12f, 0.16f, 1f)));

var window = new RenderWindow { nativeHandle = hwnd, width = 1280, height = 720 };
var target = RenderTarget.Backbuffer(window);

var pipeline = ForwardPipeline.Create(b =>
{
    b.enableDepthPrepass = true;
    b.enableShadows = true;
    b.enableTransparentPass = true;
});

var renderSystem = new RenderSystem(pipeline, new RenderSettings
{
    enableValidation = true,
    collectStatistics = true
});

renderSystem.Render(scene, view, target);
var stat = renderSystem.GetLastFrameStatistics();
```

## 3. API 詳解（含互相依賴與用法）

---

## 3.1 System 板塊

### `RenderSettings`

- `enableValidation`：開發期驗證開關。
  - 何時用：排查渲染流程錯誤時。
  - 例：`new RenderSettings { enableValidation = true }`
- `collectStatistics`：是否收集每幀統計。
  - 何時用：要顯示性能 HUD。
  - 例：`settings.collectStatistics = true;`

### `RenderRequest`

- `scene`（required）：輸入場景。
- `view`（required）：視圖描述。
- `target`（required）：輸出目標。
- 何時用：你想先組好 request，再統一提交。
- 例：

```csharp
var request = new RenderRequest { scene = scene, view = view, target = target };
renderSystem.Render(request);
```

### `RenderFrame`

- `frameIndex`：幀序號（內部遞增）。
- `timestamp`：渲染時間戳（UTC）。
- `statistics`：統計物件。
- 何時會用到：通常由 `RenderSystem` 內部建立，外部主要透過 `GetLastFrameStatistics()` 間接使用。

### `RenderFrameStatistics`

- `drawCalls`：繪製呼叫數（目前框架保留）。
- `renderablesSubmitted`：提交的 renderable 數量。
- `visibleLights`：光源數量。
- `cpuTime`：CPU 端渲染耗時。
- 例：

```csharp
var s = renderSystem.GetLastFrameStatistics();
Console.WriteLine($"{s.renderablesSubmitted} objs, {s.visibleLights} lights, {s.cpuTime.TotalMilliseconds:F2} ms");
```

### `RenderSystem`

- 建構子 `RenderSystem(RenderPipeline? pipeline = null, RenderSettings? settings = null)`：
  - 不傳 pipeline 時預設 `ForwardPipeline.Create()`。
- `pipeline`：可熱切換管線。
- `settings`：全域設定物件。
- `Render(RenderScene scene, RenderView view, RenderTarget target)`：
  - 最常用入口；內部封成 `RenderRequest`。
- `Render(RenderRequest request)`：
  - 若已有 request 物件，直接呼叫此版本。
- `GetLastFrameStatistics()`：
  - 取得上一幀統計。

互相依賴：

- `RenderSystem` 會建立 `RenderPipelineContext`（internal），交給 `pipeline.Render(context)`。

---

## 3.2 Pipeline / Pass 板塊

### `RenderPipeline`（abstract）

- `name`：管線名稱。
- `Render(context)`（internal abstract）：實際執行入口。
- 何時會互相需要：
  - `RenderSystem` 必須有 `RenderPipeline` 才能跑。

### `ForwardPipeline`

- `Create(Action<ForwardPipelineBuilder>?)`：主要建構入口。
- 內部會建立 `RenderList` 並逐一執行 pass。
- `FromFeatureSet(PipelineFeatureSet)`（internal）：
  - 依 feature 開關決定 pass 清單。

範例：

```csharp
var pipeline = ForwardPipeline.Create(b =>
{
    b.enableDepthPrepass = true;
    b.enableShadows = true;
    b.enableSkybox = true;
    b.enableTransparentPass = true;
    b.enablePostProcessing = true;
    b.enableUiPass = true;
});
```

### `ForwardPipelineBuilder`

- `enableDepthPrepass`
- `enableShadows`
- `enableSkybox`
- `enableTransparentPass`
- `enablePostProcessing`
- `enableGizmos`
- `enableObjectPicking`（目前 feature 保留）
- `enableUiPass`
- `Build()`：產出 `ForwardPipeline`。

### `RenderPipelineAsset`

- `name`：資產名（序列化用途）。
- `features`：功能開關集合。
- `CreatePipeline()`：由 asset 產生 pipeline。
- 何時用：編輯器資產化配置。

### `PipelineFeatureSet`

- 成員同 builder（上面 8 個 bool）。
- 何時用：配置快照、可儲存/還原。

### `RenderPass`（abstract）

- `name`：pass 名稱。
- `enabled`：是否啟用。
- `Execute(context)`（internal abstract）。

內建 pass：

- `OpaquePass`：`RenderItemFilter.Opaque`。
- `TransparentPass`：`RenderItemFilter.Transparent`。
- `ShadowPass`：`RenderItemFilter.ShadowCasters`。
- `DepthPrepass`：`RenderItemFilter.DepthOnly`。
- `SkyboxPass` / `GizmoPass` / `UiPass` / `PostProcessPass`：目前實作骨架（空 body）。

---

## 3.3 Scene 板塊

### `SceneEnvironment`

- `ambientColor`：環境光色。
- `ambientIntensity`：環境光強度。
- 例：`scene.environment.ambientIntensity = 0.6f;`

### `SceneRenderSettings`

- `enableShadows`：場景陰影總開關。
- `enableFog`：霧效開關（目前描述層）。

### `RenderScene`

- `environment` / `settings` / `renderables` / `lights`。
- `Add(Renderable)`、`Remove(Renderable)`。
- `Add(Light)`、`Remove(Light)`。
- 例：

```csharp
scene.Add(meshRenderable);
scene.Add(new PointLight { position = new Vector3(0, 2, 0), range = 8f });
```

### `RenderableCollection`

- `items`：只讀列表。
- `Add` / `Remove` / `Clear`。
- 何時用：批量管理場景物件。

### `Visibility`

- `Visible`, `Hidden`。
- 例：`renderable.visibility = Visibility.Hidden;`

### `ShadowMode`

- `Off`, `CastOnly`, `ReceiveOnly`, `CastAndReceive`。
- 例：`meshRenderable.shadowMode = ShadowMode.CastOnly;`

### `MotionMode`

- `Static`, `Dynamic`。
- 例：`renderable.motionMode = MotionMode.Dynamic;`

### `Renderable`（abstract）

- `name`
- `transform`
- `layerMask`
- `visibility`
- `shadowMode`
- `motionMode`
- `sortingOrder`

### `MeshRenderable`

- `mesh`（required）
- `material`（required）
- `materialOverrides`（可為 null）
- 互相需要：
  - 與 `Mesh`、`Material` 必然組合。
  - `materialOverrides` 用 `MaterialPropertyBlock` 做每物件覆寫。

### `SpriteRenderable`

- `material`（required, `SpriteMaterial`）。

### `SkyboxRenderable`

- `material`（required, `SkyboxMaterial`）。

### `FullscreenQuadRenderable`

- `material`（required, `Material`）。
- 何時用：後處理或全螢幕特效輸入。

---

## 3.4 Viewing / Camera 板塊

### `Viewport`

- `x, y, width, height`。
- 例：`new Viewport(0, 0, 1920, 1080)`。

### `RenderLayerMask`

- `value`
- `everything`：全圖層。
- `default`：僅第 0 層（`1u`）。
- 例：`view.layerMask = RenderLayerMask.everything;`

### `ClearSettings`

- `clearColor`, `clearDepth`, `clearStencil`, `color`, `depth`, `stencil`
- `Solid(Color)`：常用 solid clear。
- 例：`view.WithClear(ClearSettings.Solid(Color.BLACK));`

### `CullingSettings`

- `frustumCulling`, `occlusionCulling`
- `default = (true, false)`。
- 例：`view.culling = new CullingSettings(true, false);`

### `RenderView`

- `camera`（required）
- `viewport`
- `clear`
- `culling`
- `layerMask`
- `enablePostProcessing`
- `enableGizmos`
- `enableDebugOverlays`
- `ForCamera(camera)`：靜態工廠。
- `WithViewport(...)`：fluent 設定。
- `WithClear(clear)`：fluent 設定。

### `CameraTransform`

- `position`
- `rotation`
- `forward`（由 rotation 推導）
- `up`（由 rotation 推導）
- 例：

```csharp
camera.transform = new CameraTransform
{
    position = new Vector3(0, 1.5f, 5f),
    rotation = Quaternion.identity
};
var fwd = camera.transform.forward;
```

### `CameraExposure`

- `exposureCompensation`
- `aperture`
- `shutterSpeed`
- `iso`
- `default`：常見攝影預設。
- 例：`camera.exposure = CameraExposure.@default with { iso = 200f };`

### `Camera`（abstract）

- `transform`
- `nearClip`
- `farClip`
- `exposure`
- `GetViewMatrix()`：以 RH look-at 計算視圖矩陣。
- `GetProjectionMatrix(aspectRatio)`：抽象。

### `PerspectiveCamera`

- `fieldOfViewDegrees`
- `GetProjectionMatrix(aspectRatio)`：透視投影。

### `OrthographicCamera`

- `orthoHeight`
- `GetProjectionMatrix(aspectRatio)`：正交投影。

---

## 3.5 Resources：Transform / Mesh 板塊

### `Transform`

- `position`
- `rotation`
- `scale`
- `ToMatrix()`：S * R * T。
- `identity`：零位移/單位旋轉/單位縮放。

### `MeshTopology`

- `Triangles`, `TriangleStrip`, `Lines`, `LineStrip`, `Points`。

### `VertexSemantic`

- `Position`, `Normal`, `Tangent`, `Bitangent`, `Color0`, `TexCoord0~3`, `BlendIndices`, `BlendWeights`。

### `MeshBounds`

- `(center, extents)` AABB。

### `MeshSurface`

- `(indexStart, indexCount, materialSlot, topology)`。
- 何時用：一個 mesh 多個子材質時。

### `VertexElement`

- `(semantic, semanticIndex, offset, sizeInBytes)`。

### `StandardVertex`

- `position`, `normal`, `tangent`, `texCoord0`, `color`。

### `VertexLayout`

- 建構子 `(elements, stride)`。
- `elements`
- `stride`
- `CreateBuilder()`。

### `VertexLayoutBuilder`

- `Add(semantic, semanticIndex, sizeInBytes)`：自動計 offset。
- `Build()`：產生 `VertexLayout`。

### `Mesh`

- `name`
- `bounds`
- `vertexLayout`
- `surfaces`
- `vertexCount`
- `indexCount`
- `SetVertices<TVertex>(ReadOnlySpan<TVertex>)`
- `SetIndices(ReadOnlySpan<uint>)`
- `SetSurface(surfaceIndex, MeshSurface)`

使用案例：

```csharp
var mesh = new Mesh();
mesh.name = "LineMesh";
mesh.SetVertices<int>(stackalloc int[] { 0, 1, 2, 3 });
mesh.SetIndices(stackalloc uint[] { 0, 1, 2, 2, 3, 0 });
mesh.SetSurface(0, new MeshSurface(0, 6, 0, MeshTopology.Triangles));
```

### `MeshBuilder`

- `SetVertices<TVertex>(...)`
- `SetSemanticStream<T>(semantic, semanticIndex, values)`
- `SetSemanticLayout(layout)`
- `SetIndices(indices)`
- `AddSurface(surface)`
- `Build(name?)`

案例（最常用）：

```csharp
var mesh = new MeshBuilder()
    .SetVertices<StandardVertex>(verts)
    .SetIndices(idxs)
    .AddSurface(new MeshSurface(0, idxs.Length, 0, MeshTopology.Triangles))
    .Build("GeneratedMesh");
```

---

## 3.6 Materials 板塊

### `MaterialSurfaceType`

- `Opaque`, `Transparent`。

### `MaterialBlendMode`

- `Alpha`, `Additive`, `Multiply`。

### `MaterialCullMode`

- `None`, `Front`, `Back`。

### `MaterialDepthMode`

- `Disabled`, `ReadWrite`, `ReadOnly`。

### `MaterialPassKind`

- `Forward`, `ShadowCaster`, `DepthOnly`, `Unlit`（語義 enum）。

### `Material`

- `name`
- `surfaceType`
- `blendMode`
- `cullMode`
- `depthMode`
- `castShadows`
- `receiveShadows`
- `keywords`
- `overrides`

### `MaterialKeywords`

- `values`
- `Contains(keyword)`
- `Enable(keyword)`
- `Disable(keyword)`
- 例：

```csharp
material.keywords.Enable("USE_FOG");
if (material.keywords.Contains("USE_FOG")) { /* ... */ }
material.keywords.Disable("USE_FOG");
```

### `MaterialPropertyBlock`

- `SetFloat/SetInt/SetBool/SetVector2/SetVector3/SetVector4/SetColor/SetTexture`
- `TryGetFloat/TryGetInt/TryGetBool/TryGetVector2/TryGetVector3/TryGetVector4/TryGetColor/TryGetTexture`

案例：

```csharp
var block = new MaterialPropertyBlock();
block.SetColor("_Tint", Color.RED);
block.SetFloat("_RoughnessBias", -0.1f);
block.SetTexture("_MainTex", texture2D);
if (block.TryGetColor("_Tint", out var tint)) { /* use tint */ }
```

### 內建材質族

#### `StandardMaterial`

- `baseColor`
- `baseMap`
- `metallic`
- `roughness`
- `metallicRoughnessMap`
- `normalMap`
- `normalScale`
- `occlusionMap`
- `occlusionStrength`
- `emissiveColor`
- `emissiveMap`
- `alphaCutoff`
- `doubleSided`

案例：

```csharp
var m = new StandardMaterial
{
    baseColor = Color.WHITE,
    metallic = 0.2f,
    roughness = 0.7f,
    normalScale = 1.0f,
    alphaCutoff = 0.5f,
    doubleSided = false
};
```

#### `UnlitMaterial`

- `color`
- `colorMap`
- `opacity`

#### `SpriteMaterial`

- `tint`
- `spriteTexture`
- `pixelSnap`

#### `SkyboxMaterial`

- `skyTexture`
- `exposure`

#### `CustomMaterial`

- `shaderName`
- `properties`
- 何時用：自訂 shader 與自訂 uniform 參數。

---

## 3.7 Textures / Targets 板塊

### `TextureFormat`

- `Unknown`, `Rgba8`, `Rgba16Float`, `Depth24Stencil8`, `Depth32`。

### `TextureWrapMode`

- `Repeat`, `Clamp`, `Mirror`。

### `TextureFilterMode`

- `Nearest`, `Bilinear`, `Trilinear`。

### `TextureSampler`

- `wrapU`, `wrapV`, `filter`。
- 例：`new TextureSampler { wrapU = TextureWrapMode.Clamp, filter = TextureFilterMode.Bilinear }`

### `Texture`（abstract）

- `width`
- `height`
- `format`

### `Texture2D`

- `Texture2D(width, height, format)`。

### `TextureCube`

- `TextureCube(size, format)`（寬高相同）。

### `RenderTexture`

- `RenderTexture(width, height, format, hasDepth, hasMipmaps)`
- `hasDepth`
- `hasMipmaps`

### `RenderTargetFormat`

- `Rgba8`, `Rgba16Float`, `Depth24Stencil8`, `Depth32`。

### `RenderTargetSize`

- `(width, height)`。

### `RenderTargetDescriptor`

- `size`
- `colorFormat`
- `hasDepth`
- `hasMipmaps`

### `RenderWindow`

- `nativeHandle`
- `width`
- `height`

### `RenderTarget`（abstract）

- `width`
- `height`
- `colorTexture`
- `depthTexture`
- `Backbuffer(RenderWindow window)`
- `Texture2D(int width, int height, RenderTargetFormat format)`

### `BackbufferTarget`

- `window`
- `width/height` 來自 window。
- `colorTexture/depthTexture` 為 `null`（由平台 backbuffer 提供）。

### `TextureRenderTarget`

- `descriptor`
- `colorTexture`
- `depthTexture`（當 `hasDepth = true` 時建立）

案例（離屏渲染）：

```csharp
var offscreen = new TextureRenderTarget(new RenderTargetDescriptor
{
    size = new RenderTargetSize(1024, 1024),
    colorFormat = RenderTargetFormat.Rgba16Float,
    hasDepth = true,
    hasMipmaps = true
});
```

---

## 3.8 Lights 板塊

### `LightShadowSettings`

- `enabled`, `resolution`, `bias`
- `default`：`(true, 2048, 0.0005f)`。

### `Light`（abstract）

- `color`
- `intensity`
- `enabled`
- `shadows`

### `DirectionalLight`

- `direction`

### `PointLight`

- `position`
- `range`

### `SpotLight`

- `position`
- `direction`
- `range`
- `innerAngle`
- `outerAngle`

### `LightCollection`

- `items`
- `Add` / `Remove` / `Clear`

案例：

```csharp
scene.lights.Add(new SpotLight
{
    position = new Vector3(0, 4, 0),
    direction = Vector3.DOWN,
    range = 20f,
    innerAngle = 20f,
    outerAngle = 30f,
    shadows = new LightShadowSettings(true, 1024, 0.001f)
});
```

---

## 3.9 PostProcessing 板塊

### `PostProcessEffect`（abstract）

- `enabled`

### `BloomEffect`

- `threshold`
- `intensity`

### `ToneMappingEffect`

- `exposure`

### `ColorGradingEffect`

- `saturation`
- `contrast`

### `FxaaEffect`

- `qualitySubpix`

### `VignetteEffect`

- `intensity`
- `smoothness`

### `PostProcessStack`

- `effects`
- `Add` / `Remove` / `Clear`

案例：

```csharp
var stack = new PostProcessStack();
stack.Add(new BloomEffect { threshold = 1.1f, intensity = 0.6f });
stack.Add(new ToneMappingEffect { exposure = 1.15f });
stack.Add(new VignetteEffect { intensity = 0.2f, smoothness = 0.55f });
```

---

## 3.10 其他

### `RenderCommandQueue`

- 目前為 placeholder class（未實作命令記錄 API）。
- 何時會互相需要：未來可能作為高階命令緩衝，讓 `RenderSystem` 或 pipeline 使用。

## 4. 模組互相需要的時機（關係圖式說明）

1. `RenderSystem` 一定需要：
   - `RenderPipeline`（決定 pass 流程）
   - `RenderRequest`（scene + view + target）
2. `RenderScene` 與 `RenderView` 互補：
   - Scene 提供內容，View 提供觀察與清除策略。
3. `MeshRenderable` 必定同時依賴：
   - `Mesh`（幾何）
   - `Material`（外觀）
   - `Transform`（位置）
4. 光照結果通常同時依賴：
   - `Light`（照明參數）
   - `Material`（受光模型）
   - `SceneRenderSettings.enableShadows` 與各 `shadowMode/shadows`。
5. 後處理通常依賴：
   - `RenderView.enablePostProcessing`
   - `ForwardPipeline` 的 `enablePostProcessing`
   - `PostProcessStack`（效果參數來源，現階段屬描述層）

## 5. 內部執行框架（理解行為必看）

內部（`Internal/RenderQueue.cs`）目前關鍵邏輯：

1. `RenderList.Build(filter)` 會遍歷 `scene.renderables.items`。
2. `visibility != Visible` 直接排除。
3. Transparent 判定：目前只把 `MeshRenderable` 且 `material.surfaceType == Transparent` 視為透明。
4. sort key 使用 `int.MaxValue - sortingOrder` 做排序。

內部資源快取（`Internal/CompiledResources.cs`）：

- `RenderResourceCache` 以字典快取 `Mesh/Material/Texture/RenderTarget` 的 compiled 物件。
- `MaterialCompiler` 會從 `MaterialSurfaceType + BlendMode + CullMode` 產生 `ShaderPermutationKey`。

## 6. 實戰建議與注意事項

1. `SkyboxPass/GizmoPass/UiPass/PostProcessPass` 目前是空實作，開啟 feature 不代表已有最終 GPU 行為。
2. `Mesh.SetVertices<T>()` 目前 layout 推導較簡化，預設推成單一 `Position` semantic（stride 為 `T` 大小）。
3. `MeshBuilder.SetSemanticLayout/SetSemanticStream` 現階段偏描述用途，建置時仍有簡化路徑。
4. 若你做真實產品化，建議先明確補齊：
   - pass 的實際 draw 路徑
   - semantic layout 到 GPU vertex input 的完整映射
   - drawCalls 等統計欄位的實際更新

## 7. 快速對照：最常見「我該用哪個 API？」

- 我要渲染一幀：`RenderSystem.Render(...)`
- 我要切換渲染流程：`ForwardPipelineBuilder` / `PipelineFeatureSet`
- 我要新增一個可見物件：`RenderScene.Add(new MeshRenderable { ... })`
- 我要改鏡頭：`RenderView.ForCamera(...)` + `PerspectiveCamera`/`OrthographicCamera`
- 我要做每物件參數覆寫：`MaterialPropertyBlock`
- 我要離屏渲染：`TextureRenderTarget` 或 `RenderTarget.Texture2D(...)`
- 我要加光：`DirectionalLight` / `PointLight` / `SpotLight`
- 我要設後處理參數：`PostProcessStack` + 各 `PostProcessEffect`

