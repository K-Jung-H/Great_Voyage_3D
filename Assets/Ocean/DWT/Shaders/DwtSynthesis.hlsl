#ifndef GREAT_VOYAGE_DWT_SYNTHESIS
#define GREAT_VOYAGE_DWT_SYNTHESIS

// Dynamic Wave Trains, Fournier et al., CGF 2026, doi:10.1111/cgf.70495.
// Unity adaptation of the deep-water example at upstream commit 98d06448.
// See Documentation/DWT.md for source, scope and representation differences.
TEXTURE2D(_Exemplar); SAMPLER(sampler_Exemplar);
TEXTURE2D(_Profile); SAMPLER(sampler_Profile);
TEXTURE2D(_FoamMap); SAMPLER(sampler_FoamMap);
TEXTURE2D(_WakeImpulse); SAMPLER(sampler_WakeImpulse);
TEXTURE3D(_SprayExemplar); SAMPLER(sampler_SprayExemplar);
CBUFFER_START(UnityPerMaterial)
float4 _Exemplar_TexelSize;
float4 _Wave0, _Wave1, _Wave2, _Wave3, _Wave4;
float4 _WaterColor;
float4 _OceanOrigin;
float _HeightScale, _Speed, _MeshSpacing, _FootprintScale;
float _Roughness, _ReflectionStrength, _OceanTime;
float _DebugView;
float _FoamStrength, _SprayStrength, _BreakingGain;
int _InteractionCount;
float4 _InteractionSources[48];
float4 _InteractionDirections[48];
int _TrailCount;
float4 _TrailSegments[72];
float4 _TrailShape[72];
float4 _TrailPressure[72];
float _WakeWaveStrength;
float _WakeSeaStateResponse;
CBUFFER_END

float DwtTime() { return _OceanTime * _Speed; }

// Artist-controlled gain for a stronger base sea. Shared by mesh and shading.
float WakeSeaStateGain()
{
    return 1+max(0,_HeightScale-1)*_WakeSeaStateResponse;
}
float4 DwtWave(int i)
{
    if (i == 0) return _Wave0;
    if (i == 1) return _Wave1;
    if (i == 2) return _Wave2;
    if (i == 3) return _Wave3;
    return _Wave4;
}

float2 DwtRotate(float2 p, float a)
{
    float s, c; sincos(a, s, c);
    return float2(c*p.x-s*p.y, s*p.x+c*p.y);
}

