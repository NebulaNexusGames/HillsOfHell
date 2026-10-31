using System;
using System.Collections;
using System.Collections.Generic;
using CarGame.Combat;
using CarGame.UI;
using UnityEngine;

namespace CarGame.Items
{
    public enum SlotMachineOutcome
    {
        Loss = 0,
        Double = 1,
        Jackpot = 2,
        Annihilation = 3,
        LuckUp = 4
    }

    /// <summary>
    /// Автомат-казино: игрок кладёт ЛЮБОЙ предмет в ставку (деталь депозита с
    /// SlotMachineDeposit), панель показывает ставку, а рычаг (SlotMachineLever)
    /// крутит три барабана. И депозит, и рычаг — цели E: они реализуют
    /// IInteractable через свои компоненты, Курсор взаимодействия и клавиша E,
    /// как у подбора предметов.
    ///
    /// Барабаны крутят ТОЛЬКО 2D-иконки — ни одного предмета/3D-превью в слотах:
    /// иконка джекпота, иконка удвоения считается через неё (джекпот = 7 × ставку,
    /// удвоение = 2 × ставку предметами и ценностью), иконка удачи (поднимает
    /// шанс на удвоение И джекпот следующего рывка), иконка проигрыша (ничего
    /// не возвращает) и иконка аннигиляции (обратный отсчёт до взрыва).
    ///
    /// Исход ролится сразу в момент рывка, панель лишь красиво раскручивает
    /// барабаны к заданным символам: аннигиляция — три «бума», джекпот — три
    /// иконки джекпота, удвоение — две иконки джекпота + одна проигрыша, удача —
    /// три иконки LuckUp, проигрыш — три иконки проигрыша. Когда панель
    /// доиграла, она зовёт FinishSpin, и автомат выплачивает: джекпот
    /// возвращает ставку семикратно, удвоение — дважды. Всё выдаётся из точки
    /// выдачи (SpawnPoint) по одной единице выбрасыванием — и предметы, и
    /// болтики (валюта ставки и джекпот-бонус за ценность выигрыша). В кошелёк
    /// игрока ничего не капает. Ставка сгорает в любом исходе.
    ///
    /// Три иконки LuckUp — «удача»: после такого исхода шанс удвоения И
    /// джекпота на следующем рывке выше (каждый LuckUp +5% к базовым 8% у
    /// джекпота и 30% у удвоения, до трёх накоплений), и первый джекпот эту
    /// удачу сжигает.
    ///
    /// Аннигиляция — отдельный честный шанс: тикает обратный отсчёт (тики всё
    /// чаще — интервалы tickIntervals), потом взрыв. Взрыв — назначенный
    /// префаб ExplosionEffect с Ignore(самого автомата) либо процедурный
    /// Explosion.Detonate. После взрыва уничтожается именно корень автомата —
    /// предметы ставки и предметы в руках детей ему не имеют.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotMachineTerminal : MonoBehaviour
    {
        [Serializable]
        public sealed class BetEntry
        {
            public ItemDef Def;
            public int Amount;
        }

        [Header("Ставки")]
        [Tooltip("Шанс аннигиляции при рывке рычага, доля 0..1")]
        [SerializeField, Range(0f, 1f)] private float annihilationChance = 0.05f;

        [Header("Удача")]
        [Tooltip("Шанс выпадения трёх иконок LuckUp при рывке рычага, доля 0..1")]
        [SerializeField, Range(0f, 1f)] private float luckUpChance = 0.03f;

        [Header("Точка спавна выигрыша")]
        [Tooltip("SpawnPoint, у которого появляются выигранные предметы. Пусто — от самого автомата")]
        [SerializeField] private Transform spawnPoint;

        [Header("Показ панели")]
        [Tooltip("ScreenPoint: точка экрана автомата, на которой висит панель")]
        [SerializeField] private Transform screenPoint;

        [Tooltip("ActiveTrigger: объём, при входе в который панель появляется")]
        [SerializeField] private TerminalActivationTrigger activeTrigger;

        [Tooltip("Резервная дистанция, если ActiveTrigger не назначен")]
        [SerializeField] private float showDistance = 5f;

        [Header("Вид")]
        [Tooltip("Model автомата. Пока панель открыта, модель подсвечивается")]
        [SerializeField] private GameObject model;

        [Header("Детали")]
        [Tooltip("Деталь депозита с SlotMachineDeposit. Нужна для подсветки готовности, если назначена")]
        [SerializeField] private GameObject depositSlot;

        [Tooltip("Рычаг с SlotMachineLever")]
        [SerializeField] private GameObject leverSlot;

        [Header("Аннигиляция")]
        [Tooltip("Префаб взрыва. Пусто — процедурный Explosion.Detonate")]
        [SerializeField] private GameObject explosionPrefab;

        [Tooltip("Радиус взрыва аннигиляции, м")]
        [SerializeField] private float annihilationRadius = 7f;

        [Tooltip("Максимальный урон взрыва аннигиляции")]
        [SerializeField] private float annihilationDamage = 80f;

        [Tooltip("Отброс взрыва аннигиляции, м/с")]
        [SerializeField] private float annihilationKnockback = 22f;

        [Tooltip("Длительность обратного отсчёта перед взрывом, с")]
        [SerializeField, Range(1f, 15f)] private float countdownDuration = 5f;

        [Tooltip("Интервалы тиков отсчёта, с — короче к взрыву, страх нарастает")]
        [SerializeField] private float[] tickIntervals = { 1.4f, 1.15f, 0.95f, 0.8f, 0.7f };

        [Header("Звук")]
        [Tooltip("Звуки автомата. Пусто — процедурные из ProceduralAudio")]
        [SerializeField] private AudioClip leverClip, jackpotClip, winClip, luckUpClip, loseClip, tickClip;

        [Tooltip("Громкость звуков автомата")]
        [SerializeField, Range(0f, 1f)] private float soundVolume = 0.85f;

        private readonly List<BetEntry> bets = new List<BetEntry>();

        /// <summary>Очередь выигрыша на выдачу: одна единица = один предмет/болтик.</summary>
        private readonly Queue<ItemDef> dropQueue = new Queue<ItemDef>();

        private Coroutine dropRoutine;

        private AudioSource audioSource;
        private AudioClip generatedLever, generatedJackpot, generatedWin, generatedLuckUp, generatedLose, generatedTick;

        private SlotMachineOutcome outcome;
        private ItemDef pendingWinDef;
        private float[] tickTimes;
        private float annihilationTotal;
        private int nextTick;

        /// <summary>Накопленная удача к джекпоту после трёх LuckUp на барабанах.</summary>
        private int luckBoost;

        /// <summary>Кэш состояния триггера, если он не назначен полем.</summary>
        private bool playerInsideActive;

        public event Action BetsChanged;
        public event Action SpinStarted;

        /// <summary>Текущая ставка. Не сгорает, пока не сделан рывок рычага.</summary>
        public IReadOnlyList<BetEntry> Bets => bets;

        /// <summary>Сумма всех единиц ставки — для панели.</summary>
        public int TotalBetUnits
        {
            get
            {
                int total = 0;
                for (int i = 0; i < bets.Count; i++) total += bets[i].Amount;
                return total;
            }
        }

        /// <summary>Барабаны крутятся, панель ещё не доиграла.</summary>
        public bool IsSpinning { get; private set; }

        /// <summary>Обратный отсчёт пошёл — скоро взрыв.</summary>
        public bool Annihilating { get; private set; }

        /// <summary>Сколько секунд осталось до взрыва.</summary>
        public float AnnihilationRemaining { get; private set; }

        /// <summary>
        /// Накопленная удача после троек LuckUp: каждая тройка поднимает шанс
        /// удвоения на DoubleLuckStep и джекпота на JackpotLuckStep, первый
        /// джекпот удачу сжигает.
        /// </summary>
        public int LuckBoost => luckBoost;

        /// <summary>Какой исход ждёт панель, чтобы раскрутить барабаны.</summary>
        public SlotMachineOutcome PendingOutcome => outcome;

        /// <summary>На каком символе надо остановить барабаны в этот раз.</summary>
        public ItemDef PendingWinDef => pendingWinDef;

        /// <summary>Что показать панели после выплаты: «JACKPOT!», «DOUBLE!», «NO LUCK».</summary>
        public string LastResult { get; private set; }

        /// <summary>
        /// Счётчик проигранных звуков автомата: растёт на каждый SFX (рычаг,
        /// джекпот, выигрыш, удача, проигрыш, тик, депозит). Панель по его
        /// изменению приглушает свою фоновую музыку — это и есть ducking.
        /// </summary>
        public int SfxCount { get; private set; }

        public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;
        public Transform ScreenPoint => screenPoint != null ? screenPoint : SpawnPoint;
        public TerminalActivationTrigger ActiveTrigger => activeTrigger;
        public bool PlayerInsideActive => activeTrigger != null ? activeTrigger.IsPlayerInside : playerInsideActive;
        public GameObject Model => model;
        public GameObject DepositSlot => depositSlot;
        public GameObject LeverSlot => leverSlot;
        public float ShowDistance => showDistance > 0.1f ? showDistance : DefaultShowDistance;

        private const float DefaultShowDistance = 5f;
        private const int JackpotBoltMultiplier = 7;

        /// <summary>Базовый шанс джекпота, доля 0..1.</summary>
        private const float JackpotBaseChance = 0.08f;

        /// <summary>Сколько удачи добавляет каждая тройка LuckUp к шансу джекпота.</summary>
        private const float JackpotLuckStep = 0.05f;

        /// <summary>Базовый шанс удвоения (X2), доля 0..1. Вместе с джекпотом — 38%.</summary>
        private const float DoubleBaseChance = 0.30f;

        /// <summary>Сколько удачи добавляет каждая тройка LuckUp к шансу удвоения.</summary>
        private const float DoubleLuckStep = 0.05f;

        /// <summary>Больше удачи не копится: после трёх троек — максимум.</summary>
        private const int MaxLuckBoost = 3;

        /// <summary>Пауза между выбросами единиц выигрыша из диспенсера, с.</summary>
        private const float DispenseDelay = 0.08f;

        private void Awake()
        {
            SlotMachineUI.Show(this);
        }

        private void Update()
        {
            if (Annihilating) TickAnnihilation();
        }

        public void SetSpawnPoint(Transform value)
        {
            spawnPoint = value;
        }

        public void SetScreenPoint(Transform value)
        {
            screenPoint = value;
        }

        public void SetActiveTrigger(TerminalActivationTrigger value)
        {
            activeTrigger = value;
        }

        /// <summary>Состояние триггера. Вызывается из TerminalActivationTrigger.</summary>
        public void SetPlayerInside(bool inside)
        {
            playerInsideActive = inside;
        }

        public void SetModel(GameObject value)
        {
            model = value;
        }

        public void SetDepositSlot(GameObject value)
        {
            depositSlot = value;
        }

        public void SetLeverSlot(GameObject value)
        {
            leverSlot = value;
        }

        public void SetExplosionPrefab(GameObject value)
        {
            explosionPrefab = value;
        }

        /// <summary>
        /// Добавляет предмет в ставку. Тот же def сливается в один BetEntry,
        /// остальное копит количество. Пока крутятся барабаны и во время
        /// обратного отсчёта ставки не принимаются.
        /// </summary>
        public bool AddBet(ItemDef def, int amount)
        {
            if (def == null || amount <= 0) return false;
            if (IsSpinning || Annihilating) return false;

            for (int i = 0; i < bets.Count; i++)
            {
                if (!ReferenceEquals(bets[i].Def, def)) continue;
                bets[i].Amount += amount;
                BetsChanged?.Invoke();
                return true;
            }

            bets.Add(new BetEntry { Def = def, Amount = amount });
            BetsChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Рывок рычага. Пустая ставка автомат не запускает: крутить барабаны
        /// не за что. Исход ролится сразу: аннигиляция — честный шанс, иначе
        /// джекпот, удвоение или проигрыш. Результат панели не сообщается —
        /// она доигрывает барабаны и зовёт FinishSpin.
        /// </summary>
        public bool TryPullLever()
        {
            if (IsSpinning || Annihilating) return false;
            if (bets.Count == 0) return false;

            RollOutcome();
            IsSpinning = true;
            LastResult = null;
            PlayLeverSound();
            SpinStarted?.Invoke();
            return true;
        }

        /// <summary>
        /// Панель доиграла барабаны и говорит «всё, считай». Ставка сгорает в
        /// любом исходе — выигрыш, если будет, приходит отдельно.
        /// </summary>
        public void FinishSpin()
        {
            if (!IsSpinning) return;
            IsSpinning = false;

            switch (outcome)
            {
                case SlotMachineOutcome.Jackpot:
                    PayOut(7, true);
                    LastResult = "JACKPOT!";
                    PlayJackpotSound();
                    luckBoost = 0;
                    break;
                case SlotMachineOutcome.Double:
                    PayOut(2, false);
                    LastResult = "DOUBLE!";
                    PlayWinSound();
                    break;
                case SlotMachineOutcome.LuckUp:
                    luckBoost = Mathf.Min(MaxLuckBoost, luckBoost + 1);
                    LastResult = "LUCK UP";
                    PlayLuckUpSound();
                    break;
                case SlotMachineOutcome.Loss:
                    LastResult = "NO LUCK";
                    PlayLoseSound();
                    break;
                case SlotMachineOutcome.Annihilation:
                    LastResult = null;
                    BeginAnnihilation();
                    break;
            }

            bets.Clear();
            BetsChanged?.Invoke();
        }

        private void RollOutcome()
        {
            if (UnityEngine.Random.value < annihilationChance)
            {
                outcome = SlotMachineOutcome.Annihilation;
                pendingWinDef = null;
                return;
            }

            if (UnityEngine.Random.value < luckUpChance)
            {
                outcome = SlotMachineOutcome.LuckUp;
                pendingWinDef = null;
                return;
            }

            float jackpotChance = JackpotBaseChance + luckBoost * JackpotLuckStep;
            float doubleChance = DoubleBaseChance + luckBoost * DoubleLuckStep;
            float roll = UnityEngine.Random.value;
            if (roll < jackpotChance)
            {
                outcome = SlotMachineOutcome.Jackpot;
                pendingWinDef = PickWinDef();
            }
            else if (roll < jackpotChance + doubleChance)
            {
                outcome = SlotMachineOutcome.Double;
                pendingWinDef = PickWinDef();
            }
            else
            {
                outcome = SlotMachineOutcome.Loss;
                pendingWinDef = null;
            }
        }

        /// <summary>Символ выигрыша — первый не-валютный предмет ставки, либо любой.</summary>
        private ItemDef PickWinDef()
        {
            for (int i = 0; i < bets.Count; i++)
            {
                if (bets[i] != null && bets[i].Def != null && !bets[i].Def.IsCurrency)
                    return bets[i].Def;
            }

            return bets.Count > 0 ? bets[0].Def : null;
        }

        /// <summary>
        /// Выплата. Джекпот возвращает ставку семикратно (множитель 7),
        /// удвоение отдаёт ставку дважды (множитель 2). В кошелёк ничего не
        /// капает: и предметы, и болтики (валюта ставки и джекпот-бонус за
        /// ценность выигрыша) встают в очередь на выбрасывание по одной
        /// единице из точки выдачи.
        /// </summary>
        private void PayOut(int multiplier, bool jackpot)
        {
            int itemValue = 0;
            for (int i = 0; i < bets.Count; i++)
            {
                BetEntry entry = bets[i];
                if (entry == null || entry.Def == null || entry.Amount <= 0) continue;

                int amount = entry.Amount * multiplier;
                for (int k = 0; k < amount; k++) dropQueue.Enqueue(entry.Def);
                if (!entry.Def.IsCurrency) itemValue += entry.Def.Price * entry.Amount;
            }

            if (jackpot && itemValue > 0)
            {
                int bonus = JackpotBoltMultiplier * itemValue;
                for (int k = 0; k < bonus; k++) dropQueue.Enqueue(ItemDefs.GoldenBolt); 
            }

            if (dropRoutine == null) dropRoutine = StartCoroutine(DispenseDrops());
        }

        /// <summary>
        /// Диспенсер: одна выигранная единица за тик выбрасывается из точки
        /// выдачи. Без стака: у каждой единицы StackAmount 1, иначе после
        /// нескольких удвоений выпадал бы один неподнимаемый стек.
        /// </summary>
        private IEnumerator DispenseDrops()
        {
            while (dropQueue.Count > 0)
            {
                SpawnOne(dropQueue.Dequeue());
                yield return new WaitForSeconds(DispenseDelay);
            }

            dropRoutine = null;
        }

        /// <summary>
        /// Выбрасывает одну единицу выигрыша из точки выдачи, как дроп из
        /// терминала. Ствол получает полный магазин, каталог молчит — единица
        /// просто не вылетает.
        /// </summary>
        private void SpawnOne(ItemDef def)
        {
            if (def == null) return;

            ItemWorldCatalog itemCatalog = ItemWorldCatalog.Instance;
            if (itemCatalog == null || !itemCatalog.Has(def)) return;

            int payload = 0;
            if (def.IsFirearm)
            {
                FirearmSpec spec = FirearmSpecs.Resolve(def.Firearm);
                if (spec != null) payload = spec.MagazineCapacity;
            }

            Vector2 offset = UnityEngine.Random.insideUnitCircle * 0.35f;
            Vector3 position = SpawnPoint.position + new Vector3(offset.x, 0.6f, offset.y);
            Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

            // Точку выдачи не двигаем: единица вылетает из неё дугой вперёд-вверх
            // («в лицо» автомату), ложится на землю перед машиной, а не падает
            // на корпус и не втягивается обратно в депозит.
            Vector3 toss = transform.forward * UnityEngine.Random.Range(2.2f, 3.2f)
                           + Vector3.up * UnityEngine.Random.Range(1.8f, 2.6f);
            itemCatalog.Spawn(def, position, rotation, 1, payload, toss);
        }

        private void BeginAnnihilation()
        {
            Annihilating = true;
            AnnihilationRemaining = Mathf.Max(1f, countdownDuration);
            annihilationTotal = AnnihilationRemaining;

            float sum = 0f;
            if (tickIntervals != null)
            {
                for (int i = 0; i < tickIntervals.Length; i++) sum += Mathf.Max(0.05f, tickIntervals[i]);
            }

            float scale = sum > 0f ? AnnihilationRemaining / sum : 1f;
            tickTimes = new float[tickIntervals != null ? tickIntervals.Length : 0];
            float total = 0f;
            for (int i = 0; i < tickTimes.Length; i++)
            {
                total += Mathf.Max(0.05f, tickIntervals[i]) * scale;
                tickTimes[i] = total;
            }

            nextTick = 0;
        }

        /// <summary>
        /// Обратный отсчёт: тик на каждый интервал (они короче к концу — страх
        /// нарастает), потом взрыв и уничтожение корня автомата. Именно корень
        /// можно уничтожить: предметы ставки и руки детей ему не имеют.
        /// </summary>
        private void TickAnnihilation()
        {
            AnnihilationRemaining -= Time.deltaTime;

            float elapsed = annihilationTotal - AnnihilationRemaining;
            while (nextTick < tickTimes.Length && elapsed >= tickTimes[nextTick])
            {
                nextTick++;
                PlayTickSound();
            }

            if (AnnihilationRemaining <= 0f) Explode();
        }

        private void Explode()
        {
            Vector3 center = transform.position;

            if (explosionPrefab != null)
            {
                GameObject effect = Instantiate(explosionPrefab, center, Quaternion.identity);
                ExplosionEffect explosion = effect != null ? effect.GetComponent<ExplosionEffect>() : null;
                if (explosion != null) explosion.Ignore(gameObject);
            }
            else
            {
                Explosion.Detonate(center, Explosion.Default(annihilationRadius, annihilationDamage, annihilationKnockback));
            }

            Destroy(gameObject);
        }

        private void PlayLeverSound()
        {
            if (generatedLever == null) generatedLever = CarGame.Audio.ProceduralAudio.SlotMachineLever();
            PlayOneShot(leverClip != null ? leverClip : generatedLever);
        }

        private void PlayJackpotSound()
        {
            if (generatedJackpot == null) generatedJackpot = CarGame.Audio.ProceduralAudio.SlotMachineJackpot();
            PlayOneShot(jackpotClip != null ? jackpotClip : generatedJackpot);
        }

        private void PlayWinSound()
        {
            if (generatedWin == null) generatedWin = CarGame.Audio.ProceduralAudio.SlotMachineWin();
            PlayOneShot(winClip != null ? winClip : generatedWin);
        }

        /// <summary>
        /// Удача: отдельный светлый перезвон, не тот же, что удвоение. Иначе
        /// игрок не различал на слух, что выиграл именно бонус удачи.
        /// </summary>
        private void PlayLuckUpSound()
        {
            if (generatedLuckUp == null) generatedLuckUp = CarGame.Audio.ProceduralAudio.SlotMachineLuckUp();
            PlayOneShot(luckUpClip != null ? luckUpClip : generatedLuckUp);
        }

        private void PlayLoseSound()
        {
            if (generatedLose == null) generatedLose = CarGame.Audio.ProceduralAudio.SlotMachineLose();
            PlayOneShot(loseClip != null ? loseClip : generatedLose);
        }

        private void PlayTickSound()
        {
            if (generatedTick == null) generatedTick = CarGame.Audio.ProceduralAudio.SlotMachineTick();
            PlayOneShot(tickClip != null ? tickClip : generatedTick);
        }

        private void PlayOneShot(AudioClip clip)
        {
            if (clip == null) return;

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.pitch = 1f + UnityEngine.Random.Range(-0.02f, 0.02f);
            audioSource.PlayOneShot(clip, soundVolume);
            NotifySfx();
        }

        /// <summary>
        /// Отмечает, что автомат только что сыграл звук: панель увидит рост
        /// SfxCount и приглушит свою музыку. Депозит (SlotMachineDeposit) зовёт
        /// это же — звон при приёме ставки тоже часть звучания автомата.
        /// </summary>
        public void NotifySfx()
        {
            SfxCount++;
        }
    }
}