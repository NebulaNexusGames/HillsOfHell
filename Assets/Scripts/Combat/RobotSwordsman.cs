using CarGame.Vehicle;
using UnityEngine;

namespace CarGame.Combat
{
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
        [Tooltip("С какой дистанции робот бьёт, м")]
        [SerializeField] private float attackRange = 2.1f;
        [Tooltip("Урон за удар по игроку")]
        [SerializeField] private float attackDamage = 11f;
        [Tooltip("Множитель урона, если цель — машина")]
        [SerializeField] private float carDamageMultiplier = 1.4f;
        [Tooltip("Пауза между ударами, с")]
        [SerializeField] private float attackCooldown = 1.1f;

        [Header("Смерть")]
        [Tooltip("Звук гибели. Если пусто, будет использован встроенный процедурный звук")]
        [SerializeField] private AudioClip deathSound;
        [Tooltip("Проигрывать звук при смерти")]
        [SerializeField] private bool playDeathSound = true;
        [Tooltip("Громкость звука гибели")]
        [SerializeField] private float deathSoundVolume = 1f;

        [Header("Аниматор")]
        [Tooltip("Animator робота-мечника. Если пусто — ищется на этом объекте и на детях")]
        [SerializeField] private Animator animator;
        [Tooltip("Играть переходы между Idle/Walk/Run и замах атаки")]
        [SerializeField] private bool animateStrike = true;
        [Tooltip("Длительность перехода между состояниями, с. 0 — мгновенное переключение")]
        [SerializeField] private float crossFade = 0.12f;
        [Tooltip("Порог скорости, выше которого робот на патруле идёт (Walk), а не стоит (Idle), м/с")]
        [SerializeField] private float walkThreshold = 0.25f;

        [Header("Названия состояний в Animator")]
        [Tooltip("Стоит на месте, цель не видна")]
        [SerializeField] private string idleState = "Idle";
        [Tooltip("Идёт по патрулю, цель не видна")]
        [SerializeField] private string walkState = "Walk";
        [Tooltip("Бежит на цель (анимация Run). В SwordBot.controller переход Idle→Run идёт через триггер Run — код шлёт именно его")]
        [SerializeField] private string runState = "Run";
        [Tooltip("Атака")]
        [SerializeField] private string attackState = "Attack";

        // Кэш типов параметров AnimatorController (Bool == 4). Пересобирается
        // при смене контроллера, чтобы не читать animator.parameters каждый кадр.
        // Внимание: брать параметры нужно из Animator.parameters (AnimatorControllerParameter[]),
        // а не из RuntimeAnimatorController — у последнего свойства parameters нет.
        private AnimatorControllerParameter[] cachedParameters;
        private RuntimeAnimatorController cachedController;

        [Header("Лут")]
        [SerializeField] private LootEntry[] loot = new LootEntry[0];
        [Tooltip("Насколько разлетаются выпавшие предметы, м")]
        [SerializeField] private float scatterRadius = 0.5f;

        private Rigidbody body;
        private float nextAttackTime;
        private bool hadTarget;
        private string currentState;
        private float attackLockTime;
        private float attackStopTimer;
        // Анти-залипание преследования: если робот не приближается к цели
        // (упёрся в невидимый коллайдер/стену), после chaseGiveUpTime секунд
        // без прогресса цель сбрасывается и возвращается патруль.
        [SerializeField] private float chaseGiveUpTime = 3f;
        private float prevChaseDistance = -1f;
        private float chaseNoProgressTimer;
        private float chaseProgress;
        private RobotVoice voice;

        public Animator Animator => animator;

        /// <summary>Мечник преследует ТОЛЬКО если видит цель: без памяти цели.</summary>
        protected override bool HasSightMemory => false;

        /// <summary>Голос робота (шаги/сигнал цели). Создаётся автоматически.</summary>
        protected RobotVoice Voice
        {
            get
            {
                if (voice == null) voice = GetComponent<RobotVoice>();
                if (voice == null) voice = gameObject.AddComponent<RobotVoice>();
                return voice;
            }
        }

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

