# 缓存海·深层：Blender 整图

已交付 P0、P1、P2：29×25 格的整图机柜、灯塔、五区造型、机房桥架、文件堆和海草，以及独立 Outer / Drift。主图 **535,134 / 650,000** 三角面，外圈 **44,478 / 130,000**，漂浮物 **1,326 / 10,000**。全部空间检查与三份 FBX 回导通过。之后已接入游戏，见文末「接入游戏」。

## 文件与入口

| 文件 | 用途 |
|---|---|
| `build_scene.py` | 从空场景读取当前游戏地图、按格建造并保存 .blend |
| `scene_geometry.py` | 复制浅层的纯色网格、六种机柜立面、风扇、灯塔、海草、假人和预览 World；无浅层目录运行时导入 |
| `export_fbx.py` | 独立打开 .blend 导出三个 FBX，并逐份清空场景回导核验 |
| `validate_scene.py` | 独立打开 .blend，用实际三角面审计空间及高度 |
| `render_previews.py` / `preview_common.py` | 独立打开 .blend，用 EEVEE、反壳描边、法线边缘及暗蓝 World 渲染 |
| `cache_sea_deep.blend` | 米制主场景，默认相机在入口，保留分层集合和装配父子 |
| `export/cache_sea_deep.fbx` | 根 `CacheSeaDeep`；只有 Layout + Landmarks |
| `export/cache_sea_deep_outer.fbx` | 根 `CacheSeaDeep_Outer`；地图外六格的机柜堆 |
| `export/cache_sea_deep_drift.fbx` | 根 `CacheSeaDeep_Drift`；56 个可逐件浮动的碎片 |
| `build_report.json` / `validation_report.json` | 逐格来源、三角面、材质、高度范围及空间检查 |
| `export/roundtrip_report.json` | 三份 FBX 的包围盒、地标、材质槽/面数及根变换证据 |
| `preview/render_report.json` | 镜头、人物位置、最终取景、分辨率和耗时 |
| `pipeline_report.json` | 空场景重建退出码、实际命令、耗时、脚本 SHA256 |
| `delivery_manifest.json` | 交付 SHA256、逐张看图记录和只读输入校验 |

## 重建命令与实测耗时

Blender **5.2.3 LTS**，e13b728c6880。所有建模、导出、渲染均为 headless；不依赖 GUI、MCP、外部资产、字体下载、贴图或烘焙。在仓库根目录运行：

```sh
cd "/Volumes/OpenClawSSD/System prompt"
"/Applications/Blender.app/Contents/MacOS/Blender" -b --factory-startup --python-exit-code 1 --python assets/scenes/cache_sea_deep/build_scene.py
"/Applications/Blender.app/Contents/MacOS/Blender" -b --factory-startup --python-exit-code 1 --python assets/scenes/cache_sea_deep/validate_scene.py
"/Applications/Blender.app/Contents/MacOS/Blender" -b --factory-startup --python-exit-code 1 --python assets/scenes/cache_sea_deep/export_fbx.py
"/Applications/Blender.app/Contents/MacOS/Blender" -b --factory-startup --python-exit-code 1 --python assets/scenes/cache_sea_deep/render_previews.py
```

| 阶段 | 含 Blender 启动的秒数 | 退出码 |
|---|---:|---:|
| `build_scene.py` | 3.923 | 0 |
| `validate_scene.py` | 2.875 | 0 |
| `export_fbx.py` | 4.753 | 0 |
| `render_previews.py` | 30.953 | 0 |

连续全流程共 **42.508s**。脚本内部建造 2.560s、几何审计 2.243s、导出并回导 4.171s。最终比例假人取景调整后，仅重跑预览，耗时 43.905s；源脚本最终哈希也记录在 pipeline_report.json。单独选图：`render_previews.py -- --only start_rift,white_hall,topdown`，也支持完整 PNG 文件名去掉后缀。

## 坐标、集合与游戏契约

