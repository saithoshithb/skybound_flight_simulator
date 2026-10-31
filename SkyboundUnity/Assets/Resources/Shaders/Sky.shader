// The three.js Sky object (Preetham's daylight model, three/examples/jsm/objects/Sky.js) as a
// Unity skybox. Same constants and steps; the sun direction comes in Unity's coordinates.
Shader "Skybound/Sky"
{
    Properties
    {
        _SunPosition("Sun Position", Vector) = (0, 1000, 0, 0)
        _Turbidity("Turbidity", Float) = 2
        _Rayleigh("Rayleigh", Float) = 1
        _MieCoefficient("Mie Coefficient", Float) = 0.005
        _MieDirectionalG("Mie Directional G", Float) = 0.8
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off
        Pass
        {
            Name "Sky"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _SunPosition;
                float _Turbidity;
                float _Rayleigh;
                float _MieCoefficient;
                float _MieDirectionalG;
            CBUFFER_END

            static const float3 UP = float3(0.0, 1.0, 0.0);
            // Wavelength of the primaries (680, 550, 450 nm), already worked through.
            static const float3 TOTAL_RAYLEIGH = float3(5.804542996261093E-6, 1.3562911419845635E-5, 3.0265902468824876E-5);
            static const float3 MIE_CONST = float3(1.8399918514433978E14, 2.7798023919660528E14, 4.0790479543861094E14);
            static const float CUTOFF_ANGLE = 1.6110731556870734;
            static const float STEEPNESS = 1.5;
            static const float EE = 1000.0;
            static const float RAYLEIGH_ZENITH_LENGTH = 8.4E3;
            static const float MIE_ZENITH_LENGTH = 1.25E3;
            static const float SUN_ANGULAR_DIAMETER_COS = 0.99995667694644844;
            static const float THREE_OVER_SIXTEENPI = 0.05968310365946075;
            static const float ONE_OVER_FOURPI = 0.07957747154594767;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                float3 sunDirection : TEXCOORD1;
                float sunfade : TEXCOORD2;
                float3 betaR : TEXCOORD3;
                float3 betaM : TEXCOORD4;
                float sunE : TEXCOORD5;
            };

            float SunIntensity(float zenithAngleCos)
            {
                zenithAngleCos = clamp(zenithAngleCos, -1.0, 1.0);
                return EE * max(0.0, 1.0 - exp(-((CUTOFF_ANGLE - acos(zenithAngleCos)) / STEEPNESS)));
            }

            float3 TotalMie(float T)
            {
                float c = (0.2 * T) * 10E-18;
                return 0.434 * c * MIE_CONST;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                output.sunDirection = normalize(_SunPosition.xyz);
                output.sunE = SunIntensity(dot(output.sunDirection, UP));
                output.sunfade = 1.0 - clamp(1.0 - exp(_SunPosition.y / 450000.0), 0.0, 1.0);
                float rayleighCoefficient = _Rayleigh - (1.0 * (1.0 - output.sunfade));
                output.betaR = TOTAL_RAYLEIGH * rayleighCoefficient;
                output.betaM = TotalMie(_Turbidity) * _MieCoefficient;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);

                // optical length: cutoff angle at 90 to avoid singularity
                float zenithAngle = acos(max(0.0, dot(UP, direction)));
                float inverse = 1.0 / (cos(zenithAngle) + 0.15 * pow(max(93.885 - ((zenithAngle * 180.0) / PI), 1e-4), -1.253));
                float sR = RAYLEIGH_ZENITH_LENGTH * inverse;
                float sM = MIE_ZENITH_LENGTH * inverse;

                // combined extinction factor
                float3 Fex = exp(-(input.betaR * sR + input.betaM * sM));

                // in-scattering
                float cosTheta = dot(direction, input.sunDirection);
                float rPhase = THREE_OVER_SIXTEENPI * (1.0 + pow(cosTheta * 0.5 + 0.5, 2.0));
                float3 betaRTheta = input.betaR * rPhase;
                float g2 = _MieDirectionalG * _MieDirectionalG;
                float mPhase = ONE_OVER_FOURPI * ((1.0 - g2) / pow(1.0 - 2.0 * _MieDirectionalG * cosTheta + g2, 1.5));
                float3 betaMTheta = input.betaM * mPhase;

                float3 scatter = input.sunE * ((betaRTheta + betaMTheta) / (input.betaR + input.betaM));
                float3 Lin = pow(max(scatter * (1.0 - Fex), 0.0), 1.5);
                Lin *= lerp(float3(1.0, 1.0, 1.0), pow(max(scatter * Fex, 0.0), 0.5), clamp(pow(1.0 - dot(UP, input.sunDirection), 5.0), 0.0, 1.0));

                // nightsky
                float3 L0 = 0.1 * Fex;

                // composition + solar disc
                float sundisk = smoothstep(SUN_ANGULAR_DIAMETER_COS, SUN_ANGULAR_DIAMETER_COS + 0.00002, cosTheta);
                L0 += (input.sunE * 19000.0 * Fex) * sundisk;

                float3 texColor = (Lin + L0) * 0.04 + float3(0.0, 0.0003, 0.00075);
                float3 retColor = pow(texColor, 1.0 / (1.2 + (1.2 * input.sunfade)));
                return half4(retColor, 1.0);
            }
            ENDHLSL
        }
    }
}
