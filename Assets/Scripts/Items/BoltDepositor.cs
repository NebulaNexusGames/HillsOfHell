using CarGame.Player;
using CarGame.UI;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Деталь Deposit магазина: сюда кладётся Golden Bolt, чтобы пополнить общий
    /// кошелёк TerminalWallet.
    ///
    /// Ввод — клавиша E, как у остальных целей взаимодействия: деталь
    /// реализует IInteractable + ICanInteract, поэтому пока прицел стоит на ней,
    /// а в руке зажат Golden Bolt, прицел меняется на курсор взаимодействия, и
    /// E кладёт болт в слот. Деталь выбирается тем же лучом PlayerInteractor
    /// (QueryTriggerInteraction.Collide — триггеры считаются), своего луча и
    /// кнопок у неё нет: ловить наводку самодельным Raycast не нужно, и «меш не
    /// ловится» больше не случается.
    ///
    /// Плюс запасной путь: болт, который физически вкатился в объём детали
    /// (брошенный или уронённый), тоже засчитывается.
    ///
    /// AimingDeposit и дальше читает PlayerFirearm, чтобы прицел на детали не
    /// стрелял. Уничтожать предметы нельзя: болт из руки помечается
    /// израсходованным (MarkConsumed) и просто выключается, а из ячейки
    /// инвентаря убывает по одной штуке через PlayerHand.ConsumeHeldItem.
    /// </summary>
    [DisallowMultipleComponent]
    public class BoltDepositor : MonoBehaviour, IInteractable, ICanInteract
    {
        /// <summary>
        /// Прицел стоит на детали депозита, а в руке Golden Bolt — только тогда
        /// E кладёт болт, а не стреляет. Синхронизируется в LateUpdate по
        /// PlayerInteractor.Target, читается в PlayerFirearm.Update.
        /// </summary>
        public static bool AimingDeposit { get; private set; }

        [Tooltip("Дистанция, с которой действует депозит, м")]
        [SerializeField] private float range = 2.5f;

        [Tooltip("Сколько баланса даёт один Golden Bolt")]
        [SerializeField, Range(1, 10)] private int boltValue = 1;

        [Header("Звук")]
        [Tooltip("Звон при пополнении. Пусто — процедурный из ProceduralAudio")]
        [SerializeField] private AudioClip depositClip;

        [Tooltip("Громкость звона при пополнении")]
        [SerializeField, Range(0f, 1f)] private float depositVolume = 0.9f;

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
            if (playerHand == null || !playerHand.IsHolding) return false;

            // Пока расходник применяется, рука занята анимацией — сдавать нечего.
            if (PlayerHand.UseLocked) return false;

            ItemDef def = playerHand.ResolveHeldDef();
            if (def == null || !def.IsCurrency) return false;

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
            if (hand == null || !hand.IsHolding) return;

            ItemDef def = hand.ResolveHeldDef();
            if (def == null || !def.IsCurrency) return;

            TerminalWallet.Add(Mathf.Max(1, boltValue));
            PlayDepositSound();
            hand.ConsumeHeldItem();
        }

        private void OnTriggerEnter(Collider other)
        {
            AbsorbPhysical(other);
        }

        /// <summary>
        /// Запасной путь депозита: болт сам вкатился в объём детали — бросили,
        /// уронили или прокатили. Засчитывается так же, как вклад из руки.
        /// </summary>
        private void AbsorbPhysical(Collider other)
        {
            if (other == null) return;

            PickupItem item = other.GetComponentInParent<PickupItem>();
            if (item == null) return;
            if (item.IsConsumed || item.IsHeld || item.IsStored) return;

            ItemDef def;
            if (!ItemDefs.TryGet(item.ItemId, out def)) return;
            if (def == null || !def.IsCurrency) return;

            TerminalWallet.Add(Mathf.Max(1, boltValue));
            PlayDepositSound();
            item.MarkConsumed();
        }

        /// <summary>Звон при пополнении: кэш процедурного клипа, один источник на деталь.</summary>
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