using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GreatVoyage.Editor
{
    // Scene preparation only. This does not implement or approximate Dynamic Wave Trains.
    public static class OceanSceneSetup
    {
        public const string ScenePath = "Assets/Scenes/OceanOnly.unity";
        private const string Root = "Assets/Ocean";

        [MenuItem("Great Voyage/Create Ocean Scene")]
        public static void Create()
        {
            if (File.Exists(ScenePath))
                throw new InvalidOperationException("OceanOnly already exists; refusing to overwrite it.");
            if (SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the current scene before creating OceanOnly.");
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) throw new InvalidOperationException("An active URP asset is required.");
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Meshes");
            Directory.CreateDirectory(Root + "/Settings");
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var anchor = new GameObject("VesselAnchor");
            var environment = new GameObject("Environment");
            new GameObject("NearbyObjects");
            var water = new GameObject("OceanSurface_PreparationMesh");
            water.AddComponent<MeshFilter>().sharedMesh = MakeMesh();
            var renderer = water.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "OceanPreparation_NotDWT";
            material.SetColor("_BaseColor", new Color(.025f, .16f, .20f));
            material.SetFloat("_Smoothness", .93f);
            material.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(material, Root + "/Materials/OceanPreparation.mat");
            renderer.sharedMaterial = material;

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.rotation = Quaternion.Euler(48, 35, 0);
            camera.transform.position = anchor.transform.position - camera.transform.forward * 42;
            camera.orthographic = false;
            camera.fieldOfView = 38;
            camera.nearClipPlane = .3f;
            camera.farClipPlane = 350;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.Skybox;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.requiresDepthTexture = true;
            cameraData.requiresColorTexture = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var sunObject = new GameObject("Sun", typeof(Light));
            sunObject.transform.SetParent(environment.transform);
            sunObject.transform.rotation = Quaternion.Euler(38, -30, 0);
            var sun = sunObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, .94f, .82f);
            sun.intensity = 2;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", .025f);
            sky.SetFloat("_AtmosphereThickness", 1);
            sky.SetColor("_SkyTint", new Color(.53f, .59f, .65f));
            AssetDatabase.CreateAsset(sky, Root + "/Materials/OceanSky.mat");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, Root + "/Settings/OceanVolume.asset");
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.ACES;
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.value = .12f;
            bloom.threshold.value = 1.2f;
            var dof = profile.Add<DepthOfField>(true);
            dof.mode.value = DepthOfFieldMode.Bokeh;
            dof.focusDistance.value = 42;
            dof.focalLength.value = 65;
            dof.aperture.value = 4;
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            var volumeObject = new GameObject("Ocean Look", typeof(Volume));
            volumeObject.transform.SetParent(environment.transform);
            var volume = volumeObject.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            PlayerSettings.productName = "Great_Voyage_3D";
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            AssetDatabase.SaveAssets();
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(Vector3.zero, camera.transform.rotation, 35);
            Selection.activeGameObject = water;
        }

        private static Mesh MakeMesh()
        {
            const int resolution = 128;
            const float size = 256;
            var vertices = new Vector3[(resolution + 1) * (resolution + 1)];
            var uv = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var indices = new int[resolution * resolution * 6];
            for (int z = 0; z <= resolution; z++)
            for (int x = 0; x <= resolution; x++)
            {
                int i = z * (resolution + 1) + x;
                uv[i] = new Vector2((float)x / resolution, (float)z / resolution);
                vertices[i] = new Vector3((uv[i].x - .5f) * size, 0, (uv[i].y - .5f) * size);
                normals[i] = Vector3.up;
            }
            int offset = 0;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * (resolution + 1) + x;
                indices[offset++] = i; indices[offset++] = i + resolution + 1; indices[offset++] = i + 1;
                indices[offset++] = i + 1; indices[offset++] = i + resolution + 1; indices[offset++] = i + resolution + 2;
            }
            var mesh = new Mesh { name = "OceanGrid_128_Preparation" };
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = indices;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 32, size));
            AssetDatabase.CreateAsset(mesh, Root + "/Meshes/OceanGrid.asset");
            return mesh;
        }
    }
}
