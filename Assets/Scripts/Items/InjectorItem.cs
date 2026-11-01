using CarGame.Combat;
using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Инджектор. Работает как гаечный ключ: его нужно держать в руке,
    /// а воспользоваться можно только активной версией для руки.
    /// Отличие одно — вместо удара он восстанавливает 50 хп игроку.
    /// Перед лечением проигрывается анимация применения, звуки идут
    /// из AudioSource внутри этой анимации. После применения предмет
    /// исчезает только для игрока: вариант руки не удаляется, а отключается.
    /// </summary>
    [DisallowMultipleComponent]
    public class InjectorItem : MonoBehaviour, IHandUsable
    {
        [Header("Лечение")]
        [Tooltip("Сколько хп восстанавливает одно применение")]
        [SerializeField] private float healAmount = 50f;
        [Tooltip("Лечить ли только если игрок ещё жив. Если выключить, инджектор поднимает и мёртвого")]
        [SerializeField] private bool requireAlive = true;

        [Header("Оверхил")]
        [Tooltip("Включать ли оверхил: при 100 хп инджектор даёт запас сверх базы. Работает только пока настройка не переопределена через InjectorItem.SetOverhealEnabled (PlayerPrefs): до этого значение из инспектора и есть текущее")]
        [SerializeField] private bool enableOverheal = true;

        [Header("Таймеры применения")]
        [Tooltip("Через сколько секунд после применения инжектор восстановит хп")]
        [SerializeField] private float healDelay = 0.6f;
        [Tooltip("Через сколько секунд инжектор считается использованным: модель в руке выключается, рука освобождается, предмет исчезает")]
        [SerializeField] private float usedDelay = 2f;

        [Header("Аниматор")]
        [Tooltip("Аниматор инджектора. Если пусто — ищется в варианте руки или в копии предмета")]
        [SerializeField] private Animator animator;
        [Tooltip("Имя параметра-триггера анимации применения")]
        [SerializeField] private string useTrigger = "Use";
        [Tooltip("Имя параметра-триггера, который обнуляет анимацию и возвращает инжектор в состояние Idle")]
        [SerializeField] private string idleTrigger = "Idle";
        [Tooltip("Имя состояния, в которое инжектор возвращается при сбросе")]
        [SerializeField] private string idleState = "Idle";

        private Animator activeAnimator;
        private Health playerHealth;
        private bool inUse;
        private bool warnedAboutAnimator;
        private bool warnedAboutTrigger;
        private bool warnedAboutIdleTrigger;
        private float nextUseTime;
        private float useLockout = 0.35f;

        private const string OverhealPrefKey = "cargame.injector.overheal";
        // -1: настройку ещё никто не менял, действует enableOverheal из инспектора.
        // 0 или 1: явный выбор игрока, он переживает перезапуск (PlayerPrefs).
        private static int overhealOverride = -1;
        private static bool overhealOverrideLoaded;

        /// <summary>
        /// Включён ли оверхил. Настройка общая для всех инжекторов и лечилок на
        /// базе <c>InjectorItem</c> и переживает перезапуск (PlayerPrefs).
        /// Пока настройку не меняли, значение берётся из инспектора предмета.
        /// </summary>
        public static bool OverhealEnabled
        {
            get
            {
                LoadOverhealOverride();
                return overhealOverride != 0;
            }
            set => SetOverhealEnabled(value);
        }

        /// <summary>Явно включает или выключает оверхил для всех лечилок.</summary>
        public static void SetOverhealEnabled(bool value)
        {
            overhealOverride = value ? 1 : 0;
            PlayerPrefs.SetInt(OverhealPrefKey, overhealOverride);
            PlayerPrefs.Save();
        }

        /// <summary>Сбрасывает выбор игрока: дальше действует enableOverheal из инспектора.</summary>
        public static void ResetOverhealToDefault()
        {
            overhealOverride = -1;
            overhealOverrideLoaded = true;
            PlayerPrefs.DeleteKey(OverhealPrefKey);
            PlayerPrefs.Save();
        }

        private static void LoadOverhealOverride()
        {
            if (overhealOverrideLoaded) return;

            overhealOverrideLoaded = true;
            overhealOverride = PlayerPrefs.GetInt(OverhealPrefKey, -1);
        }

        /// <summary>
        /// Актуальный оверхил для этого предмета: если игрок менял настройку —
        /// её значение, иначе значение поля из инспектора.
        /// </summary>
        private bool ResolveOverhealEnabled()
        {
            LoadOverhealOverride();
            if (overhealOverride >= 0) return overhealOverride != 0;
            return enableOverheal;
        }

        /// <summary>
        /// Можно ли применить лечилку к этому здоровью. Без оверхила на полных
        /// 100 хп применение запрещено: предмет не должен тратиться впустую.
        /// </summary>
        private bool CanApplyHeal(Health target)
        {
            if (!ResolveOverhealEnabled() && target != null && target.IsFull) return false;
            return true;
        }

        /// <summary>Идёт применение: бросок заблокирован, повторное нажатие игнорируется.</summary>
        public bool InUse => inUse;

        public void OnUsePressed(PlayerHand hand)
        {
            if (inUse) return;
            if (Time.time < nextUseTime) return;
            if (!CanUse(hand)) return;

            // Лечилка без оверхила на полных 100 хп бессмысленна: не тратим
            // предмет, не проигрываем анимацию и не блокируем руку.
            // С оверхилом применение разрешено — оно даёт запас сверх базы.
            if (!CanApplyHeal(ResolveTargetHealth(hand)))
            {
                Debug.Log("[Injector] Хп уже полные, а оверхил выключен — инжектор не применён.", this);
                return;
            }

            ResolveAnimator(hand);
            BeginUse(hand);
        }

        public void OnUseHeld(PlayerHand hand)
        {
        }

        public void OnUseReleased(PlayerHand hand)
        {
        }

        public void OnUseCanceled()
        {
        }

        /// <summary>
        /// Инжектор только что подобран. Сбрасываем анимацию сразу: после
        /// применения вариант руки остаётся в кадре лечения, а вариант один и тот
        /// же для всех инжекторов, поэтому новый предмет в руке появлялся бы
        /// «замороженным» на последнем кадре применения.
        /// </summary>
        public void OnEquipped(PlayerHand hand)
        {
            ResolveAnimator(hand);
            ResetAnimation();
        }

        private bool CanUse(PlayerHand hand)
        {
            if (hand == null) return false;
            if (!hand.IsHolding) return false;
            if (hand.HeldItem == null && hand.HeldFromInventory == null) return false;
            return true;
        }

/// <summary>
        /// Запуск применения. Предмет остаётся в руке до конца применения,
        /// поэтому бросок на это время блокируется: иначе игрок выронит
        /// инджектор и подберёт его снова, то есть накопит копии.
        /// </summary>
        private void BeginUse(PlayerHand hand)
        {
            inUse = true;
            nextUseTime = Time.time + useLockout;

            PlayUseAnimation();

            Health target = ResolveTargetHealth(hand);

            // Проверку дублируем здесь: если вызов придёт не через
            // OnUsePressed, лечилка без оверхила всё равно не должна тратиться
            // на полных 100 хп. Откат делаем до блокировки броска — тогда
            // достаточно вернуть анимацию в Idle и снять флаг применения.
            if (!CanApplyHeal(target))
            {
                inUse = false;
                RequestIdleState();
                return;
            }

            Vector3 position = hand.HoldSocket != null ? hand.HoldSocket.position : transform.position;

            hand.SetDropBlocked(true);

            InjectorUseSequence.Start(position, healDelay, usedDelay,
                () => ApplyHeal(target),
                () => CompleteUse(hand));
        }

        private void ApplyHeal(Health target)
        {
            if (target == null)
            {
                Debug.LogWarning("[Injector] Не найдено здоровье игрока. Инжектор израсходован.", this);
                return;
            }

            if (requireAlive && target.IsDead)
            {
                Debug.Log("[Injector] Игрок мёртв, инжектор не применён.", this);
                return;
            }

            float healed = target.ApplyHeal(healAmount, ResolveOverhealEnabled());

            if (healed > 0f)
            {
                Debug.Log($"[Injector] Лечение: +{healed:0.#} хп, {target.Current:0.#}/{target.Max:0.#}", this);
            }
            else
            {
                Debug.Log("[Injector] Хп уже полные, инжектор израсходован.", this);
            }
        }

        /// <summary>
        /// Применение закончено: модель в руке выключается, рука освобождается
        /// под другой предмет, а сам инджектор помечается израсходованным.
        /// Модель руки не удаляется — она просто выключена и включится снова,
        /// когда игрок подберёт следующий инджектор с тем же ID.
        /// </summary>
        private void CompleteUse(PlayerHand hand)
        {
            inUse = false;

            // Возврат в Idle — через параметр Idle: его снимает переход
            // Heal -> Idle, и он не остаётся висеть на будущее. Просить надо
            // пока вариант руки активен: выключенный Animator триггер не примет.
            RequestIdleState();

            if (hand == null) return;

            // Инжектор теперь лежит в инвентаре, а не валяется отдельным объектом,
            // поэтому у руки два варианта: взят с земли (тогда объект надо
            // перевести в «израсходован») или выбран в ячейке (тогда из ячейки
            // убирается одна штука). Оба случая руку освобождают одинаково.
            if (hand.HeldFromInventory != null) hand.ConsumeHeldInventoryItem();
            else hand.ReleaseUsedItem();
        }

        /// <summary>
        /// Просит аниматор вернуться в Idle через параметр-триггер. Именно триггер,
        /// а не принудительный Play: сработавший переход сбрасывает параметр, и тот
        /// не ломает следующее применение.
        /// </summary>
        private void RequestIdleState()
        {
            if (activeAnimator == null) return;
            if (string.IsNullOrEmpty(idleTrigger)) return;

            if (!HasTriggerParameter(activeAnimator, idleTrigger))
            {
                if (!warnedAboutIdleTrigger)
                {
                    warnedAboutIdleTrigger = true;
                    Debug.LogWarning(
                        $"[Injector] В контроллере '{ControllerName(activeAnimator)}' нет триггера '{idleTrigger}'. " +
                        "Добавь параметр Trigger с таким именем и переход из состояния применения в Idle.",
                        this);
                }

                return;
            }

            activeAnimator.SetTrigger(idleTrigger);
        }

        /// <summary>
        /// Обнуляет анимацию инжектора и возвращает его в состояние Idle.
        ///
        /// Состояние ставится принудительно, а не триггером: параметры-триггеры
        /// переживают выключение GameObject, поэтому залипший Idle сорвал бы
        /// следующее применение — сначала он ушёл бы в Idle посреди анимации
        /// лечения. Здесь он сбрасывается вместе со всеми остальными.
        /// Модель руки не уничтожается — она просто возвращается в исходную позу.
        /// </summary>
        public void ResetAnimation()
        {
            if (activeAnimator == null) return;
            if (!activeAnimator.isActiveAndEnabled) return;

            activeAnimator.Rebind();
            ClearAllTriggers();

            int state = ResolveStateHash(activeAnimator, idleState);
            if (state != 0) activeAnimator.Play(state, 0, 0f);

            activeAnimator.Update(0f);
        }

        /// <summary>Снимает все залипшие триггеры: они переживают выключение GameObject.</summary>
        private void ClearAllTriggers()
        {
            AnimatorControllerParameter[] parameters = activeAnimator.parameters;
            if (parameters == null) return;

            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].type == AnimatorControllerParameterType.Trigger)
                    activeAnimator.ResetTrigger(parameters[i].nameHash);
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

            for (int i = 0; i < target.layerCount; i++)
            {
                int shortHash = Animator.StringToHash(stateName);
                if (target.HasState(i, shortHash)) return shortHash;
            }

            return 0;
        }

        /// <summary>
        /// Здоровье берётся у того же игрока, чья рука держит инджектор.
        /// PlayerController ищется по активности, как это делает гаечный ключ для руки.
        /// </summary>
        private Health ResolveTargetHealth(PlayerHand hand)
        {
            if (playerHealth == null)
            {
                PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
                for (int i = 0; i < players.Length; i++)
                {
                    if (players[i] == null || !players[i].isActiveAndEnabled) continue;

                    Health health = players[i].Health;
                    if (health == null) continue;

                    playerHealth = health;
                    break;
                }
            }

            return playerHealth;
        }

        private void ResolveAnimator(PlayerHand hand)
        {
            if (activeAnimator != null) return;

            if (hand != null && hand.ActiveHandVariant != null)
            {
                activeAnimator = hand.ActiveHandVariant.GetComponentInChildren<Animator>();
                if (activeAnimator != null) return;
            }

            if (animator != null)
            {
                activeAnimator = animator;
                return;
            }

            if (warnedAboutAnimator) return;

            warnedAboutAnimator = true;
            PickupItem pickup = GetComponent<PickupItem>();
            string id = pickup != null ? pickup.ItemId : string.Empty;
            Debug.LogWarning($"[Injector] У инжектора '{name}' не найден аниматор. Добавь Animator в вариант руки с ID '{id}'", this);
        }

        private void PlayUseAnimation()
        {
            if (activeAnimator == null) return;
            if (string.IsNullOrEmpty(useTrigger)) return;

            // Без такой проверки расхождение между полем и контроллером выглядит
            // как «анимация не играет» и находится только вручную.
            if (!HasTriggerParameter(activeAnimator, useTrigger))
            {
                if (warnedAboutTrigger) return;

                warnedAboutTrigger = true;
                Debug.LogWarning(
                    $"[Injector] В контроллере '{ControllerName(activeAnimator)}' нет триггера '{useTrigger}'. " +
                    $"Добавь параметр Trigger с таким именем и подпиши на него переход в состояние анимации применения.",
                    this);
                return;
            }

            // Снимаем залипшие триггеры перед запуском. Триггер Idle переживает
            // выключение GameObject, поэтому без этого сброс из прошлого подбора
            // срабатывал в середине применения и откатывал анимацию в бездействие.
            ClearAllTriggers();
            activeAnimator.SetTrigger(useTrigger);
        }

        private static bool HasTriggerParameter(Animator target, string parameterName)
        {
            if (target == null || target.runtimeAnimatorController == null) return false;

            AnimatorControllerParameter[] parameters = target.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name != parameterName) continue;
                return parameters[i].type == AnimatorControllerParameterType.Trigger;
            }

            return false;
        }

        private static string ControllerName(Animator target)
        {
            if (target == null || target.runtimeAnimatorController == null) return "<нет контроллера>";
            return target.runtimeAnimatorController.name;
        }
    }
}
