using UnityEngine;
using UnityEngine.Rendering;

namespace Skybound
{
    /// <summary>
    /// three.js materials as URP ones. MeshStandardMaterial becomes URP Lit, MeshBasicMaterial
    /// becomes the small Basic shader. The templates in Resources are made by the setup menu, so
    /// a build keeps the shader variants they use.
    /// </summary>
    public static class MaterialKit
    {
        const float SRC_ALPHA = (float)BlendMode.SrcAlpha;
        const float ONE_MINUS_SRC_ALPHA = (float)BlendMode.OneMinusSrcAlpha;

        public const string LitTemplatePath = "Skybound/Lit";
        public const string UnshadowedTemplatePath = "Skybound/LitUnshadowed";
        public const string GroundTemplatePath = "Skybound/Ground";

        /// <summary>three.js has no environment map here, so metal stays dark except for the sun's highlight.</summary>
        public static void TurnOffEnvironmentReflections(Material material)
        {
            material.SetFloat("_EnvironmentReflections", 0);
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }

        public static void TurnOnDetailMap(Material material)
        {
            material.EnableKeyword("_DETAIL_MULX2");
        }

        public static void TurnOffShadowReceiving(Material material)
        {
            material.SetFloat("_ReceiveShadows", 0);
            material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        }

        static Material NewLit(string templatePath)
        {
            var template = Resources.Load<Material>(templatePath);
            if (template != null) return new Material(template);
            // Not set up yet: this works in the Editor, though not in a build.
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            TurnOffEnvironmentReflections(material);
            if (templatePath == UnshadowedTemplatePath) TurnOffShadowReceiving(material);
            return material;
        }

        /// <summary>
        /// MeshStandardMaterial: both engines use roughness squared, so smoothness is 1 − roughness.
        /// three.js's receiveShadow is per mesh, off by default. In URP it's per material.
        /// </summary>
        public static Material Lit(string hex, float roughness = 1, float metalness = 0, bool receiveShadow = false)
        {
            var material = NewLit(receiveShadow ? LitTemplatePath : UnshadowedTemplatePath);
            material.SetColor("_BaseColor", Three.Hex(hex));
            material.SetFloat("_Smoothness", 1 - roughness);
            material.SetFloat("_Metallic", metalness);
            return material;
        }

        public static Material LitTextured(Texture texture, Vector2 repeat, float roughness)
        {
            var material = Lit("#ffffff", roughness, 0, true);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", repeat);
            return material;
        }

        /// <summary>The ground: baked vertex colours multiplied by a tiled detail map.</summary>
        public static Material LitGround(Texture colours, Texture detail, float detailRepeat)
        {
            var material = NewLit(GroundTemplatePath);
            TurnOnDetailMap(material);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", colours);
            material.SetTexture("_DetailAlbedoMap", detail);
            material.SetTextureScale("_DetailAlbedoMap", new Vector2(detailRepeat, detailRepeat));
            material.SetFloat("_Smoothness", 0);
            material.SetFloat("_Metallic", 0);
            return material;
        }

        public static Material DoubleSided(Material material)
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
            return material;
        }

        /// <summary>MeshBasicMaterial: unlit. The ToneMap pass adds tone mapping and fog.</summary>
        public static Material Basic(string hex)
        {
            var material = new Material(Resources.Load<Shader>("Shaders/Basic"));
            material.SetColor("_BaseColor", Three.Hex(hex));
            return material;
        }

        /// <summary>MeshBasicMaterial with toneMapped={false}: solid, but drawn after the ToneMap pass, so that skips it.</summary>
        public static Material BasicNotToneMapped(string hex)
        {
            var material = Basic(hex);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        /// <summary>MeshBasicMaterial with transparent, no depth write, DoubleSide.</summary>
        public static Material BasicTransparent(string hex, float opacity)
        {
            var material = Basic(hex);
            material.SetFloat("_SelfToneMap", 1);
            material.SetFloat("_SrcBlend", SRC_ALPHA);
            material.SetFloat("_DstBlend", ONE_MINUS_SRC_ALPHA);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.renderQueue = (int)RenderQueue.Transparent;
            SetOpacity(material, opacity);
            return material;
        }

        public static void SetOpacity(Material material, float opacity)
        {
            var color = material.GetColor("_BaseColor");
            color.a = opacity;
            material.SetColor("_BaseColor", color);
        }
    }
}
