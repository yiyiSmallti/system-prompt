《System Prompt》缓存海·浅层：Blender 场景与模块件

本目录交付可重建的 Blender 场景、36 个模块/地标/机关 FBX、整图 FBX、两个独立 P2 FBX 和 12 张已检查的预览。P0、P1 以及 P2 的外圈、漂浮碎片、墙脚杂物均已实施。建模这一轮没有接入 Unity，也没有修改游戏代码、输入参考图或字体；之后的游戏接入见文末「接入游戏」。

**文件与使用入口**

| 文件 | 用途 |
|---|---|
| `build_scene.py` | 从 factory startup 生成所有母本、布局、地标、物件、参考假人及 .blend |
| `export_fbx.py` | 独立打开 .blend 导出整图、Outer、Drift 和 36 个 kit；随后回导核验 |
| `render_previews.py` | 独立打开 .blend，用 EEVEE 重建预览灯光、反壳描边及机位并渲染 |
| `validate_scene.py` | 独立打开 .blend，核验格子覆盖、通道、高度、法线方向及几何完整性 |
| `cache_sea_shallows.blend` | 主场景，单位米；打开时以北向上的全图视图为默认视口 |
| `export/cache_sea_shallows.fbx` | 拼装整图，根对象 CacheSeaShallows；包含 P0/P1 和贴墙的 P2 杂物 |
| `export/cache_sea_shallows_outer.fbx` | P2 地图外六格范围内的机柜堆，独立根对象 |
| `export/cache_sea_shallows_drift.fbx` | P2 水中漂浮碎片，独立根对象 |
| `export/kit/*.fbx` | 每个母本一个 FBX，导出时根原点归零 |
| `build_report.json` | 逐件尺寸/三角面数、667 格归属、材质表和构建时间 |
| `validation_report.json` | 逐格及空间约束检查结果 |
| `export/roundtrip_report.json` | 整图和所有 kit 的回导、坐标、包围盒、单位及材质槽检查 |
| `preview/render_report.json` | 各张图的 EEVEE 渲染时间 |
| `preview/kit_contact_report.json` | 两张联系表的 36 件独立取景与比例记录 |
| `pipeline_report.json` | 四脚本按顺序运行的退出码、耗时和源脚本哈希 |
| `delivery_manifest.json` | 交付文件 SHA256、输入只读检查和本轮人工看图记录 |

**重建命令与实测时间**

在仓库根目录依次运行。四个脚本均可从 factory startup 独立运行；导出、渲染和检查会自行打开生成的 .blend。没有依赖正在运行的 Blender GUI、MCP、联网模型或外部贴图。

```sh
cd "/Volumes/OpenClawSSD/System prompt"
"/Applications/Blender.app/Contents/MacOS/Blender" -b --factory-startup --python-exit-code 1 --python assets/scenes/cache_sea_shallows/build_scene.py
"/Applications/Blender.app/Contents/MacOS/Blender" -b --factory-startup --python-exit-code 1 --python assets/scenes/cache_sea_shallows/validate_scene.py
"/Applications/Blender.app/Contents/MacOS/Blender" -b --factory-startup --python-exit-code 1 --python assets/scenes/cache_sea_shallows/export_fbx.py
"/Applications/Blender.app/Contents/MacOS/Blender" -b --factory-startup --python-exit-code 1 --python assets/scenes/cache_sea_shallows/render_previews.py
```

实测 Blender 5.2.3 LTS（e13b728c6880）：建模 1.717 s，几何审计 1.858 s，导出并回导 11.573 s，全部 12 张预览 87.374 s。这些是脚本内部计时，不包含 Blender 进程启动；建模计时截止保存 .blend 前。完整 build → validate → export → render 含启动共 108.754 s，四步退出码均为 0；顺序、逐步耗时和脚本 SHA256 见 pipeline_report.json。

单独重渲染示例：`render_previews.py -- --only kit_sheet,kit_sheet_props`；场景机位可用 `--only start_lighthouse,rack_canyon,topdown`；`--only` 的名称也可使用完整 PNG 文件名去掉扩展名。

**坐标、原点和拼装**

