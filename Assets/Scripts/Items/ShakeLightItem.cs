using CarGame.Audio;
using CarGame.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarGame.Items
{
    /// <summary>
    /// Светотряс — фонарик без батарейки: свет копится тряской. Каждый клик ЛКМ
    /// заново запускает тряску, поэтому по фонарику можно «спамить» кликами; при
    /// зажатой кнопке каждое движение мышью тоже подкидывает заряд. Чем больше
    /// заряд, тем ярче луч, а когда тряска прекратилась, заряд медленно утекает.
    ///
    /// У модели две анимации — тряска и бездействие, — переключаемые булевым
    /// параметром аниматора. В покое параметр принудительно сбрасывается, потому
    /// что в контроллере он по умолчанию включён (иначе фонарик с самого подбора
    /// стоял бы в анимации тряски).
    /// </summary>
    [DisallowMultipleComponent]
    public class ShakeLightItem : HandLightItem
    {
        [Header("Заряд")]
        [Tooltip("Сколько заряда добавляет одно нажатие ЛКМ")]
        [SerializeField, Range(0f, 0.5f)] private float chargePerPress = 0.06f;
        [Tooltip("Сколько заряда добавляет каждый пройденный мышью пиксель при зажатой ЛКМ")]
        [SerializeField] private float chargePerPixel = 0.0011f;
        [Tooltip("Множитель чувствительности к тряске")]
        [SerializeField] private float shakeSensitivity = 1f;
        [Tooltip("Утечка заряда в секунду")]
        [SerializeField] private float dischargePerSecond = 0.09f;
        [Tooltip("Заряд, ниже которого луч считается погасшим")]
        [SerializeField, Range(0f, 0.5f)] private float lightThreshold = 0.02f;

        [Header("Анимация")]
        [Tooltip("Аниматор модели. Пусто — ищется в варианте руки")]
        [SerializeField] private Animator animator;
        [Tooltip("bool-параметр: true — тряска, false — бездействие. Пусто — анимация не трогается")]
        [SerializeField] private string shakeParameter = "Shake";
        [Tooltip("Состояние тряски. Клик перезапускает его с начала, поэтому по фонарику можно спамить")]
        [SerializeField] private string shakeState = "Shake";
        [Tooltip("Сколько секунд держать состояние тряски после последнего движения")]
        [SerializeField] private float shakeHold = 0.18f;

        [Header("Звук")]
        [Tooltip("Звук тряски. Пусто — генерируется процедурно")]
        [SerializeField] private AudioClip shakeClip;
        [Range(0f, 1f)]
        [Tooltip("Громкость тряски")]
        [SerializeField] private float shakeVolume = 0.55f;
        [Tooltip("Минимальный интервал между щелчками при зажатой ЛКМ, сек")]
        [SerializeField] private float shakeSoundInterval = 0.09f;
        [Range(0f, 0.5f)]
        [Tooltip("Разброс высоты тона, чтобы щелчки не звучали одинаково")]
        [SerializeField] private float shakePitchJitter = 0.12f;

        private Animator activeAnimator;
        private bool shaking;
        private bool warnedAboutAnimator;
        private float charge;
        private float lastShakeTime;
        private bool initialized;
        private AudioSource audioSource;
        private AudioClip generatedShakeClip;
        private float lastShakeSoundTime;

        /// <summary>Текущий заряд от 0 до 1.</summary>
        public float Charge => charge;

        protected override void OnEquippedInternal(PlayerHand current)
        {
            // Заряд переживает смену слота: убрал светотряс — накопленный свет
            // остаётся, вернул в руку — он светит столько же. Пустым он бывает
            // только при самом первом подборе.
            if (!initialized)
            {
                charge = 0f;
                initialized = true;
            }

            shaking = false;
            lastShakeTime = float.NegativeInfinity;
            ResolveAnimator(current);

            // Явно, а не через SetShakeState: в контроллере «Shake» включён по
            // умолчанию, и без этой записи фонарик с самого подбора застывал бы
            // в анимации тряски.
            PushShake(false);
            ApplyBeam(charge >= lightThreshold ? charge : 0f);
        }

        protected override void OnPressed(PlayerHand current)
        {
            ResolveAnimator(current);
            charge = Mathf.Clamp01(charge + chargePerPress);
            Shake(true);
            PlayShakeSound(true);
        }

        protected override void OnHeld(PlayerHand current)
        {
            float pixels = ReadMouseTravel();
            if (pixels <= 0f) return;

            charge = Mathf.Clamp01(charge + pixels * chargePerPixel * shakeSensitivity);
            Shake(false);
            PlayShakeSound(false);
        }

        protected override void OnCanceled()
        {
            // Заряд НЕ сбрасываем: он должен пережить смену слота. Гасим только
            // анимацию тряски; сам луч тушит OnDisable убранного варианта руки.
            shaking = false;
            PushShake(false);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (!shaking && charge > 0f)
                charge = Mathf.Max(0f, charge - dischargePerSecond * dt);

            if (shaking && Time.time - lastShakeTime > shakeHold)
            {
                shaking = false;
                PushShake(false);
            }

            ApplyBeam(charge >= lightThreshold ? charge : 0f);
        }

        /// <summary>
        /// Тряска началась: держим состояние и, если это клик, перезапускаем
        /// анимацию с начала — так серия кликов превращается в серию трясок.
        /// </summary>
        private void Shake(bool restart)
        {
            lastShakeTime = Time.time;
            shaking = true;
            PushShake(true);

            if (restart) RestartShakeState();
        }

        private void RestartShakeState()
        {
            if (activeAnimator == null) return;
            if (!activeAnimator.isActiveAndEnabled) return;
            if (activeAnimator.runtimeAnimatorController == null) return;

            int hash = ResolveStateHash(activeAnimator, shakeState);
            if (hash == 0) return;

            // Перезапуск с самого первого кадра: Play сбрасывает состояние в 0,
            // а Update(0f) сразу его и отрисовывает, иначе переход «бездействие →
            // тряска» доигрывает свой хвост и визуально новый кадр не виден.
            activeAnimator.Play(hash, 0, 0f);
            activeAnimator.Update(0f);
        }

        /// <summary>Путь, который мышь прошла за этот кадр: |dx| + |dy| в пикселях.</summary>
        private static float ReadMouseTravel()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return 0f;

            Vector2 delta = mouse.delta.ReadValue();
            return Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
        }

        /// <summary>
        /// Щёлкает тряской. Клик (restart) звучит сразу, тянущееся движение — не
        /// чаще интервала: иначе PlayOneShot каждый кадр превратился бы в кашу.
        /// Источник берём общий у руки, чтобы звук не пропадал при выключении
        /// варианта предмета.
        /// </summary>
        private void PlayShakeSound(bool restart)
        {
            if (!restart && Time.time - lastShakeSoundTime < shakeSoundInterval) return;

            AudioClip clip = shakeClip;
            if (clip == null)
            {
                if (generatedShakeClip == null) generatedShakeClip = ProceduralAudio.ShakeRattle();
                clip = generatedShakeClip;
            }
            if (clip == null) return;

            AudioSource source = ResolveAudioSource();
            if (source == null) return;

            lastShakeSoundTime = Time.time;
            source.pitch = 1f + Random.Range(-shakePitchJitter, shakePitchJitter);
            source.PlayOneShot(clip, shakeVolume);
        }

        private AudioSource ResolveAudioSource()
        {
            if (Hand != null && Hand.SharedAudioSource != null) return Hand.SharedAudioSource;

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }

            return audioSource;
        }

        private void PushShake(bool value)
        {
            if (activeAnimator == null) return;
            if (!activeAnimator.isActiveAndEnabled) return;
            if (activeAnimator.runtimeAnimatorController == null) return;
            if (string.IsNullOrEmpty(shakeParameter)) return;

            activeAnimator.SetBool(shakeParameter, value);
        }

        /// <summary>
        /// Animator.HasState принимает только ПОЛНЫЙ путь состояния, поэтому сначала
        /// пробуем «Слой.Состояние», потом короткое имя.
        /// </summary>
        private static int ResolveStateHash(Animator target, string stateName)
        {
            if (target == null || string.IsNullOrEmpty(stateName)) return 0;

            for (int i = 0; i < target.layerCount; i++)
            {
                int full = Animator.StringToHash(target.GetLayerName(i) + "." + stateName);
                if (target.HasState(i, full)) return full;
            }

            int shortHash = Animator.StringToHash(stateName);
            for (int i = 0; i < target.layerCount; i++)
            {
                if (target.HasState(i, shortHash)) return shortHash;
            }

            return 0;
        }

        private void ResolveAnimator(PlayerHand current)
        {
            if (activeAnimator != null) return;
            if (animator != null)
            {
                activeAnimator = animator;
                return;
            }

            if (current != null && current.ActiveHandVariant != null)
                activeAnimator = current.ActiveHandVariant.GetComponentInChildren<Animator>(true);
            if (activeAnimator != null) return;
            if (warnedAboutAnimator) return;

            warnedAboutAnimator = true;
            Debug.LogWarning(
                $"[{name}] В варианте руки нет Animator: светотряс работает, но анимации тряски не будет. " +
                $"Добавь Animator и bool-параметр '{shakeParameter}'.", this);
        }
    }
}
