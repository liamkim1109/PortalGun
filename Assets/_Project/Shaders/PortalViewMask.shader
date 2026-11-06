Shader "Portal/PortalViewMask"
{
    Properties
    {
        _BaseMap ("Portal View", 2D) = "black" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.08)) = 0.015
        _ApertureAspect ("Aperture Aspect", Float) = 0.6
        _TextureAspect ("Texture Aspect", Float) = 1.78
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseMap_TexelSize;
                half4 _BaseColor;
                float _EdgeSoftness;
                float _ApertureAspect;
                float _TextureAspect;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 maskUV : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.maskUV = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 ellipsePosition = (input.maskUV - 0.5) * 2.0;
                float ellipseDistance = dot(ellipsePosition, ellipsePosition);
                half mask = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0 + _EdgeSoftness, ellipseDistance);

                // Keep the image's aspect ratio positive. RenderTexture texel sizes
                // can have a negative Y value on some graphics APIs to signal UV flip.
                float textureAspect = max(_TextureAspect, 0.001);
                float2 imageUV = input.uv;
                if (textureAspect > _ApertureAspect)
                    imageUV.x = 0.5 + (imageUV.x - 0.5) * (_ApertureAspect / textureAspect);
                else
                    imageUV.y = 0.5 + (imageUV.y - 0.5) * (textureAspect / _ApertureAspect);

                // The portal surface faces the viewer with the opposite horizontal
                // basis, so flip U to keep the remote world from appearing mirrored.
                imageUV.x = 1.0 - imageUV.x;

                #if UNITY_UV_STARTS_AT_TOP
                if (_BaseMap_TexelSize.y < 0.0)
                    imageUV.y = 1.0 - imageUV.y;
                #endif

                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, imageUV) * _BaseColor;
                color.a *= mask;
                return color;
            }
            ENDHLSL
        }
    }
}
