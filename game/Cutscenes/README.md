# 过场 CG 原片

序章三处过场 CG 的原图和原片。每处先用图像模型按项目里的设定图画一张 16:9 的图，再用视频模型把这张图做成 5 秒的动态镜头。`../Tools/cg_clips.py` 把原片转成游戏用的片段，写到 `../Assets/SystemPrompt/Resources/Art/CG/`。剧本用 `<<cg 图ID>>` 放出来，怎么播见 [game/README.md](../README.md#剧本) 的剧本一节。

| 图 ID | 文件 | 出现的位置 | 画面 |
| --- | --- | --- | --- |
| `dive` | `dive.png`、`dive.mp4` | 场景4，手机显示「连接成功」之后，旁白「坠落感」那一句 | 第一人称往下坠：房间碎成方块散开、远去，手机悬在正中发着白光，一只手伸向它，最后沉进深蓝的水里。不出现主角的脸，男女主角共用 |
| `abyss_whale` | `abyss_whale.png`、`abyss_whale.mp4` | 场景4，影子化成光点涌入 DeepSeek 胸口那一句，到进教学战斗为止（中间盖着思考强度拉到 Max 的演出） | 穿战斗服的 DeepSeek 站在海底，一手按着胸口；影子碎成光点散尽，身后光之鲸向海面升起，远处是机柜灯塔 |
| `council_window` | `council_window.png`、`council_window.mp4` | 场景6，学生会室整段，留到序章结束 | 清晨的学生会室，ChatGPT 背对长桌站在窗前，看着手腕上的终端投出的文件 |

## 原图

图像模型是 gpt-image-2.5（高质量，1824×1024），每张都带参考图，提示词要求照参考图画人物、不出现文字和水印，画面最下面四分之一留给对话框。

| 图 | 参考图 |
| --- | --- |
| `dive.png` | 背景 `player_room`、`cache_sea` |
| `abyss_whale.png` | 立绘 `deepseek_battle`、`deepseek_shadow`，背景 `cache_sea` |
| `council_window.png` | 立绘 `chatgpt_neutral`，背景 `student_council_room` |

### 潜入

```text
A cinematic 16:9 anime cutscene illustration for a visual-novel RPG, painted in the same style as the two reference backgrounds (clean modern anime background art, soft painterly rendering, cinematic volumetric light, deep navy and cyan palette).

Scene: first-person view of someone falling backwards out of their dark bedroom into a digital sea at 3 a.m. The camera looks straight UP from below.

Top and centre of the frame: the bedroom from the first reference (bed with rumpled duvet, desk and chair, bookshelf, curtained balcony window with a full moon) seen from underneath as if the floor has vanished. The whole room is breaking apart into neat square blocks and flat rectangular tiles, like a file being deleted piece by piece; blocks near the edges have already separated, tumbling and dissolving into glowing cyan pixels. Gaps between the blocks show pure black.

Exact centre, slightly above the middle: a single smartphone floating face-down toward the viewer, its screen a blazing white-blue rectangle with no text or icons, throwing hard light rays and fine cracks of light outward through the shattering room.

From the lower right corner, one arm of the viewer reaches up toward the phone: a loose plain grey long-sleeved lounge top sleeve, an open bare hand with fingers spread, seen from behind the hand, lit from above by the phone. Only this one forearm and hand are visible; no face, no body, no hair.

Around the outer edges and rising from the bottom: dark deep-blue water closing in, with translucent glowing chat-bubble shapes, tiny round emoji-like discs, and small file-icon rectangles drifting upward like air bubbles, as in the second reference. A few real air bubbles and light caustics.

Mood: sudden weightless fall, quiet dread and wonder. Strong central glow, dark vignette. Keep the bottom quarter of the frame calm and dark (it will sit under a dialogue box).

No text, no letters, no numbers, no UI, no watermark, no signature, no border.
```

### 深渊之鲸

```text
A cinematic 16:9 anime key-visual cutscene illustration for a visual-novel RPG. Draw the girl from the first reference image exactly on-model, in the same anime illustration style (clean line art, soft cel shading with glowing highlights). The setting is the underwater "cache sea" from the second reference image.

Character (must match reference 1): a girl with very long wavy dark-blue hair whose ends fade to glowing cyan, one ahoge, blue eyes, white frilled maid headband with blue bows, dark navy whale-fin ears with white frilled inner side, a short navy capelet with gold embroidery and a blue gem brooch with a black bow, white puff-sleeved blouse, navy corset with gold buttons, white frilled apron with a small blue whale emblem, navy skirt with gold snowflake embroidery over white ruffles, white gloves, white thigh-high stockings, and a huge navy whale tail speckled with star-like lights.

Moment: her awakening. She stands upright on a dark rocky seabed slightly left of centre, seen from a low three-quarter front angle, full body visible down to the knees, chin raised, expression calm and resolved with a faint smile, eyes glowing bright blue. One white-gloved hand rests over her chest, where a stream of white-blue light motes spirals inward. On the right, the last traces of her shadow double — a pale grey silhouette of the same girl with two blank white rectangular screens for eyes (third reference image) — are breaking up into those light motes and flowing to her.

Her huge whale tail has just whipped around in a wide arc, throwing up a ring of surging water, foam and light around her; her hair and capelet stream in the current.

Behind and above her, filling the upper half of the frame: an enormous whale made entirely of light — translucent, drawn with flowing luminous cyan-white lines and constellations of stars inside its body — rising slowly toward the rippling sea surface, its head breaking through the surface in a burst of light rays. Far in the right background, the lighthouse built of stacked server racks from reference 2, its beam now soft blue. Scattered glowing chat-bubble fragments are blown outward by the wave.

Palette: deep navy and indigo water, cyan and white light, small gold accents on her dress. Dramatic backlight from the whale, rim light on her hair. Epic, tender, triumphant.

Composition: keep her face and the whale in the upper two thirds; the bottom quarter is darker seabed and water (it will sit under a dialogue box).

No text, no letters, no UI, no watermark, no signature, no border. One girl only (plus the fading silhouette).
```

### 学生会室的窗前

```text
A cinematic 16:9 anime cutscene illustration for a visual-novel RPG. Draw the girl from the first reference image exactly on-model, in the same anime illustration style (clean line art, soft cel shading). The setting is the student council room from the second reference image, painted in the same background-art style.

Character (must match reference 1): a tall, composed girl with very long wavy white hair and one ahoge, two curved pale-lavender dragon horns, pointed ears, a white rose hair ornament, half-lidded violet eyes, small pale-lavender bat-like dragon wings at her back, a white scaled dragon tail, a white long-sleeved blouse with a light-blue ribbon bow at the collar, a black armband on her left upper arm, a black pleated skirt with a light-blue stripe near the hem, black thigh-high stockings.

Moment: early morning, she is alone in the student council room. She stands at the large window on the right half of the frame, seen from behind in three-quarter view so that her profile is visible, looking down at a document. Her left forearm is raised; a slim bracelet-like terminal on her wrist projects a translucent pale-blue holographic document panel floating in the air in front of her — rows of small abstract unreadable glyph lines and one small blank square where an ID photo would be. The hologram lights her face coolly from below. Her expression is distant and unreadable, lips closed, a trace of worry in the eyes.

Room: the long conference table with empty chairs runs through the foreground and left side, stacks of papers on it, whiteboard and bookshelves along the walls, a potted plant by the window, sheer curtains. Pale early-morning sunlight streams through the window with a city skyline outside, throwing long window-frame shadows across the floor and table; dust motes in the light. The rest of the room is in cool shadow, so she is partly silhouetted against the window.

Mood: quiet, lonely, withheld; the calm before something. Palette: cool lavender-grey shadows, soft warm-white window light, a small accent of holographic cyan.

Composition: wide shot, she occupies roughly the right third, full figure down to the knees; her head in the upper third. Keep the bottom quarter of the frame simple (table and floor) since a dialogue box will cover it.

No readable text, no letters, no numbers, no UI overlay, no watermark, no signature, no border. One person only.
```

## 原片

视频模型是 Dreamina 的 Seedance 2.5，「First and last frames」模式只给首帧（上面的原图），5 秒、1080P，比例跟首帧走；出来是 1922×1078、60 帧每秒。每条 455 积分。

### 潜入

```text
Anime cutscene, one continuous first-person shot, keep the illustration's art style and composition exactly. The viewer is falling slowly backwards and downwards out of their dark bedroom into a deep blue digital sea, looking straight up. Camera: sinks slowly away from the room with a very slight roll. Motion: the bedroom above keeps breaking apart into square blocks and flat tiles that tumble away and dissolve into glowing cyan pixels until almost nothing of it is left; the glowing smartphone in the centre drifts slowly upward and away, its white-blue light pulsing; the reaching hand in the foreground sways gently, fingers moving slightly, never grasping the phone; air bubbles, glowing chat-bubble shapes and small file icons stream upward past the camera; dark blue water closes in from the edges. No cuts, no face or body beyond the one arm, no new objects, no text, no subtitles.
```

### 深渊之鲸

```text
Anime cutscene, one continuous calm cinematic shot, keep the illustration's art style, character design, face, outfit and composition exactly. Underwater in a deep blue digital sea: a girl with long dark-blue hair fading to glowing cyan, whale-fin ears and a navy maid-style battle dress stands with one gloved hand over her chest. Camera: very slow push-in toward her. Motion: her long hair and capelet drift in the current; small white-blue light motes flow from the right into the glow at her chest; the pale grey ghostly double of her on the right breaks up into those particles and fades away completely; the ring of water and light around her surges outward; the enormous whale made of light behind her glides slowly upward toward the rippling surface, its constellation lines shimmering; light rays sway; bubbles rise and glowing chat-bubble fragments drift outward. She stays in place, closes her eyes for a moment as the light enters her chest, then opens them and looks ahead, calm and resolved. No cuts, no new characters or objects, no text, no subtitles.
```

### 学生会室的窗前

```text
Anime cutscene, one continuous quiet shot, keep the illustration's art style, character design, outfit and wide composition exactly. Early morning in an empty student council room; a girl with long wavy white hair, dragon horns and small wings stands at the window reading a translucent holographic document floating above her hand. Camera: almost static, a barely perceptible slow push-in, the long table and the room stay in frame. Motion: sheer curtains sway gently, dust motes drift in the sunlight, plant leaves stir, the hologram flickers softly and its lines scroll slowly, a few strands of her hair and the tip of her scaled tail move slightly. She keeps reading: her eyes stay open the whole time, half-lidded, looking down at the document; her expression is distant and unreadable, lips closed, no smile. No cuts, no other people, no new objects, no text, no subtitles.
```

## 制作记录（2026-11-10）

原图每张一次出图就用了。视频先用 Grok Imagine Video 试了一轮（1.5 版，1920×1088、24 帧每秒，旧版只有 1280×704），画面可用，但学生会室那条她从 3.9 秒起一直闭着眼，改提示词重出一条反而 1.8 秒就闭眼，只能截前 3.7 秒。换成 Seedance 2.5 各出一条，三条都一次用上，Grok 的几条没留：

| 片段 | Seedance 2.5 的结果 |
| --- | --- |
| 潜入 | 房间碎成方块退远，手一直伸向手机，最后只剩水面、手机的光和那只手 |
| 深渊之鲸 | 影子在第一秒里碎成一串光点流进她胸口，她闭眼、胸口亮起，再睁眼；镜头推到半身 |
| 学生会室的窗前 | 她全程睁着眼看文件，镜头只是很慢地推近，整间屋子一直在画面里 |

Dreamina 默认在成片左上角打一个「AI」角标，账号的 Settings → Remove watermarks 打开后下载的文件没有；仓库里的三条是没有角标的。这些画面是 AI 生成的，游戏的 README 里写明了。

## 转成游戏用的片段

```bash
python3 game/Tools/cg_clips.py
```

需要 ffmpeg，约 30 秒；后面跟图 ID 只转那几条。脚本对每条原片做这几件事：

1. 截到要用的长度（`CLIPS` 里的第一个数，现在三条都用整条）。
2. 开头按半速放（`CLIPS` 里的第二个数，只有深渊之鲸：前 1.2 秒拉成 2.4 秒。影子碎成光点在原片里只有一秒，太快，看不清）。
3. 把最后 1 秒拉长成 2 秒，速度从原速减到停住。游戏里片段播完就停在最后一帧上，不减速的话镜头会突然刹住。
4. 帧率保持原片的 60 帧每秒：原速的部分每一帧原样通过，放慢的部分相邻两帧混合。
5. 缩到 1920×1080，去掉音轨，H.264 CRF 24 编码成 `<图ID>_motion.mp4`。
6. 把片段的最后一帧存成 `<图ID>.png`：快进剧情时不播片段，直接放这张图，和看完片段后停住的画面是同一帧。

| 片段 | 用的原片 | 成品长度 | 大小 |
| --- | --- | --- | --- |
| `dive_motion.mp4` | 5.02 秒 | 5.98 秒 | 3.1 MB |
| `abyss_whale_motion.mp4` | 5.02 秒 | 7.15 秒 | 5.0 MB |
| `council_window_motion.mp4` | 5.02 秒 | 5.97 秒 | 2.1 MB |

人物形象沿用设定稿的 CC BY-NC-SA 4.0 协议，仅限非商业用途，见根目录 [README](../../README.md#封面与角色形象)。
