using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Рычаг автомата: нажатие E запускает вращение барабанов. Работает, только
    /// когда в автомате есть ставка и он не занят: пока крутятся барабаны или
    /// идёт обратный отсчёт аннигиляции, рычаг мёртв.
    ///
    /// Ввод — клавиша E через PlayerInteractor: рычаг реализует IInteractable +
    /// ICanInteract, поэтому курсор взаимодействия появляется на готовом рычаге,
    /// и E дёргает его. Рычаг ловится тем же лучом, что и подбор предметов
    /// (триггеры считаются), своих луча и кнопок у него нет.
    ///
    /// AimingLever и дальше читает PlayerFirearm, чтобы прицел на рычаге не
    /// стрелял.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotMachineLever : MonoBehaviour, IInteractable, ICanInteract
    {
        /// <summary>
        /// Прицел стоит на рычаге и автомат готов крутить. Синхронизируется в
        /// LateUpdate по PlayerInteractor.Target, читается в PlayerFirearm.Update.
        /// </summary>
        public static bool AimingLever { get; private set; }

        [Tooltip("Дистанция, с которой действует рычаг, м")]
        [SerializeField] private float range = 2.5f;

        [Header("Звук")]
        [Tooltip("Звук рывка рычага. Пусто — процедурный из ProceduralAudio")]
        [SerializeField] private AudioClip leverClip;

        [Tooltip("Громкость звука рывка рычага")]
        [SerializeField, Range(0f, 1f)] private float leverVolume = 0.85f;

        [Tooltip("Автомат, к которому относится рычаг. Пусто — найдётся ближайший")]
        [SerializeField] private SlotMachineTerminal terminal;

        private PlayerInteractor interactor;
        private AudioSource audioSource;
        private AudioClip generatedLeverClip;

        private void OnDisable()
        {
            AimingLever = false;
        }

        private void Update()
        {
            CachePlayer();
        }

        /// <summary>
        /// Флаг для PlayerFirearm обновляется после всех Update: к этому моменту
        /// PlayerInteractor уже выбрал Target, так что флаг всегда свежий.
        /// </summary>
        private void LateUpdate()
        {
            AimingLever = IsAimedAndReady();
        }

        private bool IsAimedAndReady()
        {
            if (interactor == null) return false;
            if (!ReferenceEquals(interactor.Target, this)) return false;
            return CanInteract(interactor);
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null) return false;

            SlotMachineTerminal slot = ResolveTerminal();
            if (slot == null || slot.IsSpinning || slot.Annihilating) return false;
            if (slot.TotalBetUnits <= 0) return false;

            return InRange(interactor);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            Pull();
        }

        private bool InRange(PlayerInteractor interactor)
        {
            Camera camera = interactor != null ? interactor.AimCamera : null;
            if (camera == null) return false;
            return Vector3.Distance(camera.transform.position, transform.position) <= Mathf.Max(0.1f, range);
        }

        private void Pull()
        {
            SlotMachineTerminal slot = ResolveTerminal();
            if (slot == null) return;

            PlayLeverSound();
            slot.TryPullLever();
        }

        private SlotMachineTerminal ResolveTerminal()
        {
            if (terminal != null) return terminal;
            terminal = FindFirstObjectByType<SlotMachineTerminal>();
            return terminal;
        }

        /// <summary>Звук рывка: кэш процедурного клипа, один источник на рычаг.</summary>
        private void PlayLeverSound()
        {
            AudioClip clip = ResolveLeverClip();
            if (clip == null) return;

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.pitch = 1f + Random.Range(-0.02f, 0.02f);
            audioSource.PlayOneShot(clip, leverVolume);
        }

        private AudioClip ResolveLeverClip()
        {
            if (leverClip != null) return leverClip;
            if (generatedLeverClip == null) generatedLeverClip = CarGame.Audio.ProceduralAudio.SlotMachineLever();
            return generatedLeverClip;
        }

        private void CachePlayer()
        {
            if (interactor != null && interactor.AimCamera != null) return;

            interactor = FindFirstObjectByType<PlayerInteractor>();
        }
    }
}