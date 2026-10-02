// HS/Toon — shared stylised shader that unifies every asset source (GDD §2: "a toon shader and shared palette unify
// the look"). Stepped main light + toonified shadows, SH ambient (+SSAO), Forward+ additional lights, rim light for
// silhouette readability at the fixed camera, screen-constant inverted-hull outline, hit flash and a blocky
// "glitch" dissolve used by the Curator. SRP Batcher compatible (all per-material data in UnityPerMaterial).
Shader "HS/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ShadeColor ("Shade Tint", Color) = (0.42,0.44,0.58,1)
        _ShadeThreshold ("Shade Threshold", Range(-1,1)) = 0.02
        _ShadeSoftness ("Shade Softness", Range(0.001,0.5)) = 0.05
        _AmbientStrength ("Ambient Strength", Range(0,2)) = 0.55
        _DirectStrength ("Direct Strength", Range(0,2)) = 0.9
        _RimColor ("Rim Color", Color) = (1,0.94,0.82,1)
        _RimPower ("Rim Power", Range(0.5,8)) = 3.2
        _RimStrength ("Rim Strength", Range(0,1)) = 0.28
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,0)
        _EmissionTexMul ("Emission x Base Map", Range(0,1)) = 0
        _OutlineColor ("Outline Color", Color) = (0.07,0.06,0.08,1)
        _OutlineWidth ("Outline Width (px)", Range(0,6)) = 1.4
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _FlashAmount ("Flash Amount", Range(0,1)) = 0
        _Dissolve ("Glitch Dissolve", Range(0,1)) = 0
        _Desaturate ("Desaturate", Range(0,1)) = 0
        _VertexColorMul ("Vertex Color Tint", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "UniversalMaterialType"="SimpleLit" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadeColor;
            half _ShadeThreshold;
            half _ShadeSoftness;
            half _AmbientStrength;
            half _DirectStrength;
            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
            half4 _EmissionColor;
            half _EmissionTexMul;
            half4 _OutlineColor;
            half _OutlineWidth;
            half _Cutoff;
            half4 _FlashColor;
            half _FlashAmount;
            half _Dissolve;
            half _Desaturate;
            half _VertexColorMul;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        // Blocky world-space hash for the "glitch" dissolve (fits the System's red-glyph glitch language).
        float GlitchHash(float3 p)
        {
            p = floor(p * 14.0);
            return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
        }

        void GlitchClip(float3 positionWS)
        {
            if (_Dissolve > 0.001)
                clip(GlitchHash(positionWS) - _Dissolve);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                half4 color : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.color = input.color;
                return o;
            }

            half Band(half ndotl)
            {
                return smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, ndotl);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                GlitchClip(input.positionWS);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = tex.rgb * _BaseColor.rgb * lerp(half3(1, 1, 1), input.color.rgb, _VertexColorMul);
                #if defined(_ALPHATEST_ON)
                    clip(tex.a * _BaseColor.a - _Cutoff);
                #endif

                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = v;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half ao = 1;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
                    ao = aoFactor.indirectAmbientOcclusion;
                #endif

                Light mainLight = GetMainLight(inputData.shadowCoord, input.positionWS, inputData.shadowMask);
                half ndotl = dot(n, mainLight.direction);
                half shadow = smoothstep(0.3, 0.7, mainLight.shadowAttenuation);
                half lit = Band(ndotl) * shadow;

                half3 ambient = SampleSH(n) * _AmbientStrength * ao;
                half3 direct = mainLight.color * lit * _DirectStrength;
                half3 shade = _ShadeColor.rgb * (1 - lit) * 0.35 * ao;
                half3 color = albedo * (direct + ambient + shade);

                #if defined(_ADDITIONAL_LIGHTS)
                    uint pixelLightCount = GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP
                    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                    {
                        Light dl = GetAdditionalLight(lightIndex, input.positionWS, inputData.shadowMask);
                        color += albedo * dl.color * Band(dot(n, dl.direction)) * dl.distanceAttenuation * dl.shadowAttenuation;
                    }
                    #endif
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light l = GetAdditionalLight(lightIndex, input.positionWS, inputData.shadowMask);
                        half atten = l.distanceAttenuation * l.shadowAttenuation;
                        half band = smoothstep(0.0, 0.12, dot(n, l.direction));
                        color += albedo * l.color * atten * (0.35 + 0.65 * band);
                    LIGHT_LOOP_END
                #endif

                half rim = pow(saturate(1 - dot(n, v)), _RimPower) * _RimStrength;
                color += _RimColor.rgb * rim * (0.35 + 0.65 * lit) * mainLight.color;
                // lit signs, screens and windows glow in their own colours (0 = a flat emission colour)
                color += _EmissionColor.rgb * lerp(half3(1, 1, 1), tex.rgb, _EmissionTexMul);

                if (_Desaturate > 0.001)
                {
                    half g = dot(color, half3(0.299, 0.587, 0.114));
                    color = lerp(color, half3(g, g, g) * half3(0.92, 0.95, 1.05), _Desaturate);
                }
                color = lerp(color, _FlashColor.rgb, _FlashAmount);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings OutlineVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float4 positionCS = TransformWorldToHClip(positionWS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 normalCS = mul((float3x3)GetWorldToHClipMatrix(), normalWS).xy;
                float len = max(length(normalCS), 1e-4);
                // Constant pixel width regardless of distance; clamp so far objects don't balloon.
                float2 offset = (normalCS / len) * (_OutlineWidth * 2.0 / _ScreenParams.y) * positionCS.w;
                offset.x *= _ScreenParams.y / _ScreenParams.x;
                positionCS.xy += offset * (_OutlineWidth > 0.001 ? 1 : 0);
                o.positionCS = positionCS;
                o.positionWS = positionWS;
                o.fogFactor = ComputeFogFactor(positionCS.z);
                return o;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                if (_OutlineWidth <= 0.001) discard;
                GlitchClip(input.positionWS);
                half3 c = lerp(_OutlineColor.rgb, _FlashColor.rgb, _FlashAmount * 0.5);
                return half4(MixFog(c, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                o.positionCS = ApplyShadowClamping(positionCS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.positionWS = positionWS;
                return o;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                GlitchClip(input.positionWS);
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                GlitchClip(input.positionWS);
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
                #endif
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DNVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 DNFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                GlitchClip(input.positionWS);
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
                #endif
                float3 n = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    float2 remapped = saturate(oct * 0.5 + 0.5);
                    return half4(PackFloat2To888(remapped), 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
