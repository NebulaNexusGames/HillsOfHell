using System.Collections;
using System.Collections.Generic;
using CarGame.Audio;
using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Терминал-обменник (он же переработчик): кладёшь ЛЮБОЙ предмет — он
    /// попадает в буфер, а панель показывает иконки, количество и суммарную
    /// стоимость. Кнопки ПОДТВЕРДИТЬ и ОТМЕНИТЬ внизу решают судьбу буфера:
    /// подтверждение превращает его в золотые болты на всю сумму, а отмена
    /// выбрасывает все предметы физическими копиями из той же точки выдачи —
    /// в инвентарь ничего не кладётся.
    ///
    /// Ввод сдачи — клавиша E через PlayerInteractor, как у автомата: терминал
    /// реализует IInteractable + ICanInteract, поэтому курсор взаимодействия
    /// появляется, когда в руке есть что сдать, а сам игрок рядом. Луч и кнопку
    /// терминал не заводит — ловится тем же лучом, что и подбор предметов;
    /// ЛКМ занимается панель.
    ///
    /// Предмет из руки убывает одной единицей через PlayerHand.ConsumeDepositableItem:
    /// уничтожать предметы нельзя, вариант руки не трогается, экипированный
    /// ствол тоже можно сдать. Выдача — только физическими болтами из каталога
    /// альт-версий (ItemWorldCatalog): в кошелёк магазина ничего не капает.
    /// </summary>
    [DisallowMultipleComponent]
    public class ExchangerTerminal : MonoBehaviour, IInteractable, ICanInteract
    {
        /// <summary>
        /// Прицел стоит на обменнике, а в руке есть что сдать — только тогда E
        /// кладёт предмет в буфер, а не стреляет. Синхронизируется в LateUpdate по
        /// PlayerInteractor.Target и читается в PlayerFirearm.Update: у ствола в
        /// руке выстрел не должен срабатывать поверх обмена.
        /// </summary>
        public static bool AimingExchange { get; private set; }

        /// <summary>Одна позиция буфера: вид предмета и сколько его лежит.</summary>
        public sealed class Pending
        {
            public ItemDef Def;
            public int Amount;
            public int Payload;

            public int Value => Def != null ? Def.Price * Amount : 0;
        }

        [Header("Взаимодействие")]
        [Tooltip("Дистанция, с которой работает обмен, м")]
        [SerializeField] private float range = 3f;

        [Header("Точка выдачи")]
        [Tooltip("SpawnPoint, у которого появляются выданные болты. Пусто — от самого терминала")]
        [SerializeField] private Transform spawnPoint;

        [Header("Показ панели")]
        [Tooltip("ScreenPoint: точка экрана терминала, на которой висит панель")]
        [SerializeField] private Transform screenPoint;

        [Tooltip("ActiveTrigger: объём, при входе в который панель появляется")]
        [SerializeField] private TerminalActivationTrigger activeTrigger;

        [Tooltip("Резервная дистанция, если ActiveTrigger не назначен")]
        [SerializeField] private float showDistance = 5f;

        [Header("Вид")]
        [Tooltip("Model терминала. Не обязательна, чисто для подсветки/вида")]
        [SerializeField] private GameObject model;

        [Header("Выдача")]
        [Tooltip("На какой высоте над точкой выдачи появляется болт, м")]
        [SerializeField] private float spawnHeight = 0.6f;

        [Header("Звук")]
        [Tooltip("Звук приёма предмета. Пусто — процедурный из ProceduralAudio")]
        [SerializeField] private AudioClip acceptClip;

        [Tooltip("Звук выдачи болтов")]
        [SerializeField] private AudioClip payoutClip;

        [Tooltip("Звук возврата предметов")]
        [SerializeField] private AudioClip cancelClip;

        [Tooltip("Звук отказа: предмет не принимается (например, золотой болт)")]
        [SerializeField] private AudioClip refuseClip;

        [Tooltip("Громкость звуков обменника")]
        [SerializeField, Range(0f, 1f)] private float soundVolume = 0.85f;

        /// <summary>Пауза между выбросом болтов, с.</summary>
        private const float DispenseDelay = 0.08f;

        /// <summary>Болтов ещё на выдачу. Один болт = одна единица ценности.</summary>
        private int pendingBolts;

        private Coroutine payoutRoutine;

        private readonly List<Pending> pending = new List<Pending>();

        private int revision;
        private bool playerInside;
        private Light poweredLight;

        private PlayerInteractor interactor;
        private PlayerHand hand;
        private AudioSource audioSource;
        private AudioClip generatedAcceptClip;
        private AudioClip generatedPayoutClip;
        private AudioClip generatedCancelClip;
        private AudioClip generatedRefuseClip;

        public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;
        public GameObject Model => model;

        public Transform ScreenPoint => screenPoint != null ? screenPoint : SpawnPoint;
        public TerminalActivationTrigger ActiveTrigger => activeTrigger;
        public float ShowDistance => showDistance;
        public bool PlayerInsideActive => playerInside || (activeTrigger != null && activeTrigger.IsPlayerInside);

        /// <summary>Позиции буфера: вид предмета и сколько его лежит.</summary>
        public IReadOnlyList<Pending> PendingItems => pending;

        /// <summary>Суммарная стоимость буфера в болтах.</summary>
        public int TotalValue
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < pending.Count; i++) sum += pending[i].Value;
                return sum;
            }
        }

        /// <summary>Сколько единиц предметов лежит в буфере.</summary>
        public int Count
        {
            get
            {
                int count = 0;
                for (int i = 0; i < pending.Count; i++) count += pending[i].Amount;
                return count;
            }
        }

        /// <summary>Растёт при каждом изменении буфера — панель по нему перестраивается.</summary>
        public int Revision => revision;

        /// <summary>Буфер изменился.</summary>
        public event System.Action Changed;

        public void SetSpawnPoint(Transform value)
        {
            spawnPoint = value;
        }

        public void SetModel(GameObject value)
        {
            model = value;
        }

        public void SetScreenPoint(Transform value)
        {
            screenPoint = value;
        }

        public void SetActiveTrigger(TerminalActivationTrigger value)
        {
            activeTrigger = value;
        }

        public void SetPlayerInside(bool inside)
        {
            playerInside = inside;
        }

        private void OnDisable()
        {
            AimingExchange = false;
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
            AimingExchange = IsAimedAndReady();
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

            // Пока расходник применяется, рука занята анимацией — сдавать нечего.
            if (PlayerHand.UseLocked) return false;

            // Сдавать можно и предмет из руки, и экипированный ствол.
            ItemDef def = playerHand.ResolveDepositableDef();
            if (def == null) return false;

            // Предмет без ценности обменивать нечего: болтов за него не будет.
            // Пусть лежит.
            if (def.Price <= 0) return false;

            return InRange(interactor);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            Exchange();
        }

        private bool InRange(PlayerInteractor interactor)
        {
            Camera camera = interactor != null ? interactor.AimCamera : null;
            if (camera == null) return false;
            return Vector3.Distance(camera.transform.position, transform.position) <= Mathf.Max(0.1f, range);
        }

        /// <summary>
        /// Забирает одну единицу предмета из руки/инвентаря и кладёт её в буфер.
        /// Ничего не уничтожается: ConsumeDepositableItem только убирает предмет
        /// из руки и инвентаря, а сам он живёт в буфере до подтверждения/отмены.
        /// </summary>
        private void Exchange()
        {
            if (hand == null) return;

            ItemDef def = hand.ResolveDepositableDef();
            if (def == null || def.Price <= 0) return;

            // Валюту самую по себе не принимаем: болт должен остаться у игрока,
            // а терминал лишь сообщает отказом, что такой предмет не обменивается.
            if (ItemDefs.Canonical(def.Id) == ItemDefs.GoldenBoltId)
            {
                PlaySound(GetGeneratedRefuseClip());
                return;
            }

            // Единица уходит из руки/инвентаря ДО добавления в буфер: если
            // Consume вернул null, сдавать было нечего.
            ItemDef consumed = hand.ConsumeDepositableItem();
            if (consumed == null) return;

            AddToBuffer(consumed, 1, 0);
            PlaySound(GetGeneratedAcceptClip());
        }

        private void OnTriggerEnter(Collider other)
        {
            AbsorbPhysical(other);
        }

        /// <summary>
        /// Запасной путь сдачи: предмет сам вкатился в объём терминала —
        /// бросили, уронили или прокатили. Засчитывается целиком, весь стек.
        /// </summary>
        private void AbsorbPhysical(Collider other)
        {
            if (other == null) return;

            PickupItem item = other.GetComponentInParent<PickupItem>();
            if (item == null) return;
            if (item.IsConsumed || item.IsHeld || item.IsStored) return;

            // Болт не засасывает: он остаётся лежать в мире, а терминал лишь
            // издаёт звук отказа.
            if (ItemDefs.Canonical(item.ItemId) == ItemDefs.GoldenBoltId)
            {
                PlaySound(GetGeneratedRefuseClip());
                return;
            }

            ItemDef def;
            if (!ItemDefs.TryGet(item.ItemId, out def)) return;
            if (def == null || def.Price <= 0) return;

            AddToBuffer(def, item.StackAmount, item.Payload);
            item.MarkConsumed();
            PlaySound(GetGeneratedAcceptClip());
        }

        private void AddToBuffer(ItemDef def, int amount, int payload)
        {
            if (def == null || amount <= 0) return;

            for (int i = 0; i < pending.Count; i++)
            {
                Pending slot = pending[i];
                if (ReferenceEquals(slot.Def, def) && slot.Payload == payload)
                {
                    slot.Amount += amount;
                    BumpRevision();
                    return;
                }
            }

            pending.Add(new Pending { Def = def, Amount = amount, Payload = payload });
            BumpRevision();
        }

        /// <summary>
        /// Подтверждение: весь буфер превращается в болты на суммарную стоимость.
        /// Болты вылетают физическими единицами из точки выдачи. Буфер пустеет.
        /// </summary>
        public bool Confirm()
        {
            if (pending.Count == 0) return false;

            int payout = TotalValue;
            pending.Clear();
            BumpRevision();

            if (payout <= 0) return true;

            pendingBolts += payout;
            PlaySound(GetGeneratedPayoutClip());
            if (payoutRoutine == null) payoutRoutine = StartCoroutine(DispenseBolts());
            return true;
        }

        /// <summary>
        /// Отмена: предметы физически выбрасываются из той же точки выдачи, что
        /// и болты при обмене, — в инвентарь ничего не кладётся. Буфер пустеет.
        /// </summary>
        public bool Cancel()
        {
            if (pending.Count == 0) return false;

            for (int i = 0; i < pending.Count; i++)
            {
                Pending slot = pending[i];
                if (slot.Def == null) continue;

                SpawnReturn(slot);
            }

            pending.Clear();
            BumpRevision();
            PlaySound(GetGeneratedCancelClip());
            return true;
        }

        /// <summary>
        /// Горизонтальное направление выброса — наружу из корпуса через точку
        /// выдачи. Панель и игрок стоят спереди (−Z локально), а SpawnPoint сбоку
        /// (+X), поэтому ни forward, ни −forward не годятся: вещи выбрасываются из
        /// боковой точки и улетают от терминала, а не сквозь него.
        /// </summary>
        private Vector3 EjectDirection()
        {
            Vector3 outward = SpawnPoint.position - transform.position;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f) outward = transform.forward;
            return outward.normalized;
        }

        /// <summary>
        /// Выбрасывает предмет из точки выдачи — как болты при обмене. Каждый
        /// предмет выдаётся отдельной одиночной копией, без стака: отмена не
        /// должна склеивать вещи в один поднимаемый стек.
        /// </summary>
        private void SpawnReturn(Pending slot)
        {
            ItemWorldCatalog catalog = ItemWorldCatalog.Instance;
            if (catalog == null || !catalog.Has(slot.Def)) return;

            int units = Mathf.Max(1, slot.Amount);
            for (int n = 0; n < units; n++)
            {
                Vector2 offset = Random.insideUnitCircle * 0.3f;
                Vector3 position = SpawnPoint.position + new Vector3(offset.x, spawnHeight, offset.y);
                Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                catalog.Spawn(slot.Def, position, rotation, 1, slot.Payload,
                    EjectDirection() * 1.5f + Vector3.up * 1.2f);
            }
        }

        /// <summary>
        /// Диспенсер болтов: по одному за тик из точки выдачи. Каждый болт —
        /// отдельная физическая единица, а не стек.
        /// </summary>
        private IEnumerator DispenseBolts()
        {
            while (pendingBolts > 0)
            {
                pendingBolts--;
                SpawnBolt();
                yield return new WaitForSeconds(DispenseDelay);
            }

            payoutRoutine = null;
        }

        private void SpawnBolt()
        {
            ItemWorldCatalog itemCatalog = ItemWorldCatalog.Instance;
            if (itemCatalog == null || !itemCatalog.Has(ItemDefs.GoldenBolt)) return;

            Vector2 offset = Random.insideUnitCircle * 0.35f;
            Vector3 position = SpawnPoint.position + new Vector3(offset.x, spawnHeight, offset.y);
            Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            Vector3 toss = EjectDirection() * Random.Range(2.0f, 3.0f)
                           + Vector3.up * Random.Range(1.6f, 2.4f);

            itemCatalog.Spawn(ItemDefs.GoldenBolt, position, rotation, 1, 0, toss);
        }

        private void BumpRevision()
        {
            revision++;
            Changed?.Invoke();
        }

        /// <summary>Подсвечивает терминал, пока панель открыта — как у магазина.</summary>
        public void SetPowered(bool on)
        {
            if (!on)
            {
                if (poweredLight != null) poweredLight.enabled = false;
                return;
            }

            if (poweredLight != null)
            {
                poweredLight.enabled = true;
                return;
            }

            if (model == null) return;

            GameObject go = new GameObject("PoweredLight");
            go.transform.SetParent(model.transform, false);

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(model.transform.position, Vector3.one * 0.5f);
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            go.transform.position = bounds.center + Vector3.up * 0.2f;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.35f, 0.9f, 0.75f);
            light.range = Mathf.Max(1.5f, bounds.size.magnitude * 1.5f);
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;

            poweredLight = light;
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip == null) return;

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.pitch = 1f + Random.Range(-0.03f, 0.03f);
            audioSource.PlayOneShot(clip, soundVolume);
        }

        private AudioClip GetGeneratedAcceptClip()
        {
            if (acceptClip != null) return acceptClip;
            if (generatedAcceptClip == null) generatedAcceptClip = ProceduralAudio.Deposit();
            return generatedAcceptClip;
        }

        private AudioClip GetGeneratedPayoutClip()
        {
            if (payoutClip != null) return payoutClip;
            if (generatedPayoutClip == null) generatedPayoutClip = ProceduralAudio.SlotMachineWin();
            return generatedPayoutClip;
        }

        private AudioClip GetGeneratedCancelClip()
        {
            if (cancelClip != null) return cancelClip;
            if (generatedCancelClip == null) generatedCancelClip = ProceduralAudio.SlotMachineTick();
            return generatedCancelClip;
        }

        private AudioClip GetGeneratedRefuseClip()
        {
            if (refuseClip != null) return refuseClip;
            if (generatedRefuseClip == null) generatedRefuseClip = ProceduralAudio.SlotMachineLose();
            return generatedRefuseClip;
        }

        private void CachePlayer()
        {
            if (interactor != null && interactor.Hand != null) return;

            interactor = FindFirstObjectByType<PlayerInteractor>();
            hand = interactor != null ? interactor.Hand : null;
        }
    }
}