# Inno.Core.Mathematics

[上一页：Logging](Inno.Core.Logging.md) · [Core 索引](README.md) · [下一页：Storage](Inno.Core.Storage.md)

Mathematics 提供引擎自有的 float/int 向量、四元数、4×4 矩阵、矩形、颜色与标量工具，并对热点点积/矩阵运算使用平台 SIMD。多数类型是可变 struct，公开分量字段采用 `x/y/z/w` 或 `m11...m44`。

## 坐标与矩阵约定

`Matrix` 使用 column-vector 语义：`v' = M * v`，`A * B` 表示先应用 B 再应用 A。内存字段按 row-major 命名，平移工厂写入最后一列 `m14/m24/m34`。

矩阵类型本身不绑定 handedness：

- `CreateLookAt` / `CreatePerspectiveFieldOfView`：left-handed。
- `CreateLookAtRH` / `CreatePerspectiveFieldOfViewRH`：right-handed。

```csharp
Matrix world = Matrix.CreateTranslation(position) *
               Matrix.CreateFromQuaternion(rotation) *
               Matrix.CreateScale(scale);
Vector3 worldPoint = Vector3.Transform(localPoint, world);
```

## MathHelper

| API | 说明 |
| --- | --- |
| `C_TOLERANCE` | 默认 float 相对容差 `1e-6f`。 |
| `AlmostEquals(a,b,tolerance)` | 相对容差比较。 |
| `Barycentric(...)` | 重心插值。 |
| `CatmullRom(...)` | Catmull–Rom spline 插值。 |
| `Distance(a,b)` | 标量绝对距离。 |
| `Hermite(...)` | Hermite 插值。 |
| `Lerp` / `LerpUnclamped` / `LerpPrecise` | 线性插值变体。 |
| `SmoothStep(...)` | 平滑插值。 |
| `ToDegrees` / `ToRadians` | 角度单位转换。 |
| `Clamp` / `Saturate` | 范围限制；Saturate 为 0..1。 |
| `IsFinite` | 排除 NaN 与 Infinity。 |
| `WrapAngle` | 将弧度归一到标准周期。 |
| `IsPowerOfTwo(int)` | 正整数 2 的幂检测。 |

## Vector2 与 Vector3

### Vector2

字段 `x/y`；常量 `ZERO`、`ONE`、`UNIT_X`、`UNIT_Y`。

- 长度：`Length()`、`LengthSquared()`、`normalized`、`NormalizeSafe(value, epsilon)`。
- 几何：`Dot`、`Angle`、`SignedAngle`、`Project`、`Reflect`。
- 插值/边界：`Lerp`、`Min`、`Max`。
- 变换：`Transform(Vector2, Matrix)`、`Transform(Vector2, Quaternion)`；Quaternion 路径使用完整 XY 投影旋转矩阵，纯 Z 旋转保持向量长度。
- 运算：`+`、`-`、一元 `-`、float `*`/`/`、近似 `==`/`!=`。
- 与 `System.Numerics.Vector2` 隐式互转；实现 `Equals`、`GetHashCode`、`ToString`。

### Vector3

字段 `x/y/z`；常量 `ZERO`、`ONE`、`UP`、`DOWN`、`LEFT`、`RIGHT`、`FORWARD`、`BACK`。

- 长度与安全归一化 API 同 Vector2。
- 几何：`Dot`、`Angle`、`SignedAngle(from,to,axis)`、`Project`、`Cross`、`Distance`、`Reflect`。
- `Lerp`。
- 变换：`Transform(position, Matrix)`、`TransformNormal(normal, Matrix)`、`Transform(value, Quaternion)`。
- 标准算术/比较 operator；与 `System.Numerics.Vector3` 隐式互转。

```csharp
Vector3 direction = Vector3.NormalizeSafe(target - origin);
float facing = Vector3.Dot(transformForward, direction);
Vector3 tangent = Vector3.Cross(Vector3.UP, direction);
```

## Vector4

字段 `x/y/z/w`；常量 `ZERO`、`ONE`、`UNIT_X/Y/Z/W`。

- `Length`、`LengthSquared`、`normalized`、`Dot`。
- `Lerp`、`Reflect`、`Transform(Vector4, Matrix)`、`ProjectToVector3()`。
- 标准算术；另有 `Matrix * Vector4`。
- 近似 equality；与 `System.Numerics.Vector4` 隐式互转。

## 整数向量

`Vector2Int`、`Vector3Int`、`Vector4Int` 分别公开整数 `x/y[/z/w]`：

- 都提供 `ZERO`、`ONE` 与单位/方向常量。
- 都支持 `+`、`-`、一元 `-`、int `*`/`/`、精确 equality、`Equals`、`GetHashCode`、`ToString`。
- 与对应 float vector 进行显式转换。
- `Vector4Int` 额外提供 `Dot`、`Lerp`、`Reflect`、`Transform(Matrix)`。