// Each source represents a sampled water contact in persistent ocean coordinates.
float2 InteractionWaves(float2 ocean, out float dispersiveHeight, out float3 trailFlow)
{
    float height = 0;
    float foam = 0;
    [loop] for (int i=0; i<_InteractionCount; i++)
    {
        float4 source = _InteractionSources[i];
        float2 delta = ocean-source.xy;
        float age = source.z;
        float travel = 2.6*age;
        float outer = travel+1.35;
        float distanceSquared = dot(delta,delta);
        if (distanceSquared >= outer*outer) continue;
        float distance = sqrt(distanceSquared);
        float offset = distance-travel;
        float packet = 1-smoothstep(.25,1.35,abs(offset));
        float2 direction = _InteractionDirections[i].xy;
        float alignment = dot(delta,direction)/max(distance,.001);
        float directional = lerp(1,smoothstep(-.35,.75,alignment),dot(direction,direction));
        float life = smoothstep(0,.18,age)*(1-smoothstep(1.3,4,age));
        float amplitude = source.w*packet*directional*life*rsqrt(1+.35*travel);
        float crest = cos(offset*5.5);
        height += amplitude*crest;
        foam = max(foam,amplitude*2.8*smoothstep(.2,1,crest));
    }
    // Pressure impulses follow the collider's historical motion. The signed
    // dispersive response sums linearly; the centerline is used only for foam.
    float wakeHeight=0, trailFoam=0;
    trailFlow=0;
    [loop] for(int trailIndex=0;trailIndex<_TrailCount;trailIndex++)
    {
        float4 segment=_TrailSegments[trailIndex];
        float4 shape=_TrailShape[trailIndex];
        float4 pressure=_TrailPressure[trailIndex];
        float2 pressureDelta=ocean-pressure.xy;
        float age=max(0,shape.z+pressure.w*.5);
        float radius=pressure.z;
        float distanceSquared=dot(pressureDelta,pressureDelta);
        float waveReach=min(80*radius,3.8*age+radius*6);
        if(distanceSquared<waveReach*waveReach)
        {
            float r=sqrt(distanceSquared)/radius;
            float tau=age*sqrt(9.81/radius);
            float2 uv=(float2(r/80,tau/30)*float2(511,191)+.5)/float2(512,192);
            float response=SAMPLE_TEXTURE2D_LOD(_WakeImpulse,sampler_WakeImpulse,uv,0).r;
            float aperture=(1-smoothstep(waveReach*.78,waveReach,sqrt(distanceSquared)))
                *(1-smoothstep(23,30,tau))*(1-smoothstep(4,5,age));
            wakeHeight+=response*aperture*shape.w*shape.w*pressure.w*_WakeWaveStrength;
        }
        float2 along=segment.zw-segment.xy;
        float2 delta=ocean-segment.xy;
        float reach=max(shape.x,shape.y)+shape.z*.75+1.7;
        if(any(abs(delta)>abs(along)+reach)) continue;
        float lengthSquared=max(dot(along,along),.001);
        float t=dot(delta,along)/lengthSquared;
        float2 offset=delta-along*saturate(t);
        float2 right=float2(along.y,-along.x)*rsqrt(lengthSquared);
        float lateral=dot(offset,right);
        float distanceAlongEnd=abs(t-saturate(t))*sqrt(lengthSquared);
        float caps=1-smoothstep(0,.45,distanceAlongEnd);
        float foamAge=max(0,shape.z+(1-saturate(t))*.16);
        float width=max(.12,lateral<0 ? shape.x : shape.y);
        float fade=(1-smoothstep(.35,5,foamAge))*shape.w*caps;
        float center=1-smoothstep(width*.15,width*.72+foamAge*.08,abs(lateral));
        float coverage=center*fade*.85;
        if (coverage>trailFoam)
        {
            trailFoam=coverage;
            // Shift the pattern downstream as this fixed history segment ages.
            float2 forward=along*rsqrt(lengthSquared);
            trailFlow=float3(forward*foamAge*lerp(.35,1.25,shape.w),coverage);
        }

    }
    dispersiveHeight=wakeHeight*WakeSeaStateGain();
    return float2(height+dispersiveHeight,saturate(foam));
}

float2 InteractionWaves(float2 ocean)
{
    float unused;
    float3 unusedFlow;
    return InteractionWaves(ocean,unused,unusedFlow);
}
float2 DwtHash(float2 p)
{
    return frac(sin(float2(dot(p,float2(127.1,311.7)),
                               dot(p,float2(269.5,183.3)))) * 43758.5453);
}

// Triangular stochastic tiling; L2-normalized weights preserve Gaussian variance.
// The homogeneous deep-water transforms are identical for all three vertices,
// so upstream's permutation of vertex slots has no effect on this weighted sum.
void DwtTriangle(float2 p, out float2 a, out float2 b, out float2 c, out float3 w)
{
    float2 q = float2(p.x - .57735027*p.y, 1.15470054*p.y);
    float2 cell = floor(q);
    float2 f = frac(q);
    float z = 1-f.x-f.y;
    if (z > 0)
    {
        a=cell; b=cell+float2(0,1); c=cell+float2(1,0);
        w=float3(z,f.y,f.x);
    }
    else
    {
        a=cell+1; b=cell+float2(0,1); c=cell+float2(1,0);
        w=float3(-z,1-f.x,1-f.y);
    }
    w *= rsqrt(max(dot(w,w),1e-8));
}

