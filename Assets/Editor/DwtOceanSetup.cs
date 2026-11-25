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
    public static class DwtOceanSetup
    {
        public const string MaterialPath = "Assets/Ocean/DWT/DynamicWaveTrains.mat";
        private const string MeshPath = "Assets/Ocean/DWT/OceanGrid.asset";

        [MenuItem("Great Voyage/Install DWT Waves")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != OceanSceneSetup.ScenePath) throw new InvalidOperationException("Open OceanOnly first.");
            ConfigureData("Assets/Ocean/DWT/Data/profile.exr", false);
            var shader = Shader.Find("GreatVoyage/DynamicWaveTrains");
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("DWT shader missing or has compilation errors.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "DynamicWaveTrains" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            if (!AssetDatabase.LoadAssetAtPath<Texture2D>(DwtSprayExemplar.PhasePath))
                DwtSprayExemplar.GeneratePhase();
            if (!AssetDatabase.LoadAssetAtPath<Texture3D>(DwtSprayExemplar.AssetPath))
                DwtSprayExemplar.Generate();
            material.SetTexture("_Exemplar", AssetDatabase.LoadAssetAtPath<Texture2D>(DwtSprayExemplar.PhasePath));
            material.SetTexture("_SprayExemplar", AssetDatabase.LoadAssetAtPath<Texture3D>(DwtSprayExemplar.AssetPath));
            if (!AssetDatabase.LoadAssetAtPath<Texture2D>(DwtWakeKernel.AssetPath))
                DwtWakeKernel.Generate();
            material.SetTexture("_WakeImpulse", AssetDatabase.LoadAssetAtPath<Texture2D>(DwtWakeKernel.AssetPath));
            material.SetTexture("_Profile", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Ocean/DWT/Data/profile.exr"));
            material.SetTexture("_FoamMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Ocean/DWT/Data/Foam.jpg"));
            material.SetFloat("_MeshSpacing", .5f);
            material.SetFloat("_OceanTime", 0);
            var water = GameObject.Find("OceanSurface_PreparationMesh") ?? GameObject.Find("OceanSurface_DWT");
            if (!water) throw new InvalidOperationException("Ocean surface missing.");
            water.name = "OceanSurface_DWT";
            water.GetComponent<MeshRenderer>().sharedMaterial = material;
            water.GetComponent<MeshFilter>().sharedMesh = MakeMesh();
            if (!water.GetComponent<GreatVoyage.Ocean.DwtOceanClock>())
                water.AddComponent<GreatVoyage.Ocean.DwtOceanClock>();
            EditorUtility.SetDirty(material);
            // Keep optical DOF available, but off while inspecting wave detail.
            var volume = UnityEngine.Object.FindAnyObjectByType<Volume>();
            if (volume && volume.sharedProfile.TryGet<DepthOfField>(out var dof))
            {
                dof.active = false;
                EditorUtility.SetDirty(volume.sharedProfile);
                EditorUtility.SetDirty(dof);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = water;
        }

        private static void ConfigureData(string path, bool repeat)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 1;
            importer.isReadable = true; // Used only by the editor data validation.
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaSource = TextureImporterAlphaSource.None;
            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.format = TextureImporterFormat.RGBAHalf;
            platform.maxTextureSize = 1024;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
        }

        private static Mesh MakeMesh()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing) return existing;
            const int cells = 256;
            const float size = 128;
            var vertices = new Vector3[(cells+1)*(cells+1)];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[cells*cells*6];
            for(int z=0;z<=cells;z++) for(int x=0;x<=cells;x++)
            {
                int i=z*(cells+1)+x;
                uv[i]=new Vector2((float)x/cells,(float)z/cells);
                vertices[i]=new Vector3((uv[i].x-.5f)*size,0,(uv[i].y-.5f)*size);
                normals[i]=Vector3.up;
            }
            int t=0;
            for(int z=0;z<cells;z++) for(int x=0;x<cells;x++)
            {
                int i=z*(cells+1)+x;
                triangles[t++]=i; triangles[t++]=i+cells+1; triangles[t++]=i+1;
                triangles[t++]=i+1; triangles[t++]=i+cells+1; triangles[t++]=i+cells+2;
            }
            var mesh=new Mesh { name="DWT_128m_0.5m", indexFormat=IndexFormat.UInt32 };
            mesh.vertices=vertices; mesh.normals=normals; mesh.uv=uv; mesh.triangles=triangles;
            mesh.bounds=new Bounds(Vector3.zero,new Vector3(size,32,size));
            AssetDatabase.CreateAsset(mesh,MeshPath);
            return mesh;
        }

        public static void ConfigureSky()
        {
            const string texturePath = "Assets/Ocean/DWT/Data/DaySky.exr";
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.textureShape = TextureImporterShape.TextureCube;
            importer.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.cubemapConvolution = TextureImporterCubemapConvolution.Specular;
            importer.SetTextureSettings(settings);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(texturePath);
            if (!cube) throw new Exception("HDR environment cubemap import failed.");
            const string skyPath = "Assets/Ocean/DWT/DaySky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (!sky)
            {
                sky = new Material(Shader.Find("Skybox/Cubemap"));
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetTexture("_Tex", cube);
            sky.SetFloat("_Exposure", 1);
            RenderSettings.skybox = sky;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.55f,.65f,.8f);
            RenderSettings.ambientEquatorColor = new Color(.25f,.32f,.4f);
            RenderSettings.ambientGroundColor = new Color(.08f,.12f,.16f);
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            material.SetColor("_WaterColor",new Color(.10f,.32f,.38f));
            material.SetFloat("_Roughness",.23f);
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(sky);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        public static string ValidateData()
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material) throw new Exception("DWT material missing.");
            var report=new System.Text.StringBuilder();
            foreach(var property in new[]{"_Exemplar","_Profile"})
            {
                var texture=(Texture2D)material.GetTexture(property);
                float min=float.MaxValue,max=float.MinValue;
                foreach(var p in texture.GetPixels()) { min=Mathf.Min(min,p.r); max=Mathf.Max(max,p.r); }
                if (!(min<-.01f && max>.01f)) throw new Exception(property+" lost signed floating point data.");
                report.AppendLine($"{property}: {texture.width}x{texture.height}, {texture.format}, mip={texture.mipmapCount}, red=[{min}, {max}]");
            }
            if(ShaderUtil.ShaderHasError(material.shader)) throw new Exception("DWT shader compilation failed.");
            report.AppendLine("DWT signed data and shader validation passed.");
            return report.ToString();
        }
    }
}
