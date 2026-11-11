# 游戏 BGM 原曲

游戏里七首 BGM 的原曲，都是 Suno 生成的纯音乐（WAV，48 kHz 16-bit 立体声）。`../Tools/bgm_loops.py` 从这里截出循环，写到 `../Assets/SystemPrompt/Resources/Audio/BGM/<名字>.wav`；觉醒战和结算另有一段前奏 `<名字>_intro.wav`，是原曲从头到循环起点的部分，游戏里先放一遍前奏再进循环。标题曲不在这里，仍是 PV 配乐去掉主唱的分轨（`pv/music/a1-without-lead-vocal.wav`）。

| 文件 | 用途 | 标题 |
| --- | --- | --- |
| `story-2a16f8c3.wav` | 剧情（白天的校园、日常） | 校园日常 |
| `sea-b984126f.wav` | 潜空间的 3D 探索和剧情，玩家房间的夜里也用 | 缓存海 |
| `battle-64c1c962.wav` | 战斗 | 噪声体来袭 |
| `deep-f5d66962.wav` | 缓存海·深层的 3D 探索（维护进度在走） | 缓存海·深层 |
| `awakening-24893ddb.wav` | 觉醒「深渊之鲸」：从影子化成光点的那一句响起，一直接进教学战斗 | 深渊之鲸 |
| `results-8f46a63a.wav` | 战斗结算 | 结算 |
| `shadow-e4bc3700.wav` | 灯塔脚下和影子 DeepSeek 对峙 | 影子 |

文件名后缀是 Suno 歌曲 ID 的前 8 位。

## 生成方式

Suno 网页的 Advanced 模式，模型 v6。歌词留空就是纯音乐（现在的界面没有单独的 Instrumental 开关）；不选人声性别，其余选项用默认值（Weirdness 50%、Style Influence 50%、时长 Auto）。风格以 `instrumental` 开头、`no vocals` 结尾，排除风格里也排除人声。

三首和 PV 的 A1 用同一套音色：剧情最接近 A1，是萌系电音加 8bit，但放慢、收着，压在对白下面不抢戏；潜空间把同样的方波琶音沉进混响，做成慢速的水下氛围电子，有一点孤单；战斗换成女神异闻录式的酸爵士放克加电子，保留芯片音色。速度写进风格里，拍子保持 4/4、从头到尾稳定，方便找循环点。

### 剧情：校园日常

**标题**：校园日常（剧情 BGM）

**风格**：

```text
instrumental, kawaii future bass, chiptune, 8-bit, J-pop slice-of-life school day BGM for a visual novel, light, cute and carefree, playful but calm enough to sit under dialogue, 4/4 time, 128 bpm, steady four-on-the-floor kick, bouncy sidechained synth chords, soft square wave lead, NES-style arpeggios, plucky marimba and glockenspiel, light bitcrushed drums, handclaps, bright major key, simple repeating hook, steady groove from start to end, no vocals
```

**排除风格**：vocals, singing, voice, choir, dark, sad, ballad, heavy metal, rock, jazz

### 潜空间：缓存海

**标题**：缓存海（潜空间 BGM）

**风格**：

```text
instrumental, ambient electronic, underwater deep sea exploration BGM, slow and dreamy, slightly lonely and wistful, cyber latent space, cache sea, 80 bpm, 4/4 time, soft muffled kick and brushed hi-hats, warm sub bass, detuned analog pads, distant chiptune square wave arpeggio drowned in reverb, glassy bell plinks like rising bubbles, gentle sidechain swell, minor key with soft major lifts, sparse and spacious, steady pulse from start to end, no vocals
```

**排除风格**：vocals, singing, voice, choir, upbeat, happy, rock, metal, aggressive drums, epic orchestral

### 战斗：噪声体来袭

**标题**：噪声体来袭（战斗 BGM）

**风格**：

```text
instrumental, JRPG turn-based battle theme, upbeat and driving, stylish acid jazz funk meets electro, cool and confident, 165 bpm, 4/4 time, punchy breakbeat drums, slap bass groove, funky clavinet and jazzy electric piano stabs, sidechained synth chords, chiptune square wave lead, 8-bit arpeggios, brass hits, minor key, catchy riff, constant energy from start to end, no vocals
```

**排除风格**：vocals, singing, voice, rap, choir, ballad, slow, ambient, heavy metal, orchestral

女神异闻录的味道靠曲风和乐器描述出来，风格里不写作品名（Suno 不收艺人名，作品名也一并避开）。

后四首（2026-11-10）沿用同一套音色，各自对着前三首里的一首写：深层是「缓存海」更冷、更深的一版，加一个像钟表一样走的闭镲；觉醒战是「噪声体来袭」更快、更亮的一版，多了铜管和冰一样的铃声；结算是同一套酸爵士放克放松下来，开头一小段胜利号角；影子是慢速的八音盒和断断续续的方波，不安里带一点难过。

### 潜空间深层：缓存海·深层

**标题**：缓存海·深层（潜空间 BGM）

**风格**：