float2 DwtGaussian(float2 world, float footprintMeters, float2 mean, float4 wave)
{
    float wavelength = max(wave.x,.01);
    float angle = radians(wave.y);
    float frequency = rcp(wavelength);
    float phaseSpeed = sqrt(9.8*wavelength / TWO_PI);
    float2 a,b,c; float3 w;
    DwtTriangle(world*frequency,a,b,c,w);
    // The reference exemplar contains ten waves per texture, not one.
    float2 uv = DwtRotate(world,angle)*(frequency*.1);
    uv.x -= phaseSpeed*frequency*DwtTime()*.1;
    // Explicit isotropic LOD permits exactly the same evaluator in vertex,
    // depth and pixel passes. Matches the paper's standard MIP footprint model.
    float footprint = footprintMeters*(frequency*.1)*_Exemplar_TexelSize.z;
    float lod = max(0,log2(max(footprint,1e-6)));
    float2 e0 = SAMPLE_TEXTURE2D_LOD(_Exemplar,sampler_Exemplar,uv+DwtHash(a),lod).rg-mean;
    float2 e1 = SAMPLE_TEXTURE2D_LOD(_Exemplar,sampler_Exemplar,uv+DwtHash(b),lod).rg-mean;
    float2 e2 = SAMPLE_TEXTURE2D_LOD(_Exemplar,sampler_Exemplar,uv+DwtHash(c),lod).rg-mean;
    return saturate(wave.z)*(e0*w.x+e1*w.y+e2*w.z+mean);
}

float2 DwtBound(float2 gaussian)
{
    float r=length(gaussian);
    // Stable radial tanh compression (Eq. 8), including a zero mean mip.
    return gaussian * ((1-exp(-2*r))/(1+exp(-2*r))) / max(r,1e-7);
}

float DwtHeight(float2 world, float2 dx, float2 dz, out float variance, out float2 swell)
{
    float height=0; variance=0;
    swell=0;
    float footprintMeters=max(length(dx),length(dz));
    float2 mean=SAMPLE_TEXTURE2D_LOD(_Exemplar,sampler_Exemplar,float2(.5,.5),16).rg;
    [unroll] for(int i=0;i<5;i++)
    {
        float4 wave=DwtWave(i);
        float2 field=DwtGaussian(world,footprintMeters,mean,wave);
        if(i==0) swell=field;
        float2 bounded=DwtBound(field);
        // Preserve the reference's implicit profile filtering in the pixel
        // stage. Always sampling mip 0 produces stepped specular highlights.
        #if defined(SHADER_STAGE_FRAGMENT)
        float profile=SAMPLE_TEXTURE2D(_Profile,sampler_Profile,bounded*.5+.5).r;
        #else
        float profile=SAMPLE_TEXTURE2D_LOD(_Profile,sampler_Profile,bounded*.5+.5,0).r;
        #endif
        height+=profile*max(wave.x,.01)/TWO_PI;
        // Isotropic slope-variance approximation as in the public demo,
        // using the trace so changing wave direction cannot change roughness.
        variance+=.125*saturate(footprintMeters/max(wave.x,.01))*saturate(wave.z);
    }
    return height*_HeightScale;
}

float DwtHeight(float2 world, float2 dx, float2 dz, out float variance)
{
    float2 unused;
    return DwtHeight(world,dx,dz,variance,unused);
}

struct DwtAttributes { float4 positionOS : POSITION; };
struct DwtVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float2 ocean : TEXCOORD1;
    float fog : TEXCOORD2;
};
DwtVaryings DwtVertex(DwtAttributes input)
{
    DwtVaryings o;
    float3 world=TransformObjectToWorld(input.positionOS.xyz);
    o.ocean=world.xz+_OceanOrigin.xz;
    float ignored;
    world.y+=DwtHeight(o.ocean,float2(_MeshSpacing,0),float2(0,_MeshSpacing),ignored)+InteractionWaves(o.ocean).x;
    o.positionWS=world;
    o.positionCS=TransformWorldToHClip(world);
    o.fog=ComputeFogFactor(o.positionCS.z);
    return o;
}
#endif
