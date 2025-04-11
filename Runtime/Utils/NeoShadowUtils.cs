using UnityEngine;
using UnityEngine.Rendering;

namespace HSR.NPRShader.Utils
{
    /// <summary>
    /// Temporary replacement for URP's ShadowUtils
    /// (com.unity.render-pipelines.universal/Runtime/ShadowUtils.cs)
    /// </summary>
    public class NeoShadowUtils
    {
        public static void SetupShadowCasterConstantBuffer(RasterCommandBuffer cmd, ref VisibleLight shadowLight, Vector4 shadowBias)
        {
            SetShadowBias(cmd, shadowBias);

            // Light direction is currently used in shadow caster pass to apply shadow normal offset (normal bias).
            Vector3 lightDirection = -shadowLight.localToWorldMatrix.GetColumn(2);
            SetLightDirection(cmd, lightDirection);

            // For punctual lights, computing light direction at each vertex position provides more consistent results (shadow shape does not change when "rotating the point light" for example)
            Vector3 lightPosition = shadowLight.localToWorldMatrix.GetColumn(3);
            SetLightPosition(cmd, lightPosition);
        }
        
        internal static void SetShadowBias(RasterCommandBuffer cmd, Vector4 shadowBias)
        {
            cmd.SetGlobalVector(ShaderPropertyId.shadowBias, shadowBias);
        }

        internal static void SetLightDirection(RasterCommandBuffer cmd, Vector3 lightDirection)
        {
            cmd.SetGlobalVector(ShaderPropertyId.lightDirection, new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0.0f));
        }

        internal static void SetLightPosition(RasterCommandBuffer cmd, Vector3 lightPosition)
        {
            cmd.SetGlobalVector(ShaderPropertyId.lightPosition, new Vector4(lightPosition.x, lightPosition.y, lightPosition.z, 1.0f));
        }

        internal static class ShaderPropertyId
        {
            public static readonly int shadowBias = Shader.PropertyToID("_ShadowBias");
            public static readonly int lightDirection = Shader.PropertyToID("_LightDirection");
            public static readonly int lightPosition = Shader.PropertyToID("_LightPosition");
        }
    }
}