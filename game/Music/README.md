# 游戏 BGM 原曲

剧情、潜空间和战斗三段 BGM 的原曲，都是 Suno 生成的纯音乐（WAV，48 kHz 16-bit 立体声）。`../Tools/bgm_loops.py` 从这里截出循环，写到 `../Assets/SystemPrompt/Resources/Audio/BGM/{story,sea,battle}.wav`。标题曲不在这里，仍是 PV 配乐去掉主唱的分轨（`pv/music/a1-without-lead-vocal.wav`）。

| 文件 | 用途 | 标题 |
| --- | --- | --- |
| `story-2a16f8c3.wav` | 剧情（白天的校园、日常） | 校园日常 |
| `sea-b984126f.wav` | 潜空间的 3D 探索和剧情，玩家房间的夜里也用 | 缓存海 |
| `battle-64c1c962.wav` | 战斗 | 噪声体来袭 |

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

实测速度是全曲平均。三首都在慢慢变快：剧情从 132.8 到 134.3，战斗从 164.7 到 166.4，潜空间基本不变。

## 循环

`bgm_loops.py` 先从音频里测出拍点：频谱通量做起拍曲线，自相关在提示词速度附近找拍长，再用动态规划逐拍跟踪（跟得很紧，不会滑到反拍上）；重拍是四个拍位里频谱变化最大的那个。循环取两条小节线，间隔是 8 小节的倍数、不短于给定长度，两边各 8 拍的音色（对数频带）和和声（音级能量）最像的一对，跳回开头时听起来像原曲本来就会接下去。结尾在 20 毫秒内对齐到和开头波形最吻合的位置，接缝交叉淡化 30 毫秒。Suno 的曲子越往后越满，循环最后两小节比开头两小节响 1.4–2.1 dB，所以整段按分贝线性倾斜增益，让首尾一样响，否则每次循环都会掉一下音量。

| 循环 | 原曲区间 | 长度 | 音色 / 和声相似度 | 增益倾斜 |
| --- | --- | --- | --- | --- |
| 剧情 | 51.25–152.02 s | 56 小节，100.8 s | 0.711 / 0.597 | −1.4 dB |
| 潜空间 | 60.28–132.52 s | 24 小节，72.2 s | 0.533 / 0.760 | −1.7 dB |
| 战斗 | 56.98–172.94 s | 80 小节，116.0 s | 0.652 / 0.796 | −2.1 dB |

**接缝检查**：脚本把每段循环连放两遍，比较接缝处和全文件其它位置的样本跳变和频谱通量。三处接缝的样本跳变分别在全文件的第 6、69、22 百分位，是普通的相邻样本差；接缝处的通量和原曲同一条小节线上的几乎一样（剧情 15.1 对 14.5，潜空间 39.6 对 43.4，战斗 20.6 对 19.3），没有多出来的爆音。潜空间的通量是这段循环里最高的，因为起点本来就是原曲一段的开头重拍，在原曲里也排在小节线的前 10%。

重新截取（约 5 秒，需要 ffmpeg 和 numpy）：

```bash
python3 game/Tools/bgm_loops.py
```
