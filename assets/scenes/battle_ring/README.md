# 战斗场地机柜环：BattleRing

已接入游戏，见文末「接入游戏」。按只读 `reference/blocks.json` 建造全部 **123** 个 4×4m 机柜块。根对象 `BattleRing`；总计 **214,693 / 250,000** 三角面。实际三角面距场地中心的最小半径 **16.500999420m**，高度最大误差 **0.000001282m**。已从空场景一次跑完建造、空间审计、FBX 导出回导和三张战斗镜头预览。

## 文件与独立重跑

| 文件 | 用途 |
|---|---|
| `build_scene.py` | 默认一键执行 build → validate → export/roundtrip → render |
| `scene_geometry.py` | 从浅层复制的机柜立面、网格、风扇、假人及 World 函数；没有浅层目录导入 |
| `export_fbx.py` | 可独立打开 .blend，导出 FBX 并空场景回导 |
| `validate_scene.py` | 可独立核对圆盘禁区、每格边界、123 个块的高度 |
| `render_previews.py` / `preview_common.py` | 可独立渲染三张战斗机位；反壳描边不导出 |
| `battle_ring.blend` | 完整分层场景，默认相机是我方身后机位 |
| `export/battle_ring.fbx` | 根 `BattleRing`，场地局部坐标，无地面、角色或灯光 |
| `build_report.json` / `validation_report.json` | 面数、123 个逐块实测、8 个内角切口与空间检查 |
| `export/roundtrip_report.json` | 重新导入的包围盒、两个块的位置、材质分配及 FBX 元数据 |
| `preview/render_report.json` | 精确相机位置、目标、镜头焦距和人物比例 |
| `pipeline_report.json` / `delivery_manifest.json` | 命令、实测耗时、退出码、脚本与交付文件 SHA256、人工看图记录 |

在仓库根目录运行，Blender **5.2.3 LTS** / e13b728c6880：

```sh
cd "/Volumes/OpenClawSSD/System prompt"
"/Applications/Blender.app/Contents/MacOS/Blender" -b --factory-startup --python-exit-code 1 --python assets/scenes/battle_ring/build_scene.py
```

含进程启动的完整一键流程 **14.364s**，退出码 0。内部计时：建造 0.743s、实际网格审计 0.697s、导出回导 1.499s；预览时间见 preview/render_report.json。

```sh
# 只重建 .blend 并校验
"/Applications/Blender.app/Contents/MacOS/Blender" -b --factory-startup --python-exit-code 1 --python assets/scenes/battle_ring/build_scene.py -- --build-only
# 只重新导出、回导
"/Applications/Blender.app/Contents/MacOS/Blender" -b --factory-startup --python-exit-code 1 --python assets/scenes/battle_ring/build_scene.py -- --export-only
# 只重渲染
"/Applications/Blender.app/Contents/MacOS/Blender" -b --factory-startup --python-exit-code 1 --python assets/scenes/battle_ring/build_scene.py -- --render-only
```

三个辅助入口也可以直接使用同样的 factory startup 命令运行。单张预览：`render_previews.py -- --only battle_02_enemy_low`。

## 块、造型与空间

Blender 原点就是战斗场地对象原点，场地中心 `(0,3.4,0)`；格 (x,y) 中心 `(4x,3.4+4y,0)`，Blender X=Unity x、Y=Unity z。每块名义格 4×4m，全部几何留在自己的格子内；指定高度范围 **4.196–19.072m**。

采用浅层六种柜面：每 4m 宽六柜列、托盘、硬盘架、配线、把手、框柱、踢脚、顶檐和顶扇。朝中心的面使用完整细节，外侧减少柜列和托盘。相邻块内部不建面；高度不同时只补露出的外部封板，避免天空透进柜体。9m 以上的块在最上层缩到 90% 的占地并加退台肩部，最高点仍与输入高度一致。风扇单独为 `*_Fan`，原点位于自己的竖轴，不与静态柜体合并。

