// A whole field of three.js Sprites in one mesh. Each puff's four corners hold its centre, so
// the vertex shader can spread them in view space, which is how a Sprite faces the camera.
// Drawn after the ToneMap pass, so it does its own tone mapping and fog.
Shader "Skybound/Cloud"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "Cloud"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ThreeColor.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION; // the puff's centre
                float2 uv : TEXCOORD0;
                float2 offset : TEXCOORD1; // this corner from the centre, in metres
                half4 color : COLOR; // linear shade, opacity in alpha
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float eyeDepth : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 view = TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz));
                view.xy += input.offset;
                output.positionCS = TransformWViewToHClip(view);
                output.uv = input.uv;
                output.color = input.color;
                output.eyeDepth = -view.z;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * input.color;
                return half4(ThreeFinish(color.rgb, input.eyeDepth), color.a);
            }
            ENDHLSL
        }
    }
}