1 BU = 1 m；METRIC、scale_length = 1.0。Blender +X 向东、+Y 向北、+Z 向上，西南角 (0,0,0)，地图尺寸 116 × 92 m。格 (c,r) 的中心为 (4c+2, 4(22-r)+2, 0)，第 0 行在北边。数据直接读取 DungeonData.cs 的 cache_sea_shallows 段，并遵循 ZoneAt 的首次命中顺序；Hash 的乘法、加法、sin、取小数均使用 numpy.float32。

统一采用模块**名义占地的底面中心/地面锚点**为根原点，根局部坐标 (0,0,0)。4m 墙格是逻辑上的整格模块：立面在该格南侧 Y≈−2，原点仍在所属 4×4m 格的底面中心；它不是一块薄立面的实际包围盒中心。海床根原点为完成面中心 Z=0，砖缝向下 0.055m；沉船/礁石也以地面锚点归零，埋入地面的几何会有负 Z。灯塔的名义占地为 12×8m，手机为 12×12m。此约定保证各类件可直接放到格中心。

立面正面朝 Blender −Y，绕 Z 旋转 0/90/180/270° 分别朝南/东/北/西。标准层高 2.30m；顶层按目标高度真正裁剪三角面，再补顶檐与灯带，没有竖向压缩柜面。相邻 # 格不建立共享内部立面，不建立机柜底面；每个 # 保留顶面。完整层和常用地面共享网格，区域材质及裁切后的顶层另用共享缓存。

可动部分单独保留：`Lighthouse_Lamp` 的原点为灯中心；`*_Fan` 的 Z 轴是转轴；信标 `*_Lamp`、剧情 `*_Card`、宝箱 `*_Lid`、防火墙 `*_Barrier`、闸门 `*_Door` 均是独立网格。箱盖铰链位于宝箱局部 (0,0.35,0.56)。主角/同伴假人为 1.75m / 1.62m，留在 Reference。

**集合结构**

```text
Kit                         模块母本，放在地图东南侧，整图导出排除
Layout / <区域英文标签>     按八个 RectInt 分区，另有 UNZONED_CHANNEL
Landmarks                   Lighthouse、SunkenPhone
Props                       B/F/C/T/X/K/O/Y/R，以及海草和悬挂线缆
Outer                       地图外六格范围内的 P2 机柜堆
Drift                       6m 以上的 P2 碎片
Reference                   假人、相机、预览灯光；不导出
```

布局对象名包含格坐标，例如 `Rack_c03_r07_E_L00`、`Beacon_c14_r01_Lamp`。Layout、Landmarks、Props 的导出根为 `CacheSeaShallows`。渲染脚本会临时在 Reference 下创建 Preview_InvertedHull、编号和标题，不写回 .blend，也不进入 FBX。

**模块件清单**

下表尺寸为母本子网格的实际局部包围盒 X×Y×Z，含倒角/尾巴/嵌字，不等同于名义格占地；精确 min/max 在 build_report.json。所有根原点均按上文的统一地面锚点约定为 (0,0,0)。

