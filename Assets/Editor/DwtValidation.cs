using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GreatVoyage.Editor
{
    public static class DwtValidation
    {
        public static string Run()
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(DwtOceanSetup.MaterialPath);
            var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Ocean/DWT/Shaders/DwtProbe.compute");
            if(!material || !compute) throw new Exception("DWT validation assets missing.");
            int kernel=compute.FindKernel("Probe");
            for(int i=0;i<5;i++) compute.SetVector("_Wave"+i,material.GetVector("_Wave"+i));
            foreach(var name in new[]{"_Exemplar","_Profile"}) compute.SetTexture(kernel,name,material.GetTexture(name));
            var exemplar=material.GetTexture("_Exemplar");
            var phasePixels=((Texture2D)exemplar).GetPixels();
            float maxDifference=0, greenMin=float.MaxValue, greenMax=float.MinValue;
            foreach(var pixel in phasePixels)
            {
                maxDifference=Mathf.Max(maxDifference,Mathf.Abs(pixel.r-pixel.g));
                greenMin=Mathf.Min(greenMin,pixel.g);
                greenMax=Mathf.Max(greenMax,pixel.g);
            }
            if(maxDifference<.01f || greenMin>=-.01f || greenMax<=.01f)
                throw new Exception("DWT phase exemplar requires two distinct signed channels.");
            compute.SetVector("_Exemplar_TexelSize",new Vector4(1f/exemplar.width,1f/exemplar.height,exemplar.width,exemplar.height));
            compute.SetVector("_OceanOrigin",Vector4.zero);
            compute.SetFloat("_HeightScale",1);
            compute.SetFloat("_Speed",1);
            using(var buffer=new ComputeBuffer(1024,sizeof(float)))
            {
                compute.SetBuffer(kernel,"_Results",buffer);
                float[] Sample(float time,float spacing)
                {
                    compute.SetFloat("_OceanTime",time);
                    compute.SetFloat("_ProbeSpacing",spacing);
                    compute.Dispatch(kernel,4,4,1);
                    var values=new float[1024]; buffer.GetData(values);
                    foreach(float value in values) if(float.IsNaN(value)||float.IsInfinity(value)) throw new Exception("Non-finite height.");
                    return values;
                }
                var first=Sample(0,.02f);
                var repeat=Sample(0,.02f);
                var later=Sample(2,.02f);
                var filtered=Sample(0,64);
                double error=0,change=0,energy=0,filteredEnergy=0;
                float min=float.MaxValue,max=float.MinValue;
                for(int i=0;i<first.Length;i++)
                {
                    error=Math.Max(error,Math.Abs(first[i]-repeat[i]));
                    change+=(first[i]-later[i])*(first[i]-later[i]);
                    energy+=first[i]*first[i]; filteredEnergy+=filtered[i]*filtered[i];
                    min=Math.Min(min,first[i]); max=Math.Max(max,first[i]);
                }
                change=Math.Sqrt(change/first.Length);
                energy=Math.Sqrt(energy/first.Length);
                filteredEnergy=Math.Sqrt(filteredEnergy/first.Length);
                if(error>1e-6 || change<.01 || energy<.01 || filteredEnergy>=energy*.25)
                    throw new Exception($"DWT invariant failed: repeat={error}, temporal={change}, RMS={energy}, filtered={filteredEnergy}");
                // Zero profile must produce a flat field, independent of time.
                for(int i=0;i<5;i++) { var wave=material.GetVector("_Wave"+i); wave.z=0; compute.SetVector("_Wave"+i,wave); }
                var calm=Sample(15,.02f);
                float calmMin=float.MaxValue,calmMax=float.MinValue;
                foreach(var height in calm) { calmMin=Math.Min(calmMin,height); calmMax=Math.Max(calmMax,height); }
                if(calmMax-calmMin>1e-6f || Math.Abs(calmMax)>.02) throw new Exception("Zero profile does not become flat.");
                string report=DwtOceanSetup.ValidateData()+
                    $"GPU samples: 1024; range=[{min:F5},{max:F5}] m\n"+
                    $"Repeat max error={error:G6}; temporal RMS delta (2s)={change:F5} m\n"+
                    $"Fine RMS={energy:F5} m; 64m footprint RMS={filteredEnergy:F5} m\n"+
                    $"Zero profile range=[{calmMin:G6},{calmMax:G6}] m\n"+
                    "PASS: finite heights, deterministic evaluation, animated waves, mip attenuation, flat zero-profile.\n";
                Directory.CreateDirectory("Documentation");
                File.WriteAllText("Documentation/DWT_Validation.txt",report);
                return report;
            }
        }
    }
}
