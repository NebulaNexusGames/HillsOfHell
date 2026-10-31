using UnityEngine;

namespace CarGame.Player
{
    /// <summary>
    /// Тряска камеры от урона: направленный импульс в сторону ОТ угрозы плюс
    /// затухающий шум. Это только математика — трансформом камеры владеет
    /// PlayerController, иначе тряска применялась бы до обновления обзора
    /// и затиралась бы им в тот же кадр.
    /// </summary>
    public sealed class CameraShake
    {
        private const float TraumaDecayPerSecond = 1.6f;
        private const float DirectionalDecay = 7.5f;
        private const float NoiseSpeed = 26f;

        private float seed;
        private bool seeded;

        private float trauma;
        private Vector3 directional;
        private float noiseTime;

        /// <summary>
        /// Задаёт зерно шума. Вызывать из Awake: Unity запрещает Random.Range
        /// в конструкторе и в инициализаторах полей MonoBehaviour, а CameraShake
        /// создаётся именно там.
        /// </summary>
        public void Initialize()
        {
            if (seeded) return;

            seed = Random.Range(0f, 64f);
            seeded = true;
        }

        /// <summary>Насколько сильная тряска прямо сейчас, 0..1.</summary>
        public float Trauma => trauma;

        /// <summary>
        /// Добавляет импульс тряски.
        /// </summary>
        /// <param name="strength">Сила импульса.</param>
        /// <param name="awayFromThreat">Направление ОТ источника урона. Для DamageInfo это direction: урон приходит по нему, поэтому уводить камеру нужно по этому же вектору.</param>
        /// <param name="directionWeight">Доля импульса, уходящая именно в направление от угрозы, 0..1.</param>
        public void AddImpulse(float strength, Vector3 awayFromThreat, float directionWeight)
        {
            float amount = Mathf.Max(0f, strength);
            if (amount <= 0f) return;

            trauma = Mathf.Clamp01(trauma + amount);

            Vector3 direction = Vector3.ClampMagnitude(awayFromThreat, 1f);
            directional += direction * (amount * Mathf.Clamp01(directionWeight));
        }

        public void Clear()
        {
            trauma = 0f;
            directional = Vector3.zero;
            noiseTime = 0f;
        }

        /// <summary>
        /// Считает смещение в локальных осях камеры: сдвиг и доворот вокруг взгляда.
        /// </summary>
        public void Evaluate(float deltaTime, float maxPosition, float maxAngle, out Vector3 localOffset, out float localRoll)
        {
            localOffset = Vector3.zero;
            localRoll = 0f;

            float dt = Mathf.Max(0f, deltaTime);
            if (dt <= 0f) return;

            trauma = Mathf.Max(0f, trauma - TraumaDecayPerSecond * dt);
            directional = Vector3.Lerp(directional, Vector3.zero, 1f - Mathf.Exp(-DirectionalDecay * dt));

            // Направленная часть гаснет быстро, поэтому шум добавляется только пока
            // есть запас энергии — иначе камера дрожит ещё секунду после урона.
            if (trauma <= 0.001f && directional.sqrMagnitude <= 0.0001f) return;

            noiseTime += dt * NoiseSpeed;

            float shake = trauma * trauma;

            localOffset = new Vector3(
                SignedNoise(seed, noiseTime) * (shake * maxPosition),
                SignedNoise(seed + 11.3f, noiseTime) * (shake * maxPosition),
                SignedNoise(seed + 27.7f, noiseTime) * (shake * maxPosition * 0.5f))
                + directional;

            localRoll = SignedNoise(seed + 41.9f, noiseTime) * (shake * maxAngle);
        }

        private static float SignedNoise(float x, float y)
        {
            return Mathf.PerlinNoise(x, y) * 2f - 1f;
        }
    }
}