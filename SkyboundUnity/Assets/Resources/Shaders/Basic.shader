// three.js MeshBasicMaterial: a flat colour, not lit. Opaque ones get tone mapping and fog from
// the ToneMap pass. Transparent ones are drawn after it, so they do it themselves. Those with
// three's toneMapped={false} are also drawn after it, and don't, which leaves their colour exact.
Shader "Skybound/Basic"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [HideInInspector] _SelfToneMap("Tone map here", Float) = 0
        [HideInInspector] _SrcBlend("Src Blend", Float) = 1
        [HideInInspector] _DstBlend("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite("Z Write", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Name "Basic"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ThreeColor.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float _SelfToneMap;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float eyeDepth : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 view = TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz));
                output.positionCS = TransformWViewToHClip(view);
                output.eyeDepth = -view.z;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 color = _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                if (_SelfToneMap > 0.5) color = ThreeFinish(color, input.eyeDepth);
                return half4(color, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
