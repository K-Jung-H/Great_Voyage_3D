using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GreatVoyage.Ocean
{
    [RequireComponent(typeof(Renderer), typeof(DwtOceanClock))]
    public sealed class DwtWaterInteractions : MonoBehaviour
    {
        private const int MaxContacts = 128, MaxWaves = 48;
        private const float Interval = .08f, Lifetime = 4f;
        private const int MaxTrails = 72;
        private const float TrailLife = 5f;
        private static readonly int TrailCountId = Shader.PropertyToID("_TrailCount");
        private static readonly int TrailSegmentsId = Shader.PropertyToID("_TrailSegments");
        private static readonly int TrailShapeId = Shader.PropertyToID("_TrailShape");
        private static readonly int TrailPressureId = Shader.PropertyToID("_TrailPressure");
        private static readonly int OriginId = Shader.PropertyToID("_OceanOrigin");
        private static readonly int CountId = Shader.PropertyToID("_InteractionCount");
        private static readonly int SourcesId = Shader.PropertyToID("_InteractionSources");
        private static readonly int DirectionsId = Shader.PropertyToID("_InteractionDirections");
        private static readonly int[] WaveIds = {
            Shader.PropertyToID("_Wave0"), Shader.PropertyToID("_Wave1"), Shader.PropertyToID("_Wave2"),
            Shader.PropertyToID("_Wave3"), Shader.PropertyToID("_Wave4")
        };
        [SerializeField] private ComputeShader contactShader;
        private struct Contact
        {
            public WaterInteractor Owner;
            public int Index;
            public Vector3 Position, Normal;
        }
        private struct PreviousContact
        {
            public Vector3 Position;
            public float Depth, Time, NextEmission;
            public bool HasSample;
        }
        private sealed class TrailState
        {
            public Vector2 Center, Rear, PressureCenter;
            public float Time, SegmentTime, WaterY;
            public Vector3 WaterNormal = Vector3.up;
            public bool HasCenter, HasWakeStart;
        }
        private readonly Dictionary<WaterInteractor, TrailState> trails = new();
        private readonly Vector4[] trailHistory = new Vector4[MaxTrails], shapeHistory = new Vector4[MaxTrails];
        private readonly Vector4[] trailSegments = new Vector4[MaxTrails], trailShapes = new Vector4[MaxTrails];
        private readonly Vector4[] pressureHistory = new Vector4[MaxTrails], trailPressures = new Vector4[MaxTrails];
        private int trailHead, trailCount;
        public int ActiveTrailSegments { get; private set; }
        public Vector2 LastWakeWidths { get; private set; }
        public bool TryGetWaterLevel(WaterInteractor interactor, out float waterY)
        {
            return TryGetSurfacePose(interactor,out waterY,out _);
        }

        public bool TryGetSurfacePose(WaterInteractor interactor, out float waterY, out Vector3 normal)
        {
            if (interactor && trails.TryGetValue(interactor,out var state))
            {
                waterY=state.WaterY;
                normal=state.WaterNormal;
                return true;
            }
            waterY=0;
            normal=Vector3.up;
            return false;
        }
        private readonly Contact[] pending = new Contact[MaxContacts];
        private readonly Vector2[] coordinates = new Vector2[MaxContacts];
        private readonly Vector3[] points = new Vector3[8], normals = new Vector3[8];
        private readonly Vector4[] sourceHistory = new Vector4[MaxWaves], directionHistory = new Vector4[MaxWaves];
        private readonly Vector4[] sources = new Vector4[MaxWaves], directions = new Vector4[MaxWaves];
        private readonly Dictionary<WaterInteractor, PreviousContact[]> previous = new();
        private readonly List<WaterInteractor> stale = new();
        private Renderer surface;
        private DwtOceanClock clock;
        private MaterialPropertyBlock block;
        private ComputeBuffer positions, heights;
        private AsyncGPUReadbackRequest request;
        private bool inFlight;
        private int pendingCount, waveHead, waveCount, kernel;
        private float pendingTime, nextProbe;
        public int ImpactEmissions { get; private set; }
        public int MotionEmissions { get; private set; }
        public int ActiveWaves { get; private set; }

        private void OnEnable()
        {
            if (!contactShader) return;
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
            {
                Debug.LogWarning("DWT interaction sampling requires compute and asynchronous GPU readback.", this);
                return;
            }
            surface = GetComponent<Renderer>();
            clock = GetComponent<DwtOceanClock>();
            block ??= new MaterialPropertyBlock();
            positions = new ComputeBuffer(MaxContacts, sizeof(float)*2);
            heights = new ComputeBuffer(MaxContacts, sizeof(float));
            kernel = contactShader.FindKernel("SampleSurface");
            previous.Clear();
            trails.Clear();
            trailHead=trailCount=0;
            waveHead = waveCount = 0;
            ImpactEmissions = MotionEmissions = ActiveWaves = 0;
            nextProbe = 0;
        }

        private void LateUpdate()
        {
            if (positions == null) return;
            float time = (float)clock.ElapsedSeconds;
            if (inFlight && request.done)
            {
                inFlight = false;
                if (!request.hasError) ResolveContacts();
            }
            if (!inFlight && time >= nextProbe)
            {
                nextProbe = time + Interval;
                Probe(time);
            }
            Upload(time);
        }

        private void Probe(float time)
        {
            surface.GetPropertyBlock(block);
            Vector4 origin = block.GetVector(OriginId);
            float seaLevel = surface.transform.position.y;
            pendingCount = 0;
            pendingTime = time;
            stale.Clear();
            foreach (var owner in trails.Keys)
                if (!owner || !owner.isActiveAndEnabled) stale.Add(owner);
            foreach (var owner in stale)
            {
                previous.Remove(owner);
                trails.Remove(owner);
            }
            foreach (var owner in WaterInteractor.Active)
            {
                int count = owner.Sample(points, normals);
                if (count == 0) { previous.Remove(owner); trails.Remove(owner); continue; }
                for (int i=0;i<count && pendingCount<MaxContacts;i++)
                {
                    Vector3 p = points[i] + new Vector3(origin.x, -seaLevel, origin.z);
                    pending[pendingCount] = new Contact { Owner=owner, Index=i, Position=p, Normal=normals[i] };
                    coordinates[pendingCount++] = new Vector2(p.x,p.z);
                }
                if (pendingCount == MaxContacts) break;
            }
            if (pendingCount == 0) return;

            Material material = surface.sharedMaterial;
            Texture phase = material.GetTexture("_Exemplar");
            if (!phase) return;
            foreach (int id in WaveIds) contactShader.SetVector(id,material.GetVector(id));
            contactShader.SetTexture(kernel,"_Exemplar",phase);
            contactShader.SetTexture(kernel,"_Profile",material.GetTexture("_Profile"));
            contactShader.SetVector("_Exemplar_TexelSize",new Vector4(1f/phase.width,1f/phase.height,phase.width,phase.height));
            contactShader.SetFloat("_HeightScale",material.GetFloat("_HeightScale"));
            contactShader.SetFloat("_MeshSpacing",material.GetFloat("_MeshSpacing"));
            contactShader.SetFloat("_Speed",material.GetFloat("_Speed"));
            contactShader.SetFloat("_OceanTime",time);
            contactShader.SetInt("_ContactCount",pendingCount);
            positions.SetData(coordinates,0,0,pendingCount);
            contactShader.SetBuffer(kernel,"_ContactPositions",positions);
            contactShader.SetBuffer(kernel,"_ContactHeights",heights);
            contactShader.Dispatch(kernel,(pendingCount+31)/32,1,1);
            request = AsyncGPUReadback.Request(heights,pendingCount*sizeof(float),0);
            inFlight = true;
        }

        private void ResolveContacts()
        {
            var data = request.GetData<float>();
            // The wake anchor still samples the base height for the review hull's bobbing.
            surface.GetPropertyBlock(block);
            Vector4 origin = block.GetVector(OriginId);
            for (int first=0;first<pendingCount;)
            {
                var owner=pending[first].Owner;
                int end=first;
                float height=0;
                while(end<pendingCount && pending[end].Owner==owner) height+=data[end++];
                if(owner && owner.isActiveAndEnabled)
                {
                    Vector3 normal=Vector3.up;
                    float waterY=height/(end-first);
                    if(owner.EmitsWake && owner.SampleWakeAttitude && end-first==5)
                    {
                        Vector3 forward=pending[first+1].Position-pending[first+2].Position;
                        Vector3 right=pending[first+3].Position-pending[first+4].Position;
                        forward.y=data[first+1]-data[first+2];
                        right.y=data[first+3]-data[first+4];
                        normal=Vector3.Cross(forward,right).normalized;
                        if(normal.y<.05f) normal=Vector3.up;
                    }
                    UpdateTrail(owner,waterY+surface.transform.position.y,normal,new Vector2(origin.x,origin.z));
                }
                first=end;
            }
            for (int i=0;i<pendingCount;i++)
            {
                Contact contact = pending[i];
                if (!contact.Owner || !contact.Owner.isActiveAndEnabled) continue;
                if (contact.Owner.EmitsWake)
                {
                    previous.Remove(contact.Owner);
                    continue;
                }
                if (!previous.TryGetValue(contact.Owner,out var records))
                {
                    records = new PreviousContact[8];
                    previous.Add(contact.Owner,records);
                }
                ref PreviousContact last = ref records[contact.Index];
                float depth = data[i]-contact.Position.y;
                float dt = pendingTime-last.Time;
                if (last.HasSample && dt>0 && contact.Owner.Response>0)
                {
                    var outward = new Vector2(contact.Normal.x,contact.Normal.z).normalized;
                    if (last.Depth<0 && depth>=0 && (depth-last.Depth)/dt>.15f)
                    {
                        float fraction = Mathf.Clamp01(-last.Depth/(depth-last.Depth));
                        Emit(Vector3.Lerp(last.Position,contact.Position,fraction),outward,
                            Mathf.Clamp((depth-last.Depth)/dt*.035f*contact.Owner.Response,0,.42f),
                            pendingTime-dt*(1-fraction));
                        ImpactEmissions++;
                        last.NextEmission = pendingTime+.35f;
                    }
                    else if (depth>=0 && depth<1.2f && pendingTime>=last.NextEmission)
                    {
                        float motion = Mathf.Abs(depth-last.Depth)/dt*.018f;
                        if (motion>.013f)
                        {
                            Emit(contact.Position,outward,Mathf.Min(.17f,motion*contact.Owner.Response),pendingTime);
                            MotionEmissions++;
                            last.NextEmission = pendingTime+.28f;
                        }
                    }
                }
                last.Position=contact.Position;
                last.Depth=depth;
                last.Time=pendingTime;
                last.HasSample=true;
            }
        }

        private void UpdateTrail(WaterInteractor owner, float waterY, Vector3 normal, Vector2 origin)
        {
            if (!trails.TryGetValue(owner,out var state))
            {
                state=new TrailState();
                trails.Add(owner,state);
            }
            state.WaterY=waterY;
            state.WaterNormal=normal;
            if (!owner.EmitsWake)
            {
                state.HasCenter=state.HasWakeStart=false;
                return;
            }
            Vector3 world=owner.WakeCenter;
            Vector2 center=new Vector2(world.x,world.z)+origin;
            float elapsed=pendingTime-state.Time;
            Vector2 velocity=state.HasCenter && elapsed>0 ? (center-state.Center)/elapsed : Vector2.zero;
            state.Center=center;
            state.Time=pendingTime;
            state.HasCenter=true;
            float speed=velocity.magnitude;
            if (speed<.35f || owner.Response<=0)
            {
                state.HasWakeStart=false;
                return;
            }
            Vector2 rear=center-velocity/speed*owner.WakeRearOffset;
            Vector2 pressureCenter=center;
            float left=owner.WakeWidth*.5f;
            float right=left;
            LastWakeWidths=new Vector2(left,right);
            if (!state.HasWakeStart || Vector2.Distance(state.Rear,rear)>Mathf.Max(2,speed*.55f))
            {
                state.Rear=rear;
                state.PressureCenter=pressureCenter;
                state.HasWakeStart=true;
                state.SegmentTime=pendingTime;
                return;
            }
            float duration=pendingTime-state.SegmentTime;
            if (duration<.075f || (state.Rear-rear).sqrMagnitude<.0009f)
                return;
            trailHistory[trailHead]=new Vector4(state.Rear.x,state.Rear.y,rear.x,rear.y);
            shapeHistory[trailHead]=new Vector4(left,right,pendingTime,Mathf.Clamp01(owner.Response));
            Vector2 midpoint=(state.PressureCenter+pressureCenter)*.5f;
            // Gaussian pressure footprint and quadrature weight for this time interval.
            pressureHistory[trailHead]=new Vector4(midpoint.x,midpoint.y,
                Mathf.Clamp((left+right)*.28f,.17f,2.5f),Mathf.Min(duration,.16f));
            trailHead=(trailHead+1)%MaxTrails;
            trailCount=Mathf.Min(MaxTrails,trailCount+1);
            state.Rear=rear;
            state.PressureCenter=pressureCenter;
            state.SegmentTime=pendingTime;
        }

        private void Emit(Vector3 p, Vector2 direction, float amplitude, float time)
        {
            if (amplitude<=0) return;
            sourceHistory[waveHead]=new Vector4(p.x,p.z,time,amplitude);
            directionHistory[waveHead]=new Vector4(direction.x,direction.y,0,0);
            waveHead=(waveHead+1)%MaxWaves;
            waveCount=Mathf.Min(MaxWaves,waveCount+1);
        }

        private void Upload(float time)
        {
            int count=0;
            for(int i=0;i<waveCount;i++)
            {
                int index=(waveHead-1-i+MaxWaves)%MaxWaves;
                Vector4 source=sourceHistory[index];
                source.z=time-source.z;
                if(source.z>=Lifetime) continue;
                sources[count]=source;
                directions[count++]=directionHistory[index];
            }
            ActiveWaves=count;
            int trailVisible=0;
            for(int i=0;i<trailCount;i++)
            {
                int index=(trailHead-1-i+MaxTrails)%MaxTrails;
                Vector4 shape=shapeHistory[index];
                shape.z=time-shape.z;
                if(shape.z>=TrailLife) break;
                trailSegments[trailVisible]=trailHistory[index];
                trailPressures[trailVisible]=pressureHistory[index];
                trailShapes[trailVisible++]=shape;
            }
            ActiveTrailSegments=trailVisible;
            surface.GetPropertyBlock(block);
            block.SetInt(CountId,count);
            block.SetInt(TrailCountId,trailVisible);
            if(count>0)
            {
                block.SetVectorArray(SourcesId,sources);
                block.SetVectorArray(DirectionsId,directions);
            }
            if(trailVisible>0)
            {
                block.SetVectorArray(TrailSegmentsId,trailSegments);
                block.SetVectorArray(TrailShapeId,trailShapes);
                block.SetVectorArray(TrailPressureId,trailPressures);
            }
            surface.SetPropertyBlock(block);
        }

        private void OnDisable()
        {
            if (inFlight && !request.done) request.WaitForCompletion();
            inFlight=false;
            positions?.Dispose();
            heights?.Dispose();
            positions=heights=null;
            if (!surface || block==null) return;
            surface.GetPropertyBlock(block);
            block.SetInt(CountId,0);
            block.SetInt(TrailCountId,0);
            surface.SetPropertyBlock(block);
        }
    }
}