| 件名 / kit FBX | 实际尺寸 m（X×Y×Z） | 三角面 | 根原点 |
|---|---:|---:|---|
| `RackFacade_V01` | 4.000 × 0.264 × 2.300 | 264 | (0,0,0) |
| `RackFacade_V02` | 4.000 × 0.264 × 2.300 | 248 | (0,0,0) |
| `RackFacade_V03` | 4.000 × 0.264 × 2.300 | 280 | (0,0,0) |
| `RackFacade_V04` | 4.000 × 0.264 × 2.300 | 246 | (0,0,0) |
| `RackFacade_V05` | 4.000 × 0.264 × 2.300 | 246 | (0,0,0) |
| `RackFacade_V06` | 4.000 × 0.264 × 2.300 | 246 | (0,0,0) |
| `RackTop` | 4.000 × 4.000 × 0.044 | 22 | (0,0,0) |
| `RackFan` | 0.898 × 0.932 × 0.100 | 38 | (0,0,0) |
| `Seabed_Tile` | 4.000 × 4.000 × 0.055 | 170 | (0,0,0) |
| `Seabed_Vent` | 4.000 × 4.000 × 0.055 | 180 | (0,0,0) |
| `LowRack` | 3.990 × 3.990 × 1.300 | 508 | (0,0,0) |
| `BubbleStack_1` | 3.880 × 3.938 × 5.340 | 612 | (0,0,0) |
| `BubbleStack_2` | 3.880 × 3.938 × 5.340 | 612 | (0,0,0) |
| `BubbleStack_3` | 3.880 × 3.938 × 5.340 | 612 | (0,0,0) |
| `BubbleLow` | 3.680 × 3.817 × 1.020 | 204 | (0,0,0) |
| `EmojiCoral_1` | 3.980 × 3.980 × 6.340 | 1,818 | (0,0,0) |
| `EmojiCoral_2` | 3.980 × 3.980 × 6.410 | 1,818 | (0,0,0) |
| `EmojiCoral_3` | 3.980 × 3.980 × 6.340 | 1,818 | (0,0,0) |
| `GraveTall` | 3.750 × 3.750 × 7.320 | 1,250 | (0,0,0) |
| `GraveLowPair` | 3.660 × 1.400 × 1.300 | 1,816 | (0,0,0) |
| `SunkenPhone` | 11.411 × 11.136 × 1.582 | 966 | (0,0,0) |
| `KelpEdge` | 0.585 × 2.590 × 2.444 | 210 | (0,0,0) |
| `CableHanging` | 0.881 × 0.978 × 8.789 | 392 | (0,0,0) |
| `Beacon` | 2.100 × 2.100 × 3.020 | 428 | (0,0,0) |
| `Fragment_Post` | 0.910 × 0.840 × 1.735 | 88 | (0,0,0) |
| `Fragment_Message` | 0.910 × 0.840 × 1.735 | 88 | (0,0,0) |
| `Fragment_Draft` | 0.910 × 0.840 × 1.735 | 98 | (0,0,0) |
| `DataChest` | 1.000 × 0.720 × 0.700 | 122 | (0,0,0) |
| `Terminal` | 1.100 × 0.820 × 1.370 | 93 | (0,0,0) |
| `ResetConsole` | 1.100 × 0.820 × 1.000 | 182 | (0,0,0) |
| `Firewall` | 4.000 × 0.900 × 4.000 | 306 | (0,0,0) |
| `Gate` | 4.000 × 0.900 × 4.000 | 110 | (0,0,0) |
| `CacheBlock` | 2.500 × 2.500 × 2.500 | 228 | (0,0,0) |
| `Socket` | 3.000 × 3.000 × 0.090 | 96 | (0,0,0) |
| `Lighthouse` | 11.351 × 9.459 × 36.820 | 13,922 | (0,0,0) |
| `WallFoot_Cables` | 2.463 × 0.129 × 0.150 | 52 | (0,0,0) |

六种立面的柜列顺序不同，包含 1U/2U 托盘、硬盘载架、配线面板与短线环、通风百叶、空挡板。每段 4m 宽容纳六个约 0.6m 的柜列；门缝、凸出的托盘、把手、立柱凹槽、踢脚、顶檐和 LED 块为几何。顶面另有单独的五叶风扇。

**整图、比例与预算**

| 部分 | 三角面数 |
|---|---:|
| 地图机柜、海床、区域替代墙和墙脚杂物 | 558,514 |
| 灯塔和沉没手机 | 14,888 |
| 机关、交互物和植被 | 6,629 |
| 主 FBX 合计（连 P2 墙脚杂物） | **580,031 / 600,000** |
| P2 Outer（独立） | 45,226 |
| P2 Drift（独立） | 2,916 |
| 地图加 Outer/Drift，不含 Kit/Reference | 628,173 |

主图有 2741 个网格对象，使用 361 份网格数据。301 个 # 中，262 格是机柜，18 格是气泡堆，19 格是珊瑚，2 格是高墓碑；每个非 # 格都有 4×4m 海床，共 366 格。37 个 = 中 9 格合并为手机，其余采用矮机柜、单层气泡或双墓碑。

