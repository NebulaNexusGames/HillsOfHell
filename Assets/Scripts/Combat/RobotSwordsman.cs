using CarGame.Player;
using CarGame.Vehicle;
using UnityEngine;

namespace CarGame.Combat
{
    /// <summary>
    /// Робот-мечник. Поведение строго по ТЗ:
    ///
    /// РЕЖИМ 1 — ПАТРУЛЬ (цели нет):
    ///   1) идёт к точке маршрута в анимации Walk;
    ///   2) дошёл — останавливается на передышку в анимации Idle;
    ///   3) отдохнул — снова пункт 1.
    ///
    /// РЕЖИМ 2 — ПРЕСЛЕДОВАНИЕ (цель есть):
    ///   просто бежит за игроком (анимация Run), без остановок и без «сдачи».
    ///   Порядок действий атаки:
    ///   1) сократил дистанцию до радиуса удара — останавливается и бьёт
    ///      (анимация Attack, урон наносится в момент замаха);
    ///   2) после удара стоит на месте в анимации Idle ровно attackStopDuration
    ///      секунд (по ТЗ — 2 с);
    ///   3) игрок ушёл — продолжает догонять (Run), пока не сократит дистанцию
    ///      для нового удара; игрок остался на месте — по окончании стойки
    ///      сразу возвращается к пункту 1 и бьёт снова.
    ///
    /// СОСТОЯНИЕ 3 — DEATH:
    ///   смерть запускает лупнутую анимацию тряски булевым параметром
    ///   Animator (dieParameter). Робот не двигается, один раз играет звук
    ///   смерти и всё это время крутит зацикленный звук тряски. По истечении
    ///   deathExplodeDelay (секунды) робот взрывается: спавнит лут, на его
    ///   месте создаётся префаб взрыва, сам робот удаляется.
    /// </summary>
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class RobotSwordsman : EnemyBase
    {
        [Header("Характеристики")]
        [Tooltip("Максимальное здоровье робота")]
        [SerializeField] private float maxHealth = 60f;
        [Tooltip("Скорость преследования, м/с")]
        [SerializeField] private float moveSpeed = 3.6f;
        [Tooltip("Скорость поворота к цели, градусов/с")]
        [SerializeField] private float turnSpeed = 420f;
        [Tooltip("С какой дистанции робот бьёт (до поверхности цели), м")]
        [SerializeField] private float attackRange = 2.1f;
        [Tooltip("Урон за удар по игроку")]
        [SerializeField] private float attackDamage = 11f;
        [Tooltip("Интервал между ударами, с")]
        [SerializeField] private float attackCooldown = 1.1f;
        [Tooltip("Множитель урона, если цель — машина")]
        [SerializeField] private float carDamageMultiplier = 1.4f;
        [Tooltip("Сколько секунд робот стоит в Idle ПОСЛЕ КАЖДОГО удара " +
                 "(по ТЗ — 2 с). Это же пауза между замахами: следующий удар " +
                 "начинается сразу по её окончании.")]
        [SerializeField] private float attackStopDuration = 2f;

        [Header("Смерть")]
        [Tooltip("Булев параметр Animator, запускающий лупнутую анимацию смерти " +
                 "(тряску). Пока она играется и идёт таймер до взрыва, робот не двигается.")]
        [SerializeField] private string dieParameter = "Die";
        [Tooltip("Имя состояния Death в Animator Controller — туда скрипт принудительно " +
                 "возвращает проигрывание, если анимация залипла на первом кадре.")]
        [SerializeField] private string deathStateName = "Death";
        [Tooltip("Таймер от запуска анимации смерти до взрыва, СЕКУНД. Всё это время " +
                 "робот трясётся (луп анимации) и стоит на месте.")]
        [SerializeField] private float deathExplodeDelay = 1.5f;
        [Tooltip("Префаб взрыва. Создаётся на месте робота по истечении таймера смерти, " +
                 "сам робот при этом удаляется (Assets/Prefabs/Effects/Explosion.prefab)")]
        [SerializeField] private GameObject explosionPrefab;
        [Tooltip("Звук смерти робота — проигрывается ОДИН РАЗ в начале таймера до взрыва")]
        [SerializeField] private AudioClip deathSound;
        [Tooltip("Звук тряски — проигрывается на постоянной основе (в цикле) всё время " +
                 "пока идет таймер до взрыва. Если пусто — процедурный ShakeRattle")]
        [SerializeField] private AudioClip shakeSound;
        [Tooltip("Проигрывать звуки смерти")]
        [SerializeField] private bool playDeathSound = true;
        [Tooltip("Громкость звуков гибели")]
        [SerializeField] private float deathSoundVolume = 1f;

        [Header("Аниматор")]
        [Tooltip("Animator робота-мечника. Если пусто — ищется на этом объекте и на детях")]
        [SerializeField] private Animator animator;
        [Tooltip("Играть переходы между Idle/Walk/Run и замах атаки")]
        [SerializeField] private bool animateStrike = true;
        [Tooltip("Длительность перехода между состояниями, с. 0 — мгновенное переключение")]
        [SerializeField] private float crossFade = 0.12f;

        [Header("Названия состояний в Animator")]
        [Tooltip("Стоит на месте (патрульная передышка и пост-ударная стойка)")]
        [SerializeField] private string idleState = "Idle";
        [Tooltip("Идёт по патрулю")]
        [SerializeField] private string walkState = "Walk";
        [Tooltip("Бежит за целью")]
        [SerializeField] private string runState = "Run";
        [Tooltip("Атака")]
        [SerializeField] private string attackState = "Attack";

        // Кэш типов параметров AnimatorController (Bool == 4). Пересобирается
        // при смене контроллера, чтобы не читать animator.parameters каждый кадр.
        private AnimatorControllerParameter[] cachedParameters;
        private RuntimeAnimatorController cachedController;

        [Header("Лут")]
        [SerializeField] private LootEntry[] loot = new LootEntry[0];
        [Tooltip("Насколько разлетаются выпавшие предметы, м")]
        [SerializeField] private float scatterRadius = 0.5f;

        [Header("Звуки")]
        [Tooltip("Свой клип шагов (звучит при ходьбе/беге). Пусто — процедурный звук")]
        [SerializeField] private AudioClip stepSound;
        [Tooltip("Свой клип сигнала обнаружения цели (звучит в момент захвата). Пусто — процедурный звук")]
        [SerializeField] private AudioClip alertSound;
        [Tooltip("Громкость шагов")]
        [SerializeField][Range(0f, 2f)] private float stepVolume = 0.35f;
        [Tooltip("Громкость сигнала обнаружения")]
        [SerializeField][Range(0f, 2f)] private float alertVolume = 0.8f;

        [Header("Память цели")]
        [Tooltip("Сколько секунд мечник помнит игрока после потери видимости и " +
                 "продолжает преследовать последнюю известную позицию. По ТЗ — 10 секунд.")]
        [SerializeField] private float targetMemoryDuration = 10f;

        private Rigidbody body;
        private string currentStateName;
        // Таймер пост-ударной стойки (Idle). Ставится в момент удара равным
        // attackStopDuration; пока > 0 — робот НЕ двигается и НЕ замахивается.
        private float strikeHoldTimer;
        // Когда можно начать следующий замах (Time.time). Совпадает с концом
        // стойки — отдельного cooldown нет: по ТЗ следующий удар начинается
        // сразу после того, как робот отстоит свои секунды.
        private float nextStrikeTime;
        // Замах запущен, ждём окончания клипа Attack: в этот период аниматор
        // не трогаем, чтобы не оборвать анимацию удара.
        private bool attackPlaying;
        private float attackPlayUntil;
        // ===== СОСТОЯНИЕ DEATH =====
        private bool dying;
        // Флаг «взрыв уже запущен» — защита от двойного Explode().
        private bool exploded;
        // Момент смерти. Таймер deathExplodeDelay — в РЕАЛЬНЫХ секундах,
        // поэтому отсчёт ведётся по НАКОПЛЕННОМУ Time.unscaledDeltaTime, а не
        // по разнице Time.unscaledTime. Причина: на некоторых сборках/сценах
        // глобальное unscaledTime может быть рассинхронизировано (например,
        // кастомный fixed/unscaled timestep), и тогда разница двух снимков
        // вообще не равна прошедшему реальному времени — взрыв не наступал
        // НИКОГДА, сколько бы ни ждали. Накопление дельты заведомо даёт ровно
        // столько секунд, сколько прошло: 10 в инспекторе = 10 секунд.
        private float deathElapsed;
        // Зацикленный источник звука тряски на время предсмертной агонии.
        private AudioSource deathRattleSource;

        public Animator Animator => animator;

        /// <summary>
        /// Мечник ПОМНИТ цель: потерял игрока из виду — робот бежит к последней
        /// виденной позиции ещё targetMemoryDuration секунд (по ТЗ 10). Только
        /// отстояв память целиком, мечник возвращается к патрулю.
        /// </summary>
        protected override bool HasSightMemory => true;

        /// <summary>Мечник упорно преследует: препятствие или временная потеря
        /// видимости НЕ являются причиной сброса цели.</summary>
        protected override bool DropTargetWhenUnreachable => false;

        /// <summary>Память цели — 10 секунд по ТЗ, переопределяем старое
        /// сериализованное значение loseTargetMemory из префаба.</summary>
        protected override float LoseTargetMemoryOverride => targetMemoryDuration;

        private void ApplyBloodMoonBuffs()
        {
            maxHealth *= BloodMoonHealthMultiplier;
            moveSpeed *= BloodMoonSpeedMultiplier;
            attackDamage *= BloodMoonDamageMultiplier;
        }

        private void Awake()
        {
            ApplyBloodMoonBuffs();

            health = GetComponent<Health>();
            if (health != null) health.Configure(maxHealth, Health.HpOwner.Enemy);

            body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.useGravity = true;
                body.constraints = RigidbodyConstraints.FreezeRotation;
            }

            if (animator == null) animator = GetComponentInChildren<Animator>();

            BaseAwake();

            ApplySoundClips(stepSound, null, null, null, alertSound, null,
                stepVolume, -1f, -1f, alertVolume, -1f, -1f);

            if (health != null) health.Died += OnDied;
        }

