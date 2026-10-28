// The UI transitions' filters (UI/Elements/FxSurface.cs): a picture (a grab of the screen, or one of
// Art/Transitions) drawn with Graphics.Blit into a texture an element shows. In order: CRT collapse, glitch
// slices, zoom and spin, pixelation, the picture's own motion (an endless fall into its centre, or columns
// raining down), RGB split or zoom blur, static, tint, light running along its bright lines, an added
// overlay, rising data bits, radial speed lines, scanlines, vignette, then a mask that clears the picture
// away with a lit edge. Sizes are in frame pixels (_Res), so a 24 px block is the same on screen whatever
// the texture's resolution.
Shader "SystemPrompt/ScreenFx"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "black" {}
        _MainRect ("Picture rect (offset xy, scale zw)", Vector) = (0,0,1,1)
        _Res ("Surface size in frame px", Vector) = (1920,1080,0,0)
        _Noise ("Noise (R fbm, G white, B fbm)", 2D) = "gray" {}
        _Over ("Overlay", 2D) = "black" {}
        _OverRect ("Overlay rect", Vector) = (0,0,1,1)
        _OverAmount ("Overlay amount", Float) = 0
        _Tint ("Tint", Color) = (1,1,1,1)
        _Add ("Added colour", Color) = (0,0,0,0)
        _Desat ("Desaturate", Range(0,1)) = 0
        _Zoom ("Zoom", Float) = 1
        _Spin ("Spin (radians)", Float) = 0
        _Center ("Zoom centre (uv)", Vector) = (0.5,0.5,0,0)
        _Blur ("Zoom blur", Range(0,1)) = 0
        _Pixel ("Pixel size", Float) = 1
        _Split ("RGB split", Float) = 0
        _Slice ("Glitch slices", Float) = 0
        _Seed ("Glitch seed", Float) = 0
        _Scan ("Scanlines", Range(0,1)) = 0
        _Static ("Static", Range(0,1)) = 0
        _Vignette ("Vignette", Range(0,1)) = 0
        _Pulse ("Current along the picture", Float) = 0
        _Fly ("Fall into the centre (zoom doublings per second)", Float) = 0
        _FlyAdd ("Fall: 1 adds the copies (pictures on black), 0 blends them", Float) = 0
        _Rain ("Columns raining down (picture heights per second)", Float) = 0
        _RainColumns ("Rain columns", Float) = 48
        _Current ("Light packets along bright lines", Float) = 0
        _CurrentColor ("Packet colour", Color) = (0.6,1,0.8,1)
        _Bits ("Data bits", Float) = 0
        _BitsSpeed ("Bits speed (px per second, + up)", Float) = 160
        _BitsColor ("Bits colour", Color) = (0.4,1,0.7,1)
        _Streaks ("Radial speed lines", Float) = 0
        _StreakSpeed ("Speed lines' speed", Float) = 1
        _StreakColor ("Speed lines' colour", Color) = (0.8,0.95,1,1)
        _Collapse ("CRT collapse", Range(0,1)) = 0
        _Mode ("Mask: 0 none, 1 dissolve, 2 radial, 3 blocks, 4 sweep down, 5 sweep up, 6 soft open", Float) = 0
        _Reveal ("Mask progress", Range(0,1)) = 0
        _Block ("Mask block size", Float) = 64
        _Edge ("Mask edge colour", Color) = (0.25,0.75,0.5,1)
        _EdgeWidth ("Mask edge width", Float) = 0.06
        _FxTime ("Time", Float) = 0
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _Noise, _Over;
            float4 _MainRect, _Res, _OverRect, _Tint, _Add, _Center, _Edge;
            float4 _CurrentColor, _BitsColor, _StreakColor;
            float _OverAmount, _Desat, _Zoom, _Spin, _Blur, _Pixel, _Split, _Slice, _Seed, _Scan, _Static,
                _Vignette, _Pulse, _Collapse, _Mode, _Reveal, _Block, _EdgeWidth, _FxTime,
                _Fly, _FlyAdd, _Rain, _RainColumns, _Current, _Bits, _BitsSpeed, _Streaks, _StreakSpeed;

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float3 Pic(float2 uv) { return tex2D(_MainTex, _MainRect.xy + uv * _MainRect.zw).rgb; }

            // The picture in motion. Falling: two copies a zoom step apart grow out of the centre and
            // crossfade, so the fall never ends. Rain: the picture cut into columns, each sliding down at its
            // own speed (the textures repeat).
            float3 Moving(float2 q)
            {
                if (_Fly > 0)
                {
                    float3 acc = 0;
                    float sum = 0;
                    for (int n = 0; n < 2; n++)
                    {
                        float f = frac(_FxTime * _Fly * 0.5 + n * 0.5);
                        float w = sin(f * UNITY_PI);
                        acc += Pic(_Center.xy + (q - _Center.xy) / exp2(f * 2)) * w;
                        sum += w;
                    }
                    return _FlyAdd > 0.5 ? acc : acc / max(sum, 0.001);
                }
                if (_Rain > 0)
                {
                    float column = floor(q.x * _RainColumns);
                    q.y += _FxTime * _Rain * (0.35 + 0.9 * Hash(float2(column, 4.2)));
                }
                return Pic(q);
            }

            // Light running along the picture's bright lines: packets in lanes of rows running sideways and
            // of columns running up and down, each lane at its own speed with a comet tail behind, and rings of
            // power spreading out from the centre.
            float Packets(float2 p)
            {
                float lane = 28;
                float2 cell = floor(p / lane);
                float2 u = p / _Res.xy;
                float hr = Hash(float2(cell.y, 1.3)), hc = Hash(float2(cell.x, 2.9));
                float dr = hr > 0.5 ? 1 : -1, dc = hc > 0.5 ? 1 : -1;
                float headX = frac(hr * 7.1 + _FxTime * (0.2 + 0.5 * Hash(float2(cell.y, 5.7))) * dr);
                float headY = frac(hc * 3.7 + _FxTime * (0.2 + 0.5 * Hash(float2(cell.x, 6.3))) * dc);
                float x = exp(-frac((headX - u.x) * dr * 2) * 14) * step(0.45, Hash(float2(cell.y, 8.8)));
                float y = exp(-frac((headY - u.y) * dc * 2) * 8) * step(0.45, Hash(float2(cell.x, 9.9)));
                float ring = pow(frac(length((p - _Center.xy * _Res.xy) / _Res.y) * 0.9 - _FxTime * 0.7), 6);
                return max(max(x, y), ring);
            }

            // Little blocks of data drifting up (or down), some flickering.
            float Bits(float2 p)
            {
                float size = 36;
                float column = floor(p.x / size);
                float speed = _BitsSpeed * (0.5 + Hash(float2(column, 5.5)));
                float y = p.y - _FxTime * speed;
                float2 cell = float2(column, floor(y / size));
                float h = Hash(cell + 0.21);
                if (h < 0.86) return 0;
                float2 local = float2(frac(p.x / size), frac(y / size)) * size - size * 0.5;
                float side = 3 + 6 * Hash(cell + 0.67);
                float wide = side * (Hash(cell + 0.93) > 0.7 ? 3 : 1);
                float on = step(abs(local.x), wide * 0.5) * step(abs(local.y), side * 0.5);
                return on * (0.45 + 0.55 * step(0.5, frac(_FxTime * (2 + 6 * h) + h * 9)));
            }

            // Warp-speed lines shooting out from the centre.
            float Streaks(float2 p)
            {
                float2 d = (p - _Center.xy * _Res.xy) / _Res.y;
                float r = length(d) / 1.05;
                float a = atan2(d.y, d.x) / (2 * UNITY_PI) + 0.5;
                float bins = 240;
                float b = floor(a * bins);
                float h = Hash(float2(b, 3.3));
                float head = frac(h * 5 + _FxTime * _StreakSpeed * (0.5 + 1.2 * Hash(float2(b, 8.1))));
                float t = (head - r) / (0.06 + 0.3 * Hash(float2(b, 1.7)));
                float streak = step(0.5, h) * step(0, t) * (1 - saturate(t));
                streak *= 1 - smoothstep(0.1, 0.55, abs(frac(a * bins) - 0.5) * 2);
                return streak * smoothstep(0.04, 0.3, r) * r * 1.6;
            }

            // Where the mask is at this pixel: hidden below the threshold, lit just above it.
            float MaskField(float2 uv, float2 p)
            {
                if (_Mode < 1.5) return tex2D(_Noise, uv * float2(_Res.x / _Res.y, 1) * 1.4).r;
                if (_Mode < 2.5)
                {
                    // A ring opening from the centre, stepped into 24 px blocks with a ragged rim.
                    float2 cell = floor(p / 24) * 24 + 12;
                    float2 d = (cell - _Center.xy * _Res.xy) / _Res.y;
                    float far = length(float2(0.5 * _Res.x / _Res.y, 0.5)) + 0.05;
                    return saturate(length(d) / far + (Hash(floor(p / 24)) - 0.5) * 0.06);
                }
                if (_Mode < 3.5) return Hash(floor(p / _Block) + 0.37);
                if (_Mode > 5.5)
                {
                    // A ring opening from the centre, its rim pushed in and out by the clouds.
                    float2 d = (p - _Center.xy * _Res.xy) / _Res.y;
                    float far = length(float2(0.5 * _Res.x / _Res.y, 0.5)) + 0.05;
                    float clouds = tex2D(_Noise, uv * float2(_Res.x / _Res.y, 1) * 1.1).r;
                    return saturate(length(d) / far * 0.8 + clouds * 0.2);
                }
                float row = (Hash(float2(floor(p.y / 6), _Seed)) - 0.5) * 0.03;
                return (_Mode < 4.5 ? 1 - uv.y : uv.y) * 0.97 + 0.015 + row;
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;

                // CRT power-off: the picture squashes into a bright line, then the line into a dot.
                float flare = 0;
                if (_Collapse > 0)
                {
                    float sy = 1 - smoothstep(0, 0.6, _Collapse) * 0.996;
                    float sx = 1 - smoothstep(0.6, 1, _Collapse) * 0.998;
                    uv = (uv - 0.5) / float2(sx, sy) + 0.5;
                    if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return fixed4(0, 0, 0, 1);
                    flare = smoothstep(0.25, 0.6, _Collapse);
                }

                float2 p0 = uv * _Res.xy;
                float2 p = p0;

                // Glitch slices: bands of rows of random height thrown sideways.
                if (_Slice > 0)
                {
                    float h = lerp(6, 70, Hash(float2(floor(p.y / 40), _Seed)));
                    float band = floor(p.y / h);
                    float on = step(0.6, Hash(float2(band, _Seed + 3.1)));
                    p.x += on * (Hash(float2(band, _Seed + 7.7)) - 0.5) * 2 * _Slice;
                }

                float2 c = _Center.xy * _Res.xy;
                float2 d = p - c;
                float s = sin(_Spin), k = cos(_Spin);
                d = float2(d.x * k - d.y * s, d.x * s + d.y * k) / _Zoom;
                p = c + d;
                if (_Pixel > 1) p = (floor(p / _Pixel) + 0.5) * _Pixel;
                float2 q = p / _Res.xy;
                float2 off = float2(_Split / _Res.x, 0);

                float3 col;
                if (_Blur > 0)
                {
                    // Zoom blur toward the centre, the colours fanned apart like a lens.
                    float3 acc = 0;
                    for (int n = 0; n < 8; n++)
                    {
                        float f = 1 - _Blur * 0.3 * n / 7.0;
                        float2 t = _Center.xy + (q - _Center.xy) * f;
                        acc += float3(Moving(t + off).r, Moving(t).g, Moving(t - off).b);
                    }
                    col = acc / 8;
                }
                else col = float3(Moving(q + off).r, Moving(q).g, Moving(q - off).b);
                float lines = _Current > 0 ? smoothstep(0.06, 0.3, dot(col, float3(0.299, 0.587, 0.114))) : 0;
                if (_Rain > 0)
                {
                    // Each column has bright heads with fading trails, falling faster than the text.
                    float column = floor(q.x * _RainColumns);
                    float fall = _FxTime * _Rain * (0.35 + 0.9 * Hash(float2(column, 4.2))) * 2.5;
                    float trail = frac((q.y + fall) * 1.3 + Hash(float2(column, 9.1)));
                    col *= 0.3 + 1.6 * pow(1 - trail, 3);
                }

                if (_Static > 0)
                {
                    float n = tex2D(_Noise, uv * _Res.xy / 512 + frac(_FxTime * float2(13.7, 7.3))).g;
                    col = lerp(col, n.xxx, _Static);
                }
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, lum.xxx, _Desat) * _Tint.rgb + _Add.rgb;
                if (_Pulse > 0)
                {
                    // A band of current running diagonally across (the circuit board lighting up).
                    float b = frac(q.x * 0.7 + q.y * 0.35 - _FxTime * 0.55);
                    col += col * _Pulse * pow(1 - abs(b - 0.5) * 2, 10);
                }
                if (_Current > 0) col += _CurrentColor.rgb * _Current * lines * Packets(p0);
                col += tex2D(_Over, _OverRect.xy + q * _OverRect.zw).rgb * _OverAmount;
                if (_Bits > 0) col += _BitsColor.rgb * _Bits * Bits(p0);
                if (_Streaks > 0) col += _StreakColor.rgb * _Streaks * Streaks(p0);
                if (_Scan > 0)
                {
                    col *= 1 - _Scan * 0.45 * (0.5 + 0.5 * sin(p0.y * UNITY_PI * 0.5));
                    col *= 1 + _Scan * 0.35 * smoothstep(0.9, 1, frac(uv.y * 1.3 - _FxTime * 0.6));
                }
                col *= 1 - _Vignette * saturate(dot(uv - 0.5, uv - 0.5) * 2.4);
                col = lerp(col, float3(0.9, 1, 0.95), flare);

                float a = 1;
                if (_Mode > 0.5)
                {
                    float v = MaskField(uv, p0);
                    float r = lerp(-_EdgeWidth, 1, _Reveal);
                    if (_Mode > 5.5)
                    {
                        // Soft: fading out across the rim, which glows in the edge colour on the way.
                        a = smoothstep(r, r + _EdgeWidth, v);
                        col = lerp(col, _Edge.rgb, a * (1 - a) * 2.4);
                    }
                    else
                    {
                        a = step(r + _EdgeWidth, v);
                        float edge = step(r, v) - a;
                        col = lerp(col, _Edge.rgb, edge);
                        a = max(a, edge);
                    }
                }
                return fixed4(saturate(col), a);
            }
            ENDCG
        }
    }
}