单层立面最高 280 三角面（限制 1,500）；海床为 170/180（限制 300）；灯塔 13,922（限制 60,000）。信标和各类机关均远低于每件 5,000 的上限。

灯塔中心 (58,76)，离地 3m 以下的几何限制在 X 52–64、Y 72–80；以上部分只使用允许的 2m 外挑。七层柜塔各递增旋转 8°，有栏杆外廊、窗带、粗线缆、玻璃灯室及屋顶。灯心 (58,76,33.2)，顶端 Z=36.58m。预览不建立灯塔光束。

常规矮墙上沿最大 1.300m。手机几何只占九个指定 = 格，地上最高 Z=1.067m，低于 1.6m；机身含沉入地下的部分，所以实际 Z 包围盒厚度为 1.582m。

| 普通机柜区域 | 实测内区高度 m | 实测地图边缘高度 m |
|---|---:|---:|
| Channel | 5.630 – 8.170 | 10.348 – 14.130 |
| Coral | 3.493 – 4.327 | 6.787 – 10.537 |
| Graveyard | — | 8.000 – 11.797 |
| Plaza | 7.035 – 9.883 | — |
| Ridge | — | 11.109 – 14.742 |
| Canyon | 11.049 – 15.980 | 18.217 – 21.994 |

边缘格按 High+2+Hash×4，因此峡谷边界可高于 16m；气泡/珊瑚/高碑遵循 SeaDecor 的特殊造型，不套普通机柜高度。所有孤岛均由全地图 # 四连通分量是否触及外边界判定，非按单一区域判断。

**材质表**

交付 .blend 共 38 个材质。下表颜色为源 sRGB；JSON 另有发光强度。纯色、不使用位图、烘焙或 UV。机柜/海床有 EEVEE 的分段明暗预览节点；自发光物件保留 Principled Emission，后续 Unity 按材质名映射着色器。

| 材质 | sRGB | 发光强度 |
|---|---|---:|
| `M_Rack_Body` | `#1A2944` | 0 |
| `M_Rack_Trim` | `#0A1222` | 0 |
| `M_Rack_Shadow` | `#050A14` | 0 |
| `M_Rack_LED_Channel` | `#40F2D9` | 3.2 |
| `M_Seabed_Channel` | `#15294B` | 0 |
| `M_Rack_LED_Reef` | `#7BFFD2` | 3.2 |
| `M_Seabed_Reef` | `#183442` | 0 |
| `M_Rack_LED_Coral` | `#FF8AB4` | 3.2 |
| `M_Seabed_Coral` | `#2C2740` | 0 |
| `M_Rack_LED_Graveyard` | `#FF5A64` | 3.2 |
| `M_Seabed_Graveyard` | `#242237` | 0 |
| `M_Rack_LED_Ridge` | `#BFD8FF` | 3.2 |
| `M_Seabed_Ridge` | `#21304B` | 0 |
| `M_Rack_LED_Canyon` | `#5CE1FF` | 3.2 |
| `M_Seabed_Canyon` | `#14314B` | 0 |
| `M_Rack_LED_Plaza` | `#E8F0FF` | 3.2 |
| `M_Seabed_Plaza` | `#28354B` | 0 |
| `M_Rock` | `#172942` | 0 |
| `M_Glass` | `#A3DFFF` | 0 |
| `M_Bubble_White` | `#EEF3FF` | 0.09 |
| `M_Bubble_Green` | `#6EE08F` | 0.08 |
| `M_Bubble_Blue` | `#4A8BFF` | 0.12 |
| `M_Coral_Gold` | `#FFD23F` | 0.12 |
| `M_Coral_Pink` | `#FF6FA8` | 0.12 |
| `M_Coral_Orange` | `#FF8A3D` | 0.12 |
| `M_Coral_Cyan` | `#5CE1FF` | 0.12 |
| `M_Coral_Stem` | `#7A3A62` | 0 |
| `M_Coral_Mound` | `#5A2A4A` | 0 |
| `M_Grave` | `#4A5268` | 0 |
| `M_Phone_Body` | `#101A2B` | 0 |
| `M_Phone_Screen` | `#163C70` | 0.55 |
| `M_Emit_ColdWhite` | `#DCEBFF` | 5 |
| `M_Emit_Green` | `#4CFF99` | 3.2 |
| `M_Emit_Firewall` | `#FF455C` | 2.1 |
| `M_Emit_Amber` | `#FFB347` | 2.3 |
| `M_Emit_ChannelGrid` | `#3380FF` | 2.6 |
| `M_Reference` | `#A9B8CA` | 0 |
| `M_Drift_Glass` | `#7BCFFF` | 0.4 |

