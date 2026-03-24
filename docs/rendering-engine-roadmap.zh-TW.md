# Inno Engine 渲染路線圖（對齊現代主流 / URP 級可擴展目標）

> 範圍：`Inno.Rendering`、`Inno.Graphics`、`Inno.Graphics.Bgfx`  
> 核心原則：先建立可擴展架構，再堆疊效果；每個里程碑都必須「可驗收、可回歸、可量測」。

## 1. 產品級目標（Definition of Done）

「完成」不是 pass 名稱存在，而是具備以下完整能力：

- Renderer 架構可擴展：可用 Feature/Pass 插拔，且不需改核心渲染迴圈即可擴展新效果。
- 光照與陰影是現代路線：Directional/Point/Spot + Shadow Map（含 CSM、Bias、PCF/PCSS 路線）。
- 材質系統可量產：PBR 主流程、穩定 shader variant 管理、MaterialPropertyBlock、全域/區域常量分層。
- 後處理是串接式管線：Tone Mapping、Color Grading、Bloom、TAA/FXAA 可按 Feature 組裝。
- 資源/同步閉環：FrameGraph/RenderGraph 管理 transient 資源、明確 lifetime、可做 async readback。
- 美術工作流友善：Shader metadata + Graph 對接點，支持未來可視化 shader 編程。

## 2. 現況盤點（截至 2026-03-24）

### 已有基礎

- `RenderSystem -> RenderPipeline -> RenderPass -> GraphicsRuntime` 主鏈可運作。
- bgfx 後端可執行多 pass、建立 RT、建立 shader/program、提交 draw。
- `.sc` shader + runtime compile 已接上 ToolRunner。
- 已開始落地真正 shadow map 路徑（不再是平面投影陰影）。

### 主要缺口（與 URP 級能力相比）

- 缺 RenderGraph/FrameGraph：目前 pass 依賴與資源調度仍偏手動，擴展成本高。
- 阴影系統未到產品級：CSM、穩定化、slope-scale bias、shadow debug 可視化未完整。
- 材質與 shader 生態未完整：變體治理、property metadata、graph 對接尚未形成規範。
- 後處理與 picking 雖有 pass 殼，但全鏈路與工程化驗收尚不足。

## 3. 架構藍圖（URP 對齊）

## 3.1 Renderer 層

- `RendererAsset`：序列化渲染器配置（Feature 列表、品質設定、平台覆蓋）。
- `Renderer`：執行管線，負責建立 `FrameContext`、調度 Feature。
- `RenderFeature`：可插拔功能模組（ShadowFeature、PostFXFeature、PickingFeature...）。
- `RenderPass`：具體 pass，聲明 input/output 資源與排序事件（BeforeOpaque/AfterSkybox...）。

## 3.2 資源與依賴層

- `RenderGraph`：聲明 pass 讀寫資源，負責 alias、生命週期、barrier、最小化 RT 分配。
- `RenderResourceRegistry`：統一管理 persistent/transient RT、buffer、readback buffer。

## 3.3 材質與 Shader 層

- `ShaderLibrary`：集中 include、函式庫、平台 profile 規則。
- `ShaderMetadata`：定義 properties、keywords、passes、resource binding。
- `MaterialCompiler`：從 Material + Keywords 產生變體 key，控制 program cache。
- `GraphBackend`（後續）：視覺化節點輸出到 `.sc` + metadata。

## 3.4 平台後端層

- `Inno.Graphics` 只保留跨後端抽象語義。
- `Inno.Graphics.Bgfx` 專注 bgfx 正確映射與性能調優（view id、state、uniform/sampler 生命周期）。

## 4. M 系列里程碑（修正版）

## M1：Renderer/Feature 基座（可擴展優先）

實作重點：

- 將現有 forward pipeline 重構為 `Renderer + Feature + PassEvent` 調度模式。
- 建立 `FrameContext`（camera、lights、quality、frame constants、debug flags）。
- 建立 pass 註冊機制，支持 feature 插拔與條件開關。

驗收：

- 在不改 `RenderSystem` 主循環下，可新增一個自定義 Feature 並插入指定 pass event。

## M2：現代陰影系統（先做對，再做快）

實作重點：

- Directional Shadow Map 升級為 `CSM(2~4 cascades)`。
- 實作穩定化（texel snapping）、深度偏移（constant + slope-scale bias）、PCF（再預留 PCSS）。
- 提供 `ShadowSettings`（atlas size、cascade count、split、bias、normal bias、strength）。
- 提供 debug overlay（cascade color、shadow map preview、bias heatmap）。

驗收：

- 角色/場景移動時陰影不明顯抖動；可用 UI 即時調 bias/strength/cascade 看效果變化。

## M3：PBR 與光照完整化

實作重點：

- PBR metallic-roughness 主鏈（baseColor/normal/metallic/roughness/AO/emissive）。
- IBL（diffuse irradiance + specular prefilter + BRDF LUT）。
- 多燈策略（Forward+ 或 clustered 為後續選項；先完成可擴展資料路徑）。

驗收：

- glTF 常見 PBR 材質在 demo 中可與參考渲染結果接近。

## M4：RenderGraph 與後處理

實作重點：

- 導入 RenderGraph 管理後處理鏈與中間 RT（tone mapping / bloom / color grading / TAA/FXAA）。
- Readback + fence 策略完善（包含 object picking）。

驗收：

- 啟用多個後處理效果時，資源分配可觀察到 alias 重用，且無明顯同步錯誤。

## M5：美術工作流與 Shader Graph 對接

實作重點：

- Shader metadata 規範化（可被編輯器讀取）。
- 材質 inspector 對應 metadata 自動生成。
- Graph to `.sc` 編譯流程（先 MVP：Unlit/Lit 節點集合）。

驗收：

- 不改 C# 核心程式即可新增/替換 shader graph 產物並在材質中使用。

## M6：工程化、性能、品質保證

實作重點：

- GPU/CPU profiler 標記、capture workflow、性能基準場景。
- 視覺回歸測試（golden image）與核心渲染單元測試。
- 平台矩陣驗證（macOS/Windows，至少 Metal + 一種非 Metal backend）。

驗收：

- 每次渲染核心改動可跑自動回歸；性能與視覺差異可量化。

## 5. 與 Unity URP 的對位說明

- 目前狀態：**有基礎，但尚未到 URP 級完整度**。
- 目標狀態：以 `RendererFeature + ScriptablePass + RenderGraph + PBR + CSM` 為對位能力。
- 差異策略：不抄 API 形狀，但保持同級擴展能力與工程可控性。

## 6. 近期執行策略（從現在開始）

1. 先完成 M2 的「可交付版本」：CSM 最少 2 cascade + bias/strength 可調 + debug overlay。
2. 接著補 M1 的調度抽象缺口（Feature/PassEvent），讓 M3/M4 能低摩擦接入。
3. 最後以 M3->M4->M5 逐步形成美術可用工作流，而不是一次性大改。

## 7. 驗收原則（必須遵守）

- 每個里程碑都提供：
  - 可運行 demo 場景
  - 可調參數
  - 可視化 debug 輔助
  - 驗收清單（預期結果 + 失敗診斷）
- 若無法在 demo 中穩定重現，即視為尚未完成。
