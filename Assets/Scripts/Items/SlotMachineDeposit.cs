using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Деталь «приём ставки» автомата: сюда кладётся ЛЮБОЙ предмет — не только
    /// валюта. Нажатие E кладёт одну единицу в ставку, физически вкатившийся
    /// предмет засчитывается целиком (весь стек: у металла и патронов это
    /// StackAmount). Ставка уходит в SlotMachineTerminal.AddBet и показывается
    /// панелью автомата.
    ///
    /// Ввод — клавиша E через PlayerInteractor: деталь реализует IInteractable
    /// + ICanInteract, поэтому курсор взаимодействия появляется, когда в руке
    /// есть что класть и автомат не занят. Деталь ловится тем же лучом, что и
    /// подбор предметов (триггеры считаются), своих луча и кнопок у неё нет.
    ///
    /// AimingDeposit и дальше читает PlayerFirearm, чтобы прицел на детали не
    /// стрелял. Уничтожать предметы нельзя: из руки единица убывает через
    /// PlayerHand.ConsumeHeldItem, вкатившийся предмет помечается MarkConsumed
    /// и просто выключается — вариант в руке не трогается.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotMachineDeposit : MonoBehaviour, IInteractable, ICanInteract
    {
        /// <summary>
        /// Прицел стоит на детали, а в руке есть предмет — только тогда E кладёт
        /// ставку, а не стреляет. Синхронизируется в LateUpdate по
        /// PlayerInteractor.Target, читается в PlayerFirearm.Update.
        /// </summary>
        public static bool AimingDeposit { get; private set; }

        [Tooltip("Дистанция, с которой действует депозит, м")]
        [SerializeField] private float range = 2.5f;

        [Header("Звук")]
        [Tooltip("Звон при приёме ставки. Пусто — процедурный из ProceduralAudio")]
        [SerializeField] private AudioClip depositClip;

        [Tooltip("Громкость звона при приёме ставки")]
        [SerializeField, Range(0f, 1f)] private float depositVolume = 0.9f;

        [Tooltip("Автомат, к которому относится деталь. Пусто — найдётся ближайший")]
        [SerializeField] private SlotMachineTerminal terminal;

        private PlayerInteractor interactor;
        private PlayerHand hand;
        private AudioSource audioSource;
        private AudioClip generatedDepositClip;

        private void OnDisable()
        {
            AimingDeposit = false;
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
            AimingDeposit = IsAimedAndReady();
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

            PlayerHand playerHand = interactor.Hand;
            if (playerHand == null) return false;

            // Пока расходник применяется, рука занята анимацией — ставить нечего.
            if (PlayerHand.UseLocked) return false;

            // Можно ставить и предмет из руки, и экипированный ствол: оружие
            // в руку не встаёт, поэтому раньше его нельзя было положить в ставку.
            ItemDef def = playerHand.ResolveDepositableDef();
            if (def == null) return false;

            SlotMachineTerminal slot = ResolveTerminal();
            if (slot == null || slot.IsSpinning || slot.Annihilating) return false;

            return InRange(interactor);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            Deposit();
        }

        private bool InRange(PlayerInteractor interactor)
        {
            Camera camera = interactor != null ? interactor.AimCamera : null;
            if (camera == null) return false;
            return Vector3.Distance(camera.transform.position, transform.position) <= Mathf.Max(0.1f, range);
        }

        private void Deposit()
        {
            if (hand == null) return;

            ItemDef def = hand.ResolveDepositableDef();
            if (def == null) return;

            SlotMachineTerminal slot = ResolveTerminal();
            if (slot == null || !slot.AddBet(def, 1)) return;

            PlayDepositSound();
            hand.ConsumeDepositableItem();
        }

        private void OnTriggerEnter(Collider other)
        {
            AbsorbPhysical(other);
        }

        /// <summary>
        /// Запасной путь ставки: предмет сам вкатился в объём детали — бросили,
        /// уронили или прокатили. Засчитывается целиком, весь стек.
        /// </summary>
        private void AbsorbPhysical(Collider other)
        {
            if (other == null) return;

            PickupItem item = other.GetComponentInParent<PickupItem>();
            if (item == null) return;
            if (item.IsConsumed || item.IsHeld || item.IsStored) return;

            ItemDef def;
            if (!ItemDefs.TryGet(item.ItemId, out def)) return;
            if (def == null) return;

            SlotMachineTerminal slot = ResolveTerminal();
            if (slot == null || !slot.AddBet(def, item.StackAmount)) return;

            PlayDepositSound();
            item.MarkConsumed();
        }

        private SlotMachineTerminal ResolveTerminal()
        {
            if (terminal != null) return terminal;
            terminal = FindFirstObjectByType<SlotMachineTerminal>();
            return terminal;
        }

        /// <summary>Звон при приёме ставки: кэш процедурного клипа, один источник на деталь.</summary>
        private void PlayDepositSound()
        {
            AudioClip clip = ResolveDepositClip();
            if (clip == null) return;

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.pitch = 1f + Random.Range(-0.03f, 0.03f);
            audioSource.PlayOneShot(clip, depositVolume);

            // Звон приёма ставки — часть звучания автомата: отмечаем его, чтобы
            // панель приглушила фоновую музыку (ducking).
            SlotMachineTerminal slot = ResolveTerminal();
            if (slot != null) slot.NotifySfx();
        }

        private AudioClip ResolveDepositClip()
        {
            if (depositClip != null) return depositClip;
            if (generatedDepositClip == null) generatedDepositClip = CarGame.Audio.ProceduralAudio.Deposit();
            return generatedDepositClip;
        }

        private void CachePlayer()
        {
            if (interactor != null && interactor.Hand != null) return;

            interactor = FindFirstObjectByType<PlayerInteractor>();
            hand = interactor != null ? interactor.Hand : null;
        }
    }
}