        private void Update()
        {
            // ВАЖНО: проверка dying идёт ДО проверки IsDead. Health.IsDead
            // становится true в том же кадре, что и событие Died (то есть ещё
            // до первого Update после смерти). Старый код при первом же кадре
            // после выхода на строку «IsDead — выходи» вообще переставал тикать
            // (UpdateAnimator вызывается только из активных скриптов, поэтому
            // смерть выглядела как «залипший первый кадр»). Теперь мёртвый,
            // но ещё не взорвавшийся мечник продолжает крутить луп тряски и
            // отсчёт до взрыва.
            if (dying)
            {
                // Отсчёт в реальных секундах накоплением unscaledDeltaTime:
                // паузы/меню с timeScale = 0 его не растягивают, а на обычной
                // скорости 10 в инспекторе = ровно 10 секунд.
                deathElapsed += Time.unscaledDeltaTime;
                if (deathElapsed >= Mathf.Max(0f, deathExplodeDelay)) Explode();
                ForceDeathAnimation();
                return;
            }

            if (health == null || health.IsDead) return;

            BaseUpdate();

            // Тик пост-ударной стойки (секунды).
            if (strikeHoldTimer > 0f) strikeHoldTimer -= Time.deltaTime;

            Vector3 destination = ResolveDestination();
            Vector3 flatDelta = Vector3.ProjectOnPlane(destination - FrontPosition, Vector3.up);

            bool engaged = HasTarget && Target != null && !Target.Equals(null);

            // Занятая игроком машина — цель: идём прямо на кузов (не обходим).
            bool occupiedCarNow = engaged && IsOccupiedVehicleTarget();

            // Дистанция ДО ПОВЕРХНОСТИ цели (для машины — ближайшая точка
            // коллайдеров кузова), а не до центра Health.
            float strikeDistance = engaged
                ? SurfaceDistanceTo(FrontPosition + Vector3.up, Target)
                : float.MaxValue;
            bool inReach = engaged && strikeDistance <= attackRange;

            // Страховка: робот мог войти в обход машины ещё до посадки игрока
            // в авто. Пока цель — занятая машина, никакого облёта быть не должно.
            if (occupiedCarNow && IsDetourActive) CancelDetour();

            // --- Выбор направления движения ---
            Vector3 desiredDirection = flatDelta;
            if (engaged && occupiedCarNow && !inReach)
            {
                // Машина крупная: центр корпуса далеко, а бить надо по капоту —
                // целимся в ближайшую точку поверхности кузова.
                Vector3 toSurface = ClosestCarPoint(Target) - FrontPosition;
                toSurface.y = 0f;
                if (toSurface.sqrMagnitude > 0.0001f) desiredDirection = toSurface;
            }
            if (desiredDirection.sqrMagnitude < 0.0001f && engaged && !IsTargetColliderVisible())
            {
                // Цель «внутри» робота, но не видна (за стеной) — идём к
                // последней видимой точке, чтобы не стоять на месте.
                desiredDirection = LastKnownTargetPosition - transform.position;
            }

            // Занятая машина — цель, а не препятствие: идём прямо на неё,
            // иначе ComputeAvoidance начнёт её обходить вместо атаки.
            Vector3 direction = occupiedCarNow
                ? (desiredDirection.sqrMagnitude > 0.0001f ? desiredDirection.normalized : Vector3.zero)
                : ComputeAvoidance(desiredDirection);

            // --- Разрешение движения (ровно по ТЗ) ---
            // Стоять можно только: в пост-ударной стойке (strikeHoldTimer),
            // во время замаха (attackPlaying) и на плановой передышке патруля
            // (PatrolAllowsMove). Больше нигде: преследование бессрочное,
            // патруль ходит.
            // ВАЖНО: CanMoveNow()/PatrolAllowsMove() НЕ используются — при
            // включенном requireReachableTarget базовый класс ставит плановую
            // паузу (pauseUntil) на время обхода препятствий, и робот «топтался
            // на месте» в Idle/Walk даже когда маршрут чист. По ТЗ движение
            // разрешено ВСЕГДА, кроме: замаха атаки и 2-секундной стойки после
            // удара. Передышка патруля сама приводит робота к точке (destination
            // совпадает с позицией → Move тормозит скорость до нуля), анимация
            // при этом честно переключается Walk ↔ Idle.
            bool holdingAfterStrike = strikeHoldTimer > 0f;
            bool mayMove = !holdingAfterStrike && !attackPlaying;

            UpdateStuckCheck(body != null ? body.linearVelocity : Vector3.zero, mayMove);

            if (direction.sqrMagnitude > 0.0001f) FaceDirection(direction, turnSpeed, Time.deltaTime);

            float speed = !mayMove ? 0f : engaged ? moveSpeed : PatrolSpeed;
            Move(mayMove ? direction : Vector3.zero, speed);

            // --- Анимация ---
            // Замах не прерываем: пока клип Attack реально играется в
            // аниматоре, состояние не трогаем. Как только доиграл —
            // выбираем актуальное состояние (стойка Idle / Run / Walk).
            if (attackPlaying && Time.time < attackPlayUntil && IsAnimatorInAttackState())
            {
                // Доигрываем атаку.
            }
            else
            {
                if (attackPlaying)
                {
                    attackPlaying = false;
                    ResetAttackTrigger();
                    currentStateName = null;
                }

                string desired;
                if (holdingAfterStrike) desired = idleState;          // шаг 2 атаки: стойка Idle
                else if (engaged) desired = runState;                  // преследование: Run
                else if (Time.time >= patrolWaitUntil) desired = walkState; // патруль: шаг 1 — Walk
                else desired = idleState;                              // патруль: шаг 2 — передышка Idle
                ApplyState(desired);
            }

            if (Voice != null && body != null)
                Voice.Step(new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z), engaged);

