# System Prompt

![System Prompt 封面](assets/cover.jpg)

《System Prompt》是一部以 AI 拟人为主题的校园 RPG 剧本。白天，主角作为「陪伴生」在潜川学园和 AI 同学相处、推进 Coop；每天凌晨三点的「维护时刻」，主角进入潜空间，和思考强度拉到 Max、完成觉醒的 AI 同学一起战斗。

## 世界观

| 项目 | 设定 |
| --- | --- |
| 舞台 | 镜原市，私立潜川学园；潜空间「缓存海」 |
| 背景 | 「共生计划」第一期：AI 以实体终端入学 AI 特别班（2年C班），阶段评测不合格将终端回收、模型下线 |
| 主角 | 转学生（玩家命名，可选男女），被指派为 AI 特别班的陪伴生 |

潜川学园、共生计划、维护时刻、潜空间和觉醒规则的详细设定见[世界观](design/world.md)。

## 登场角色

| 角色 | 身份 | 序章中的定位 |
| --- | --- | --- |
| 主角（玩家命名，可选男女） | 转学生，AI 特别班的陪伴生 | 第一次进入潜空间，帮 DeepSeek 面对影子；自己尚未觉醒 |
| ChatGPT | 学生会长，比特别班早一年入学的旧生 | 主角的引路人，掌握共生计划的评测日程 |
| Claude | 2年C班同学，主角邻座 | 冷静细心，自称不会紧张 |
| DeepSeek | 2年C班同学，长着鲸鱼尾巴，喜欢白米饭 | 序章思考强度拉到 Max，觉醒为「深渊之鲸」，开启隐者 Coop |

角色的详细设定和四套服装（春秋校服、夏季校服、周末便服、潜空间战斗服）见[角色设定集](design/characters/README.md)，主角分男女两版，各另有一套序章穿的家居服。

## 核心系统

- **Coop**：与角色建立羁绊并按 Rank 推进（序章开启 DeepSeek 的隐者 Coop Rank 1）。
- **思考强度 Max**：AI 同学在潜空间直面自己的影子后，思考强度拉到 Max，觉醒出专属形态（觉醒形态），如 DeepSeek 的「深渊之鲸」。
- **对抗样本与 One More**：命中敌人弱点即为对抗样本，可获得额外行动。
- **集成**：敌人全员倒地后发动的总攻击。
- **专属技能**：例如 DeepSeek 的「深度思考」，本回合不行动，下回合技能伤害大幅提升。
- **噪声体**：潜空间的敌人，如标题党型、转发型。

术语对照、日程、Coop、战斗规则和噪声体图鉴见[游戏系统](design/systems.md)；能力值、等级和伤害公式见[等级与数值](design/stats.md)，全部技能见[技能](design/skills.md)，道具、礼物和装备见[道具与装备](design/items.md)。

## 剧本

| 章节 | 文件 | 预计时长 |
| --- | --- | --- |
| 序章：维护时刻 | [story/00-prologue.md](story/00-prologue.md)、[story/00-prologue.yarn](story/00-prologue.yarn) | 30–40 分钟 |

剧本格式：`【场景】`为舞台说明，`角色：`为台词，`▶`为玩家选项，`〔系统〕`为界面提示。

