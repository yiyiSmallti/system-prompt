// Turns a Live2D portrait render (premultiplied alpha, as Cubism draws) into the straight alpha that
// UI Toolkit images expect. See Live2DPortrait.cs.
Shader "Hidden/SystemPrompt/Live2DUnpremultiply"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            fixed4 frag (v2f_img i) : SV_Target
            {
                half4 c = tex2D(_MainTex, i.uv);
                c.rgb = saturate(c.rgb / max(c.a, 1.0 / 255.0));
                return c;
            }
            ENDCG
        }
    }
}