区域地面将 SeaDecor.Look.Floor 混入深蓝底色；机柜 LED 使用 Look.Rack。主航道地缝和通风格另用 #3380FF。气泡的白/绿/蓝使用 BubbleColours 的主色，珊瑚使用四组 CoralColours。所有中文已由仓库 NotoSansSC-Bold.ttf 转为三角化轮廓网格；.blend 和 FBX 不依赖该字体文件，也没有外部贴图引用。

**FBX 坐标映射、单位和回导（第二轮修正）**

```python
axis_forward="-Z"
axis_up="Y"
apply_scale_options="FBX_SCALE_UNITS"
bake_space_transform=True
mesh_smooth_type="FACE"
object_types={"MESH", "EMPTY"}
bake_anim=False
```

场景 FBX（主图、Outer、Drift）和本地 kit 使用不同的导出暂存变换，不能把两者的映射混用。场景在临时展平时对每个 MESH 执行 `matrix_world = Matrix.Rotation(pi, 4, "Z") @ original_world_matrix`；根 EMPTY 保持单位变换。导出后恢复原父对象、世界矩阵和临时属性，不保存回 .blend。

修正后的**场景映射为 Unity (X,Y,Z) = Blender (X,Z,Y)**，所以灯心为 Unity (58,33.2,76)，维护终端为 (94,0,22)。本目录当前的 `export/cache_sea_shallows.fbx`、`cache_sea_shallows_outer.fbx` 和 `kit/RackFacade_V01.fbx`，已由主会话在临时 Unity 工程里（**6000.0.85f1、默认 ModelImporter、batchmode**）导入复核，没有写进 `game/`。结果如下：

- `Instantiate(prefab)` 和 `Instantiate(prefab, Vector3.zero, Quaternion.identity)` 的结果相同，场景预制体根节点的 `localRotation` 都是 (0,0,0)。
- 主图包围盒为 X −0.09…116.09、Z −0.09…92.09。
- `Lighthouse_Lamp` (58,33.2,76)、`Terminal_c23_r17_Mesh` (94,0,22)、`CacheBlock_c20_r12_Mesh` (82,0,42)、`Gate_c23_r16_Door` (94,0,26)、`Beacon_c26_r11_Lamp` (106,2.35,46) 都和 `DungeonMap.World()` 一致。
- `Rack_c24_r10_S_L00` 的包围盒中心是 Z 48.05，落在所属格南沿。
- Outer 的包围盒为 X/Z −24…140 / −24…116。
- kit 立面在单位旋转下位于 Z +1.82…+2.08，正面朝 +Z。

代表性子网格（Lighthouse_Lamp、Terminal）的 `localRotation` 是 (0,180,0)，这是预期结果，不应清零。带原始方向旋转的立面和海草继续保留自身朝向。

上一轮仅使用 `axis_forward="Z"`、不预旋转的做法，在默认 Unity 预制体实例化时实际得到 (−Bx,Bz,−By)，上一轮 README 对它的正向映射结论是错误的。只把 forward 改为 −Z 也不足以修正场景，必须同时施加上述世界原点的 180° 暂存旋转。

36 个 **kit FBX 使用 −Z/Y，但不加 180° 暂存旋转**。kit 的本地映射为 Unity (−Bx,Bz,−By)，与仓库角色约定一致。立面正面 Blender −Y 变成 Unity forward +Z，原点仍按名义格中心放置；实例可先放到目标 Unity 格中心，再按下表设置根对象 Y 旋转。

| 立面朝向 | Unity 根 Y 旋转 | Unity 正面向量 |
|---|---:|---|
| 北 | 0° | (0,0,+1) |
| 东 | 90° | (+1,0,0) |
| 南 | 180° | (0,0,−1) |
| 西 | 270° | (−1,0,0) |

