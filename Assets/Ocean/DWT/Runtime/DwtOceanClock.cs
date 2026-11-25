using UnityEngine;

namespace GreatVoyage.Ocean
{
    // Intentionally not ExecuteAlways: edit-mode water is a static t=0 surface.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public sealed class DwtOceanClock : MonoBehaviour
    {
        private static readonly int OceanTimeId = Shader.PropertyToID("_OceanTime");
        private Renderer surface;
        private MaterialPropertyBlock properties;
        private double elapsed;

        public double ElapsedSeconds => elapsed;

        private void OnEnable()
        {
            surface = GetComponent<Renderer>();
            properties ??= new MaterialPropertyBlock();
            elapsed = 0;
            ApplyTime(0);
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            elapsed += Time.deltaTime;
            ApplyTime((float)elapsed);
        }

        private void OnDisable()
        {
            // Exiting Play Mode must not leave the renderer's last runtime time.
            elapsed = 0;
            ApplyTime(0);
        }

        private void ApplyTime(float value)
        {
            if (!surface || properties == null) return;
            surface.GetPropertyBlock(properties);
            properties.SetFloat(OceanTimeId, value);
            surface.SetPropertyBlock(properties);
        }
    }
}
