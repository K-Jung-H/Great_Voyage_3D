using System;
using UnityEditor;
using UnityEngine;

namespace GreatVoyage.Editor
{
    public static class DwtWakeKernel
    {
        public const string AssetPath = "Assets/Ocean/DWT/Data/DeepWaterImpulse.asset";

        [MenuItem("Great Voyage/Generate Deep Water Impulse")]
        public static void Generate()
        {
            const int width = 512, height = 192, bands = 256;
            const double radiusRange = 80, timeRange = 30, dk = 8.0 / bands;
            var radialTerms = new double[width, bands];
            for (int kIndex = 0; kIndex < bands; kIndex++)
            {
                double k = (kIndex + .5) * dk;
                // Radial inverse transform of -k * P(k) * sin(sqrt(k)*t)/sqrt(k).
                double weight = -Math.Pow(k, 1.5) * Math.Exp(-.5 * k * k) * dk;
                for (int x = 0; x < width; x++)
                    radialTerms[x, kIndex] = weight * BesselJ0(k * radiusRange * x / (width - 1));
            }

            var pixels = new Color[width * height];
            var phase = new double[bands];
            for (int y = 0; y < height; y++)
            {
                double t = timeRange * y / (height - 1);
                for (int k = 0; k < bands; k++)
                    phase[k] = Math.Sin(Math.Sqrt((k + .5) * dk) * t);
                for (int x = 0; x < width; x++)
                {
                    double response = 0;
                    for (int k = 0; k < bands; k++) response += radialTerms[x, k] * phase[k];
                    pixels[y * width + x] = new Color((float)response, 0, 0, 1);
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RHalf, false, true)
            {
                name = "DeepWaterImpulse",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
            if (existing)
            {
                EditorUtility.CopySerialized(texture, existing);
                UnityEngine.Object.DestroyImmediate(texture);
                texture = existing;
            }
            else AssetDatabase.CreateAsset(texture, AssetPath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(DwtOceanSetup.MaterialPath);
            material.SetTexture("_WakeImpulse", texture);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        private static double BesselJ0(double x)
        {
            if (x < 16)
            {
                double sum = 0;
                for (int i = 0; i < 32; i++)
                    sum += Math.Cos(x * Math.Cos(Math.PI * (i + .5) / 32));
                return sum / 32;
            }
            double phase = x - Math.PI / 4;
            return Math.Sqrt(2 / (Math.PI * x)) *
                (Math.Cos(phase) + Math.Sin(phase) / (8*x) - 9*Math.Cos(phase)/(128*x*x));
        }
    }
}
