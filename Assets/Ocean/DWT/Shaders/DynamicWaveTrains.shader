Shader "GreatVoyage/DynamicWaveTrains"
{
    Properties
    {
        [NoScaleOffset] _Exemplar("DWT complex Gaussian exemplar",2D)="black"{}
        [NoScaleOffset] _Profile("DWT trochoidal profile (linear signed EXR)",2D)="black"{}
        _Wave0("Swell: wavelength m / angle deg / profile",Vector)=(18,20,.38,0)
        _Wave1("Wave 2: wavelength m / angle deg / profile",Vector)=(9,38,.30,0)
        _Wave2("Wave 3: wavelength m / angle deg / profile",Vector)=(4.2,5,.24,0)
        _Wave3("Wave 4: wavelength m / angle deg / profile",Vector)=(1.8,55,.18,0)
        _Wave4("Ripples: wavelength m / angle deg / profile",Vector)=(.65,-15,.12,0)
        _HeightScale("Height scale",Range(0,2))=1
        _Speed("Time scale",Range(0,2))=1
        _MeshSpacing("Mesh footprint (metres)",Float)=.5
        _FootprintScale("Pixel footprint filter",Range(1,2))=1.2
        _WaterColor("Water body (linear lighting)",Color)=(.10,.32,.38,1)
        _Roughness("Base perceptual roughness",Range(.03,.6))=.23
        _ReflectionStrength("Environment reflection",Range(0,2))=1
        _OceanOrigin("Logical ocean offset XZ",Vector)=(0,0,0,0)
        [HideInInspector] _OceanTime("Runtime ocean time",Float)=0
        [NoScaleOffset] _FoamMap("Foam texture",2D)="white"{}
        [NoScaleOffset] _WakeImpulse("Deep water impulse response",2D)="black"{}
        _WakeWaveStrength("Dispersive wake strength",Range(0,8))=5.4
        _WakeSeaStateResponse("Wake response to higher seas",Range(0,2))=1
        [NoScaleOffset] _SprayExemplar("Periodic spray exemplar",3D)=""{}
        _FoamStrength("Foam coverage",Range(0,1))=.85
        _SprayStrength("Spray coverage",Range(0,1))=.75
        _BreakingGain("Breaking energy response",Range(0,32))=15
        [Enum(Shaded,0,Height,1,Normals,2,Foam,3,Spray,4,Spray Mask,5,Spray Texture,6)] _DebugView("View",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "DwtSynthesis.hlsl"
        ENDHLSL
        Pass
        {
            Name "DWT Forward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DwtVertex
            #pragma fragment DwtFragment
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float SampleFoamTile(float2 uv, float2 dx, float2 dy, float2 tile, float time)
            {
                float2 seed=DwtHash(tile);
                float2 drift=(seed-.5)*.065*time;
                float2 churn=sin(TWO_PI*seed+time*float2(1.7,2.3))*.15;
                float sampleValue=SAMPLE_TEXTURE2D_GRAD(_FoamMap,sampler_FoamMap,
                    uv+seed+drift+churn,dx,dy).r;
                // The same tile keeps its phase on both sides of every triangle edge.
                float pulse=sin(time*(2.2+seed.y)+TWO_PI*DwtHash(tile+17).x);
                return smoothstep(.09,.56,sampleValue+.13*pulse);
            }

            float SampleFoamPattern(float2 ocean, float2 oceanDx, float2 oceanDy, float time)
            {
                float2 a,b,c;
                float3 weights;
                DwtTriangle(ocean*.22,a,b,c,weights);
                weights /= dot(weights,float3(1,1,1));
                float2 uv=ocean*.27;
                float2 dx=oceanDx*.27, dy=oceanDy*.27;
                float3 samples=float3(SampleFoamTile(uv,dx,dy,a,time),
                    SampleFoamTile(uv,dx,dy,b,time),SampleFoamTile(uv,dx,dy,c,time));
                return dot(samples,weights);
            }

            float SampleSprayVolume(float2 p, float time, float footprint)
            {
                // Fixed metres per tile and texture repeat. No wave-frequency transform.
                float2 a,b,c;
                float3 weights;
                DwtTriangle(p*.5,a,b,c,weights);
                weights/=dot(weights,float3(1,1,1));
                float3 uv=float3(p*.56,time*1.65);
                float lod=max(0,log2(max(footprint*.56*256,1e-5)));
                float3 offsetA=float3(DwtHash(a),DwtHash(a+29).x);
                float3 offsetB=float3(DwtHash(b),DwtHash(b+29).x);
                float3 offsetC=float3(DwtHash(c),DwtHash(c+29).x);
                float3 values=float3(
                    SAMPLE_TEXTURE3D_LOD(_SprayExemplar,sampler_SprayExemplar,uv+offsetA,lod).r,
                    SAMPLE_TEXTURE3D_LOD(_SprayExemplar,sampler_SprayExemplar,uv+offsetB,lod).r,
                    SAMPLE_TEXTURE3D_LOD(_SprayExemplar,sampler_SprayExemplar,uv+offsetC,lod).r);
                return dot(values,weights);
            }

            float SampleSprayBubbles(float2 ocean, float time, float footprint)
            {
                float2 p=DwtRotate(ocean,radians(_Wave0.y));
                p.x-=sqrt(9.8*max(_Wave0.x,.01)/TWO_PI)*DwtTime();
                // Independent cross-flow and life-cycle phase keep the spray
                // changing even when tracked together with the advancing crest.
                p.y-=.22*time;
                return SampleSprayVolume(p,time*1.35,footprint);
            }

            float2 WakeWhitewater(float2 ocean, float2 dx, float2 dy, float3 trailFlow,
                float wakeHeight, float2 wakeSlope)
            {
                float time=_OceanTime;
                float foamMask=saturate(trailFlow.z*_FoamStrength*sqrt(WakeSeaStateGain()));
                float sprayMask=smoothstep(.012,.10,wakeHeight)
                    *smoothstep(.015,.14,length(wakeSlope))*_SprayStrength;
                float2 coverage=0;
                [branch] if (foamMask>0)
                {
                    float2 flow=trailFlow.xy;
                    float2 along=flow*rsqrt(max(dot(flow,flow),1e-6));
                    float2 patternPosition=ocean+flow+along*(.23*sin(2.1*time));
                    float pattern=SampleFoamPattern(patternPosition,dx,dy,time*1.4);
                    coverage.x=saturate(foamMask*pattern*1.65);
                }
                [branch] if (sprayMask>0)
                {
                    // Gravity-wave propagation provides the crest mask. A short,
                    // bounded drift breaks up bubbles independently of its shape.
                    float2 p=ocean+trailFlow.xy*.65+float2(.11,-.17)*time;
                    float bubbles=SampleSprayVolume(p,time*1.18,max(length(dx),length(dy)));
                    coverage.y=sprayMask*smoothstep(.02,.42,bubbles)*.78;
                }
                return coverage;
            }

            float DwtBreakingEnergy(float2 field)
            {
                float radius=length(field);
                float decay=exp(-2*radius);
                return saturate((radius-(1-decay)/(1+decay))*_BreakingGain);
            }

            float2 DwtWhitewater(float2 ocean, float2 swell, float2 dx, float2 dy, float vesselFoam)
            {
                float energy=DwtBreakingEnergy(swell);
                float phase=atan2(swell.x,swell.y+1e-8);
                // The source profile peaks at phase pi/2. Phase decreases as time advances.
                float age=frac(.25-phase/TWO_PI+.5)-.5;
                float time=_OceanTime;
                float edge=clamp(fwidth(age),.006,.05);
                float foam=energy*smoothstep(-edge,.045+edge,age)
                    *(1-smoothstep(.035,.035+.32*energy+edge,age));
                float sprayWidth=.18*energy;
                // Round both ends over a fraction of the band, not only one pixel.
                float feather=max(edge,.40*sprayWidth);
                float spray=smoothstep(-sprayWidth-feather,-sprayWidth+feather,age)
                    *(1-smoothstep(-feather,feather,age))*smoothstep(0,.22,energy);
                foam*=_FoamStrength;
                spray*=_SprayStrength;
                float foamCoverage=0, sprayCoverage=0;
                float pattern=0;
                [branch] if (max(max(foam,vesselFoam),spray)>0)
                {
                    // Resident foam follows the wave more slowly than spray.
                    float2 direction=DwtRotate(float2(1,0),-radians(_Wave0.y));
                    float phaseSpeed=sqrt(9.8*max(_Wave0.x,.01)/TWO_PI);
                    float2 foamPosition=ocean-direction*(.27*phaseSpeed*time);
                    pattern=SampleFoamPattern(foamPosition,dx,dy,time);
                    foamCoverage=max(foam,vesselFoam)*pattern;
                    foamCoverage=saturate(foamCoverage*1.65);
                }
                [branch] if (spray>0)
                {
                    // Rotation preserves length. Derivatives are evaluated by the caller
                    // before this divergent branch and are valid at the mask boundary.
                    float footprint=max(length(dx),length(dy));
                    float bubbles=SampleSprayBubbles(ocean,time,footprint);
                    float softClumps=smoothstep(.035,.29,bubbles);
                    float breakup=lerp(.25,1,smoothstep(.12,.58,pattern));
                    sprayCoverage=spray*lerp(bubbles,softClumps,.35)*breakup*.72;
                }
                if (_DebugView>4.5 && _DebugView<5.5) sprayCoverage=spray;
                if (_DebugView>5.5 && _DebugView<6.5) sprayCoverage=SampleSprayBubbles(ocean,time,max(length(dx),length(dy)));
                return saturate(float2(foamCoverage,sprayCoverage));
            }

            half4 DwtFragment(DwtVaryings input) : SV_Target
            {
                float2 dx=ddx(input.ocean), dy=ddy(input.ocean);
                float variance;
                float2 swell;
                float height=DwtHeight(input.ocean,dx*_FootprintScale,dy*_FootprintScale,variance,swell);
                float wakeHeight;
                float3 trailFlow;
                float2 interaction=InteractionWaves(input.ocean,wakeHeight,trailFlow);
                if(_DebugView>6.5 && _DebugView<7.5) return half4(saturate(.5+interaction.x*3).xxx,1);
                height+=interaction.x;
                // Screen derivatives -> ocean coordinates (Eq. 22).
                float det=dx.x*dy.y-dx.y*dy.x;
                float invDet=abs(det)>1e-10 ? rcp(det) : 0;
                float hx=ddx(height), hy=ddy(height);
                float2 slope=float2(hx*dy.y-hy*dx.y, hy*dx.x-hx*dy.x)*invDet;
                float3 N=normalize(float3(-slope.x,1,-slope.y));
                if(_DebugView>1.5 && _DebugView<2.5) return half4(N*.5+.5,1);
                if(_DebugView>.5 && _DebugView<1.5) return half4((.5+height*.15).xxx,1);
                float wakeDx=ddx(wakeHeight), wakeDy=ddy(wakeHeight);
                float2 wakeSlope=float2(wakeDx*dy.y-wakeDy*dx.y,wakeDy*dx.x-wakeDx*dy.x)*invDet;
                if(_DebugView>7.5)
                {
                    float2 trail=WakeWhitewater(input.ocean,dx,dy,trailFlow,wakeHeight,wakeSlope);
                    float coverage=_DebugView>8.5 ? trail.y : trail.x;
                    return half4(sqrt(coverage).xxx,1);
                }
                float2 whitewater=DwtWhitewater(input.ocean,swell,dx,dy,
                    _DebugView>2.5 ? 0 : interaction.y);
                if(_DebugView>3.5) return half4(sqrt(whitewater.yyy),1);
                if(_DebugView>2.5) return half4(whitewater.xxx,1);
                whitewater=max(whitewater,WakeWhitewater(input.ocean,dx,dy,trailFlow,wakeHeight,wakeSlope));
                float3 V=GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light sun=GetMainLight();
                float3 L=sun.direction;
                float3 H=SafeNormalize(L+V);
                float nv=max(saturate(dot(N,V)),.001);
                float nl=saturate(dot(N,L));
                float nh=saturate(dot(N,H));
                float vh=saturate(dot(V,H));
                float rough=clamp(_Roughness+variance*_HeightScale*_HeightScale,.035,.8);
                // Expose the actual dispersive wave slopes in the reflection.
                rough=lerp(rough,min(rough,.28),smoothstep(.012,.10,length(wakeSlope)));
                float alpha=rough*rough;
                float a2=alpha*alpha;
                float denom=nh*nh*(a2-1)+1;
                float D=a2/max(PI*denom*denom,1e-7);
                float gv=nl*sqrt(nv*nv*(1-a2)+a2);
                float gl=nv*sqrt(nl*nl*(1-a2)+a2);
                float visibility=.5/max(gv+gl,1e-5);
                float fresnelSun=.02037+.97963*pow(1-vh,5);
                float fresnelView=.02037+.97963*pow(1-nv,5);
                half3 reflection=GlossyEnvironmentReflection(reflect(-V,N),input.positionWS,rough,1);
                half3 ambient=max(SampleSH(float3(0,1,0)),half3(.08,.10,.12));
                half3 body=_WaterColor.rgb*(ambient+sun.color*nl*.25);
                half3 color=body*(1-fresnelView)+reflection*fresnelView*_ReflectionStrength;
                color+=sun.color*(D*visibility*fresnelSun*nl);
                half3 foamLight=half3(.88,.94,.96)*(ambient+sun.color*(.12+nl*.42));
                color=lerp(color,foamLight,whitewater.x);
                color=lerp(color,foamLight*1.12,whitewater.y);
                return half4(MixFog(color,input.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DWT Depth"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DwtVertex
            #pragma fragment DepthFragment
            half DepthFragment(DwtVaryings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
    }
    FallBack Off
}
