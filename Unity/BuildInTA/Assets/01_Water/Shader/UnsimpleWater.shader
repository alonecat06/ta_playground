Shader "SimpleWater/UnsimpleWater"
{
    Properties
    {
        [Normal]_NormalTex ("Normal map", 2D) = "bump" {}
        _DepthTex ("Depth map", 2D) = "red" {}
        _ReflectionTex ("Reflection map", 2D) = "white" {}
        _BeachColor ("Color on shallow beach RGBA", color) = (0.0122, 0.5, 0.1, 0.83)
        _WaterColor ("Color on middle beach RGBA", color) = (0.5, 0.5, 0.5, 1)
        _DeepWaterColor ("Deep water color", color) = (0.5, 0.5, 0.5, 1)

        _d00("End of shallow beach", float) = 0.0
        _d10("Start of middle beach", float) = 0.1
        _d11("End of middle beach", float) = 0.4
        _d20("Start of deep water", float) = 0.9
        
        [Space]
        _Speed("velocity parameter (x: wind, y: wave)", vector) = (0, 0, 0, 0)

        [Space]
        _NormalScale("Normal scale", Range(0, 1)) = 1
        _Opacity("Opacity", Range(0, 1)) = 0.95
        _FresnelCol("Fresnel color", color) = (0.1195, 0.6376, 0.6724, 1)
        _ReflectionIntensity("Reflection intensity", Range(0, 1)) = 0.5
        _ReflectionDistortion("Reflection distortion", Range(0, 1)) = 0.5
        _Specular("Specular", float) = 0
        _SpecColor("Specular color", color) = (0.4, 0.4, 0.4, 1)

        [HideInInspector] _WaveColor("Wave color", color) = (0.2, 0.2, 0.2, 1)
        _LightWrapping("Light parameter", float) = 1

		// caustic begin
        _CausticTexture("Caustic texture", 2D) = "white" {}
        _CausticTint("Caustic color", color) = (1, 1, 1, 1)
		// caustic end

		// wave begin
        _WaveSpeed("Wave speed", float) = -12.64
        _WaveRange("Wave range", float) = 0.3
        _Range("Range", vector) = (0.13, 1.53, 0.37, 0.78)
		//wave end
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "true" }
        //LOD 100

        Pass
        {
            blend srcalpha oneminussrcalpha
            Cull off
            zwrite off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityPBSLighting.cginc"

            struct v2f
            {
                float2 uv0 : TEXCOORD0;
                float2 uv_NormalTex : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                float4 TW0 : TEXCOORD3;
                float4 TW1 : TEXCOORD4;
                float4 TW2 : TEXCOORD5;
                float4 projPos : TEXCOORD6;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            uniform sampler2D _CameraDepthTexture;

            uniform fixed _DepthSize;
            uniform sampler2D _DepthTex;
            uniform sampler2D _ReflectionTex;
            uniform float4x4 _DepthVPMat;
            uniform float4x4 _MainIVP;

            uniform float _LightWrapping;

            half4 _Speed;

            half _NormalScale;
            half _Opacity;
            half _ReflectionIntensity;
            half _ReflectionDistortion;

            sampler2D _Gradient;
            sampler2D _NormalTex;
            sampler2D _CausticTexture;

            float4 _NormalTex_ST;
            float4 _DepthTex_TexelSize;

            half4 _CausticTexture_ST;
            half4 _FresnelCol;
            half4 _CausticTint;
            half4 _CausticSpeed;

            half4 _WaterColor;
            half4 _BeachColor;
            half4 _DeepWaterColor;
            half4 _WaveColor;
            half _Specular;
            
            float _d00;
            float _d10;
            float _d11;
            float _d20;

            float _MediumDistance;
            float _LongDistance;

            float4 _Range;
            half _WaveSpeed;
            half _WaveRange;

            half4 _MainLightColor;
            float4 _MainLightPosition;

            v2f vert (appdata_full v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                o.uv0 = v.texcoord;
                o.uv_NormalTex = TRANSFORM_TEX(v.texcoord, _NormalTex);

                UNITY_TRANSFER_FOG(o, o.vertex);

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                fixed3 worldNormal = UnityObjectToWorldNormal(v.normal);
                fixed3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                fixed tangentSign = v.tangent.w * unity_WorldTransformParams.w;
                fixed3 worldBinormal = cross(worldNormal, worldTangent) * tangentSign;

                o.TW0 = float4(worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x);
                o.TW1 = float4(worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y);
                o.TW2 = float4(worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z);

                o.color = v.color;
                o.projPos = ComputeScreenPos(o.vertex);
                COMPUTE_EYEDEPTH(o.projPos.z);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 normalCol = (tex2D(_NormalTex, i.uv_NormalTex + fixed2(_Time.x*_Speed.x, 0))
                    + tex2D(_NormalTex, fixed2(_Time.x * _Speed.x + i.uv_NormalTex.y, i.uv_NormalTex.x))) / 2;

                half3 worldNormal = UnpackNormal(normalCol);

                float timeR = _Time.r * 0.01;
                half3 worldPos = half3(i.TW0.w, i.TW1.w, i.TW2.w);

                float2 sceneUVs = (i.projPos.xy / i.projPos.w);

                float2 depthuv = (worldPos.xz / 512); // replace tex const
                float depth = tex2D(_DepthTex, depthuv).r;

                // disturb depth
                depth += depth * 0.5 * sin(timeR);//dot(worldNormal, fixed3(0, 1, 0))

                float diffZ = 20 * depth; //abs(sceneZ - _WaterZ); // current pixel's response depth map interpolation

                i.color.r = depth;

                worldNormal = lerp(half3(0, 0, 1), worldNormal, _NormalScale);
                worldNormal = normalize(fixed3(dot(i.TW0.xyz, worldNormal), dot(i.TW1.xyz, worldNormal), dot(i.TW2.xyz, worldNormal)));

                // Watre sample gradully change base on vertex's color r channel
                float _clampedDistance1 = 1.2;
                float _clampedDistance2 = 0.6;

                float d00 = _d00; // 0.01; // shallow beach end
                float d10 = _d10; // 0.1; // middle beach start
                float d11 = _d11; // 0.4; // middle beach end
                float d20 = _d20; // 0.9; // deep water start

                float mid_d0 = (d00 + d10) * 0.5; // center between shallow beach end and middle beach start
                float dd10 = mid_d0 - d00;
                float dd11 = d10 - mid_d0;
                float r0 = depth - mid_d0;

                float map_d0;
                float map_d1;
                if (depth < d00) // shallow beach
                {
                    map_d0 = 0;
                    map_d1 = 0;
                }
                else if (depth < d10) // shallow beach end to middle beach start
                {
                    if (r0 > 0)//close to middle beach start
                    {
                        map_d0 = saturate(0.5 + r0 / dd11 * 0.5);
                    }
                    else
                    {
                        map_d0 = saturate(0.5 + r0 / dd10 * 0.5);
                    }
                    map_d1 = 0;
                }
                else if (depth < d11) // middle beach
                {
                    map_d0 = 1;
                    map_d1 = 0;
                }
                else if (depth < d20) // middle beach end to deep water start
                {
                    float mid_d2 = (d11 + d20) * 0.5;// center between middle beach end and deep water start

                    float dd20 = mid_d2 - d11;
                    float dd21 = d20 - mid_d2;
                    
                    float r1 = depth - mid_d2;
                    if (r1 > 0)//close to deep water start
                    {
                        map_d1 = saturate(0.5 + r1 / dd21 * 0.5);
                    }
                    else
                    {
                        map_d1 = saturate(0.5 + r1 / dd20 * 0.5);
                    }

                    map_d0 = 1;
                }
                else // deep water
                {
                    map_d1 = 1;
                    map_d0 = 1;
                }

                fixed4 col = fixed4(0, 0, 0, 1);

                col = lerp(_BeachColor, _WaterColor, map_d0);
                col = lerp(col, _DeepWaterColor, map_d1);

                float dist = length(_WorldSpaceCameraPos - worldPos);
                float3 worldDepthPos = float3(worldPos.x, worldPos.y + diffZ, worldPos.z) + sin(_Time.x) * 5;

                // Sample reflection sky box
                half3 viewDir = normalize(UnityWorldSpaceViewDir(worldPos));
                half3 refl = reflect(-viewDir, worldNormal);
                float worldDistance = distance(worldPos.xyz, _WorldSpaceCameraPos);

                half vdn = saturate(dot(viewDir, worldNormal));
                half vdWorldN = dot(viewDir, fixed3(0, 1, 0));

                half base_f = 0.05;

                half fresnelScale = 0.95;
                half fresnel = base_f + fresnelScale * pow(1.0 - vdWorldN, 1.0);
                fresnel = saturate(fresnel);
                half3 lerpWaterColor = lerp(_DeepWaterColor.rgb, _WaterColor.rgb, fresnel);
                
                col.rgb = lerp(col.rgb, lerpWaterColor, vdWorldN);

                half3 reflColor = half3(0, 0, 0);

                float2 reflectUV = sceneUVs.xy + worldNormal.xz * _ReflectionDistortion;
                reflColor = tex2D(_ReflectionTex, reflectUV);

                if (_LightWrapping > 0.8)
                {
                    col.rgb = col.rgb * (reflColor + _WaveColor.rgb * (1.5 - vdWorldN));
                }

                // light effect
                float3 lightDirection = normalize(_MainLightPosition.xyz);

                float rawNdotL = dot(worldNormal, lightDirection);
                float NdotL = saturate(max(0.1, rawNdotL));
                float3 w = float3(_LightWrapping, _LightWrapping, _LightWrapping); // light wrapping
                float3 NdotLWrap = NdotL * w;
                float3 forwardLight = max(float3(0.0, 0.0, 0.0), NdotLWrap);

                float3 directDiffuse = forwardLight * lerp(1, _MainLightColor.rgb, 0.5);

                float3 indirectDiffuse = 0;// UNITY_LIGHTMODEL_AMBIENT.rgb;
                col.rgb *= (directDiffuse + indirectDiffuse);

                // calculate caustic
                float2 caus_pos = worldDepthPos.xz * (0.5) + depth * 10;
                float2 caus_uv = TRANSFORM_TEX(caus_pos, _CausticTexture);

                half deltaDepth = _Range.z * (depth * 2);
                float depthFade = exp(-depth * 400);
                caus_uv += worldNormal.xz * 0.25;
                caus_uv += (1 - min(_Range.z, deltaDepth) / _Range.z + _WaveRange * sin(_Time.x * _WaveSpeed));
                fixed3 caustics = tex2D(_CausticTexture, caus_uv);
                float distFade = 1.0 - saturate(dist * 0.001);
                caustics = caustics * _CausticTint * distFade * depthFade;
                col.rgb += caustics;

                // calculate specular
                half3 h = normalize(viewDir + normalize(_MainLightPosition.xyz));
                fixed ndh = max(0, dot(worldNormal, h));
                col.rgb += _MainLightColor.rgb * pow(ndh, _Specular * 128.0) * _SpecColor;

                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);

                half fresnelScaleAlpha = 1.2;
                half fresnelAlpha = 0.8 + fresnelScaleAlpha * pow(1.0 - vdWorldN, 1.5);
                col.a *= _Opacity;
                col.a *= fresnelAlpha;

                return col;
            }
            ENDCG
        }
    }
}