地图西南角 (0,0)，范围 X=0–116、Y=0–100m。格 (c,r) 中心 `(4c+2, 4(24-r)+2, 0)`，第 0 行在北。读取当前 `DungeonData.cs` 的 `cache_sea_deep` Map 和按首次命中排序的五个 RectInt；Hash 每个阶段用 numpy.float32，算法与浅层一致。

```text
Layout / Channel, Rift, Servers, RecycleBin, Context, WhiteHall
Landmarks / Lighthouse_Structure, Lighthouse_Lamp, WhiteDoor_Frame
Outer / CacheSeaDeep_Outer
Drift / CacheSeaDeep_Drift -> 56 个独立网格
Reference / 假人、Preview_Stage、相机、灯光（不导出）
```

386 个 # 全部有机柜顶面；只建外露面，邻接 # 内部不建面。高出较矮邻柜的部分补外露封板，避免从低机位看到悬空顶板。完整立面、风扇、海草、屋顶和四组文件堆共享网格；顶部短层真正裁切，不拉伸柜面。主图共 3,233 个网格对象、1,101 份独立网格数据。

`*_Fan` 单独保留竖轴原点，`KelpEdge_cXX_rYY_Mesh` 地面锚定；`Lighthouse_Lamp` 原点就是灯心。没有导出 `Seabed_`、信标、碎片、宝箱、出口、光板、光源、噪声体、巡逻点、残骸或门板。没有会被 `SeaScene.CodeBuilt` 隐藏的网格。材质名全部与 SeaScene 的同名映射对应。海床及预览地缝只是 Reference 舞台。

## P0 / P1 / P2 实现

| 优先级 | 实际产物 |
|---|---|
| P0 | 全部 386 个 # 的分层机柜、15 个 = 的相应造型、浅层同几何灯塔；灯心 (58,−18,33.2) |
| P1 Rift | 收缩错位的柜列、剪切的柜面、缺失托盘、分支红缝、柜顶向内弯的断缆 |
| P1 Servers | r10/r12/r14/r16 各两段服务器行，两条长面均有完整细节；端面保留平整标牌带；两道开放桥架 |
| P1 Context | 紫色横向记录条、嵌墙气泡面板；走道上方 3.5–6.5m 无模型 |
| P1 WhiteHall | 白色主体/框/凹处、三格 1.3m 矮栏、两根粗柱与上方过梁；D 格整格低于 11.5m 保持空 |
| P1 RecycleBin | 12 格 = 的文件堆，含倾斜纸张、黄文件夹、纸团、倒桶；四格贴边发光海草 |
| P1 墙脚 | 仅贴墙的束线和硬件，全部收在墙格内 |
| P2 Outer | 339 个地图外堆，严格按六格/Hash/环数高度和灯塔 3.2 格排除规则 |
| P2 Drift | 56 个小卡、晶片和气泡碎片，完整足迹高于 6.5m，M/Q/D 上方再抬高 |

白门门框高 13m；过梁底 Z=11.55m。两柱在 c12/c16,r1 的 # 格内，不伸入第 2 行。门板预留盒 X=52–64、Y=92–92.8、Z=0–11.2m 无几何，D 三格在 Z=0–11.5m 也为空；后墙位于 r0，即 Y≥96m，因此画面可透过空门框看见后方机柜。接入游戏后门板覆盖前部视线。门框是独立地标，不参与普通机柜高度抽样。

桥架横跨 X≈37.5–78.5m，中心 Y=38/54m；开放横档下沿约 6.235m，走道上没有落地支柱。低于 6m 的支撑只在已有 # 柜顶。端面 Z=3.2–4.0m 是单一平面；下方和上方恢复柜面细节，为代码的行号牌留位置。代码实际识别的岛状机柜组件也有相同端面带，审计共 37 个端面，含指定八段行的两端。

## 尺寸与面数

| 部分 | 三角面 |
|---|---:|
| Layout：机柜、文件、海草、桥架、墙脚 | 519,052 |
| Landmarks：灯塔与门框 | 14,066 |
| **主 FBX 合计** | **533,118 / 650,000** |
| Outer | 44,478 / 130,000 |
| Drift | 1,326 / 10,000 |