**16.5m 禁区优先：** JSON 按块中心筛选，因此八块的内角原本会进入圆盘。对 `(-3,-3)、(-4,-2)、(4,-2)、(4,2)、(-3,3)、(3,3)、(-2,4)、(2,4)` 用面向圆心的切线切角，并补带详细柜面的斜面。没有移动块中心、改高或把几何移出格子；因此这些格的足迹是保留外边界的切角方格，内侧不再是完整正方形。

```text
Layout / Ring_Racks / BattleRing 与逐块装配
Landmarks / Outer / Drift（结构预留，空）
Reference / Preview_Stage、相机、灯光、1.75m/1.62m 假人（不导出）
```

对象名编码格坐标，`xn03_yp04` 表示 (-3,+4)，例如 `BattleRack_xn03_yn09_N_L00`。完整场景保留每块装配父子；导出只保留根下一层的独立网格和 `assembly_parent` 元数据。主图有 1,655 个网格对象、589 份网格数据，完整常用立面与所有普通风扇为链接复制。

## 面数、材质与验证

| 部分 | 三角面 |
|---|---:|
| **机柜环合计** | **214,693 / 250,000** |
| fans | 4,674 |
| tops_and_shoulders | 3,209 |
| inner_chamfers | 6,643 |
| height_difference_closures | 1,529 |

上表主要件已包含在合计内。空间审计逐块核对所有顶点的格子边界，并计算**每个实际三角形在 XY 平面的最近点**到场地中心的距离，包含边和三角形内部，不只检查顶点。123/123 块齐全，格子越界 0，圆盘侵入 0；切角半径设为 16.501m，留出 1mm 数值余量；实测最小半径 16.500999420m，严格大于 16.5m。最大高度误差 0.000001282m，远小于 0.05m。无零面积三角面、n-gon、未应用修改器、UV 依赖。

导出只用四个游戏已有材质；预览假人额外用浅层已有的 `M_Reference`，不进入 FBX。没有新增材质名称。

| 名字 | sRGB | Emission Strength |
|---|---|---:|
| `M_Rack_Body` | `#1A2944` | 0 |
| `M_Rack_Trim` | `#0A1222` | 0 |
| `M_Rack_Shadow` | `#050A14` | 0 |
| `M_Rack_LED_Channel` | `#40F2D9` | 3.2 |
| `M_Reference` | `#A9B8CA` | 0 |

## FBX 坐标、原点和回导

1 BU = 1 m，METRIC，scale_length = 1；Blender +X 东、+Y 北、+Z 上。根 EMPTY 是平移零、旋转零、缩放一。

`export_fbx.py` 的 `SETTINGS` 原样复制浅层：

```python
axis_forward='-Z'
axis_up='Y'
apply_scale_options='FBX_SCALE_UNITS'
bake_space_transform=True
mesh_smooth_type='FACE'
object_types={'MESH', 'EMPTY'}
use_selection=True
add_leaf_bones=False
bake_anim=False
use_mesh_modifiers=True
path_mode='STRIP'
use_custom_props=True
```

暂存时只将 MESH 展平到导出根下，逐件执行 `matrix_world = Matrix.Rotation(pi, 4, 'Z') @ original_world_matrix`，导出后恢复父对象、世界矩阵及属性；不把暂存状态保存进 .blend。FBX 根保持单位变换，所有网格为根下一层，`assembly_parent` 保留原装配归属。Drift 也是根下一层的独立网格，适配 SeaScene 的逐件漂浮。

Unity 坐标映射为 **(x,y,z) = Blender (X,Z,Y)**。回到空白 Blender 的原始坐标应为 `Rz(180°) @ authored`，所以检查分两步：先比较原始回导与旋转后的预期，再逆旋转还原并比较原始 .blend 的包围盒、地标原点/中心。还核对网格数、三角面数、每个网格的材质槽及每种材质的三角面数量；最后解析 FBX 二进制的根变换、单位和轴系。UnitScaleFactor=100 表示每单位 100 cm，坐标没有放大 100 倍。

本次执行的是 Blender FBX 回导和 FBX 元数据核验；没有执行 Unity 导入或修改游戏。表中的 Unity 数值是按浅层已验证的映射换算的目标坐标。共享网格导出时 Blender 5.2 会打印材质索引警告；回导逐槽及逐材质面数比较均为零差异。