推导：Unity 绕 +Y 从上往下看为顺时针，初始正面向量 +Z 经 θ 旋转为 `(sin θ,0,cos θ)`，依次为北、东、南、西。这个表也意味着立面几何会落到所属格的对应边上；例如南向立面应使用 180°，不会只改变门面朝向却把几何留在格北沿。推导和向量断言同时保存在 export_fbx.py 与 roundtrip_report.json。

Blender 5.2 的 bake_space_transform 对多层 EMPTY 父子存在位置偏移，因此导出仍保留临时展平。所有场景 FBX 为根下一层的独立部件，并保留 `assembly_parent` 属性；完整父子层级保存在 .blend。FBX UnitScaleFactor=100 表示每导出单位 100cm，即 1m，实际坐标没有放大 100 倍。

**回导同时比较两套坐标，没有删除原来的检查。** 场景 FBX 回到空白 Blender 后应整体为 `Rz(180°) @ authored`，先直接比较旋转后的原始预期，再用逆旋转还原后与 .blend 比较；kit 不旋转，直接比较。三份场景和 36 个 kit 都核对包围盒、网格数量、面数和每个对象的材质槽，主图另核对七个地标/部件原点、包围盒中心和机柜立面法线。导出暂存结束后还检查原场景坐标已恢复。

| 场景文件 | 回导网格 | 三角面 | 还原后最大包围盒误差 m |
|---|---:|---:|---:|
| `cache_sea_shallows.fbx` | 2741 | 580,031 | 0.00000763 |
| `cache_sea_shallows_outer.fbx` | 341 | 45,226 | 0.00000763 |
| `cache_sea_shallows_drift.fbx` | 48 | 2,916 | 0.00000572 |

主图回导恢复后的最大包围盒误差为 **0.00000763m（0.0076mm）**。所有对象材质槽一致。36 个 kit 最大包围盒误差为 0.00000286m。FBX 根模型变换按显式属性及 FBX 默认值检查为平移零、旋转零、缩放一。

| 部件/采样 | Blender 原始坐标 | Blender 原始回导坐标 | 预期 Unity 坐标 |
|---|---|---|---|
| `Lighthouse_Lamp` 原点 | (58.0000, 76.0000, 33.2000) | (-58.0000, -76.0000, 33.2000) | (58.0000, 33.2000, 76.0000) |
| `Terminal_c23_r17_Mesh` 原点 | (94.0000, 22.0000, 0.0000) | (-94.0000, -22.0000, 0.0000) | (94.0000, 0.0000, 22.0000) |
| `Rack_c24_r10_S_L00` 包围盒中心 | (98.0000, 48.0478, 1.1500) | (-98.0000, -48.0478, 1.1500) | (98.0000, 1.1500, 48.0478) |

Unity 数据来源、引擎版本和两种实例化方式写在 `unity_validation`；本轮 Blender 的原始/还原测量值、单位和轴系写在 `scene_roundtrips`，两类证据明确区分。

**有效检查及预览**

validate_scene.py 覆盖 667/667 格，缺失几何 0，空间越界 0，高度误差 0，机柜立面朝向错误 0，零面积三角面 0，重复三角面 0，n-gon 0，未应用修改器 0，UV 层 0。对实际三角面先裁到 Z=0.15–2.5m，再核查 XY 足迹；普通墙允许的走道侧投影最大实测 0.08448m，低于 0.15m。所有机关、矮墙和手机的足迹均限制在对应格/九格之内。

海草复用 KelpEdge 西侧母本，在每格按指定边绕 Z 旋转。12 格全部通过 Z=0.15–2.5m 内的实际三角面足迹检查，足迹只落在相应边约 1m 的条带；其余三条边中央至少 2m 的入口保持空。两格峡谷线缆 (25,4)、(25,8) 保持原样，下端超过 2.7m。灯塔额外分段检查低于 3m 和允许外挑的高处足迹。以起点眼高 1.65m 对灯心射线，第一命中为 Z≈32.22m 的灯室结构，沿途没有被其他墙挡住。