            if (health != null) health.Died += OnDied;
        }

        private void Update()
        {
            if (health == null || health.IsDead) return;

            BaseUpdate();

            if (attackStopTimer > 0f) attackStopTimer -= Time.deltaTime;

            Vector3 destination = ResolveDestination();
            Vector3 flatDelta = Vector3.ProjectOnPlane(destination - FrontPosition, Vector3.up);
            float distance = flatDelta.magnitude;

            bool engaged = HasTarget;

            // Дистанция преследования: если робот упёрся в невидимый коллайдер
            // (стекло, триггер-блокер) и физически не может подойти ближе —
            // цель всё равно «не видна для атаки». Чтобы не залипать в беге на
            // месте, при отсутствии прогресса цель сбрасывается и робот
            // возвращается в патруль.
            if (engaged && distance > attackRange * 1.2f)
            {
                float progress = (prevChaseDistance < 0f ? 0f : prevChaseDistance - distance);
                chaseProgress += progress;
                chaseNoProgressTimer += Time.deltaTime;
                if (chaseNoProgressTimer > chaseGiveUpTime && chaseProgress < 0.35f)
                {
                    Target = null;
                    BeginPause(0.8f);
                    engaged = false;
                }
            }
            else
            {
                chaseNoProgressTimer = 0f;
                chaseProgress = 0f;
            }
            prevChaseDistance = engaged ? distance : -1f;

            bool striking = attackStopTimer > 0f;

            Vector3 direction = striking ? Vector3.zero : ComputeAvoidance(flatDelta);

            UpdateStuckCheck(
                body != null ? body.linearVelocity : Vector3.zero,
                !striking && direction.sqrMagnitude > 0.0001f);

            if (direction.sqrMagnitude > 0.0001f) FaceDirection(direction, turnSpeed, Time.deltaTime);

            // Пауза на точке патруля: робот обязан встать, а не «идти» на месте.
            // Вне паузы мечник реально шагает к точке маршрута (анимация Walk),
            // дошёл «N шагов» — встаёт на restDuration секунд (анимация Idle).
            bool mayMove = PatrolAllowsMove();
            float speed = !mayMove || striking ? 0f : engaged ? moveSpeed : PatrolSpeed;
            Move(mayMove ? direction : Vector3.zero, speed);

            UpdateAnimation(engaged);

            RobotVoice voice = Voice;
            if (voice != null && body != null)
                voice.Step(new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z), engaged);

            if (!engaged || Target == null) return;
            if (distance > attackRange) return;
            if (Time.time < nextAttackTime) return;

            nextAttackTime = Time.time + attackCooldown;
            attackStopTimer = Mathf.Max(attackCooldown, AttackStateLength());
            PlayAttack();
            Strike(Target, distance);
        }

        private void UpdateAnimation(bool engaged)
        {
            hadTarget = engaged;
            if (animator == null) return;

            // SwordBot.controller НЕ имеет параметров MoveSpeed/InCombat — он
            // управляется псевдо-триггерами Idle / Walk / Run (тип Bool) и
            // реальным триггером Attack. Определяем это динамически: если у
            // контроллера есть хотя бы один параметр состояния, переключаем
            // анимацию ТОЛЬКО параметрами и никогда не зовём animator.Play() —
            // иначе Play каждый кадр обрывал цикл шагов и робот «топтался на
            // месте».
            bool parameterDriven = IsStateParameter(idleState) || IsStateParameter(walkState) || IsStateParameter(runState);

            if (!parameterDriven)
            {
                // Старый путь для контроллеров, живущих на параметрах движения.
                Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
                float speed = new Vector2(velocity.x, velocity.z).magnitude;
                SetFloat("MoveSpeed", speed);
                SetBool("InCombat", engaged);
                animator.speed = 1f;
                currentState = null;
                return;
            }

            if (attackLockTime > 0f)
            {
                attackLockTime -= Time.deltaTime;
                return;
            }

            animator.speed = 1f;

            // Механика мечника: два состояния ИИ.
            // Idle-состояние: патрулирование — идёт анимацией Walk, после
            // рандомного числа шагов встаёт (пауза маршрута) — анимация Idle.
            // Атака: увидел цель — бежит к ней (Run), догнал — останавливается
            // и бьёт (Attack). Выбор состояния идёт ПОЛНОСТЬЮ по логике ИИ, а
            // НЕ по замеренной скорости: раньше скорость на переходе Idle⇄Walk
            // проседала ниже порога и аниматор каждую секунду переклинивало
            // между состояниями.
            string desired;
            if (engaged)
            {
                // Увидел цель → бег к ней (анимация Run). Если бежать некуда
                // (цель в пределах удара и робот стоит) — остаёмся в Run-стойке,
                // атаку инициирует блок Strike выше.
                desired = runState;
            }
            else if (PatrolAllowsMove())
            {
                // Патрулирование: идём анимацией Walk.
                desired = walkState;
            }
            else
            {
                // Пауза на restDuration секунд / простоял после потери цели — Idle.
                desired = idleState;
            }

            ApplyState(desired);
        }

        /// <summary>
        /// Переключает состояние анимации под текущий тип параметра в
        /// SwordBot.controller:
        /// - Bool («псевдо-триггеры» Idle/Walk/Run): выставляем целевой параметр
        ///   в true, остальные — в false. Флаг ставится ОДИН РАЗ на смену
        ///   состояния: переходы в контроллере мгновенные (duration 0), и если
        ///   держать флаг поднятым каждый кадр, завершающий переход (например
        ///   Attack→Idle) тут же подхватывал поднятый флаг и возвращал обратно —
        ///   аниматор переклинивало между Idle и Run каждую секунду;
        /// - Trigger (Attack): шлём SetTrigger ровно один раз на смену состояния.
        ///     Раньше для Bool-параметров вызывали SetTrigger каждый раз при
        ///     смене состояния — Unity печатал ошибку и состояние не переключалось,
        ///     из-за чего мечник проигрывал Walk/Run-переходы лишь частично и
        ///     визуально «топтался на месте».
        /// </summary>
        private void ApplyState(string state)
        {
            if (string.IsNullOrEmpty(state)) return;

            // Смена состояния = ровно одно событие в аниматор. Пока desired не
            // изменился, аниматор не трогаем: цикл Walk/Run доигрывается целиком.
            if (currentState == state) return;

            AnimatorControllerParameterType type = GetParameterType(state);

            if (type == AnimatorControllerParameterType.Bool)
            {
                currentState = state;
                ResetOtherStateBools(state);
                animator.SetBool(state, true);
                return;
            }

            if (type == AnimatorControllerParameterType.Trigger)
            {
                currentState = state;
                ResetOtherStateBools(state);
                // Сброс до установки: иначе «залипший» с прошлых циклов триггер
                // срабатывал повторно и аниматор клинил между состояниями.
                animator.ResetTrigger(state);
                animator.SetTrigger(state);
                return;
            }

            // Параметра с таким именем нет — ничего не делаем (не ломаем старый путь).
        }

        private void ResetOtherStateBools(string except)
        {
            string[] states = { idleState, walkState, runState, attackState };
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

            // У RuntimeAnimatorController свойства parameters НЕ существует —
            // список параметров доступен только через Animator.parameters.
            // Кэшируем его, пересобирая только при смене контроллера.
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

        private bool IsStateParameter(string name)
        {
            AnimatorControllerParameterType type = GetParameterType(name);
            return type == AnimatorControllerParameterType.Bool
                || type == AnimatorControllerParameterType.Trigger;
        }

        private void PlayAttack()
        {
            if (!animateStrike || animator == null) return;
            if (string.IsNullOrEmpty(attackState)) return;

            // SwordBot.controller переключает атаку триггером "Attack":
            // Idle/Walk/Run → Attack по этому триггеру, Attack → Idle сам.
            // Форсированный Play здесь только сбивал переходы.
            if (IsStateParameter(attackState))
            {
                ApplyState(attackState);
                attackLockTime = AttackStateLength();
                return;
            }

            int hash = Animator.StringToHash(attackState);
            if (!HasState(hash)) return;

            currentState = attackState;
            attackLockTime = AttackStateLength();

            if (crossFade <= 0.001f) animator.Play(hash, 0, 0f);
            else animator.CrossFadeInFixedTime(hash, crossFade * 0.5f, 0, 0f);
        }

        private float AttackStateLength()
        {
            AnimationClip[] clips = animator.runtimeAnimatorController != null
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

        private bool HasState(int hash)
        {
            if (animator == null || !animator.isActiveAndEnabled) return false;
            if (animator.runtimeAnimatorController == null) return false;

            int layers = animator.layerCount;
            for (int i = 0; i < layers; i++)
            {
                if (animator.HasState(i, hash)) return true;
            }

            return false;
        }

        private void SetBool(string parameter, bool value)
        {
            if (HasParameter(parameter)) animator.SetBool(parameter, value);
        }

        private void SetFloat(string parameter, float value)
        {
            if (HasParameter(parameter)) animator.SetFloat(parameter, value);
        }

        private void FireTrigger(string parameter)
        {
            if (HasParameter(parameter)) animator.SetTrigger(parameter);
        }

        private bool HasParameter(string parameter)
        {
            if (animator == null || string.IsNullOrEmpty(parameter)) return false;
            if (animator.parameters == null || animator.parameters.Length == 0) return false;

            for (int i = 0; i < animator.parameters.Length; i++)
            {
                if (animator.parameters[i].name == parameter) return true;
            }

            return false;
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
            Vector3 point = target.transform.position + Vector3.up;

            if (Physics.Linecast(origin, point, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(target.transform)
                && !hit.collider.transform.IsChildOf(transform))
                return;

            float damage = attackDamage;
            CarController car = target.GetComponentInParent<CarController>();
            if (car != null) damage *= carDamageMultiplier;

            target.TakeDamage(new DamageInfo(damage, gameObject, point, (point - origin).normalized));
        }

        private void OnDied(Health dead)
        {
            FireTrigger("Dead");
            SetBool("Attacking", false);
            SetBool("InCombat", false);

            DropLoot();

            PlayDeathSound();

            if (body != null) body.linearVelocity = Vector3.zero;

            Destroy(gameObject, 0.1f);
        }

        /// <summary>
        /// Мечник не взрывается: на его месте не должно быть опасной зоны урона.
        /// Гибель обозначается только звуком, после чего робот разлетается на лут.
        /// </summary>
        private void PlayDeathSound()
        {
            if (!playDeathSound) return;

            if (deathSound != null)
            {
                AudioSource.PlayClipAtPoint(deathSound, transform.position, deathSoundVolume);
                return;
            }

            Explosion.PlayFallbackSound(transform.position, deathSoundVolume);
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