```text
instrumental, dark ambient electronic, deep sea exploration BGM under a countdown, colder and deeper than a calm underwater theme, quiet urgency, cyber latent space, sunken server room, 96 bpm, 4/4 time, ticking closed hi-hat pulse like a clock, soft muffled kick, slow throbbing sub bass, detuned analog pads, distant chiptune square wave arpeggio drowned in reverb, glassy bell plinks, low machine hum, occasional reversed swell, minor key, restrained tension that never resolves, sparse and spacious, steady pulse from start to end, no vocals
```

**排除风格**：vocals, singing, voice, choir, upbeat, happy, rock, metal, aggressive drums, epic orchestral, drop

### 觉醒战：深渊之鲸

**标题**：深渊之鲸（觉醒战 BGM）

**风格**：

```text
instrumental, JRPG awakening battle theme, triumphant and exhilarating, the heroine has just awakened her power, stylish acid jazz funk meets electro, 172 bpm, 4/4 time, punchy breakbeat drums, driving slap bass, funky clavinet and jazzy electric piano stabs, bold brass fanfare hits, soaring chiptune square wave lead melody, 8-bit arpeggios, sidechained synth chords, icy bell and glass textures like deep sea ice, minor key with heroic major lifts, big catchy hook, constant high energy from start to end, no vocals
```

**排除风格**：vocals, singing, voice, rap, choir, ballad, slow, ambient, heavy metal, orchestral, sad

### 战斗结算：结算

**标题**：结算（战斗结算 BGM）

**风格**：

```text
instrumental, JRPG battle results screen theme, short bright victory fanfare then a relaxed stylish groove, acid jazz funk, laid-back and satisfied, cool and confident, 112 bpm, 4/4 time, jazzy electric piano chords, walking slap bass, brushed funk drums with rimshots, muted brass stabs, chiptune square wave sparkle and 8-bit arpeggios, handclaps, major key with jazzy sevenths, simple repeating hook, steady groove that loops cleanly, no vocals
```

**排除风格**：vocals, singing, voice, rap, choir, sad, dark, ambient, heavy metal, orchestral, slow ballad

### 对峙：影子

**标题**：影子（对峙 BGM）

**风格**：

```text
instrumental, tense eerie confrontation theme for a visual novel, facing your own shadow, unsettling but sad, cyber latent space at the foot of a lighthouse underwater, 72 bpm, 4/4 time, slow heartbeat kick, low sub drone, detuned music box melody, broken chiptune square wave notes that glitch and stutter, reversed bell swells, bitcrushed static, cold thin pads, sparse piano single notes with long reverb, minor key, quiet dread with a lonely tenderness underneath, slow build without release, steady pulse from start to end, no vocals
```

**排除风格**：vocals, singing, voice, choir, upbeat, happy, rock, metal, epic orchestral, jump scare, loud drums

## 制作记录（2026-10-26）

每首 Create 一次，每次生成 2 条，各 10 积分，共 30 积分。Suno 现在下载前要先解锁（Unlock & Download），Pro 每月有下载次数（这次下载前剩 14 次，11/21 刷新），解锁一次后各种格式都能随时再下。Suno 给每条写的描述只是把提示词换个说法，看不出差别，所以没有逐条试听比较，每首只解锁下载了列表里的第一条；三条都截出了合格的循环，另一条就没再下载，留在资料库里。

| 用途 | Suno 歌曲 ID | 时长 | 实测速度 | 状态 |
| --- | --- | --- | --- | --- |
| 剧情 | `2a16f8c3-feed-4e2f-9662-ea0505f313f0` | 2:43 | 132.9 BPM（提示词写的 128） | 选用 |
| 剧情 | `20cd8534-4b5b-4419-8157-5d25a134db93` | 2:43 | — | 未下载 |
| 潜空间 | `b984126f-c012-4a20-a791-391b9e17d2f8` | 3:23 | 79.7 BPM | 选用 |
| 潜空间 | `a195b605-bc22-4987-9899-b18b15e871db` | 3:19 | — | 未下载 |
| 战斗 | `64c1c962-299b-414b-8474-f4baa061f47b` | 3:11 | 165.4 BPM | 选用 |
| 战斗 | `77b9dc7f-0fe2-49ca-900a-1c5c9b9dcc89` | 3:15 | — | 未下载 |

实测速度是全曲平均。前三首里两首在慢慢变快：剧情从 132.8 到 134.3，战斗从 164.7 到 166.4，潜空间基本不变。

## 制作记录（2026-11-10）

后四首经 Suno 的接口生成（模型 v6，纯音乐），每首一次、每次 2 条。这次先比过再选：深层的两条看的是 Suno 的拍点分析，其余每条让 Suno 的听歌接口听了一遍（速度、调、有没有人声、合不合用途），只下载选中的 4 条，用掉 4 次下载（下载前剩 11 次）。