| 海草格（列,行） | 贴边 | Z 旋转（母本原在西） |
|---|---|---:|
| (17,10) | 东 | 180° |
| (17,11) | 东 | 180° |
| (5,12) | 西 | 0° |
| (1,13) | 西 | 0° |
| (5,13) | 北 | -90° |
| (1,14) | 西 | 0° |
| (11,15) | 南 | 90° |
| (11,16) | 西 | 0° |
| (4,18) | 西 | 0° |
| (4,19) | 北 | -90° |
| (8,20) | 南 | 90° |
| (8,21) | 南 | 90° |

特别是 (11,15) 改贴南边，珊瑚林门口的东西向通行口保持打开；(17,10)、(17,11) 改贴东侧机柜。逐格实测范围见 validation_report.json 的 kelp_edge_checks。

预览采用 EEVEE、反壳描边与法线边缘辅助、分段明暗和少量辉光；水面纹理仅是 World 中的程序化预览背景，不作为 FBX 网格或贴图。全部透视相机高 2.4–2.9m，每张都有至少一个可见比例假人。本轮重渲染全部 12 张 PNG；新件表以六行审阅图逐格检查全部 36 件，含小型风扇、碎片、箱子、终端、插槽和墙脚杂物，每格均能辨认造型。

| 预览 | 内容 | 分辨率 |
|---|---|---|
| [preview/topdown.png](preview/topdown.png) | 北在上，0–28 列 / 0–22 行编号，可与布局图逐格核对 | 1800×1500 |
| [preview/persp_start_lighthouse.png](preview/persp_start_lighthouse.png) | 起点望向灯室，气泡礁作为前景 | 1600×900 |
| [preview/persp_sunken_phone.png](preview/persp_sunken_phone.png) | 主航道手机与无人回复的「在吗」 | 1600×900 |
| [preview/persp_lighthouse_plaza.png](preview/persp_lighthouse_plaza.png) | 灯塔七层机柜、礁石、灯室与广场 | 1600×900 |
| [preview/persp_emoji_coral.png](preview/persp_emoji_coral.png) | 四色几何表情、茎和土丘 | 1600×900 |
| [preview/persp_deleted_posts.png](preview/persp_deleted_posts.png) | 双面高碑和低碑阵列 | 1600×900 |
| [preview/persp_rack_canyon.png](preview/persp_rack_canyon.png) | 4m 走廊、堆叠机柜与人体比例 | 1600×900 |
| [preview/persp_maintenance.png](preview/persp_maintenance.png) | 缓存块、矮柜、信标与维护间 | 1600×900 |
| [preview/persp_maintenance_gate.png](preview/persp_maintenance_gate.png) | 嵌墙闸门及插槽边框 | 1600×900 |
| [preview/kit_sheet.png](preview/kit_sheet.png) | 18 个模块独立取景，每格有件名、尺寸、面数和同尺度 1.75m 假人 | 3200×2400 |
| [preview/kit_sheet_props.png](preview/kit_sheet_props.png) | 18 个物件/地标独立取景，使用同样的比例与标注 | 3200×2400 |
| [preview/kit_rack_detail.png](preview/kit_rack_detail.png) | 六立面、两海床和人体比例近景 | 1600×1000 |
| [preview/unity_ingame_compare.png](preview/unity_ingame_compare.png) | 接入游戏后在 Unity 里的同机位对比：左边原来的代码生成，右边模型 | 1600×2300 |


联系表固定同一 3/4 俯视方向，逐件按真实包围盒和一名未单独缩放的 1.75m 假人取景。渲染时仅把包围盒落到展示地面，不改变交付模型的原点和相对比例。每格 510×730px，正文与标注分区；两张各 6×3 格，顶部没有原件表的大块空白。深色柜体使用明亮的工作室地面、主光/补光和描边辅助。每格取景范围与 36 件实际尺寸/面数保存在 preview/kit_contact_report.json。中间单件渲染使用 /private/tmp 临时目录并自动删除。

**与参考的差别、已知边界**

