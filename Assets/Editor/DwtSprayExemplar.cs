using System;
using UnityEditor;
using UnityEngine;

namespace GreatVoyage.Editor
{
    public static class DwtSprayExemplar
    {
        public const string AssetPath = "Assets/Ocean/DWT/Data/SprayExemplar.asset";
        public const string PhasePath = "Assets/Ocean/DWT/Data/ComplexPhaseExemplar.asset";

        [MenuItem("Great Voyage/Generate DWT Spray Exemplar")]
        public static void Generate()
        {
            const int cells = 20, size = 256, frames = 32;
            const int candidates = 3;
            var random = new System.Random(4721);
            var seeds = new Vector4[cells, cells, candidates];
            for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
            {
                float u = 2 * Mathf.PI * (x + .5f) / cells;
                float v = 2 * Mathf.PI * (y + .5f) / cells;
                float density = Mathf.Clamp01(.59f + .29f * Mathf.Sin(2 * u + v)
                    + .24f * Mathf.Cos(u - 3 * v) + .12f * Mathf.Sin(4 * u + 2 * v));
                for (int i = 0; i < candidates; i++)
                {
                    float probability = i == 0 ? .42f + .58f * density : density * density * (i == 1 ? .95f : .65f);
                    float radius = random.NextDouble() < probability
                        ? Mathf.Lerp(.12f, .31f, (float)random.NextDouble()) : 0;
                    seeds[x, y, i] = new Vector4((float)random.NextDouble(), (float)random.NextDouble(),
                        (float)random.NextDouble(), radius);
                }
            }

            var volume = new Texture3D(size, size, frames, TextureFormat.R8, true);
            volume.name = "Spray Exemplar";
            volume.wrapMode = TextureWrapMode.Repeat;
            volume.filterMode = FilterMode.Trilinear;
            var data = new byte[size * size * frames];
            var radii = new float[cells, cells, candidates];
            for (int frame = 0; frame < frames; frame++)
            {
                for (int y = 0; y < cells; y++)
                for (int x = 0; x < cells; x++)
                for (int i = 0; i < candidates; i++)
                {
                    var seed = seeds[x, y, i];
                    float phase = 2 * Mathf.PI * ((frame + .5f) / frames + seed.z);
                    radii[x, y, i] = seed.w * (.52f + .48f * Mathf.Sin(phase));
                }
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + .5f) * cells / size, (y + .5f) * cells / size);
                    int ix = (int)p.x, iy = (int)p.y;
                    float nearest = 1;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    for (int i = 0; i < candidates; i++)
                    {
                        int sx = (ix + dx + cells) % cells;
                        int sy = (iy + dy + cells) % cells;
                        float radius = radii[sx, sy, i];
                        if (radius < .035f) continue;
                        var seed = seeds[sx, sy, i];
                        var delta = p - new Vector2(ix + dx + seed.x, iy + dy + seed.y);
                        nearest = Mathf.Min(nearest, delta.magnitude - radius);
                    }
                    data[(frame * size + y) * size + x] =
                        (byte)Mathf.RoundToInt(255 * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.04f, .06f, nearest))));
                }
            }
            volume.SetPixelData(data, 0);
            volume.Apply(true, false);
            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(AssetPath);
            if (existing)
            {
                EditorUtility.CopySerialized(volume, existing);
                UnityEngine.Object.DestroyImmediate(volume);
                volume = existing;
            }
            else AssetDatabase.CreateAsset(volume, AssetPath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(DwtOceanSetup.MaterialPath);
            material.SetTexture("_SprayExemplar", volume);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        // Recreate the public demo's periodic Gaussian-windowed quadrature field.
        // Unity's import of the reference EXR replicates one channel into RGB.
        [MenuItem("Great Voyage/Generate DWT Complex Phase Exemplar")]
        public static void GeneratePhase()
        {
            const int size = 512, kernelSize = 256;
            var random = new System.Random(32841);
            var field = new Color[size * size];
            var weights = new float[kernelSize * kernelSize];
            for (int y = 0; y < kernelSize; y++)
            for (int x = 0; x < kernelSize; x++)
            {
                float px = x / 128f - 1, py = y / 128f - 1;
                weights[y * kernelSize + x] = .39894228f * Mathf.Exp(-4.5f * (px * px + py * py));
            }
            for (int kernel = 0; kernel < 256; kernel++)
            {
                int ox = random.Next(size), oy = random.Next(size);
                float phase = 2 * Mathf.PI * (float)random.NextDouble();
                for (int x = 0; x < kernelSize; x++)
                {
                    float angle = phase + 2 * Mathf.PI * x * 10 / size;
                    float real = Mathf.Sin(angle), imaginary = Mathf.Cos(angle);
                    int tx = (x + ox) % size;
                    for (int y = 0; y < kernelSize; y++)
                    {
                        int index = ((y + oy) % size) * size + tx;
                        float weight = weights[y * kernelSize + x];
                        field[index].r += weight * real;
                        field[index].g += weight * imaginary;
                        field[index].a = 1;
                    }
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBAHalf, true, true);
            texture.name = "Complex Phase Exemplar";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.SetPixels(field);
            texture.Apply(true, false);
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(PhasePath);
            if (existing)
            {
                EditorUtility.CopySerialized(texture, existing);
                UnityEngine.Object.DestroyImmediate(texture);
                texture = existing;
            }
            else AssetDatabase.CreateAsset(texture, PhasePath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(DwtOceanSetup.MaterialPath);
            material.SetTexture("_Exemplar", texture);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }
    }
}
