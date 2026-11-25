using UnityEngine;
using UnityEngine.InputSystem;

namespace GreatVoyage.Ocean
{
    [RequireComponent(typeof(WaterInteractor))]
    public sealed class WakeTestHullController : MonoBehaviour
    {
        [SerializeField] private DwtWaterInteractions water;
        [SerializeField] private Camera followCamera;
        [SerializeField, Min(.1f)] private float maxForwardSpeed = 8;
        [SerializeField, Min(.1f)] private float maxReverseSpeed = 3;
        [SerializeField, Min(.1f)] private float acceleration = 3;
        [SerializeField, Min(.1f)] private float deceleration = 5;
        [SerializeField, Min(.1f)] private float turnSpeed = 65;
        [SerializeField] private float waterlineOffset = .28f;
        [SerializeField] private Vector3 cameraOffset = new(0,8,-15);
        private WaterInteractor interactor;
        private float heading;
        private bool cameraPlaced;
        public float Speed { get; private set; }

        private void OnEnable()
        {
            interactor=GetComponent<WaterInteractor>();
            interactor.SampleWakeAttitude=true;
            heading=transform.eulerAngles.y;
            Speed=0;
            cameraPlaced=false;
        }

        private void OnDisable()
        {
            if(interactor) interactor.SampleWakeAttitude=false;
        }

        private void Update()
        {
            Keyboard keyboard=Keyboard.current;
            float throttle=0, steering=0;
            if(keyboard!=null)
            {
                throttle=(keyboard.wKey.isPressed ? 1 : 0)-(keyboard.sKey.isPressed ? 1 : 0);
                steering=(keyboard.dKey.isPressed ? 1 : 0)-(keyboard.aKey.isPressed ? 1 : 0);
            }
            float dt=Time.deltaTime;
            float target=throttle>=0 ? throttle*maxForwardSpeed : throttle*maxReverseSpeed;
            bool braking=throttle==0 || Speed*target<0;
            Speed=Mathf.MoveTowards(Speed,target,(braking ? deceleration : acceleration)*dt);
            float steerGrip=Mathf.Clamp01(Mathf.Abs(Speed)/2);
            heading+=steering*turnSpeed*steerGrip*(Speed<0 ? -1 : 1)*dt;
            Vector3 forward=Quaternion.Euler(0,heading,0)*Vector3.forward;
            Vector3 position=transform.position+forward*(Speed*dt);
            Quaternion rotation=Quaternion.LookRotation(forward,Vector3.up);
            if(water && water.TryGetSurfacePose(interactor,out float level,out Vector3 normal))
            {
                position.y=Mathf.Lerp(position.y,level+waterlineOffset,1-Mathf.Exp(-5*dt));
                // Keep heading in the XZ plane while adding pitch and roll around it.
                Vector3 tangent=Vector3.ProjectOnPlane(forward,normal);
                rotation=Quaternion.LookRotation(tangent,normal);
            }
            transform.SetPositionAndRotation(position,Quaternion.Slerp(transform.rotation,rotation,1-Mathf.Exp(-7*dt)));
            if(water)
            {
                // Follow in mesh-sized steps while preserving world-space evaluation.
                const float gridStep=4;
                Transform plane=water.transform;
                plane.position=new Vector3(Mathf.Round(position.x/gridStep)*gridStep,
                    plane.position.y,Mathf.Round(position.z/gridStep)*gridStep);
            }
        }

        private void LateUpdate()
        {
            if(!followCamera) return;
            Vector3 focus=transform.position+Vector3.up*.55f;
            Quaternion yaw=Quaternion.Euler(0,heading,0);
            Vector3 target=focus+yaw*cameraOffset;
            Transform cameraTransform=followCamera.transform;
            float blend=cameraPlaced ? 1-Mathf.Exp(-9*Time.deltaTime) : 1;
            cameraTransform.position=Vector3.Lerp(cameraTransform.position,target,blend);
            cameraTransform.rotation=Quaternion.LookRotation(focus+ yaw*Vector3.forward*1.8f-cameraTransform.position);
            cameraPlaced=true;
        }
    }
}
