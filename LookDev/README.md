# LookDev

标准化的效果展示工作区。LookDev 只负责提供可重复的相机、灯光、曝光、环境和测试几何，不承载具体效果的实现逻辑。

`Effects/` 保存效果实现与效果资源，`LookDev/` 保存跨效果复用的观察条件和校准资源。测试材质通过材质替换工具临时注入，不再为每个材质维护一份 Capture Profile。

## 目录职责

- `Scenes/`：可直接打开的标准展示场景。
- `Prefabs/`：环境球、灯光、校准物和运动目标等复用组件。
- `Calibration/`：Color Chart、参考材质、环境 Volume Profile 和校准网格。
- `Environment/`：LookDev 共用的 HDRI、天空盒和环境球材质。
- `Editor/`：场景重建和材质替换工具。
- `Runtime/`：运动驱动与可选的目标标记组件。

## 场景

- `Scenes/LookDev-RMTest.unity`
  - 5x5 Metallic/Roughness 参数校准场景；行是 Metallic，列是 Roughness。
- `Scenes/LookDev-ThicknessTest.unity`
  - 薄、中、厚片与厚度楔形，用于 Transmission、SSS 和体积边缘观察。
- `Scenes/LookDev-DepthTest.unity`
  - 地面、墙角、台阶和深度锚点，用于深度、法线、遮挡和层次关系。
- `Scenes/LookDev-ReflectTest.unity`
  - 地面、后墙、Reflection Card 和彩色对象，用于 SSR、SSPR 等反射效果。
- `Scenes/LookDev-MotionTest.unity`
  - 前景、中景、后景，以及静态目标和循环运动目标，用于 Motion Blur、景深和焦点关系。
- `Scenes/LookDev-ChartTest.unity`
  - 默认球、Color Chart、灰球、镜面球和粗糙球，用于颜色、曝光和材质响应校准。

所有场景共享固定的环境球、灯光、相机和曝光。每个场景只保留与测试职责有关的几何，避免通过运行时隐藏来制造“万能场景”。

## 材质替换

打开 Unity 菜单 `Tools/LookDev 对照`。工具通过测试类型下拉菜单直接选择对应 LookDev 场景，不提供任意 Renderer 或 SceneAsset 选择：

- `ChartTest`：固定替换默认球、灰球、镜面球和粗糙球；默认球直接引用测试材质资产，后三者使用副本分别写入金属度和粗糙度；颜色卡保持默认。
- `RMTest`：固定替换 5x5 网格，根据行列写入 25 组金属度/粗糙度。
- `ThicknessTest`：固定替换薄片、中片、厚片和厚度楔形。
- `DepthTest`：固定替换五级台阶、遮挡块和法线柱。
- `ReflectTest`：固定替换反射卡和被反射球。
- `MotionTest`：固定替换静态目标和循环运动目标。

使用步骤：

1. 在 `测试类型` 下拉菜单中选择通用材质、金属/粗糙度、厚度、深度、反射或运动测试。
2. 指定 `测试材质`，按需要调整通用材质测试的三组参数。
3. 点击 `打开并应用`，工具会自动打开对应场景并替换固定测试目标。
4. 点击 `恢复当前场景默认材质` 进行 A/B 对比。窗口不会自动保存场景。

`ChartTest` 和 `RMTest` 要求测试材质 Shader 提供金属度属性 `_Metallic`，以及粗糙度 `_Roughness` 或平滑度 `_Smoothness`/`_Glossiness`。缺少属性时工具会弹窗并阻止应用，避免生成没有可比性的结果。

## 重建

首次初始化或需要恢复默认布局时，打开 `Tools/LookDev 对照/重建标准场景`。该操作只重建当前六类标准场景和共享 Prefab，会覆盖这些默认资产的手动修改。

效果实现仍保留在各自的 `Effects/` 目录中；旧的 Preview、Carrier、VolumePreview、独立示例场景和 Capture Profile 截图流程已移除。
