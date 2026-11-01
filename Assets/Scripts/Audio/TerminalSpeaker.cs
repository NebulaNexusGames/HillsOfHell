using UnityEngine;

namespace CarGame.Audio
{
    /// <summary>
    /// Общий аппарат музыки терминалов: маленький динамик у самой панели.
    /// Источник пространственный (трек слышен от терминала, а не «в голове»),
    /// а сигнал проходит через полосовой фильтр и лёгкий перегруз — «радио /
    /// динамик». Источник живёт на дочернем объекте панели, поэтому не спорит
    /// с её служебными звуками и умирает вместе с ней.
    /// </summary>
    public static class TerminalSpeaker
    {
        public const float DefaultMinDistance = 1f;
        public const float DefaultMaxDistance = 18f;
        public const float LowPassCutoff = 3400f;
        public const float HighPassCutoff = 220f;
        public const float DistortionLevel = 0.28f;

        /// <summary>
        /// Создаёт (или берёт готовый) пространственный источник-динамик на
        /// дочернем объекте панели и вешает на него фильтры радио.
        /// </summary>
        public static AudioSource Ensure(MonoBehaviour host, ref AudioSource source, string name = "TerminalSpeaker")
        {
            if (source != null) return source;
            if (host == null) return null;

            GameObject go = new GameObject(name);
            go.layer = host.gameObject.layer;
            go.transform.SetParent(host.transform, false);

            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.spatialize = false;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = DefaultMinDistance;
            source.maxDistance = DefaultMaxDistance;
            source.volume = 0f;

            AudioLowPassFilter low = go.AddComponent<AudioLowPassFilter>();
            low.cutoffFrequency = LowPassCutoff;
            low.lowpassResonanceQ = 1f;

            AudioHighPassFilter high = go.AddComponent<AudioHighPassFilter>();
            high.cutoffFrequency = HighPassCutoff;
            high.highpassResonanceQ = 1f;

            AudioDistortionFilter distortion = go.AddComponent<AudioDistortionFilter>();
            distortion.distortionLevel = DistortionLevel;

            return source;
        }

        /// <summary>Ставит динамик в точку терминала, чтобы звук шёл от панели.</summary>
        public static void PlaceAt(AudioSource source, Transform anchor)
        {
            if (source == null || anchor == null) return;

            source.transform.position = anchor.position;
            source.transform.rotation = anchor.rotation;
        }
    }
}
