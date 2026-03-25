# Inno.Rendering Architecture

This document describes the renderer composition and extension model after the runtime decoupling refactor.

## 1. Layered Model

### Authoring Layer
- `RenderScene`, `Renderable`, `Material`, `Camera`, `RenderView`, `RenderTarget`
- This is the artist/gameplay-facing API.

### Frame Composition Layer
- `RenderPipeline`, `RenderPass`, `RenderFeature`
- `ForwardPipelineBuilder` composes passes through `IForwardPassProvider`.
- `RenderPassGraphCompiler` compiles dependencies from pass resource read/write declarations.

### Runtime Execution Layer
- `GraphicsRenderRuntime` is the facade/orchestrator.
- `ScenePassExecutor` executes scene pass draw submission.
- `ShadowPassExecutor` executes shadow caster rendering.
- `GpuResourceRegistry` owns mesh/texture/render-target/resource-set caches.
- `PipelineStateLibrary` owns shader/program/pipeline creation and caches.
- `GlobalParameterBinder` owns global light/camera/shadow uniform binding.
- `IMaterialParameterBinder` binds material/property parameters.
- `IMaterialTextureResolver` resolves texture bindings.
- `IRuntimePassFeature` routes pass filters to runtime executors.

## 2. Pass Composition (Registry/Provider)

### Built-in flow
- `ForwardPipelineBuilder` loads built-in providers from `ForwardPipeline.CreateDefaultPassProviders()`.
- Providers contribute pass instances based on `PipelineFeatureSet`.
- `RenderFeature` remains supported and runs after providers.

### Extend pass composition
1. Implement `IForwardPassProvider`.
2. Use `ForwardPipelineBuilder.AddPassProvider(...)`.
3. Optionally mix with custom `RenderFeature`.

This replaces previous fixed toggle-if pass assembly with provider-driven contribution.

## 3. Render Classification (Classifier Registry)

- `RenderList` now delegates inclusion logic to `RenderItemClassifierRegistry`.
- Built-in behavior is assembled by `DefaultRenderItemClassifier` entries.

### Extend classification
1. Implement `IRenderItemClassifier`.
2. Register it in a custom `RenderItemClassifierRegistry`.
3. Inject the registry when constructing `RenderList` for your pipeline/runtime path.

This removes direct hard-coded filter/type switch logic from `RenderList.Build`.

## 4. Material Binding Contract

### Material parameters
- `DefaultMaterialParameterBinder` uploads:
  - Base material render/shadow state
  - Built-in material fields (`StandardMaterial`, `UnlitMaterial`, `SpriteMaterial`, `SkyboxMaterial`)
  - Property block values from `Material.overrides`, `CustomMaterial.properties`, and `MeshRenderable.materialOverrides`

### Property block support
- `MaterialPropertyBlock.EnumerateProperties()` provides a generic enumeration API for binders.

### Texture binding
- `MaterialTextureResolverRegistry` resolves primary textures through registered `IMaterialTextureResolver` implementations.
- `DefaultMaterialTextureResolver` implements legacy compatibility keys (`_MainTex`, `_BaseMap`, etc.).

This establishes a full runtime route from material/property APIs to GPU-bound uniforms/textures.

## 5. Runtime Facade Split

`GraphicsRenderRuntime` keeps orchestration and caching, while pass execution policy is delegated:
- `ScenePassExecutor` (forward scene draws)
- `ShadowPassExecutor` (shadow map draws)
- `GpuResourceRegistry` (GPU resource lifecycle and cache)
- `PipelineStateLibrary` (pipeline and shader lifecycle/cache)
- `GlobalParameterBinder` (frame global uniforms)

This removes core draw-loop policy from the runtime facade and enables service-level replacement.

## 6. Render Graph Dependency Behavior

- `RenderPassGraphCompiler` now derives pass dependencies from resource access conflicts:
  - pass A writes/readwrites resource X
  - pass B reads/writes/readwrites resource X
  - then B depends on A when A appears earlier in pass event order

This replaces the previous strict linear chain scheduler.

## 7. Recommended Next Steps

1. Introduce public phase/tag model replacing `RenderItemFilter`.
2. Add pass culling and transient aliasing in render graph planning.
3. Add explicit shader contract metadata for robust per-pass material bindings.
