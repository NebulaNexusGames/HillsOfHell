using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Расходник, который работает как инджектор: его нужно держать в руке,
    /// а воспользоваться можно только активной версией для руки. Перед
    /// эффектом проигрывается анимация применения, звуки идут из AudioSource
    /// внутри этой анимации. После применения предмет исчезает только для
    /// игрока: вариант руки не удаляется, а выключается и включится снова,
    /// когда игрок подберёт такой же предмет.
    ///
    /// Отличие инджектора от этих предметов задаётся наследниками через
    /// ApplyEffect — сам тайминг, аниматор и освобождение руки общие.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class ConsumableItem : MonoBehaviour, IHandUsable
    {
        [Header("Таймеры применения")]
        [Tooltip("Через сколько секунд после применения сработает эффект")]
        [SerializeField] private float effectDelay = 0.6f;
        [Tooltip("Через сколько секунд предмет считается использованным: модель в руке выключается, рука освобождается, предмет исчезает")]
        [SerializeField] private float usedDelay = 2f;

        [Header("Аниматор")]
        [Tooltip("Аниматор предмета. Если пусто — ищется в варианте руки")]
        [SerializeField] private Animator animator;
        [Tooltip("Имя параметра-триггера анимации применения")]
        [SerializeField] private string useTrigger = "Drink";
        [Tooltip("Имя параметра-триггера, который обнуляет анимацию и возвращает предмет в состояние Idle")]
        [SerializeField] private string idleTrigger = "Idle";
        [Tooltip("Имя состояния, в которое предмет возвращается при сбросе")]
        [SerializeField] private string idleState = "Idle";

        private Animator activeAnimator;
        private PlayerController player;
        private bool inUse;
        private bool warnedAboutAnimator;
        private bool warnedAboutTrigger;
        private bool warnedAboutIdleTrigger;
        private float nextUseTime;
        private float useLockout = 0.35f;

        /// <summary>Идёт применение: бросок заблокирован, повторное нажатие игнорируется.</summary>
        public bool InUse => inUse;

        /// <summary>Эффект расходника. Вызывается в момент effectDelay.</summary>
        protected abstract void ApplyEffect(PlayerController player);

        /// <summary>Можно ли применить эффект этому игроку. По умолчанию — да.</summary>
        protected virtual bool CanApply(PlayerController player)
        {
            return true;
        }

        /// <summary>Имя расходника для логов.</summary>
        protected virtual string LogName => name;

        public void OnUsePressed(PlayerHand hand)
        {
            if (inUse) return;
            if (Time.time < nextUseTime) return;
            if (!CanUse(hand)) return;

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
        /// Предмет только что подобран. Сбрасываем анимацию сразу: после
        /// применения вариант руки остаётся в кадре применения, а вариант один
        /// и тот же для всех таких предметов, поэтому новый предмет в руке
        /// появлялся бы «замороженным» на последнем кадре.
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

            // В руке должен быть предмет: взят с земли (HeldItem) или выбран
            // из ячейки (HeldFromInventory). Иначе эффект срабатывал бы и с
            // пустой рукой.
            if (hand.HeldItem == null && hand.HeldFromInventory == null) return false;

            return true;
        }

        private void BeginUse(PlayerHand hand)
        {
            inUse = true;
            nextUseTime = Time.time + useLockout;

            ResolveAnimator(hand);
            PlayUseAnimation();

            PlayerController target = ResolveTargetPlayer(hand);
            if (!CanApply(target))
            {
                inUse = false;
                return;
            }

            Vector3 position = hand.HoldSocket != null ? hand.HoldSocket.position : transform.position;

            // Бросок блокируется до конца применения: предмет должен остаться
            // в руке, иначе игрок выронит копию и подберёт её снова.
            hand.SetDropBlocked(true);

            InjectorUseSequence.Start(position, effectDelay, usedDelay, () => ApplyEffect(target), () => CompleteUse(hand));
        }

        /// <summary>
        /// Применение закончено: модель в руке выключается, рука освобождается
        /// под другой предмет, а сам расходник помечается израсходованным.
        /// Модель руки не удаляется — она просто выключена и включится снова,
        /// когда игрок подберёт следующий расходник с тем же ID.
        /// </summary>
        private void CompleteUse(PlayerHand hand)
        {
            inUse = false;
            nextUseTime = Time.time + useLockout;

            // Возврат в Idle — через параметр Idle: его снимает переход
            // Drink -> Idle, и он не остаётся висеть на будущее. Просить надо
            // пока вариант руки активен: выключенный Animator триггер не примет.
            RequestIdleState();

            if (hand == null) return;

            // Расходник теперь лежит в инвентаре, а не валяется отдельным объектом,
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
                        $"[{LogName}] В контроллере '{ControllerName(activeAnimator)}' нет триггера '{idleTrigger}'. " +
                        "Добавь параметр Trigger с таким именем и переход из состояния применения в Idle.",
                        this);
                }

                return;
            }

            activeAnimator.SetTrigger(idleTrigger);
        }

        /// <summary>
        /// Обнуляет анимацию предмета и возвращает его в состояние Idle.
        /// Состояние ставится принудительно, а не триггером: параметры-триггеры
        /// переживают выключение GameObject, поэтому залипший Idle сорвал бы
        /// следующее применение. Модель руки не уничтожается.
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
        /// Игрок берётся по активности, как у гаечного ключа. Плеер держит руку,
        /// значит он и получает эффект.
        /// </summary>
        private PlayerController ResolveTargetPlayer(PlayerHand hand)
        {
            if (player != null && player.isActiveAndEnabled) return player;

            if (hand != null)
            {
                player = hand.GetComponentInParent<PlayerController>();
                if (player != null) return player;
            }

            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null || !players[i].isActiveAndEnabled) continue;

                player = players[i];
                break;
            }

            return player;
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
            Debug.LogWarning($"[{LogName}] У расходника '{name}' не найден аниматор. Добавь Animator в вариант руки с ID '{id}'", this);
        }

        private void PlayUseAnimation()
        {
            if (activeAnimator == null) return;
            if (string.IsNullOrEmpty(useTrigger)) return;

            if (!HasTriggerParameter(activeAnimator, useTrigger))
            {
                if (warnedAboutTrigger) return;

                warnedAboutTrigger = true;
                Debug.LogWarning(
                    $"[{LogName}] В контроллере '{ControllerName(activeAnimator)}' нет триггера '{useTrigger}'. " +
                    "Добавь параметр Trigger с таким именем и подпиши на него переход в состояние анимации применения.",
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