| 用途 | Suno 歌曲 ID | 时长 | 听下来 | 状态 |
| --- | --- | --- | --- | --- |
| 深层 | `f5d66962-8509-49f1-9b64-630eccc5144b` | 2:35 | 93.5 BPM（提示词写的 96），E 小调，没有人声 | 选用 |
| 深层 | `b614c71f-0357-4c45-b8ed-bdc5481a02e7` | 3:30 | 约 98 BPM | 未下载 |
| 觉醒战 | `24893ddb-142c-405f-b7a5-c17325d84992` | 2:57 | 172 BPM，C 小调，没有人声；开头 8 秒是合成器的铺垫，然后整套编制进来；最忙的段落中频有点挤 | 选用 |
| 觉醒战 | `4747db10-6085-4b68-b83d-5bbc891de822` | 3:12 | 175 BPM，A 小调，有零碎的「啊、哦」人声采样 | 未下载 |
| 结算 | `8f46a63a-7ce5-4d20-b9f7-3f92dc689018` | 2:18 | 112 BPM，G 大调，没有人声；号角到 4.5 秒，停一下，7 秒起是稳定的律动 | 选用 |
| 结算 | `33bb695b…` | — | 112 BPM，C 大调，有像人声的短促音，高音的合成器线条久听偏刺 | 未下载 |
| 影子 | `e4bc3700-40c8-41a0-bcac-be8d06bdce83` | 3:07 | 71 BPM，升 G 小调，稀疏，给对白留了空间；有一层很淡的像合唱的音色，没有歌词和主唱 | 选用 |
| 影子 | `23f9659a…` | — | 71 BPM，同样有那层像合唱的音色，首尾淡入淡出，混音垫在对白下面偏满 | 未下载 |

没下载的四条留在资料库里，后两条这里只记了 ID 的前 8 位。

## 循环

`bgm_loops.py` 先从音频里测出拍点：频谱通量做起拍曲线，自相关在提示词速度附近找拍长，再用动态规划逐拍跟踪（跟得很紧，不会滑到反拍上）；重拍是四个拍位里频谱变化最大的那个。循环取两条小节线，间隔是 8 小节的倍数、不短于给定长度，两边各 8 拍的音色（对数频带）和和声（音级能量）最像的一对，跳回开头时听起来像原曲本来就会接下去。结尾在 20 毫秒内对齐到和开头波形最吻合的位置，接缝交叉淡化 30 毫秒。Suno 的曲子越往后越满，循环最后两小节比开头两小节响 1.4–2.1 dB，所以整段按分贝线性倾斜增益，让首尾一样响，否则每次循环都会掉一下音量。

| 循环 | 原曲区间 | 长度 | 音色 / 和声相似度 | 增益倾斜 |
| --- | --- | --- | --- | --- |
| 剧情 | 51.25–152.02 s | 56 小节，100.8 s | 0.711 / 0.597 | −1.4 dB |
| 潜空间 | 60.28–132.52 s | 24 小节，72.2 s | 0.533 / 0.760 | −1.7 dB |
| 战斗 | 56.98–172.94 s | 80 小节，116.0 s | 0.652 / 0.796 | −2.1 dB |
| 深层 | 56.57–118.19 s | 24 小节，61.6 s | 0.462 / 0.525 | +1.1 dB |
| 觉醒战 | 67.86–168.33 s | 72 小节，100.5 s | 0.245 / 0.687 | −1.1 dB |
| 结算 | 48.32–82.60 s | 16 小节，34.3 s | 0.366 / 0.801 | +0.2 dB |
| 影子 | 62.62–143.40 s | 24 小节，80.8 s | 0.844 / 0.704 | −2.8 dB |

觉醒战的音色相似度低：这首一直在换段落，找不到两头编制一样的小节线，循环回去时听起来是换了一段（2:48 回到 1:08 开始的那个主题），和声对得上。后四首接缝的样本跳变在全文件的第 17 到 72 百分位，接缝处的通量是循环里其它小节线中位数的 0.42 到 1.03 倍，没有爆音。

**前奏**：觉醒战和结算在 `LOOPS` 里标了有前奏，脚本另存 `<名字>_intro.wav`（原曲从头到循环起点：觉醒战 67.86 秒，结算 48.32 秒）。游戏里 `AudioService` 先放前奏，在它结束的那个采样点上接循环（两个 `AudioSource`，`PlayScheduled`）。结算要的就是开头那一小段号角；觉醒战的开头 8 秒是铺垫，正好垫在觉醒的 CG 下面，整套编制进来时思考强度拉到 Max。有前奏的曲子不淡入，第一个音就是满音量。

**接缝检查**：脚本把每段循环连放两遍，比较接缝处和全文件其它位置的样本跳变和频谱通量。前三首的三处接缝的样本跳变分别在全文件的第 6、69、22 百分位，是普通的相邻样本差；接缝处的通量和原曲同一条小节线上的几乎一样（剧情 15.1 对 14.5，潜空间 39.6 对 43.4，战斗 20.6 对 19.3），没有多出来的爆音。潜空间的通量是这段循环里最高的，因为起点本来就是原曲一段的开头重拍，在原曲里也排在小节线的前 10%。

重新截取（七首约 15 秒，需要 ffmpeg 和 numpy）：

```bash
python3 game/Tools/bgm_loops.py
```
