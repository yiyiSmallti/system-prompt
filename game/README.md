# System Prompt Unity 工程

这里是游戏本体。3D 校园还在制作，目前做好了：标题画面、陪伴生登记、序章剧情播放（立绘、表情、点击反应、回看）、手机（日程、通讯录、Coop、持有物）、存读档、设置、序章的教学战斗，以及序章里的两段潜空间 3D 探索（缓存海·浅层和深层）。界面全部用 UI Toolkit 在代码里搭，样式在一份 USS 里；潜空间的场景和噪声体由代码用几何体拼出，主角和 DeepSeek 是用 Tripo 生成的人形模型，都配三渲二着色器。没有场景和 Prefab 需要配置。

工程在 Unity 6000.0.85f1（macOS，Apple Silicon）上编译通过，并用[端到端验证](#端到端验证)从标题一路玩到序章结束。

## 打开与运行

1. 用 Unity Hub 打开 `game/` 文件夹，编辑器版本用 `ProjectSettings/ProjectVersion.txt` 里的 6000.0.85f1（Unity 6.0 LTS）。要构建 macOS 播放器还需要装 Mac Build Support 模块。
2. 依赖写在 `Packages/manifest.json`：Input System 1.20.0、uGUI 2.0.0（提供 TextMeshPro，Yarn Spinner 需要）、Yarn Spinner for Unity 3.2.8。Yarn Spinner 从 [OpenUPM](https://openupm.com) 的 scoped registry 下载，打开工程的机器需要能访问 `package.openupm.com`。没有改用 GitHub 的 git 依赖：在 macOS 27 上，Unity 包管理进程调用系统 git 会因 Command Line Tools 只有 arm64 而失败。
3. Player Settings 里 Active Input Handling 已经设为 Input System。代码同时支持新旧两套输入，改成 Both 也可以。
4. 打开 `Assets/SystemPrompt/Scenes/Boot.unity`（一个空场景），直接进入 Play。`GameBootstrap` 会在场景加载后自动创建相机、`GameRoot`、UI 和音频，场景里不需要放任何东西。

第一次导入时字体和立绘较大（三套 Noto Sans SC 各 10 MB），会等一会儿。`Editor/UiAssetImporter.cs` 会给 `Resources/Art` 下的图片关闭 Mipmap、改用高质量压缩，把 BGM 压成 Vorbis。`Editor/ModelAssetImporter.cs` 把 `Resources/Models` 下的 FBX 导入成人形骨骼、动作循环，不导入材质。

## 操作

| 操作 | 键盘 / 鼠标 | 手柄 |
| --- | --- | --- |
| 推进对话 / 确定 | Enter、Space、左键、滚轮下 | Ⓐ |
| 返回 / 菜单 | Esc、右键 | Ⓑ、Start |
| 选择 | 方向键、WASD | 十字键 |
| 自动播放 | Q | Ⓧ |
| 快进 | F 切换，按住 Ctrl | 按住 RB |
| 回看 | L、滚轮上 | LB |
| 手机 | Tab | Ⓨ |
| 隐藏界面 | H | — |
| 快速存档 / 读档 | F5 / F9 | — |
| 删除存档 | Delete | View |

潜空间探索里：

| 操作 | 键盘 / 鼠标 | 手柄 |
| --- | --- | --- |
| 移动 | WASD、方向键 | 左摇杆 |
| 冲刺 | 按住 Shift | 按下左摇杆、RT |
| 转视角 | 鼠标、Q / E | 右摇杆 |
| 调查 / 攻击 / 推 / 转动 | Enter、左键 | Ⓐ |
| 菜单 / 手机 / 回看 | Esc / Tab / L | Start / Ⓨ / LB |

探索时鼠标被锁定用来转视角，打开菜单或对话时解锁。

两张地图都分成几个区域，每个区域的海床颜色、墙和摆设不同，第一次进入时屏幕左侧打出区域名，有的区域 DeepSeek 会说一两句。浅层：气泡礁（起点）、主航道（沉在海底的巨大手机）、表情包珊瑚林、删帖墓地、灯塔背面、机柜峡谷、维护间、灯塔广场；从起点就能看见灯塔和两道防火墙，要绕灯塔一圈到维护终端，再穿过打开的防火墙回来。深层：裂缝、沉没机房，然后西边的回收站（近，有信标）和东边的长上下文回廊（远，有延时补丁）二选一，最后是白门大厅。矮墙挡路但不挡视线，也挡不住光。

噪声体在地图上巡逻，碰上就进入回合制战斗：走近按 Enter 攻击，没被发现时是先制攻击，背对着被追上的噪声体撞上是被偷袭。打倒的噪声体从地图上消失，信标回满 HP 和 SP，战斗失败回到标题。觉醒前主角一个人用截图战斗，DeepSeek 在灯塔入队后两人一起打。

两张地图各有一个机关。浅层维护间：站在缓存块后面按 Enter 往前推一格，两块都推进插槽后，橘色的门打开，门后是维护终端；推进死角时按复位台。深层白门大厅：灯塔的光从裂缝照进大厅，四块反射板每按一次转 45 度，把光引到门边的空地上，出口才出现。小地图上画着插槽、缓存块、反射板和光路。

剧情画面右上角是一排 P5 风格的图标徽章（自动、快进、回看、存档、读档、手机、设置、隐藏界面、菜单），独立于对话框，可以直接点。徽章歪斜错落，角上贴着对应按键（Q、F、L、Tab、H、Esc）；鼠标停在上面时徽章放大、图标变绿，下方弹出名称；自动和快进开启时徽章亮成绿色，手机有未读时变红。日程期限卡在图标下面。标题画面和手机资料页的立绘可以点头和身体，连点太多次会有专门的反应。标题画面上，三个人的视线会跟着鼠标走。标题上的对话气泡浮在所有立绘之上、三人头顶之间的天空处，尾巴指向说话的人；同一时间只有一个人说话，点了另一个人，前一个人的气泡收起、表情复原。

标题的游戏名是单行字标：白色 SYSTEM，后面是歪贴在绿色色块上的黑字 PROMPT，末尾一个闪烁的终端下划线光标。开场时 SYSTEM 从左滑入，PROMPT 色块随着重音砸下来。

## 端到端验证

```bash
game/Tools/e2e.sh
```

脚本先用 batchmode 构建一个测试版播放器，再启动它。测试版产品名是 System Prompt E2E，存档和设置与正式版分开，每次都从空存档开始。启动后，`Assets/SystemPrompt/E2E/PrologueRun.cs` 用虚拟键盘从标题玩到序章结束，沿途经过：

- 标题画面、立绘动态特写、依次点三人的头（核对只剩一个气泡），以及设置
- 陪伴生登记和序章剧情
- 手机、暂停菜单（从菜单进回看、进存档）、回看和快速存档
- 缓存海·浅层探索（DeepSeek 跟在身边）：从背后攻击礁石出口发呆的噪声体（先制攻击，第一场单人战斗）、点亮信标、珊瑚林、墓地和峡谷各打一场、读墓地的草稿碎片、在维护间推 11 次缓存块打开门（核对插槽全满）、接入终端解除防火墙、穿过两道防火墙走到灯塔，核对战斗场数和走过的区域数
- 教学战斗和 Coop 卡片
- 缓存海·深层探索：从背后攻击发呆的噪声体（先制攻击）、走东边的长上下文回廊读碎片、开宝箱（延时补丁让维护进度下降）、在白门大厅转动四块反射板把光引到出口（核对出口出现）、走到出口
- 回到标题后打开读档

运行期间真实鼠标会被屏蔽，只用虚拟键盘操作，免得桌面上停在窗口里的鼠标悬停改掉选中项。潜空间里按最短路径逐格按住移动键，遇到巡逻就转身攻击，战斗里一直按 Enter 用第一个指令打第一个目标。每个界面和演出都会截图，同时核对游戏状态（等级、道具、Coop、期限、通讯录），并检查日志里有没有报错。画面切换（开始游戏、换场景、进入战斗、战斗结束）、暂停菜单展开、从菜单进入回看和退回菜单、设置开机、存档快门和战斗结算录成胶片：一张图里 10 到 12 帧，按固定间隔从左到右、从上到下排列。标题立绘另录一张 20 帧的半尺寸脸部特写（间隔 0.1 秒），用来看呼吸、摆头、头发和眨眼。结果写到 `game/E2E/<时间>/`：报告是 `report.md`，旁边是截图、`build.log` 和 `player.log`。全部检查通过时退出码为 0。

运行期间会弹出一个 1600×900 的窗口，不要碰键盘鼠标。编辑器按 `ProjectVersion.txt` 的版本在 Unity Hub 的安装目录里找，找不到时用 `UNITY=/path/to/Unity game/Tools/e2e.sh` 指定。也可以在编辑器菜单 **System Prompt → 构建端到端测试版** 只做构建。驱动代码只编进编辑器和带 `SP_E2E` 定义的测试版，正式构建里没有。

## 目录

| 路径 | 内容 |
| --- | --- |
| `Assets/SystemPrompt/Scripts/Core/` | 启动、全局流程、输入、音频、存档、设置、游戏状态和静态数据（角色、场景、道具、期限） |
| `Assets/SystemPrompt/Scripts/Story/` | Yarn 对话的接入：台词分类、对话呈现、命令注册、检查点 |
| `Assets/SystemPrompt/Scripts/Battle/` | 战斗数值：能力值成长、技能、敌人、伤害公式、教学战斗的回合规则 |
| `Assets/SystemPrompt/Scripts/Dungeon/` | 潜空间探索：区域地图、分区和配置（`DungeonData.cs`）、碰撞、视线与寻路、用几何体生成的场景（机柜墙、海床、灯塔、信标、宝箱，各区域的摆设在 `SeaDecor.cs`）、主角和 DeepSeek 的模型与动作（`SeaFigure.cs`）、噪声体（巡逻、视野、追赶）、缓存块和反射板机关（`SeaPuzzles.cs`） |
| `Assets/SystemPrompt/Scripts/UI/` | 界面：通用元件（`Elements/`）、立绘（`Portraits/`）、标题（`Screens/`）、剧情（`Story/`）、菜单类弹窗（`Modals/`）、手机（`Phone/`）、战斗（`Battle/`）、探索 HUD 和小地图（`Dungeon/`） |
| `Assets/SystemPrompt/Editor/` | 编辑器工具：剧本同步、图片和模型导入设置、测试版构建 |
| `Assets/SystemPrompt/E2E/` | 端到端验证的驱动，只编进测试版 |
| `Assets/SystemPrompt/Scenes/` | 空的启动场景 |
| `Assets/SystemPrompt/Resources/UI/` | `SystemPrompt.uss` 样式表和 UI Toolkit 主题 |
| `Assets/SystemPrompt/Resources/Shaders/` | 潜空间的着色器：三渲二（描边、两段色阶、边缘光）、机柜墙（按区域换指示灯颜色）、海床（按区域染色）、发光体、场景里的文字、噪声体，雾在着色器里算 |
| `Assets/SystemPrompt/Resources/Models/` | 潜空间的角色模型：`Characters/` 是男女主角和 DeepSeek（FBX 带 Mixamo 骨骼和各自的待机动作，旁边是基础色贴图），`Motions/` 是三人共用的行走和跑步 |
| `Assets/SystemPrompt/Resources/Art/` | 立绘、背景、战斗收尾特写 |
| `Assets/SystemPrompt/Resources/Audio/` | 标题 BGM 和 8bit 音效 |
| `Assets/SystemPrompt/Resources/Fonts/` | Noto Sans SC 三个字重、JetBrains Mono 两个字重和字体许可 |
| `Assets/SystemPrompt/Resources/Story/` | `Prologue.yarnproject`，以及从 `story/` 同步过来的剧本副本 |
| `Tools/e2e.sh` | 端到端验证脚本 |
| `E2E/` | 端到端验证的报告和截图，每次运行一个文件夹 |

界面固定按 1920×1080 布局，窗口比例不同时加黑边。斜切面板、网点、爆炸框等用 Painter2D 在代码里画（`UI/Elements/Shape.cs`），USS 负责尺寸、位置、文字和各种 `is-*` 状态。界面配色和字体沿用 PV 的设计，见 [pv/README.md](../pv/README.md)。

画面切换参照 P 系列，在 `UI/Elements/Transitions.cs`：

- 标题和剧情之间、读档、战斗结束：一列斜切色块从左扫过，盖住时显示 NOW LOADING。
- 剧情换场景：同样的斜切扫过，中间停一下显示地点卡。
- 进入战斗：爆炸框从中心炸开，显示 ENCOUNTER，再沿对角线裂成两半退出。

切换期间不接受输入。弹窗打开时内容从左下斜着甩进来，菜单项逐个滑入；背后是移动的斜条纹和飘动的数据碎片。

暂停菜单参照《P4 Revival》的菜单（`UI/Modals/GameMenu.cs`）：

- 打开菜单：一道白线横切过屏，绿色笔刷从左扫入，右边斜着滑进同伴的大特写（队伍里的同伴，没有就用最近认识的角色），菜单字一个个旋转飞入，停下时每个字都略歪。光标移到哪一项，那一项的字会跳一下。菜单底色不透明，剧情画面不会透出来。
- 进入子页面（存档、读档、手机、设置、回看）：菜单字散开飞走，镜头猛推进特写，子页面在推进途中接管画面。
- 关掉子页面：镜头从特写拉回，菜单字重新落位。

每个子页面有自己的界面和进出场动画，不管从菜单、标题、快捷栏还是快捷键打开都一样（入场和出场用 `Modal.PlayMotion`）：

- 存档 / 读档（`SaveLoadModal.cs`）：相册。左边是选中存档的大照片（场景图贴在歪斜的相纸上，配胶带）和大号日期，右边是存档卡片，选中的卡片变成白色相纸、带页面色的硬阴影、向左抽出。存档是绿色，读档是蓝色。打开时快门叶片合拢再张开，伴随闪光和快门声（存档上下合拢，读档左右合拢）；存档成功时照片再闪一次，盖上 SAVED! 印章。
- 设置（`SettingsModal.cs`）：终端里打开的配置文件 `system_prompt.cfg`。每页是一个文件标签（`audio.cfg`、`text.cfg`……），每行带行号，写成「键 = 值」：滑块是方块条，开关是 `[ ON OFF ]`，选项是 `◀ 值 ▶`，选中行有闪烁光标，说明以 `//` 注释逐字打出，底部是状态栏。打开时像 CRT 开机（一道亮线展开成画面），各行依次打出来；关闭时缩成一条线再消失。
- 回看（`BacklogModal.cs`）：聊天记录。角色台词是左侧带头像的气泡，主角的台词和选择在右侧（绿色），心声是带绿边的白气泡，旁白是居中的说明文字，系统信息是等宽小条。打开时像录像带倒带：记录快速滚落到位，扫描条划过，右上角闪 ◀◀ REWIND；关闭时快进上滑。
- 手机（`UI/Phone/`）保持原来的手机界面。

剧情对话框也换成了 P5 式的歪斜黑框：白描边、说话人颜色的硬阴影，尖角指向正在说话的立绘；名牌是斜贴的白纸条，选项是错位排列的不规则黑框，选中项翻成白底。快捷栏是一排小斜标签。打开菜单或子页面时，对话框、快捷栏和 HUD 一起淡出，确认框这类小弹窗不会。

战斗结算（`UI/Battle/BattleFx.cs` 的 `ResultsFx`）：墨色和绿色斜带横扫过屏，白色笔刷带出巨型 RESULT，DeepSeek 的立绘从右边滑进、压在面板后面；EXP 爆炸框数字跳涨，每个队员的经验条依次填满，升级时弹出 LEVEL UP! 爆炸框；掉落物卡片翻入，最后闪烁「▶ NEXT」。按确定后面板甩出画面。集成的收尾特写里，「吃饭饭！」是 P5 式对话气泡，叠在 DeepSeek 前面。

背景粒子在 `UI/Elements/ParticleField.cs`，每个场景在 `GameData.Scenes` 里指定一种：校门和天台是樱花，教室和学生会室是光点，自己房间是夜里的微光，潜空间和战斗是气泡加数据碎片。背景图会缓慢漂移和呼吸缩放。设置 → 辅助 →「背景动态」可以关掉粒子和漂移。

## Live2D 立绘

标题、手机资料页和剧情里的立绘都通过 `UI/Portraits/PortraitView.cs` 显示：切表情、说话人高亮和变暗、说话时动嘴。某个角色有 Live2D 模型时用模型，没有就用 `Resources/Art/Portraits/` 里的 PNG，两种可以混用。战斗里的头像和特写仍然是 PNG。

**没有模型时的动态立绘。** 现在三个角色都还没有 Cubism 模型（`.moc3` 只能在 Cubism Editor 里做骨骼和变形后导出）。标题和手机资料页先用 `UI/Portraits/RiggedPortrait.cs` 让 PNG 动起来：立绘画成一张 32×48 的网格，每帧按 `GameData` 里角色的 `PortraitRig`（脖子、脸、身体左右边界、肩线、腰线、闭眼贴片位置，单位是 1024×1536 立绘的像素）弯折网格。效果有：

- 呼吸：腰线以上随呼吸抬起，上半身轻微左右晃。
- 头部：绕脖子缓慢摆动，脸在头部轮廓里朝视线方向偏移，看起来像转头；说话时会点头。
- 视线：标题上跟着鼠标，鼠标不动 2.5 秒后自己四处看。
- 头发：身体左右两侧、肩线以下的长发各自随风摆，有惯性，被点到时会甩开再荡回来。
- 眨眼：每 2 到 5.5 秒一次，偶尔连眨两下。闭眼图是 `Resources/Art/Portraits/<角色>_blink.png`，只覆盖双眼一块、边缘羽化，渐显在默认表情上；其他表情的眼睛位置不同，不眨眼。

闭眼贴片的做法：截取默认表情的脸部，用图像编辑生成同一构图的闭眼版，按图像相关性对齐（三张都无需位移），取两图差异最大的两块区域（双眼）做羽化遮罩，再按遮罩边缘的颜色差校正色调后裁出。换了立绘就要按同样步骤重做贴片，并重新量 `PortraitRig` 的坐标。

有了 Live2D 模型之后，放进 `Resources/Live2D/` 就会优先用模型，`RiggedPortrait` 不再参与。剧情里的立绘仍是静态 PNG。

**安装 SDK。** 从 [live2d.com](https://www.live2d.com/en/sdk/download/unity/) 下载 Cubism SDK for Unity 的内置管线（BiRP）版。本工程用内置渲染管线；最新的 R6 只支持 URP，用不了。下载时要同意 Live2D 的两份软件许可，然后在编辑器里用 **Assets → Import Package** 导入，位置是 `Assets/Live2D/`。Cubism Core 不允许再分发，所以 SDK 不进仓库，已写进 `.gitignore`，每台机器各自导入。`Editor/Live2D/Live2DDefine.cs` 会检查 SDK 在不在，自动开关 `SP_LIVE2D` 编译定义：没装 SDK 时工程照常编译，全部用 PNG。

**放模型。** 用 Cubism Editor 导出运行时文件（`.moc3`、`.model3.json`、贴图、`.physics3.json`、`.pose3.json`、`.exp3.json`），放到 `Assets/SystemPrompt/Resources/Live2D/<角色名>/<角色名>.model3.json`。例如 DeepSeek 放在 `Resources/Live2D/DeepSeek/DeepSeek.model3.json`，战斗形态放在 `Resources/Live2D/DeepSeek_battle/DeepSeek_battle.model3.json`。SDK 的导入器会自动生成同名 prefab，游戏运行时加载它。模型需要满足这些约定：

- 表情文件以剧本里的表情名命名：`smile.exp3.json`、`pout.exp3.json` 等，`neutral` 可以不做。没有对应文件的表情显示模型的默认样子。
- 眨眼用 model3.json 里的 `EyeBlink` 参数组。
- 口型用 `LipSync` 参数组，没有就用 `ParamMouthOpenY`。说话时按台词长度开合；没有配音，不做真正的口型同步。
- 呼吸用 `ParamBreath`，轻微摆动用 `ParamAngleX/Y/Z` 和 `ParamBodyAngleX`。参数缺了就跳过对应效果。

每个 Live2D 立绘有自己的相机，把模型渲染到一张 1024×1536 的贴图上，和 PNG 立绘一样大，界面布局不用改。Cubism 按预乘透明输出，UI Toolkit 按普通透明混合，所以相机输出先经 `Resources/Live2D/Unpremultiply.shader` 换算，否则模型轮廓会发黑。取景按 Cubism Editor 里的画布：整张画布缩放进贴图，所以画布最好也做成 2:3，人物在画布里的位置和 PNG 立绘一致。立绘不在屏幕上时，相机和模型都会停下来。

目前还没有做：模型的动作文件（`.motion3.json`）不会播放；点头和点身体仍然用界面上的头部和身体区域判定，没有用模型的 HitArea。

## 剧本

剧本只在仓库根目录的 `story/` 维护。Unity 只能导入 `Assets/` 下的文件，所以 `Editor/YarnStorySync.cs` 在打开编辑器、每次进入 Play 前和构建测试版前，把 `story/*.yarn` 复制到 `Resources/Story/Synced/`（已加入 `.gitignore`），`Prologue.yarnproject` 编译这个文件夹。也可以手动点菜单 **System Prompt → 同步剧本**。不要直接改 `Synced/` 里的副本。

剧本里的命令（`<<calendar>>`、`<<scene>>`、`<<battle>>` 等）在 `Scripts/Story/StoryCommands.cs` 注册，新增命令也加在这里。说话人决定台词的呈现方式：`旁白` 是旁白框，`系统` 是左侧弹出的系统提示，`屏幕` 显示在手机画面上，`文件` 是终端投影，主角的台词加 `#inner` 是内心独白，DeepSeek 的台词加 `#thinking` 显示在思考框里。立绘表情用行标签 `#face:表情`，可选表情写在 `story/00-prologue.yarn` 开头的注释里。角色第一次说话时自动登场，换场景时清场。

战斗中的提示和台词放在 `Prologue_Battle_*` 节点，由战斗系统在对应时机调用。`<<dungeon 区域ID>>` 进入潜空间探索，探索中的教学、区域、碎片和事件放在 `Prologue_Sea_*`、`Prologue_Deep_*` 节点，节点名、地图、分区和宝箱写在 `Scripts/Dungeon/DungeonData.cs`；这些节点在探索画面的对话框里播放，不能放选项。

## 存档

存档、设置和已读记录放在 `Application.persistentDataPath`：

| 文件 | 内容 |
| --- | --- |
| `saves/slot_00.json` | 快速存档（F5） |
| `saves/slot_01.json` – `slot_12.json` | 普通存档位 |
| `settings.json` | 设置 |
| `system.json` | 已读台词，用于「跳过未读文字」 |

存档是检查点：每进入一个 Yarn 节点，就记录一次节点名、游戏状态和 Yarn 变量，存档写入的是最近一次检查点，读档时从那个节点的开头重新播放。所以存档界面会提示「从本节开头继续」。战斗和潜空间探索中不能存档。

## 素材来源

| 素材 | 来源 |
| --- | --- |
| 立绘、背景、战斗收尾特写 | AI 图像生成，角色形象取自 [lmy414/ai-chan-sheets](https://github.com/lmy414/ai-chan-sheets) 的设定稿，服装按 [design/characters](../design/characters/README.md) 的设定 |
| 标题 BGM | PV 配乐去掉主唱的分轨（`pv/music/a1-without-lead-vocal.wav`） |
| 音效 | PV 的 8bit 音效（`pv/sfx/`，由 `pv/scripts/sfx.py` 合成） |
| 潜空间角色模型 | [Tripo](https://studio.tripo3d.uk) 生成：先按 `assets/characters/turnarounds` 的正面图生成 T 字姿势的图（手臂和衣服分开，绑骨后不粘连），再转成约 4 万面的模型，自动绑定 Mixamo 人形骨骼，动作用 Tripo 的预设（站立待机、行走、跑步）。男女主角是家居服（4-lounge），DeepSeek 是潜空间战斗服（4-battle） |
| 字体 | [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC) 和 [JetBrains Mono](https://www.jetbrains.com/lp/mono/)，SIL Open Font License 1.1，见 `Resources/Fonts/OFL.txt` |

基于设定稿角色形象制作的立绘、特写和 3D 模型沿用原设定稿的 [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/) 协议，仅限非商业用途，详见根目录 [README](../README.md#封面与角色形象)。主角目前没有立绘，战斗里用名字首字代替头像。

## 还没做的

3D 校园、序章以外的潜空间区域、日程推进、Coop 剧情、礼物和装备界面都还没开始；目前只能玩序章。潜空间里的噪声体还是几何体拼的，信标还不能存档。DeepSeek 的人形骨骼没有尾巴骨，鲸尾跟着腰腿一起动。战斗系统只实现了教学战斗用到的部分（普通攻击、技能、深度思考、防御、道具、One More、集成），数值按 [design/stats.md](../design/stats.md) 计算。
