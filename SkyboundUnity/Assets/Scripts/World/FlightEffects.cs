using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Skybound
{
    /// <summary>
    /// Subtle bloom on lights and the sun plus gently darker corners on the High tier. Built from
    /// code so it can follow the graphics tier. Its priority puts it over the template's Global
    /// Volume. Tone mapping is off here: the ToneMap renderer features do three.js's curve instead,
    /// and the vignette with it, since URP's vignette has different controls.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FlightEffects : MonoBehaviour
    {
        const float VOLUME_PRIORITY = 100;
        const float BLOOM_INTENSITY = 0.35f;
        const float BLOOM_SCATTER = 0.9f;

        WorldBuilder world;
        VolumeProfile profile;
        Bloom bloom;
        UniversalAdditionalCameraData cameraData;
        GraphicsProfile applied;
        bool appliedPostFx;

        public bool EffectsOn => bloom != null && bloom.intensity.value > 0;

        void Awake()
        {
            world = FindAnyObjectByType<WorldBuilder>();
            cameraData = GetComponent<Camera>().GetUniversalAdditionalCameraData();

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tonemapping = profile.Add<Tonemapping>();
            tonemapping.mode.Override(TonemappingMode.None);
            bloom = profile.Add<Bloom>();
            // The browser's luminanceThreshold .92 is linear; URP's threshold is in gamma.
            bloom.threshold.Override(Mathf.LinearToGammaSpace(0.92f));
            bloom.intensity.Override(BLOOM_INTENSITY);
            // The browser's mipmap blur spreads the sky's glow right across the ground.
            bloom.scatter.Override(BLOOM_SCATTER);
            // The template's profile has these; the browser game doesn't.
            profile.Add<MotionBlur>().intensity.Override(0);
            profile.Add<Vignette>().intensity.Override(0);

            var holder = new GameObject("Flight effects");
            holder.transform.SetParent(transform, false);
            var volume = holder.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = VOLUME_PRIORITY;
            volume.sharedProfile = profile;
        }

        void OnDestroy()
        {
            Destroy(profile);
            Shader.SetGlobalFloat("_SkyboundComposer", 0);
        }

        void Update()
        {
            var postFx = SettingsStore.Get().postFx;
            if (world.Profile == null || (world.Profile == applied && postFx == appliedPostFx)) return;
            applied = world.Profile;
            appliedPostFx = postFx;
            var on = applied.postFx && postFx;
            // Intensity rather than active: with this one inactive, the template's own bloom would show.
            bloom.intensity.Override(on ? BLOOM_INTENSITY : 0);
            Shader.SetGlobalFloat("_SkyboundComposer", on ? 1 : 0);
            // Low and Medium have nothing for URP's post-processing pass to do.
            cameraData.renderPostProcessing = on;
        }
    }
}