整数向量的 float → int 转换使用实现中的显式整数转换规则；涉及取整语义时应先在业务层明确处理。

## Quaternion

字段 `x/y/z/w`，单位旋转为 `identity`。

| 分类 | API |
| --- | --- |
| 范数 | `Length`, `LengthSquared`, `normalized`, `Normalize` |
| 基本运算 | `Conjugate`, `Inverse`, quaternion `operator *`, equality |
| 插值 | `Slerp(a,b,t)` |
| 构造 | `CreateFromAxisAngle`, `FromRotationMatrix`, `LookRotation`, `CreateFromYawPitchRoll` |
| Euler XYZ | `ToEulerAnglesXYZ`, `ToEulerAnglesXYZDegrees`, `FromEulerAnglesXYZ`, `FromEulerAnglesXYZDegrees` |
| Euler ZYX | `ToEulerAnglesZYX`, `ToEulerAnglesZYXDegrees`, `FromEulerAnglesZYX`, `FromEulerAnglesZYXDegrees` |
| 转换 | `ToMatrix()`；与 `System.Numerics.Quaternion` 隐式互转 |

`FromEulerAnglesXYZ` 与 `ToEulerAnglesXYZ` 使用同一个 `Rx * Ry * Rz` 约定，复合角度可以稳定往返；`ZYX` API 保持独立且不应与 XYZ 配对混用。

角度版本未带 `Degrees` 时使用弧度。

## Matrix

公开 16 个 float 字段：`m11`–`m14`、`m21`–`m24`、`m31`–`m34`、`m41`–`m44`，并提供 16 参数构造函数与 `identity`。

| 分类 | API |
| --- | --- |
| TRS | `CreateTranslation(float,float,float/Vector3)`, `CreateScale(float/xyz/Vector3)`, `CreateRotationX/Y/Z`, `CreateFromQuaternion` |
| 投影 | `CreatePerspectiveFieldOfView`, `CreatePerspectiveFieldOfViewRH`, `CreateOrthographic`, `CreateOrthographicOffCenter` |
| 相机 | `CreateLookAt`, `CreateLookAtRH` |
| 代数 | `Multiply`, `Determinant`, `Transpose`, `Invert`, `operator *` |
| 分解 | `Decompose(out scale, out rotation, out translation)`, `Extract2DTransform` |
| GPU 数据 | `CopyToColumnMajor(float[], startIndex)`, `ToColumnMajorArray()` |
| 互操作 | 与 `System.Numerics.Matrix4x4` 隐式互转 |
| 值语义 | `==`, `!=`, `Equals`, `GetHashCode`, `ToString` |

`CopyToColumnMajor` 会验证 destination 容量。向图形 API 上传前应根据后端约定选择它，而不是直接假定 struct 字段布局。

## Rect 与 RectInt

两者公开 `x/y/width/height`，派生属性 `left/right/top/bottom/min/max/size/center`。

- `Overlaps(other)`、`Contains(rect)`、`Contains(point components/vector)`。
- `FromMinMax(min,max)`、`Union(a,b)`、`TryIntersect(a,b,out intersection)`。
- `+`/`-` 按分量运算，支持 equality/value methods。
- `Rect` 与 `System.Numerics.Vector4` 隐式互转。
- `RectInt` 与 `System.Drawing.Rectangle`、`Vector4Int` 隐式互转。

## Color

字段 `r/g/b/a` 使用 0..1 float。构造 `Color(r,g,b,a=1)`；`FromBytes` 和 `ToBytes` 在 byte 通道间转换，`ToUInt32ARGB` 输出 packed ARGB。

内置颜色：`TRANSPARENT`, `WHITE`, `BLACK`, `RED`, `GREEN`, `BLUE`, `YELLOW`, `MAGENTA`, `CYAN`, `GRAY`, `LIGHTGRAY`, `DARKGRAY`, `ORANGE`, `PINK`, `PURPLE`, `BROWN`, `CORNFLOWERBLUE`。

`Color * float` 按通道缩放并 clamp；支持 equality/value methods。

## SimdMath

公开低层 helper：

- `Dot4(Vector128<float>, Vector128<float>)`
- `Dot2(ax,ay,bx,by)`
- `Dot3(ax,ay,az,bx,by,bz)`
- `Dot4(ax,ay,az,aw,bx,by,bz,bw)`

通常应优先使用 Vector API；只有需要避免构造临时向量的底层代码才直接调用这些方法。

## 数值注意事项

- float vector equality 使用容差，而 HashCode 使用原始分量；不要把近似相等的 float vector 依赖为严格 hash-key 等价关系。
- 零向量的 `normalized` 返回 ZERO；Quaternion 近零归一化/求逆返回 identity。
- `Lerp` 是否 clamp 取决于具体 API；需要外插时优先选明确的 Unclamped 版本或先检查实现。
