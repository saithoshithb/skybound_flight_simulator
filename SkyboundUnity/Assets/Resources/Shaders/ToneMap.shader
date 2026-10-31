// three.js's tone mapping and fog, as full-screen passes. URP's ACES mode is a different curve (it
// greys the sky), and its fog is mixed in before tone mapping, so neither can match.
// See ThreeColor.hlsl for the two orders these passes follow.
Shader "Skybound/ToneMap"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "ThreeColor.hlsl"
        ENDHLSL

        // Everything opaque, before the transparent objects. Pixels at the far plane are sky: no fog.
        Pass
        {
            Name "Opaque"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            half4 frag(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;
                float depth = SampleSceneDepth(input.texcoord);
                #if UNITY_REVERSED_Z
                    bool sky = depth <= 0.0;
                #else
                    bool sky = depth >= 1.0;
                #endif
                float eyeDepth = sky ? 0 : LinearEyeDepth(depth, _ZBufferParams);
                return half4(ThreeFinish(color, eyeDepth), 1.0);
            }
            ENDHLSL
        }

        // After URP's bloom, with effects on: the rest of the browser's EffectComposer.
        Pass
        {
            Name "Composer"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            static const float VIGNETTE_OFFSET = 0.32;
            static const float VIGNETTE_DARKNESS = 0.42;

            half4 frag(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;
                if (_SkyboundComposer > 0.5)
                {
                    float2 coord = (input.texcoord - 0.5) * VIGNETTE_OFFSET;
                    color = lerp(color, 1.0 - VIGNETTE_DARKNESS, dot(coord, coord));
                    color = ThreeAces(color);
                }
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