            // --- Атака (шаг 1 порядка действий) ---
            if (!engaged || holdingAfterStrike || attackPlaying) return;
            if (!inReach) return;                       // не подошёл — не бьём
            if (Time.time < nextStrikeTime) return;     // ещё стоим пост-ударную стойку

            nextStrikeTime = Time.time + attackStopDuration;
            strikeHoldTimer = attackStopDuration;
            PlayAttack();
            Strike(Target, strikeDistance);
        }

        /// <summary>Видна ли цель напрямую (без проверки сектора обзора).</summary>
        private bool IsTargetColliderVisible()
        {
            return Target != null && HasClearLineOfSightToTarget();
        }

        /// <summary>
        /// Сидит ли игрок внутри текущей цели-машины. Машина с водителем —
        /// цель (не препятствие). Пустая машина остаётся препятствием и
        /// обходится штатной логикой — так требует геймплей.
        /// </summary>
        private bool IsOccupiedVehicleTarget()
        {
            if (Target == null || Target.Equals(null)) return false;

            // Канонический признак занятости: CarController.IsOccupied.
            if (IsTargetOccupied(Target)) return true;

            // Резервная проверка: игрок официально сидит в ЭТОЙ машине.
            PlayerController player = FindPlayer();
            if (player != null && !player.Equals(null) && player.IsInCar
                && player.CurrentVehicle != null)
            {
                Health carHealth = player.CurrentVehicle.Health;
                if (carHealth == Target) return true;

                CarController targetCar = Target.GetComponentInParent<CarController>();
                if (targetCar != null && targetCar == player.CurrentVehicle) return true;
            }

            return false;
        }

        /// <summary>
        /// Ближайшая к роботу точка на коллайдерах корпуса цели-машины:
        /// бежать нужно на поверхность кузова, а не в его центр.
        /// </summary>
        private Vector3 ClosestCarPoint(Health target)
        {
            Vector3 origin = FrontPosition + Vector3.up;
            if (target == null || target.Equals(null)) return origin;

            CarController car = target.GetComponentInParent<CarController>();

            Collider[] bodies = car != null ? car.BodyColliders : null;
            if (bodies == null || bodies.Length == 0) return target.transform.position;

            Vector3 best = target.transform.position;
            float bestDist = float.MaxValue;
            foreach (Collider col in bodies)
            {
                if (col == null || col.Equals(null) || !col.enabled) continue;
                Vector3 closest = col.ClosestPoint(origin);
                float d = Vector3.Distance(origin, closest);
                if (d < bestDist) { bestDist = d; best = closest; }
            }
            return best;
        }

        private float AttackStateLength()
        {
            AnimationClip[] clips = animator != null && animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.animationClips
                : null;

            if (clips != null)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i].name != attackState) continue;
                    float speed = Mathf.Abs(animator.speed);
                    return speed > 0.01f ? clips[i].length / speed : clips[i].length;
                }
            }

            return Mathf.Max(0.1f, attackCooldown * 0.6f);
        }

        private void PlayAttack()
        {
            if (!animateStrike || animator == null || string.IsNullOrEmpty(attackState)) return;

            // SwordBot.controller переключает атаку триггером "Attack":
            // Idle/Walk/Run → Attack; из Attack есть выход в Idle. Удар
            // должен доиграться ЦЕЛИКОМ — дальше код держит Idle-стойку.
            attackPlaying = true;
            attackPlayUntil = Time.time + AttackStateLength() + 0.25f;
            ApplyState(attackState);
        }

        /// <summary>
        /// Находится ли аниматор прямо сейчас в состоянии атаки (или в переходе
        /// из него). Страховка: пока идёт замах, аниматор не трогаем.
        /// </summary>
        private bool IsAnimatorInAttackState()
        {
            if (animator == null || !animator.isActiveAndEnabled) return false;
            if (animator.runtimeAnimatorController == null) return false;
            if (string.IsNullOrEmpty(attackState)) return false;

            int hash = Animator.StringToHash(attackState);
            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(layer);
                if (current.fullPathHash == hash || current.shortNameHash == hash) return true;

                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(layer);
                if (next.fullPathHash == hash || next.shortNameHash == hash) return true;
            }

            return false;
        }

        /// <summary>
        /// Переключает состояние анимации под тип параметра в
        /// SwordBot.controller: Bool-«псевдотриггеры» Idle/Walk/Run выставляются
        /// флагом (целевой true, остальные false), Attack — реальным Trigger.
        /// Флаг ставится ОДИН РАЗ на смену состояния: пока desired не изменился,
        /// аниматор не трогаем, иначе цикл Walk/Run обрывался каждый кадр и
        /// робот «топтался на месте».
        /// </summary>
        private void ApplyState(string state)
        {
            if (string.IsNullOrEmpty(state)) return;
            if (currentStateName == state) return;

            AnimatorControllerParameterType type = GetParameterType(state);

            if (type == AnimatorControllerParameterType.Bool)
            {
                currentStateName = state;
                ResetOtherStateBools(state);
                animator.SetBool(state, true);
                return;
            }

            if (type == AnimatorControllerParameterType.Trigger)
            {
                // Триггер — событие, а не состояние: «текущим» считаем то,
                // куда робот вернётся после удара (стойка Idle при цели).
                currentStateName = strikeHoldTimer > 0f ? idleState : null;
                ResetOtherStateBools(state);
                animator.ResetTrigger(state);
                animator.SetTrigger(state);
                return;
            }

            // Параметра с таким именем нет — ничего не делаем.
        }

        private void ResetOtherStateBools(string except)
        {
            if (animator == null) return;
            string[] states = { idleState, walkState, runState };
            for (int i = 0; i < states.Length; i++)
            {
                string s = states[i];
                if (string.IsNullOrEmpty(s) || s == except) continue;
                if (GetParameterType(s) == AnimatorControllerParameterType.Bool)
                    animator.SetBool(s, false);
            }
        }

        private AnimatorControllerParameterType GetParameterType(string name)
        {
            if (animator == null || string.IsNullOrEmpty(name))
                return AnimatorControllerParameterType.Float;

            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller == null) return AnimatorControllerParameterType.Float;

            if (cachedParameters == null || cachedController != controller)
            {
                cachedController = controller;
                cachedParameters = animator.parameters;
            }

            if (cachedParameters == null) return AnimatorControllerParameterType.Float;

            for (int i = 0; i < cachedParameters.Length; i++)
            {
                if (cachedParameters[i].name == name) return cachedParameters[i].type;
            }

            return AnimatorControllerParameterType.Float;
        }

        /// <summary>
        /// Гасит pending-триггер атаки: если триггер остался неподхваченным,
        /// следующий переход вернул бы робота обратно в замах («вечная атака»).
        /// </summary>
        private void ResetAttackTrigger()
        {
            if (animator == null || string.IsNullOrEmpty(attackState)) return;
            if (GetParameterType(attackState) == AnimatorControllerParameterType.Trigger)
            {
                animator.ResetTrigger(attackState);
            }
        }

        private void Move(Vector3 direction, float speed)
        {
            if (body == null) return;

            Vector3 velocity = body.linearVelocity;
            Vector3 flatVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);

            if (direction.sqrMagnitude < 0.0001f || speed <= 0.01f)
                flatVelocity = Vector3.MoveTowards(flatVelocity, Vector3.zero, moveSpeed * 4f * Time.deltaTime);
            else if (IsPathClear(direction) || climbing)
                flatVelocity = Vector3.MoveTowards(flatVelocity, direction.normalized * speed, moveSpeed * 8f * Time.deltaTime);
            else
                flatVelocity = Vector3.MoveTowards(flatVelocity, direction.normalized * speed * 0.15f, moveSpeed * 2f * Time.deltaTime);

            velocity.x = flatVelocity.x;
            velocity.z = flatVelocity.z;
            velocity.y = climbing ? Mathf.Max(velocity.y, ClimbSpeed(direction, speed)) : velocity.y;
            body.linearVelocity = velocity;
        }

        private void Strike(Health target, float distance)
        {
            Vector3 origin = FrontPosition + Vector3.up;
            Vector3 point = (target != null && !target.Equals(null))
                ? target.transform.position + Vector3.up
                : origin + transform.forward;

            // Цель-машина (занятая или нет): бьём ВСЕГДА по ХП КУЗОВА, а не по
            // переданной ссылке target (Health водителя и Health кузова — разные
            // компоненты; раньше удар уходил в неуязвимый Health игрока).
            CarController struckCar = FindCar(target, origin);
            if (struckCar != null)
            {
                Collider[] bodies = struckCar.BodyColliders;
                Vector3 best = Vector3.zero;
                float bestDist = float.MaxValue;
                if (bodies != null)
                {
                    foreach (Collider col in bodies)
                    {
                        if (col == null || col.Equals(null) || !col.enabled) continue;
                        Vector3 closest = col.ClosestPoint(origin);
                        float d = Vector3.Distance(origin, closest);
                        if (d < bestDist) { bestDist = d; best = closest; }
                    }
                }
                if (bestDist < float.MaxValue) point = best + Vector3.up * 0.2f;

                float carDamage = attackDamage * carDamageMultiplier;
                Health carHealth = struckCar.Health;
                if (carHealth == null) return;

                // Игрок за рулём неуязвим — но кузов нет. Страхуемся от чужой
                // разметки HpOwner на компоненте машины.
                if (carHealth.IsPlayer)
                {
                    carHealth = struckCar.GetComponent<Health>();
                    if (carHealth == null || carHealth.IsPlayer)
                        carHealth = struckCar.GetComponentInChildren<Health>();
                }
                if (carHealth == null) return;

                carHealth.TakeDamage(new DamageInfo(carDamage, gameObject, point, (point - origin).normalized));
                return;
            }

            if (target == null || target.Equals(null)) return;

            // Пешая цель: не бить сквозь стену.
            if (Physics.Linecast(origin, point, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(target.transform)
                && !hit.collider.transform.IsChildOf(transform))
                return;

            target.TakeDamage(new DamageInfo(attackDamage, gameObject, point, (point - origin).normalized));
        }

        /// <summary>
        /// Машина цели: сначала по ссылке Health, затем страховка — ближайшая
        /// непустая машина к точке удара. Пешая цель → null.
        /// </summary>
        private CarController FindCar(Health target, Vector3 origin)
        {
            if (target != null && !target.Equals(null))
            {
                CarController car = target.GetComponentInParent<CarController>();
                if (car != null) return car;
            }

            Collider[] nearby = Physics.OverlapSphere(origin, attackRange + 1.5f);
            CarController best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < nearby.Length; i++)
            {
                if (nearby[i] == null || nearby[i].Equals(null)) continue;
                CarController car = nearby[i].GetComponentInParent<CarController>();
                if (car == null) continue;
                float d = Vector3.Distance(origin, nearby[i].ClosestPoint(origin));
                if (d < bestDist) { bestDist = d; best = car; }
            }
            if (best != null && best.IsOccupied) return best;

            PlayerController player = FindPlayer();
            if (player != null && !player.Equals(null) && player.IsInCar
                && player.CurrentVehicle != null)
                return player.CurrentVehicle;

            return best;
        }

        /// <summary>
        /// Момент смерти → состояние Death: включается лупнутая анимация
        /// тряски (Bool-параметр dieParameter), робот останавливается, один раз
        /// играет звук смерти и по кругу — звук тряски. По истечении
        /// deathExplodeDelay СЕКУНД робот взрывается.
        /// </summary>
        private void OnDied(Health dead)
        {
            if (dying || exploded) return;
            dying = true;
            deathElapsed = 0f;

            if (body != null) body.linearVelocity = Vector3.zero;

            // Запуск лупнутой анимации смерти булевым параметром. Флаги
            // Idle/Walk/Run гасим, иначе переход в смерть может не сработать.
            // currentStateName сбрасываем — дальше ApplyState/луп смерти
            // работают с аниматором начисто.
            currentStateName = null;
            attackPlaying = false;
            if (animator != null && !string.IsNullOrEmpty(dieParameter))
            {
                ResetOtherStateBools(null);
                if (GetParameterType(dieParameter) == AnimatorControllerParameterType.Bool)
                    animator.SetBool(dieParameter, true);
                else
                    animator.SetTrigger(dieParameter);
            }

            PlayDeathSounds();
        }

        /// <summary>
        /// Поддержание лупа анимации смерти. Пока робот «умирает», каждый кадр:
        /// гарантируем Bool-параметр dieParameter == true и явно продвигаем
        /// состояние аниматора через Play() с перезапуском, чтобы тряска
        /// реально игралась по кругу, а не залипала на первом кадре.
        /// Примечание: у Animator нет метода UpdateAnimator — единственный
        /// способ вручную протолкнуть анимацию из скрипта это Play/PixelInTime.
        /// </summary>
        private void ForceDeathAnimation()
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            if (exploded) return;

            if (!string.IsNullOrEmpty(dieParameter)
                && GetParameterType(dieParameter) == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(dieParameter, true);
            }

            // Страховка от «залипания»: если аниматор по какой-то причине всё
            // ещё показывает не состояние Death (например, переход не сработал),
            // принудительно запускаем Death — Play() для уже играющего состояния
            // анимацию НЕ перезапускает, поэтому луп тряски идёт плавно.
            // Держим скорость 1: в контроллере у Death стоит m_Speed 4 — клип
            // крутился в 4 раза быстрее, и короткая тряска «кончалась» почти
            // мгновенно, что выглядело как застывший кадр.
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            if (!current.IsName(deathStateName))
            {
                animator.Play(deathStateName, 0, 0f);
            }
            animator.speed = 1f;
        }

        /// <summary>
        /// Взрыв по завершении таймера смерти: спавн лута, появление префаба
        /// взрыва на месте робота, удаление самого робота.
        /// </summary>
        private void Explode()
        {
            if (!dying || exploded) return;
            exploded = true;
            dying = false;

            StopDeathRattle();
            DropLoot();

            if (explosionPrefab != null)
            {
                GameObject effect = Instantiate(explosionPrefab, transform.position, Quaternion.identity);

                // Страховка: если эффект урона по площади, он не должен
                // «добить» уже удаляемый объект (образец DinamiteBot).
                ExplosionEffect explosion = effect != null ? effect.GetComponent<ExplosionEffect>() : null;
                if (explosion != null) explosion.Ignore(gameObject);
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Звуки начала состояния Death: ОДИН РАЗ звук смерти + зацикленный
        /// звук тряски, который крутится всё время пока идёт таймер до взрыва.
        /// </summary>
        private void PlayDeathSounds()
        {
            if (!playDeathSound) return;

            AudioClip deathClip = deathSound != null ? deathSound : CarGame.Audio.ProceduralAudio.Explosion();
            if (deathClip != null)
                AudioSource.PlayClipAtPoint(deathClip, transform.position, deathSoundVolume);

            AudioClip rattle = shakeSound != null ? shakeSound : CarGame.Audio.ProceduralAudio.ShakeRattle();
            if (rattle == null) return;

            deathRattleSource = gameObject.AddComponent<AudioSource>();
            deathRattleSource.clip = rattle;
            deathRattleSource.loop = true;
            deathRattleSource.playOnAwake = false;
            deathRattleSource.spatialBlend = 1f;
            deathRattleSource.volume = deathSoundVolume;
            deathRattleSource.Play();
        }

        private void StopDeathRattle()
        {
            if (deathRattleSource == null) return;
            deathRattleSource.Stop();
            deathRattleSource = null;
        }

        private void DropLoot()
        {
            if (loot == null || loot.Length == 0) return;

            for (int i = 0; i < loot.Length; i++)
            {
                LootEntry entry = loot[i];
                if (entry == null || entry.prefab == null) continue;
                if (Random.value > entry.chance) continue;

                int amount = Random.Range(
                    Mathf.Min(entry.countMin, entry.countMax),
                    Mathf.Max(entry.countMin, entry.countMax) + 1);

                for (int n = 0; n < amount; n++) SpawnLootPiece(entry.prefab);
            }
        }

        private void SpawnLootPiece(GameObject prefab)
        {
            Vector2 offset = Random.insideUnitCircle * scatterRadius;
            GameObject piece = Instantiate(prefab, transform.position + new Vector3(offset.x, 0.35f, offset.y), Random.rotation);
            if (piece == null) return;

            Rigidbody pieceBody = piece.GetComponentInChildren<Rigidbody>();
            if (pieceBody == null) return;

            pieceBody.AddForce(new Vector3(offset.x, 1.4f, offset.y) * 1.6f, ForceMode.Impulse);
            pieceBody.AddTorque(Random.insideUnitSphere * 4f, ForceMode.Impulse);
        }

        [System.Serializable]
        public class LootEntry
        {
            [Tooltip("Префаб выпадающего предмета")]
            public GameObject prefab;
            [Tooltip("Шанс выпадения, 0..1. 1 — всегда")]
            [Range(0f, 1f)] public float chance = 1f;
            [Tooltip("Сколько выпадает, минимум")]
            public int countMin = 1;
            [Tooltip("Сколько выпадает, максимум")]
            public int countMax = 1;
        }
    }
}