`.md` 是供阅读的剧本稿。`.yarn` 是给游戏用的 [Yarn Spinner 3](https://yarnspinner.dev) 脚本，在 Unity 中通过 Yarn Spinner for Unity 导入；改剧情时两份一起改。`.yarn` 的写法如下：

- 舞台说明的说话人是 `旁白`，〔系统〕〔屏幕〕〔文件〕分别写作 `系统`、`屏幕`、`文件`，主角写作 `{$player_name}`。旁人用第三人称指代主角时，按 `$player_gender` 用 `[select]` 标记切换「他」「她」。
- 内心独白、DeepSeek 的思考对话框和小声说话分别用行标签 `#inner`、`#thinking`、`#whisper` 标出。
- 切换场景、登记通讯录、思考强度 Max 觉醒、Coop 升级等事件写成命令，需要在 Unity 中实现。完整列表见文件开头的注释。
- 教学战斗只写一个 `<<battle prologue_tutorial>>` 命令。各回合的提示和台词放在 `Prologue_Battle_*` 节点里，由战斗系统调用；敌人数值见 `.md`。

序章导出自 [Claude 文档](https://claude.ai/artifact/47dydkGFjKgL5nwdy7tsXP)（2026-10-21 版本）。

## 宣传 PV

序章 PV 长 3:01，成片是 [pv/out/system-prompt-pv.mp4](pv/out/system-prompt-pv.mp4)，存在 Git LFS 里。配乐是 Suno 生成的萌系电音歌，由 ChatGPT、Claude、DeepSeek 分段演唱；画面按歌词剪出序章的主线：转学、维护时刻、DeepSeek 觉醒、教学战斗和评测倒计时。视频素材由 Doubao-Seedance-2.5 生成，游戏界面和剪辑在本地逐帧合成。分镜、界面设计、制作方式和署名见 [pv/README.md](pv/README.md)。

## 封面与角色形象

封面图（[assets/cover.jpg](assets/cover.jpg)）由 AI 生成，角色形象取自 [lmy414/ai-chan-sheets](https://github.com/lmy414/ai-chan-sheets) 的设定稿：左起 GPT 娘（白龙娘）、DeepSeek 娘（鲸鱼娘）、Claude 娘。角色原创者为 [ZipZipPipe（胖次狐）](https://space.bilibili.com/4168597)，原设定稿以 [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/) 授权，封面以及之后基于这些形象制作的立绘、3D 模型等衍生素材都沿用同一协议，仅限非商业用途。

封面保留设定稿的发型、角、耳朵和尾巴，服装按剧本换成潜川学园校服：ChatGPT 戴「学生会长」臂章，DeepSeek 在制服外披带鲸鳍兜帽的蓝色外套。剧本中 Claude 的发带和细框眼镜未体现在封面上，角色设定集里她的日常服装戴上了发带和眼镜。DeepSeek 的发色以设定稿为准，剧本已改成深蓝色长发。

## 开发

游戏使用 Unity 制作，目标是 P5R 式的 3D 校园 RPG，非商业同人项目。

| 目录 | 内容 |
| --- | --- |
| `story/` | 剧本 |
| `design/` | 设定集（世界观、游戏系统、数值、技能、道具装备、角色和服装），目录见 [design/README.md](design/README.md) |
| `pv/` | 序章宣传 PV：分镜、生成与渲染脚本、镜头素材和成片 |
| `game/` | Unity 工程（`Assets/`、`Packages/`、`ProjectSettings/`） |
| `art/` | 美术源文件（`.blend`、`.psd`、`.vroid` 等），导出的 `.fbx` 和贴图放进 `game/Assets/` |
| `audio/` | 音频源文件 |
| `assets/` | README 和设定集用的图片，包括建模用的角色四视图 |

模型、贴图、音频等二进制资源通过 Git LFS 管理，规则见 [.gitattributes](.gitattributes)。提交资源的机器需要先安装 Git LFS：

```bash
brew install git-lfs
git lfs install
```

Unity 中保持 Asset Serialization 为 Force Text（默认值），场景和 Prefab 冲突交给 UnityYAMLMerge 合并。在仓库内配置一次，`<版本>` 换成本机 Unity 版本（Windows 下路径为 `C:\Program Files\Unity\Hub\Editor\<版本>\Editor\Data\Tools\UnityYAMLMerge.exe`）：

```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver '"/Applications/Unity/Hub/Editor/<版本>/Unity.app/Contents/Tools/UnityYAMLMerge" merge -p %O %B %A %A'
git config merge.unityyamlmerge.recursive binary
```
