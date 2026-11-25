using System.Collections.Generic;
using UnityEngine;

namespace GreatVoyage.Ocean
{
    public sealed class WaterInteractor : MonoBehaviour
    {
        internal static readonly List<WaterInteractor> Active = new();
        [SerializeField] private Collider contactShape;
        [SerializeField, Min(0)] private float response = 1;
        internal float Response => response;
        public enum InteractionMode { Contact, Wake }

        [SerializeField] private InteractionMode mode;
        [SerializeField] private Transform wakeAnchor;
        [SerializeField, Min(.1f)] private float wakeWidth = 2.6f;
        [SerializeField, Min(0)] private float wakeRearOffset = 3.5f;
        internal bool EmitsWake => mode == InteractionMode.Wake;
        internal Vector3 WakeCenter => wakeAnchor ? wakeAnchor.position : transform.position;
        internal float WakeWidth => Mathf.Max(.1f, wakeWidth);
        internal float WakeRearOffset => Mathf.Max(0, wakeRearOffset);
        internal bool SampleWakeAttitude { get; set; }

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        internal int Sample(Vector3[] points, Vector3[] normals)
        {
            if (EmitsWake)
            {
                points[0] = WakeCenter;
                normals[0] = Vector3.zero;
                if (!SampleWakeAttitude) return 1;
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward,Vector3.up).normalized;
                Vector3 side = Vector3.Cross(Vector3.up,forward);
                float halfLength = Mathf.Max(.5f,WakeRearOffset*.8f);
                float halfWidth = WakeWidth*.45f;
                points[1] = points[0]+forward*halfLength;
                points[2] = points[0]-forward*halfLength;
                points[3] = points[0]+side*halfWidth;
                points[4] = points[0]-side*halfWidth;
                return 5;
            }
            if (!contactShape || !contactShape.enabled || !contactShape.gameObject.activeInHierarchy)
                return 0;
            Transform shape = contactShape.transform;
            if (contactShape is BoxCollider box)
            {
                Vector3 half = box.size * .5f;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? -1 : 1,
                        (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                    points[i] = shape.TransformPoint(box.center + Vector3.Scale(corner, half));
                    normals[i] = shape.TransformDirection(new Vector3(corner.x, 0, corner.z));
                }
                return 8;
            }
            if (contactShape is SphereCollider sphere)
            {
                Vector3 scale = shape.lossyScale;
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x),
                    Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                points[0] = shape.TransformPoint(sphere.center) - Vector3.up * radius;
                normals[0] = Vector3.zero;
                return 1;
            }
            if (contactShape is CapsuleCollider capsule)
            {
                Vector3 axis = capsule.direction == 0 ? Vector3.right :
                    capsule.direction == 1 ? Vector3.up : Vector3.forward;
                Vector3 side = capsule.direction == 0 ? Vector3.forward : Vector3.right;
                Vector3 other = Vector3.Cross(axis, side);
                float axisScale = shape.TransformVector(axis).magnitude;
                float radius = capsule.radius * Mathf.Max(shape.TransformVector(side).magnitude,
                    shape.TransformVector(other).magnitude);
                Vector3 center = shape.TransformPoint(capsule.center);
                Vector3 worldAxis = shape.TransformDirection(axis);
                Vector3 worldSide = Vector3.Cross(Vector3.up, worldAxis).normalized;
                float span = Mathf.Max(0, capsule.height * axisScale * .5f - radius);
                for (int i = 0; i < 6; i++)
                {
                    float along = (i / 2) - 1;
                    Vector3 outward = worldSide * ((i & 1) == 0 ? -1 : 1) + worldAxis * along;
                    points[i] = center + worldAxis * (along * span)
                        + outward.normalized * (radius * .7f) - Vector3.up * (radius * .65f);
                    normals[i] = outward;
                }
                points[6] = center - worldAxis * span * Mathf.Sign(worldAxis.y) - Vector3.up * radius;
                normals[6] = Vector3.zero;
                return 7;
            }
            return 0;
        }
    }
}