| 主要件（已包含在主图中） | 三角面 |
|---|---:|
| lighthouse | 13,922 |
| white_door_frame | 144 |
| file_piles | 14,256 |
| kelp | 840 |
| cable_bridges | 1,524 |
| fans | 10,830 |
| low_white_rails | 1,524 |
| rift_damage | 5,370 |

灯塔沿用浅层名义底座 12×8m，七层错角柜塔，灯心高 33.2m、塔顶约 36.58m；几何包围盒厚度约 36.82m，包含少量埋入地面的礁石。机柜名义格 4×4m，标准层高 2.30m；白色矮栏上沿 1.300m，所有文件堆均低于 1.3m。

| 区域 | 实测内区机柜高度 m | 实测地图边缘高度 m |
|---|---:|---:|
| Channel | 5.6305–8.1848 | 10.2547–14.1805 |
| Rift | 9.7256–12.9180 | 16.2344–18.6445 |
| Servers | 4.6164–5.1883 | — |
| RecycleBin | 4.1406–6.9180 | 9.0000–12.9375 |
| Context | 7.0195–8.9375 | 11.3125–14.9941 |
| WhiteHall | 9.1406–11.8828 | 14.1094–17.7969 |

边缘格遵循 `High+2+Hash*4`，所以可以高于区域的内区范围；内区遵循 `Low+Hash*(High-Low)`。没有为满足内区区间而削低地图外边墙。

## 空间审计结果

`validate_scene.py` 从保存后的 .blend 读取实际网格，将三角面裁到目标 Z 高度段，再与格子矩形或门板盒求交；不只检查对象原点。725 格逐格比较，405 个 #/=/% 必建格完整覆盖。结果：

- 可走格 Z=0.15–2.5m，除分配边条中的海草外，侵入 **0**；墙面投影最大 0.000000m。
- 4 个 M、1 个 Q、3 个 D 的 0–11.5m 保留空间侵入 **0**；门板预留盒侵入 **0**。
- Context 可走格 Z=3.5–6.5m 侵入 **0**；桥架走道部分低于 6m 的侵入 **0**。
- 15 格 = 均留在自己的格内，最高 1.300000m；高度和格子边界超限 **0**。
- 37 个行端面标牌带最大平整度误差 0.000000m；机柜目标高度误差 **0 项**。
- 零面积、重复三角面、n-gon、未应用修改器、UV 依赖、错误立面法线均 **0**。灯塔、Outer、Drift 也参与侵入和网格完整性检查。

| 海草格 (c,r) | 贴边 | 该方向的相邻字符 | 中间保留 |
|---|---|---|---|
| (1,9) | W | # | ≥2m |
| (1,10) | W | # | ≥2m |
| (6,16) | E | % | ≥2m |
| (7,16) | E | # | ≥2m |

**输入布局例外：** (6,16) 四邻是普通可走格或另一格 %，没有相邻 #；它的 E 边朝向 c8 的最近机柜，和 (7,16) 保持同一朝向。未把这格海草跨格移到墙上，四格都通过独立边条与通道宽度检查。

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
| `cache_sea_deep.fbx` | 3,233 | 533,118 | 23 | 0.000007629 |
| `cache_sea_deep_outer.fbx` | 339 | 44,478 | 3 | 0.000007629 |
| `cache_sea_deep_drift.fbx` | 56 | 1,326 | 7 | 0.000003815 |

| 探针 | Blender 原点 | Blender 原始回导原点 | 目标 Unity 原点 |
|---|---|---|---|
| `Lighthouse_Lamp` | (58, -18, 33.2) | (-58, 18.00001, 33.2) | (58, 33.2, -18) |
| `WhiteDoor_Frame` | (58, 94.5, 0) | (-58.00001, -94.49999, 0) | (58, 0, 94.5) |
| `ServerRow_c10_r10_N_L00` | (42, 58, 0) | (-42, -58, 0) | (42, 0, 58) |

