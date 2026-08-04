// realvirtual.io (formerly game4automation) (R) a Framework for Automation Concept Design, Virtual Commissioning and 3D-HMI
// (c) 2024 realvirtual GmbH - Usage of this source code only allowed based on License conditions see https://realvirtual.io/unternehmen/lizenz  

using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;
using RenderSettings = UnityEngine.RenderSettings;

namespace realvirtual
{
    public class SkyboxSetup : MonoBehaviour
    {
        public Material skyMaterial;
        
        public bool useAmbientGradient = false;
        [ShowIf("useAmbientGradient")]
        public Color ambientColorTop = Color.white;
        [ShowIf("useAmbientGradient")]
        public Color ambientColorMiddle = Color.gray;
        [ShowIf("useAmbientGradient")]
        public Color ambientColorBottom = Color.black;

        public bool advanced = false;
        [FormerlySerializedAs("reflectionMaterial")] [ShowIf("advanced")]
        public Material environmentMaterial;
        [ShowIf("advanced")]
        public GameObject skyball;
        
        [Button]
        public void Apply()
        {
            // sky
            
            if (advanced)
            {
                RenderSettings.skybox = environmentMaterial;
                skyball.gameObject.SetActive(true);
            }
            else
            {
                RenderSettings.skybox = skyMaterial;
                skyball.gameObject.SetActive(false);
            }
            
            // ambient
            if(useAmbientGradient){
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = ambientColorTop;
                RenderSettings.ambientEquatorColor = ambientColorMiddle;
                RenderSettings.ambientGroundColor = ambientColorBottom;
            }else{
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            }
            
            DynamicGI.UpdateEnvironment();
            
            // No baked lighting is generated here: a GI bake would create a per-scene
            // LightingData/ReflectionProbe folder. The runtime skybox reflection from
            // DynamicGI.UpdateEnvironment() above is the live equivalent.
            
            
            
            
            
        }

    }
}
