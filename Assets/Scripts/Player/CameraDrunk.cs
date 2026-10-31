using UnityEngine;

namespace CarGame.Player
{
    /// <summary>
    /// Качание камеры от пива: только математика, как и CameraShake.
    /// Трансформом камеры владеет PlayerController, поэтому эффект
    /// оценивается там же и применяется после UpdateView, иначе обзор
    /// затирал бы наклон в тот же кадр.
    /// </summary>
    public sealed class CameraDrunk
    {
        private float remaining;
        private float duration;
        private float power;
        private float phase;
        private float seed;
        private float time;

        /// <summary>Идёт эффект.</summary>
        public bool Active => remaining > 0f;

        /// <summary>
        /// Задаёт зерно. Вызывать из Awake: Unity запрещает Random.Range
        /// в конструкторе и в инициализаторах полей MonoBehaviour.
        /// </summary>
        public void Initialize()
        {
            if (seed > 0f) return;
            seed = Random.Range(1f, 32f);
        }

        /// <summary>
        /// Запускает (или продлевает) эффект. Если эффект уже идёт, берётся
        /// больший срок: свежий глоток не укорачивает уже начатое опьянение.
        /// </summary>
        /// <param name="seconds">Сколько секунд держится, с.</param>
        /// <param name="amount">Насколько сильно, 0..1.</param>
        public void Trigger(float seconds, float amount)
        {
            float seconds0 = Mathf.Max(0.05f, seconds);
            power = Mathf.Clamp01(amount);

            remaining = Mathf.Max(remaining, seconds0);
            if (duration <= 0f || remaining > duration) duration = remaining;
        }

        public void Clear()
        {
            remaining = 0f;
            duration = 0f;
            power = 0f;
            phase = 0f;
        }

        /// <summary>
        /// Считает наклон камеры. envelope сначала растёт, потом спадает:
        /// эффект мягко начинается и так же мягко заканчивается, без щелчка
        /// на последнем кадре (когда таймер доходит до нуля).
        /// </summary>
        public void Evaluate(float deltaTime, float maxAngle, float frequency, out float roll, out float yaw)
        {
            roll = 0f;
            yaw = 0f;

            float dt = Mathf.Max(0f, deltaTime);
            if (remaining <= 0f || dt <= 0f) return;

            remaining = Mathf.Max(0f, remaining - dt);

            float t = 1f - Mathf.Clamp01(remaining / Mathf.Max(0.01f, duration));

            // Полный синус по прогрессу даёт мягкие края. SmoothStep в начале
            // и в конце дополнительно гасит производную до нуля, поэтому
            // покачивание входит и выходит без рывка, а не «дёргается» на
            // первом и последнем кадре.
            float envelope = Mathf.Sin(t * Mathf.PI);
            envelope *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.1f));
            envelope *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) / 0.1f));

            float angle = maxAngle * power * envelope;

            phase += dt * Mathf.Max(0.1f, frequency) * Mathf.PI * 2f;
            time += dt;

            // Плавный случайный дрейф поверх синусоиды даёт «плавающее»,
            // как от отравления, искажение вместо механического качания.
            float wanderRoll = (Mathf.PerlinNoise(seed, time * 0.35f) - 0.5f) * 2f;
            float wanderYaw = (Mathf.PerlinNoise(seed + 17.3f, time * 0.27f) - 0.5f) * 2f;

            roll = (Mathf.Sin(phase + seed) * 0.6f + wanderRoll * 0.7f) * angle;
            yaw = (Mathf.Sin(phase * 0.5f + seed * 0.7f) * 0.5f + wanderYaw * 0.6f) * angle;
        }
    }
}