白门门框实际包围盒中心换算为 Unity **(58, 6.5, 94.45375)**；单独核对中心而非只核对其地面锚点。全部材质槽与逐材质三角面数量一致。

## 材质表

纯色，无贴图、UV 或烘焙；自发光保留 Principled Emission。以下包括 Reference 使用的同名浅层预览材质，主 FBX 实际仅 23 种，Outer 3 种，Drift 7 种。新增名称只使用任务给定的 12 个名称；没有额外新增游戏材质。

| 名字 | sRGB | Emission Strength |
|---|---|---:|
| `M_Rack_Body` | `#1A2944` | 0 |
| `M_Rack_Trim` | `#0A1222` | 0 |
| `M_Rack_Shadow` | `#050A14` | 0 |
| `M_Rack_LED_Channel` | `#40F2D9` | 3.2 |
| `M_Rack_LED_Rift` | `#FF6A4A` | 3.2 |
| `M_Rack_LED_Servers` | `#FFB347` | 3.2 |
| `M_Rack_LED_RecycleBin` | `#C2CCE0` | 3.2 |
| `M_Rack_LED_Context` | `#B596FF` | 1.45 |
| `M_Rack_LED_WhiteHall` | `#FFFFFF` | 3.2 |
| `M_White_Body` | `#DCE4F2` | 0 |
| `M_White_Trim` | `#B8C6E0` | 0 |
| `M_White_Shadow` | `#8A98B8` | 0 |
| `M_File_Sheet` | `#E8EEF8` | 0 |
| `M_File_Folder` | `#F2C14E` | 0 |
| `M_Bin` | `#5A6680` | 0 |
| `M_Rock` | `#172942` | 0 |
| `M_Emit_Crack` | `#FF5A3C` | 3.2 |
| `M_Emit_ColdWhite` | `#DCEBFF` | 5 |
| `M_Emit_Green` | `#4CFF99` | 3.2 |
| `M_Emit_Amber` | `#FFB347` | 2.3 |
| `M_Rack_LED_Plaza` | `#E8F0FF` | 3.2 |
| `M_Rack_LED_Ridge` | `#BFD8FF` | 3.2 |
| `M_Bubble_White` | `#EEF3FF` | 0.09 |
| `M_Bubble_Green` | `#6EE08F` | 0.08 |
| `M_Bubble_Blue` | `#4A8BFF` | 0.12 |
| `M_Glass` | `#A3DFFF` | 0 |
| `M_Drift_Glass` | `#7BCFFF` | 0.4 |
| `M_Reference` | `#A9B8CA` | 0 |
| `M_Emit_ChannelGrid` | `#3380FF` | 2.6 |

## 预览与看图记录

EEVEE、反壳描边与法线边缘、暗蓝 World；程序化水面只属于预览 World。透视图均采用 2.25–2.7m 高的相机，含真实 1.75m / 1.62m 比例假人。已逐张打开最终 PNG，检查分区、走道、门框、机柜、桥架、灯塔完整轮廓；原始图未缩成拼贴来代替审阅。

| 图片 | 内容 | 分辨率 |
|---|---|---|
| [preview/topdown.png](preview/topdown.png) | 北在上、列行编号、5 区边框；与 reference 布局逐格对照 | 1800×1600 |
| [preview/persp_start_rift.png](preview/persp_start_rift.png) | 起点、破损柜面、红缝、通往机房的入口 | 1600×900 |
| [preview/persp_servers_aisle.png](preview/persp_servers_aisle.png) | 4m 走道，两侧服务器托盘、硬盘架和 LED | 1600×900 |
| [preview/persp_servers_bridges.png](preview/persp_servers_bridges.png) | 开放梯架、真实横档、束线及仅落在柜顶的支撑 | 1600×900 |
| [preview/persp_recycle_files.png](preview/persp_recycle_files.png) | 白纸、黄文件夹、纸团、倾倒的小桶和贴边海草 | 1600×900 |
| [preview/persp_context.png](preview/persp_context.png) | 紫色横向记录长条、墙上气泡面板，聊天悬空区域留空 | 1600×900 |
| [preview/persp_white_hall.png](preview/persp_white_hall.png) | 白色机柜、粗门柱与过梁；门板空间留空，可见后方机柜墙 | 1600×900 |
| [preview/persp_outer_lighthouse.png](preview/persp_outer_lighthouse.png) | 完整灯塔、礁石底座、灯心和南边外圈 | 1600×900 |
| [preview/unity_ingame_compare.png](preview/unity_ingame_compare.png) | 接入游戏后在 Unity 里的同机位对比：左边原来的代码生成，右边模型 | 1600×2310 |