| 文件 | 回导网格 | 三角面 | 材质数 | 最大还原包围盒误差 m |
|---|---:|---:|---:|---:|
| `battle_ring.fbx` | 1,655 | 214,693 | 4 | 0.000002612 |

| 探针 | Blender 原点 | Blender 原始回导原点 | 目标 Unity 原点 |
|---|---|---|---|
| `BattleRack_xn03_yn09_Top` | (-12, -32.6, 5.819) | (12, 32.6, 5.819) | (-12, 5.819, -32.6) |
| `BattleRack_xp00_yn09_Top` | (0, -32.6, 16.065) | (0, 32.6, 16.065) | (0, 16.065, -32.6) |

两个探针都是相应块的顶面锚点：其 X/Y 表示块的输入中心，Z 表示 JSON 给定高度。FBX 根变换为单位变换；两个锚点、全环包围盒、网格/三角面计数及逐材质面数均一致。

## 预览与比例

三张均为 1600×900，EEVEE、暗蓝程序 World、反壳描边及法线边缘辅助。已逐张打开最终 PNG，核对外围饱满、退台及高低柜面闭合、没有露空顶板、战斗活动区留空。

| 图片 | 相机 Blender 坐标 | 看向 | 焦距 |
|---|---|---|---:|
| [preview/battle_01_party.png](preview/battle_01_party.png) | (0.6,−4.5,1.7) | (0,6.4,1.4) | 19mm |
| [preview/battle_02_enemy_low.png](preview/battle_02_enemy_low.png) | (1.5,3.0,0.5) | (0,7.0,2.6) | 14mm |
| [preview/battle_03_victory.png](preview/battle_03_victory.png) | (6,−9,7) | (0,3.4,2) | 21mm |

接入游戏后的 Unity 战斗镜头（总览、选人、敌人、胜利）拼在 [preview/unity_ingame.png](preview/unity_ingame.png)（1600×900）。

我方三人固定在 (−2.3,0)、(−0.3,−0.2)、(1.7,−0.4)，比例分别 1.75、1.62、1.75m；敌方假人 1.62m，在 (0,6.4)。敌人低机位回看不到身后的我方，因此该张额外在 (−1.2,7.0) 放 1.75m 标尺假人；只是 Reference 预览物，不进入 .blend 主模型或 FBX。14mm 广角保持指定相机位置/目标，同时容纳两个完整比例假人。

## 与参考的差别、剩余边界

保留 blocks.json 的 123 个中心及高度，采用浅层的详细机柜语言；外部高差面为简化封板，远景没有铺满高成本小零件。内侧八块切角是为同时满足输入格和 16.5m 禁区。预览 World、水面纹样、地面、假人、光照、描边不属于导出模型；游戏仍负责真实场地和战斗角色。

没有待修复的几何或回导失败。实际 Unity 战斗镜头下的雾、换材质、角色和 UI 遮挡尚由主会话接入时验证。本次没有改 game/、浅层场景或 reference/，没有 git 写操作或下载外部资产。89 份场景输入保持原始哈希；1,529 份只读工作区基线中发现 UI 的 `Transitions.cs` 在任务期间有独立变化，本任务未写入或回退。哈希复核及文件清单见 delivery_manifest.json。

## 接入游戏（2026-10-27，主会话）

`export/battle_ring.fbx` 复制到 `game/Assets/SystemPrompt/Resources/Scenes/BattleRing/`。`BattleStage.BuildRacks()` 不再用 `SeaArt.Walls` 生成机柜，改为 `SeaScene.Ring(transform)` 把模型放在战斗场地自身坐标下，`Update` 里让顶部风扇转动。四个材质按名字换成游戏的卡通着色，`M_Rack_LED_Channel` 只留发光；雾、海面和粒子沿用原来的战斗场地。

验证：完整 E2E（`game/E2E/20261027-094908/`）里所有战斗截图的背景都换成了模型机柜环，镜头总览、选人、斩击、集成、胜利都没有穿进机柜，日志 0 错误 0 警告。同机位截图拼在 [preview/unity_ingame.png](preview/unity_ingame.png)。
