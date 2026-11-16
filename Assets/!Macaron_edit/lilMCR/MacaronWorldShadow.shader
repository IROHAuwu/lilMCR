Shader "Hidden/liltoon_MCR/WorldShadow"
{
    Properties
    {
        [HideInInspector] _CaptureKey ("Capture owner key", Float) = 0
        _ShadowMap ("Depth", 2D) = "white" {}
        _Strength ("Strength", Range(0,1)) = 0.4
        _Tint ("Transmission", Color) = (0.35,0.38,0.43,1)
        _Softness ("Softness (texels)", Range(0,6)) = 1.5
        _Bias ("Depth bias (m)", Float) = 0.015
        _Extent ("Half width", Float) = 2
        _Near ("Near", Float) = 0.01
        _Far ("Far", Float) = 8
        _EdgeFade ("Edge fade", Range(0.01,0.3)) = 0.08
        [Toggle] _FlipY ("Flip capture Y", Float) = 0
        [HideInInspector] _ReceiverMask ("Avatar exclusion bit", Float) = 128
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest+49" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            Blend DstColor Zero
            Cull Front
            ZWrite Off
            ZTest LEqual
            // Marked avatar pixels keep their own lilMCR/PCSS shading. Only receivers
            // without this bit are multiplied; other stencil bits are left untouched.
            Stencil { Ref [_ReceiverMask] ReadMask [_ReceiverMask] Comp NotEqual Pass Keep }
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            UNITY_DECLARE_SCREENSPACE_TEXTURE(_CameraDepthTexture);
            sampler2D _ShadowMap;
            float4 _ShadowMap_TexelSize;
            float4 _Tint;
            float _CaptureKey;
            float _Strength, _Softness, _Bias, _Extent, _Near, _Far, _EdgeFade, _FlipY;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float4 screen : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                // VRChat may put avatar children on Player layers. Suppress our volume in
                // its own capture even then, avoiding a read/write feedback on the depth RT.
                if(unity_OrthoParams.w > 0.5 && distance(_WorldSpaceCameraPos, unity_ObjectToWorld._m03_m13_m23) < 0.001)
                    o.pos = float4(0,0,0,1);
                // Keep this eye-local; apply the stereo texture rectangle exactly once in frag.
                o.screen = ComputeNonStereoScreenPos(o.pos);
                return o;
            }
            float sampleShadow(float2 uv, float receiver)
            {
                if(any(uv <= 0) || any(uv >= 1)) return 0;
                float4 data = tex2D(_ShadowMap, uv);
                // Ordinary avatar/world colors cannot become depth samples. A stale map
                // also stops casting if VRChat removed or disabled its capture camera.
                if(_CaptureKey < 1 || abs(data.g + _CaptureKey) > 0.25 || abs(data.b + 1 + _Time.y) > 0.25) return 0;
                float caster = lerp(_Near, _Far, data.r);
                return data.r >= 0 && data.r < 0.99999 && receiver > caster + _Bias;
            }
            struct FragmentOutput { half4 color : SV_Target; float depth : SV_Depth; };
            FragmentOutput frag(v2f i)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.screen.xy / i.screen.w;
                // One read: deviceDepth is written back unchanged as SV_Depth, raw is converted for the projection.
                float deviceDepth = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CameraDepthTexture, UnityStereoTransformScreenSpaceTex(uv)).r;
                float raw = deviceDepth;
                #if defined(UNITY_REVERSED_Z)
                    if(raw < 0.000001) discard;
                #else
                    if(raw > 0.999999) discard;
                    raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float2 ndc = uv * 2 - 1;
                ndc.y *= _ProjectionParams.x;
                // Solve the active GPU projection rather than mixing raw device depth with
                // unity_CameraInvProjection (which uses a different depth convention).
                float3 view;
                if(unity_OrthoParams.w > 0.5)
                {
                    view.xy = (ndc - float2(UNITY_MATRIX_P._m03, UNITY_MATRIX_P._m13)) / float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11);
                    view.z = (raw - dot(float2(UNITY_MATRIX_P._m20,UNITY_MATRIX_P._m21),view.xy) - UNITY_MATRIX_P._m23) / UNITY_MATRIX_P._m22;
                }
                else
                {
                    float2 ray = -(ndc + float2(UNITY_MATRIX_P._m02,UNITY_MATRIX_P._m12)) / float2(UNITY_MATRIX_P._m00,UNITY_MATRIX_P._m11);
                    view.z = -UNITY_MATRIX_P._m23 / (raw + dot(float2(UNITY_MATRIX_P._m20,UNITY_MATRIX_P._m21),ray) + UNITY_MATRIX_P._m22);
                    view.xy = ray * view.z;
                }
                float3 world = mul(UNITY_MATRIX_I_V, float4(view, 1)).xyz;
                float3 p = mul(unity_WorldToObject, float4(world,1)).xyz;
                float2 suv = p.xy / (2 * _Extent) + 0.5;
                if(any(suv <= 0) || any(suv >= 1) || p.z <= _Near || p.z >= _Far) discard;
                float edge = min(min(suv.x, suv.y), min(1-suv.x, 1-suv.y));
                float fade = smoothstep(0, _EdgeFade, edge) * (1-smoothstep(_Far*0.85, _Far, p.z));
                suv.y = lerp(suv.y, 1-suv.y, _FlipY);
                float2 texel = abs(_ShadowMap_TexelSize.xy) * _Softness;
                float shadow = 0;
                if(_Softness <= 0.0)
                    shadow = sampleShadow(suv, p.z) * 16.0;
                else
                {
                // A bounded 9-tap tent filter for nonzero softness.
                [unroll] for(int y=-1; y<=1; y++)
                [unroll] for(int x=-1; x<=1; x++)
                    shadow += sampleShadow(suv + float2(x,y)*texel, p.z) * (x==0?2:1) * (y==0?2:1);
                }
                shadow *= 0.0625 * saturate(_Strength) * fade;
                FragmentOutput output;
                output.color = half4(lerp(1.0.xxx, saturate(_Tint.rgb), shadow), 1);
                // Compare reconstructed receiver depth to the LIVE depth buffer.
                // Missing/stale background depth must not draw over foreground geometry.
                output.depth = deviceDepth;
                return output;
            }
            ENDCG
        }
    }
    Fallback Off
}