## 与概念图的差别和剩余边界

保留游戏的 4m 格和真实走道宽度，没有把概念图的长廊扩大。插画中的地面散纸收进 = 格；海草只在 % 的边条。机柜采用浅层同语言的分层纯色几何，细节密度受游戏面数预算控制；水面、焦散、辉光和灯光属于预览。聊天浮字、行号牌、出口光柱、地面红裂纹、光板、光源及门板由游戏代码生成，预览中不伪造这些成品演出。

已知输入例外只有 (6,16) 无邻接墙的海草放置，见上表。Unity 中的材质替换、动态门板、聊天气泡和角色实际遮挡尚需主会话接入时验证；本轮不把 Blender 回导当作 Unity 运行验证。本任务没有写入 game/、浅层目录或两个 reference/；89 份建模代码、浅层和参考文件的哈希与开始时一致。整个工作区另记录了 1,529 份只读基线，其中 UI 的 `Transitions.cs` 在检查期间出现独立变化；本任务没有写入或回退它，详情见交付清单。没有 git 写操作、网络资产下载或嵌套代理。

## 接入游戏（2026-10-27，主会话）

整图、Outer、Drift 三个 FBX 复制到 `game/Assets/SystemPrompt/Resources/Scenes/CacheSeaDeep/`，`DungeonData.cs` 里缓存海·深层的 `Scene` 指向它们，导入规则和摆放代码沿用浅层（`Editor/ModelAssetImporter.cs`、`Dungeon/SeaScene.cs`，见浅层 README 的「接入游戏」）。23 个材质都已在 SeaScene 的 Looks、Lights、Glass 表里：白门大厅的 `M_White_*` 比模型原色压暗一档，让门板保持全场最亮；回收站的文件用 SeaDecor 原来的纸白和文件夹黄；各区的 LED 和红裂缝只留发光。

白门门板和镜子、四块光板、光源、出口、道具和海床仍由代码生成；裂缝的地面红纹、机房行号牌、上下文回廊的聊天气泡、回收站的漂浮卡片和白门的出口光柱照旧由 `SeaDecor` 的区域装饰生成。代码生成的机柜、文件堆和海草在这张图上不再生成。灯塔的灯、光晕和光束挂在模型灯心 Unity (58, 33.2, −18)。

验证：临时编辑器脚本在同机位渲染了代码版和模型版（[preview/unity_ingame_compare.png](preview/unity_ingame_compare.png)），模型版深层构建约 1.3 秒；完整 E2E（`game/E2E/20261027-094908/`）从标题玩到序章结束全部通过，日志 0 错误 0 警告，深层截图 81–92 覆盖裂缝、机房、回廊偷袭、宝箱、光束、光源、白门镜子和出口。

## 方块倒角（2026-10-28）

`Geo.box` 不再出直角长方体：最短边不小于 0.25m、且五个侧面都在（只可省底面）的盒子，自动按 `min(0.15, max(0.03, 最短边×0.06))` 倒角，每个面向里缩出倒角宽度，12 条棱补斜面、8 个角补三角面；更薄的板件和缺侧面的件不倒角。显式传 `bevel=` 可以覆盖。重建后主图 535,134 三角面（原 533,118），校验和三份 FBX 回导仍全部通过；预览图已按倒角后的场景重渲。