地图格子、区域先后顺序、所有机关中心和灯塔中心均沿用游戏数据。相对概念图，实际广场保持游戏尺寸，灯塔收在北半部的 12×8m 底座内，r2 矮墙位于灯塔北侧；没有为了画面扩大走道或在塔前补一堵概念图式的矮墙。灯塔用七层有真实柜面细节的错角塔体与礁石构成，轮廓和细节密度按低面数游戏资产控制。

原游戏 GraveLowPair 高于本任务要求，已按任务缩到 1.3m；手机倾斜为 X=3°、Y=−1.8°，以服从九格足迹和低矮墙语义。文字采用独立的字体轮廓嵌字网格，表情采用眼嘴几何；均没有字体贴图或纹理烘焙。

Outer 是远景层次较简化的外立面，仍按六格外圈的 Hash/跳过规则摆放；Drift 是 48 个静态摆件，含透明玻璃材质的聊天泡、删帖卡片、表情球和像素块。墙脚杂物并入主 FBX，但始终贴着墙脚，未占通道中线。

FBX 会保留材质名和基础参数，Blender 的 toon 节点、World 水面、反壳预览、灯光和辉光不等于 Unity 的最终着色器。这些在接入游戏时按材质名换掉，见下一节。

**接入游戏（2026-10-27，主会话）**

整图、Outer、Drift 三个 FBX 复制到 `game/Assets/SystemPrompt/Resources/Scenes/CacheSeaShallows/`，由 `Editor/ModelAssetImporter.cs` 的 `Resources/Scenes/` 规则导入：只要网格，不导入动画、相机和灯光，网格设为可读以便静态合批。`DungeonData.cs` 里缓存海·浅层的 `Scene` 字段指向它们；没有 `Scene` 的区域仍由代码生成。

`Dungeon/SeaScene.cs` 负责摆放和换材质。模型以世界坐标原样放下，38 个材质按名字换成游戏的 `SystemPrompt/Toon` 卡通着色：机柜、礁石、墓碑和手机外壳会被玩家手里的灯照亮（Toon 新增的 `_Lamp`，效果同代码机柜的 Rack.shader）；边缘光只给气泡和珊瑚这类圆形物件，大平面上关掉，否则掠射角下会发白；LED 和自发光材质只留发光；`M_Glass` 和 `M_Drift_Glass` 改用游戏的叠加发光材质。262 个风扇绕竖轴转，12 丛海草从根部摆动，48 个漂浮物上下浮动并慢慢自转，其余静态合批。

海床、道具（信标、碎片、宝箱、终端、复位台）、防火墙和大门的栅栏、缓存块、插槽仍由代码生成，保留原来的动画和交互，模型里的同名部件在运行时隐藏；防火墙和大门的门框用模型的。灯塔的灯换成灯塔光色的发光材质，光晕和两道扫过海面的光束挂在模型灯心上。代码生成的机柜、珊瑚、气泡、墓碑、海草和沉没手机在这张图上不再生成；墓地的漂浮「[已删除]」、广场光环等区域装饰照旧。碰撞仍按地图格子判定，模型只负责画面。

Blender 5.2 的 FBX 导出器对共享网格会打印重复材质索引提示。本次已对所有回导网格逐一比较材质槽和面数，均一致；保留共享网格以减少重复数据。所有导出都是静态网格/必要根对象，没有相机、灯光、假人、预览标记或 Kit 混入整图。

工作区原有其它 `game/` UI、E2E 与转场文件改动。这些文件不属于本交付，未由场景脚本写入，也未被覆盖或回退。45 个本次读取并建立基线的参考/地图代码/着色器/字体文件 SHA256 均与开始时一致，具体清单见 delivery_manifest.json。

## 方块倒角（2026-10-28）

`Geo.box` 不再出直角长方体：最短边不小于 0.25m、且五个侧面都在（只可省底面）的盒子，自动按 `min(0.15, max(0.03, 最短边×0.06))` 倒角，每个面向里缩出倒角宽度，12 条棱补斜面、8 个角补三角面；更薄的板件和缺侧面的件不倒角。显式传 `bevel=` 可以覆盖。重建后整图 582,143 三角面（原 580,031），校验和三份 FBX 回导仍全部通过；预览图已按倒角后的场景重渲。
