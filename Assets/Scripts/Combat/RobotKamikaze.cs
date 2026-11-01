using System.Collections.Generic;
using UnityEngine;

namespace CarGame.Combat
{
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class RobotKamikaze : EnemyBase
    {
        public enum JumpPhase
        {
            Idle,
            ReadyToAttack,
            Attack,
            AttackFlying,
            Jump,
            Falling,
            Landing,
            ChasePause
        }

        [Header("Характеристики")]
        [Tooltip("Максимальное здоровье робота")]
        [SerializeField] private float maxHealth = 40f;

        [Header("Прыжки")]
        [Tooltip("Скорость вверх при прыжке в сторону игрока, м/с")]
        [SerializeField] private float chaseJumpUpSpeed = 8.5f;
        [Tooltip("Скорость вверх при прыжке на патруле, м/с")]
        [SerializeField] private float patrolJumpUpSpeed = 6f;
        [Tooltip("Скорость вверх при боевом прыжке. Он должен долететь до цели, поэтому выше обычного")]
        [SerializeField] private float attackJumpUpSpeed = 9.5f;
        [Tooltip("Насколько быстро гасится вертикальная скорость в боевом полёте, м/с за секунду. Нужно, чтобы робот не перелетал цель сверху")]
        [SerializeField] private float attackRiseDamping = 7.5f;
        [Tooltip("Горизонтальная скорость в полёте на патруле, м/с")]
        [SerializeField] private float patrolAirSpeed = 4.5f;
        [Tooltip("Горизонтальная скорость в полёте на цель, м/с")]
        [SerializeField] private float chaseAirSpeed = 8f;
        [Tooltip("Нижняя граница горизонтальной скорости прыжка. Слишком высокая заставляет робота перелетать цель на последних метрах, поэтому держим её низкой")]
        [SerializeField] private float minJumpAirSpeed = 1f;
        [Tooltip("Верхняя граница горизонтальной скорости прыжка, когда робот подгоняет дальность под цель")]
        [SerializeField] private float maxJumpAirSpeed = 13f;
        [Tooltip("Кулдаун перед прыжком, когда цель не видна, с")]
        [SerializeField] private float jumpCooldown = 1.2f;
        [Tooltip("Сколько длится фаза приземления, с")]
        [SerializeField] private float landingDuration = 0.18f;
        [Tooltip("Пауза перед сближающим прыжком. Короткая, но достаточная, чтобы оценить дистанцию и успеть замахнуться, если игрок сам подошёл")]
        [SerializeField] private float chasePauseDuration = 0.2f;
        [Tooltip("Сколько длится замах перед боевым прыжком, с")]
        [SerializeField] private float readyDuration = 0.4f;
        [Tooltip("Сколько длится сжатие перед боевым прыжком, с")]
        [SerializeField] private float attackDuration = 0.25f;
        [Tooltip("На этом расстоянии робот переходит в замах вместо прыжка по патрулю, м")]
        [SerializeField] private float attackRange = 4.5f;
        [Tooltip("На каком расстоянии от цели робот хочет приземлиться после обычного прыжка преследования. Цель оказывается в зоне досягаемости, но не вплотную — иначе робот в неё врезается")]
        [SerializeField] private float approachStandoff = 2.5f;
        [Tooltip("Если цель ближе этого расстояния, робот прыгает строго вверх, а не в сторону. Иначе он улетел бы от цели и взорвался впустую")]
        [SerializeField] private float verticalHopDistance = 0.8f;
        [Tooltip("На этом расстоянии начинается подготовка к атаке. Обычно больше attackRange, чтобы замах шёл издалека, м")]
        [SerializeField] private float prepareRange = 9f;
        [Tooltip("Насколько быстро робот доворачивает в полёте, градусов в секунду")]
        [SerializeField] private float airTurnSpeed = 420f;
        [Tooltip("Дальность одного преследующего прыжка, м. Робот НЕ задирает прыжок ради дальности до цели: он считает, сколько коротких ПРЫЖКОВ этой фиксированной дальности нужно, чтобы долететь до цели, и летит ими подряд. После каждой цепочки цель перепроверяется заново.")]
        [SerializeField] private float chaseHopDistance = 6f;
        [Tooltip("Насколько быстро робот доворачивает на земле, градусов в секунду")]
        [SerializeField] private float groundTurnSpeed = 640f;
        [Tooltip("Игнорировать ли препятствия. Включи только на открытой местности")]
        [SerializeField] private bool ignoreObstacles;

        [Header("Касание в боевом полёте")]
        [Tooltip("Сколько секунд после отталкивания не считать касание. Нужно, чтобы робот успел оторваться от земли, иначе он взрывается, не прыгнув")]
        [SerializeField] private float attackFlightMinTime = 0.25f;
        [Tooltip("Если за это время боевой прыжок так и не оторвался от земли, он повторяется. Не даёт роботу зависнуть на месте")]
        [SerializeField] private float attackFlightStuckTime = 1.2f;
        [Tooltip("Если за это время прыжок на патруле или преследование так и не оторвался от земли, фаза прерывается. Не даёт роботу навсегда зависнуть в анимации полёта")]
        [SerializeField] private float airStuckTime = 0.6f;
        [Tooltip("Страховка от зависания: если фаза длится дольше, робот возвращается в Idle и перезапускает цикл. Не даёт застрять ни в одной анимации")]
        [SerializeField] private float maxPhaseDuration = 6f;
        [Tooltip("Радиус проверки касания вокруг робота в боевом полёте, м")]
        [SerializeField] private float contactRadius = 0.7f;
        [Tooltip("Дополнительный запас к радиусу при проверке попадания по цели, м")]
        [SerializeField] private float targetTouchBonus = 0.6f;
        [Tooltip("На какой высоте над центром робота искать касание, м")]
        [SerializeField] private float contactHeight = 0.15f;
        [Tooltip("Слои, которые считаются касанием в боевом полёте")]
        [SerializeField] private LayerMask contactMask = ~0;

        [Header("Проверка земли")]
        [Tooltip("Слои пола и подъёмов. Отдельное имя от EnemyBase.groundMask: одинаковые имена в базовом и производном классе Unity сериализует в один ключ, из-за чего маски путались")]
        [SerializeField] private LayerMask kamikazeGroundMask = ~0;
        [Tooltip("Насколько ниже центра робота искать землю, м")]
        [SerializeField] private float groundProbe = 0.35f;
        [Tooltip("Запасной зонд вниз для склонов и края платформы. Сколько ещё ниже подошв считать землёй")]
        [SerializeField] private float groundProbeSlack = 0.15f;


        [Header("Эффект взрыва")]
        [Tooltip("Готовый объект взрыва со своими триггерами. Ставится в точке, где робот взорвался")]
        [SerializeField] private GameObject explosionEffectPrefab;

        [Header("Лут")]
        [SerializeField] private LootEntry[] loot = new LootEntry[0];
        [Tooltip("Насколько разлетаются выпавшие предметы, м")]
        [SerializeField] private float scatterRadius = 0.5f;

        [Header("Анимации")]
        [SerializeField] private Animator animator;
        [SerializeField] private float crossFade = 0.1f;

        [Header("Названия состояний в Animator")]
        [SerializeField] private string idleState = "Idle";
        [SerializeField] private string jumpState = "Jump";
        [SerializeField] private string fallingState = "Falling";
        [SerializeField] private string landingState = "Landing";
        [SerializeField] private string readyState = "ReadyForAttack";
        [SerializeField] private string attackState = "Attack";
        [SerializeField] private string attackFlyingState = "AttackFly";

        private Rigidbody body;
        private CapsuleCollider bodyCollider;
        private JumpPhase phase;
        private float phaseTimer;
        private float cooldownTimer;
        private bool exploded;
        private bool hasLeftGround;
        private bool wasAirborne;
        private float launchTime;
        private float phaseStartedAt;
        private readonly HashSet<string> warnedStates = new HashSet<string>();
        private string currentState;

        /// <summary>
        /// Робот в ветке атаки. Пока флаг стоит, анимация бездействия не
        /// показывается: цепочка боя её не содержит.
        /// </summary>
        private bool attackChain;

        private Vector3 lockedJumpDirection;

        /// <summary>
        /// Горизонтальная скорость, рассчитанная при отталкивании под точку
        /// приземления. В полёте она больше не пересчитывается.
        /// </summary>
        private float lockedJumpSpeed;

        public Animator Animator => animator;
        public JumpPhase Phase => phase;

        private void ApplyBloodMoonBuffs()
        {
            maxHealth *= BloodMoonHealthMultiplier;

            float speedScale = BloodMoonSpeedMultiplier;
            chaseJumpUpSpeed *= speedScale;
            patrolJumpUpSpeed *= speedScale;
            attackJumpUpSpeed *= speedScale;
            patrolAirSpeed *= speedScale;
            chaseAirSpeed *= speedScale;
            minJumpAirSpeed *= speedScale;
            maxJumpAirSpeed *= speedScale;
        }

        private void Awake()
        {
            ApplyBloodMoonBuffs();

            health = GetComponent<Health>();
            if (health != null) health.Configure(maxHealth, Health.HpOwner.Enemy);

            body = GetComponent<Rigidbody>();
            bodyCollider = GetComponent<CapsuleCollider>();
            if (body != null)
            {
                body.useGravity = true;
                body.constraints = RigidbodyConstraints.FreezeRotation;
            }

            if (animator == null) animator = GetComponentInChildren<Animator>();

            BaseAwake();

            phase = JumpPhase.Idle;
            cooldownTimer = jumpCooldown;
            attackChain = false;
        }

        /// <summary>
        /// Канонические имена состояний контроллера DinamiteBot. Нужны как запасной
        /// вариант: если в инспекторе опечатка (AttackFlying вместо AttackFly), состояние
        /// всё равно находится по нормализованному имени, иначе боевое полётное
        /// состояние молча не проигрывается и робот выглядит зависшим.
        /// </summary>
        private static readonly string[] CanonicalStates =
        {
            "Idle",
            "Jump",
            "Falling",
            "Landing",
            "ReadyForAttack",
            "Attack",
            "AttackFly"
        };

        private void Update()
        {
            if (exploded) return;

            if (health == null || health.IsDead)
            {
                Detonate();
                return;
            }

            BaseUpdate();

            if (phaseTimer > 0f) phaseTimer -= Time.deltaTime;

            WatchdogPhase();

            switch (phase)
            {
                case JumpPhase.Idle: UpdateIdle(); break;
                case JumpPhase.ReadyToAttack: UpdateReady(); break;
                case JumpPhase.Attack: UpdateAttackWindup(); break;
                case JumpPhase.AttackFlying: UpdateAttackFlight(); break;
                case JumpPhase.Jump: UpdateAir(); break;
                case JumpPhase.Falling: UpdateAir(); break;
                case JumpPhase.Landing: UpdateLanding(); break;
                case JumpPhase.ChasePause: UpdateChasePause(); break;
            }

            UpdateAnimation();
        }

        /// <summary>
        /// Ветка бездействия: робот стоит и доворачивает. Ходить он не умеет вообще,
        /// единственный способ перемещения — прыжки, поэтому на земле горизонтальная
        /// скорость каждый кадр обнуляется.
        /// Стоит цель на средней дистанции — сразу переходим в ветку атаки: из неё
        /// анимация бездействия убрана.
        /// </summary>
        private void UpdateIdle()
        {
            cooldownTimer -= Time.deltaTime;

            if (HasTarget)
            {
                float distance = DistanceToTarget();

                // Цель уже на боевой дистанции — готовимся сразу. Раньше здесь
                // стоял прыжок, и робот начинал штурмовать цель, до которой
                // можно было дойти пешком на месте.
                if (distance <= attackRange)
                {
                    attackChain = true;
                    cooldownTimer = 0f;
                    EnterPhase(JumpPhase.ReadyToAttack, readyDuration);
                    return;
                }

                if (distance <= prepareRange)
                {
                    attackChain = true;
                    cooldownTimer = 0f;

                    // Не прыгаем сразу: короткая остановка даёт перегруппироваться
                    // и решить, нужен ли ещё прыжок вообще.
                    EnterPhase(JumpPhase.ChasePause, chasePauseDuration);
                    return;
                }
            }

            FaceDirection(DesiredDirection(true), groundTurnSpeed, Time.deltaTime);

            if (cooldownTimer <= 0f)
            {
                Launch(false);
                return;
            }

            HoldGround();
        }

        private void UpdateReady()
        {
            if (TargetLost())
            {
                attackChain = false;
                EnterPhase(JumpPhase.Idle, 0f);
                cooldownTimer = jumpCooldown;
                return;
            }

            FaceDirection(DesiredDirection(true), groundTurnSpeed, Time.deltaTime);
            HoldGround();

            if (phaseTimer <= 0f) EnterPhase(JumpPhase.Attack, attackDuration);
        }

        private void UpdateAttackWindup()
        {
            if (TargetLost())
            {
                attackChain = false;
                EnterPhase(JumpPhase.Idle, 0f);
                cooldownTimer = jumpCooldown;
                return;
            }

            FaceDirection(DesiredDirection(true), groundTurnSpeed, Time.deltaTime);
            HoldGround();

            if (phaseTimer <= 0f) Launch(true);
        }

        /// <summary>
        /// Прыгающий робот не теряет цель из-за собственной высоты.
        ///
        /// Базовая проверка считает цель недостижимой, когда робот высоко над ней
        /// и близко: уклон получается круче maxClimbSlope, и проверка падает на
        /// «достать ли». Для камикадзе это неверно — он сам поднимается в прыжке
        /// на несколько метров. Раньше он в полёте ронял цель, причём вместе с
        /// lastSeenTime, из-за чего не мог подобрать её обратно и оставался без
        /// дистанции: атаковать было нечем.
        ///
        /// Поэтому в полёте и во время атаки считаем только расстояние по земле.
        /// </summary>
        protected override bool IsTargetReachable(Health candidate)
        {
            if (base.IsTargetReachable(candidate)) return true;

            bool airborne = !IsGrounded() || attackChain || phase == JumpPhase.AttackFlying;
            if (!airborne) return false;

            return HorizontalDistanceTo(candidate) <= DetectRange;
        }

        private float HorizontalDistanceTo(Health candidate)
        {
            if (candidate == null) return 0f;

            Vector3 delta = candidate.transform.position - transform.position;
            return new Vector2(delta.x, delta.z).magnitude;
        }

        /// <summary>
        /// Цель пропала или отошла слишком далеко. Значит замах отменяется,
        /// иначе робот прыгает в пустоту и зря тратит попытку.
        /// </summary>
        private bool TargetLost()
        {
            if (!HasTarget) return true;

            return DistanceToTarget() > ReadyRange * 1.6f;
        }

        /// <summary>
        /// Боевой прыжок. Направление задано один раз в Launch и в полёте не
        /// меняется: робот не умеет вилять в воздухе. Взрыв происходит при
        /// столкновении с чем угодно — с игроком, со стеной, с полом.
        /// </summary>
        private void UpdateAttackFlight()
        {
            DampAttackRise();
            HoldLockedDirection();
            UpdateAnimationState(attackFlyingState);

            TrackAirborne();

            // Первые мгновения после толчка не считаем касанием: робот ещё
            // стоит на том самом полу, с которого только что оттолкнулся, и
            // проверка на землю сработала бы мгновенно.
            if (Time.time - launchTime < attackFlightMinTime) return;

            if (TouchesTarget() || TouchesAnything())
            {
                Detonate();
                return;
            }

            // Так и не оторвался от земли. Повторно толкаться в воздухе нельзя,
            // поэтому просто возвращаемся к оценке обстановки: робот либо снова
            // сокращает расстояние прыжком, либо уходит в замах.
            if (wasAirborne) return;

            if (Time.time - launchTime < attackFlightStuckTime) return;

            EnterLanding();
        }

        /// <summary>
        /// Гасит вертикальную скорость боевого прыжка. Без этого робот взлетает
        /// на полную высоту, перелетает цель сверху и падает уже за ней — урон
        /// не наносится, а фаза выглядит как зависание в воздухе.
        /// </summary>
        private void DampAttackRise()
        {
            if (body == null) return;

            // Первые мгновения после толчка не гасим, иначе робот не успевает
            // оторваться от земли и остаётся ползать в анимации полёта.
            if (Time.time - launchTime < attackFlightMinTime) return;

            Vector3 velocity = body.linearVelocity;
            if (velocity.y <= 0f) return;

            velocity.y = Mathf.MoveTowards(velocity.y, 0f, attackRiseDamping * Time.deltaTime);
            body.linearVelocity = velocity;
        }

        /// <summary>
        /// Не даёт перелететь цель. Скорость падает пропорционально расстоянию,
        /// поэтому робот подлетает вплотную, а не пролетает мимо на высокой скорости.
        /// </summary>
        private void UpdateAir()
        {
            HoldLockedDirection();

            Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;

            TrackAirborne();

            if (!HasTarget && phase == JumpPhase.Jump && velocity.y <= 0.01f)
                EnterPhase(JumpPhase.Falling, 0f);

            UpdateAnimationState(ResolveAirState(velocity));

            if (hasLeftGround)
            {
                if (IsGrounded()) EnterLanding();
                return;
            }

            // Так и не оторвались от земли: заканчиваем полёт, иначе робот навсегда
            // остаётся в анимации прыжка и ползёт по полу вместо смены анимаций.
            if (Time.time - launchTime >= airStuckTime) EnterLanding();
        }

        /// <summary>
        /// Какую анимацию показывать в фазе полёта. Раньше здесь стояло условие
        /// "HasTarget ||", из-за чего фаза Falling при наличии цели проигрывала
        /// прыжок: робот выглядел так, будто застрял в анимации подъёма.
        /// </summary>
        private string ResolveAirState(Vector3 velocity)
        {
            if (velocity.y > 0.01f) return jumpState;
            if (velocity.y < -0.01f) return fallingState;

            return phase == JumpPhase.Falling ? fallingState : jumpState;
        }

        private void EnterLanding()
        {
            EnterPhase(JumpPhase.Landing, landingDuration);
        }

        /// <summary>
        /// Развилка после приземления.
        /// Ветка атаки: анимации бездействия здесь нет — пока расстояние больше
        /// минимального, робот снова прыгает, сокращая дистанцию; когда дошло до
        /// минимального, уходит в замах и в боевой прыжок.
        /// Ветка бездействия: возвращается в Idle.
        /// </summary>
        private void UpdateLanding()
        {
            HoldGround();

            if (phaseTimer > 0f) return;

            if (attackChain)
            {
                cooldownTimer = 0f;

                if (HasTarget)
                {
                    // Ещё не на минимальной дистанции — но сначала короткая
                    // остановка: игрок мог подойти сам, и тогда нужен замах,
                    // а не ещё один прыжок мимо.
                    if (DistanceToTarget() > attackRange)
                    {
                        EnterPhase(JumpPhase.ChasePause, chasePauseDuration);
                        return;
                    }

                    EnterPhase(JumpPhase.ReadyToAttack, readyDuration);
                    return;
                }

                attackChain = false;
                cooldownTimer = jumpCooldown;
                EnterPhase(JumpPhase.Idle, 0f);
                return;
            }

            cooldownTimer = jumpCooldown;

            if (HasTarget && DistanceToTarget() <= prepareRange)
            {
                attackChain = true;
                EnterPhase(JumpPhase.ChasePause, chasePauseDuration);
                return;
            }

            EnterPhase(JumpPhase.Idle, 0f);
        }

        /// <summary>
        /// Короткая остановка в преследовании.
        ///
        /// Робот не ходит, поэтому после каждого прыжка он оказывается в полёте
        /// «вслепую»: направление зафиксировано на отталкивании, и игрок, зашедший
        /// в зону досягаемости, пока робот летит, оставался без внимания — тот
        /// пролетал мимо и начинал цикл заново.
        ///
        /// Пауза даёт robot'у секунду перегруппироваться: развернуться к цели,
        /// заново посчитать дистанцию и либо начать замах, либо прыгнуть ещё ближе.
        /// Пауза намеренно короткая: она меньше времени прыжка, поэтому робот
        /// в сумме остаётся быстрее игрока, но достаточно длинная, чтобы в неё
        /// успевал зайти человек.
        /// </summary>
        private void UpdateChasePause()
        {
            HoldGround();

            if (TargetLost())
            {
                attackChain = false;
                EnterPhase(JumpPhase.Idle, 0f);
                cooldownTimer = jumpCooldown;
                return;
            }

            FaceDirection(DesiredDirection(true), groundTurnSpeed, Time.deltaTime);

            if (HasTarget && DistanceToTarget() <= attackRange)
            {
                // Игрок сам добрался до робота — замах начинается сразу, не дожидаясь
                // конца паузы.
                attackChain = true;
                cooldownTimer = 0f;
                EnterPhase(JumpPhase.ReadyToAttack, readyDuration);
                return;
            }

            if (phaseTimer > 0f) return;

            // Всё ещё далеко — прыгаем ближе. Пауза перед этим прыжком уже была.
            Launch(false);
        }

        /// <summary>
        /// Отталкивание. Направление выбирается здесь один раз и дальше не меняется.
        /// Прыжок разрешён только с земли: в воздухе робот не отталкивается повторно.
        /// </summary>
        private void Launch(bool attack)
        {
            // Прыгать, не коснувшись земли, робот не может.
            if (!IsGrounded()) return;

            PlanJump(attack, out Vector3 direction, out float speed, out int hops);

            lockedJumpDirection = direction;
            lockedJumpSpeed = speed;

            // Разворот делаем сразу при отталкивании: в полёте доворачивать уже нельзя.
            FaceDirection(lockedJumpDirection, airTurnSpeed, Time.deltaTime);

            if (body != null)
            {
                body.linearVelocity = new Vector3(
                    lockedJumpDirection.x * lockedJumpSpeed,
                    upSpeed,
                    lockedJumpDirection.z * lockedJumpSpeed);
            }

            hasLeftGround = false;
            wasAirborne = false;
            launchTime = Time.time;
            cooldownTimer = jumpCooldown;

            EnterPhase(attack ? JumpPhase.AttackFlying : JumpPhase.Jump, 0f);
        }

        /// <summary>
        /// План прыжка: направление и горизонтальная скорость.
        ///
        /// С цели робот целится не в её текущую точку, а в ту, где цель окажется
        /// к моменту касания земли, и подбирает скорость под дальность полёта.
        /// Без этого фиксированный курс вёлся бы в пустоту: прыжок на 13 м из
        /// цели в 5 м всегда улетал за неё, и робот прыгал туда-сюда, не сокращая
        /// дистанцию.
        ///
        /// Дальность до точки приземления берётся с запасом: на обычном прыжке
        /// преследования робот не долетает до цели, а оставляет approachStandoff,
        /// иначе он приземляется прямо на неё. Боевой прыжок идёт без запаса:
        /// там столкновение с целью и есть взрыв.
        ///
        /// Без цели прыжок идёт в точку патрулирования обычной скоростью.
        /// </summary>
        private void PlanJump(bool attack, out Vector3 direction, out float speed)
        {
            speed = HasTarget ? chaseAirSpeed : patrolAirSpeed;

            if (!HasTarget)
            {
                direction = SafePlanarDirection(DesiredDirection(false), transform.forward);
                return;
            }

            Vector3 start = FrontPosition;

            float upSpeed = attack ? attackJumpUpSpeed : chaseJumpUpSpeed;
            float riseDamping = attack ? attackRiseDamping : 0f;

            Vector3 toward = SafePlanarDirection(Target.transform.position - start, transform.forward);

            // Сколько времени займёт полёт при обычной скорости — от этого
            // зависит, насколько цель успеет уехать с места прыжка.
            PredictLanding(start, toward, speed, upSpeed, riseDamping, out _, out float flightTime);

            if (flightTime < 0.05f)
            {
                direction = toward;
                return;
            }

            Vector3 lead = Vector3.ProjectOnPlane(PredictTargetPosition(flightTime) - start, Vector3.up);

            // Уже стоим вплотную к цели: прыгаем строго вверх. Скорость в
            // сторону тут бессмысленна — робот улетел бы мимо и взорвался в
            // стороне от цели, а не под ней.
            if (lead.magnitude <= verticalHopDistance)
            {
                direction = Vector3.zero;
                speed = 0f;
                return;
            }

            if (lead.sqrMagnitude < 0.0025f)
            {
                direction = toward;
                return;
            }

            direction = lead.normalized;

            // Дальность горизонтального полёта жёстко связана со временем:
            // speed = distance / flightTime, поэтому упреждение превращается
            // ровно в ту дальность, которая приведёт робота к цели.
            //
            // Но на обычном прыжке преследования «привести к цели» нельзя: робот
            // приземлился бы прямо на неё и врезался. Поэтому прыжок ведётся
            // в упреждённую точку за вычетом approachStandoff — цель остаётся
            // в зоне досягаемости, но не вплотную. Боевой прыжок идёт по полной
            // траектории: там столкновение и есть взрыв.
            float required = attack
                ? lead.magnitude
                : Mathf.Max(0f, lead.magnitude - approachStandoff);

            speed = Mathf.Clamp(required / flightTime, minJumpAirSpeed, maxJumpAirSpeed);
        }

        /// <summary>
        /// Прогон баллистики вперёд по тем же правилам, что и настоящий полёт:
        /// та же вертикальная скорость, то же гашение подъёма, та же гравитация.
        /// Возвращает точку касания исходной высоты и время полёта.
        /// Именно эта точка и есть «место, где ждёт цель».
        /// </summary>
        private void PredictLanding(Vector3 start, Vector3 horizontal, float speed, float upSpeed, float riseDamping, out Vector3 landing, out float flightTime)
        {
            const float step = 1f / 60f;

            float gravity = Physics.gravity.y;

            Vector3 position = start;
            Vector3 velocity = new Vector3(horizontal.x * speed, upSpeed, horizontal.z * speed);

            landing = start + horizontal * speed;
            flightTime = 0f;

            for (float t = step; t <= 5f; t += step)
            {
                if (riseDamping > 0f && velocity.y > 0f)
                    velocity.y = Mathf.MoveTowards(velocity.y, 0f, riseDamping * step);

                velocity.y += gravity * step;

                position += velocity * step;
                flightTime = t;

                if (velocity.y < 0f && position.y <= start.y)
                {
                    landing = new Vector3(position.x, start.y, position.z);
                    return;
                }
            }

            landing = new Vector3(position.x, start.y, position.z);
        }

        /// <summary>
        /// Где цель окажется через заданное время. Стреляем только в будущую точку:
        /// по текущей позиции движущаяся цель всегда уходит из-под траектории.
        /// </summary>
        private Vector3 PredictTargetPosition(float time)
        {
            if (!HasTarget || Target == null) return transform.position + transform.forward;

            return Target.transform.position + TargetVelocity() * time;
        }

        /// <summary>
        /// Текущая скорость цели. Игрок едет на CharacterController, машина — на
        /// Rigidbody, поэтому проверяем оба, и на самой цели, и на её детях.
        /// </summary>
        private Vector3 TargetVelocity()
        {
            if (!HasTarget || Target == null) return Vector3.zero;

            GameObject targetObject = Target.gameObject;

            CharacterController controller = targetObject.GetComponent<CharacterController>();
            if (controller != null) return controller.velocity;

            Rigidbody body = targetObject.GetComponent<Rigidbody>();
            if (body != null) return body.linearVelocity;

            controller = Target.GetComponentInChildren<CharacterController>();
            if (controller != null) return controller.velocity;

            body = Target.GetComponentInChildren<Rigidbody>();
            if (body != null) return body.linearVelocity;

            return Vector3.zero;
        }

        /// <summary>Направление в плоскости, с безопасным запасным вариантом, если цель ровно над роботом.</summary>
        private static Vector3 SafePlanarDirection(Vector3 raw, Vector3 fallback)
        {
            Vector3 flat = Vector3.ProjectOnPlane(raw, Vector3.up);

            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.ProjectOnPlane(fallback, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;

            return flat.normalized;
        }

        /// <summary>
        /// Держит горизонтальное движение в полёте строго по направлению и
        /// скорости, зафиксированным в момент прыжка. Менять курс в воздухе
        /// робот не может, иначе упреждение в точке приземления теряет смысл.
        /// </summary>
        private void HoldLockedDirection()
        {
            if (body == null) return;

            // Нулевое направление — не ошибка, а прыжок строго вверх: тогда
            // горизонтальная скорость должна быть нулевой.
            Vector3 velocity = body.linearVelocity;
            velocity.x = lockedJumpDirection.x * lockedJumpSpeed;
            velocity.z = lockedJumpDirection.z * lockedJumpSpeed;
            body.linearVelocity = velocity;
        }

        private void EnterPhase(JumpPhase next, float duration)
        {
            phase = next;
            phaseTimer = duration;
            phaseStartedAt = Time.time;
        }

        /// <summary>
        /// Аварийный выход из фазы. Ни одна ветка не должна длиться дольше
        /// maxPhaseDuration, но если физика или триггеры ведут себя неожиданно,
        /// робот обязан вернуться в Idle, а не замереть в одной анимации.
        /// </summary>
        private void WatchdogPhase()
        {
            if (maxPhaseDuration <= 0f) return;
            if (Time.time - phaseStartedAt <= maxPhaseDuration) return;

            JumpPhase stuck = phase;

            phase = JumpPhase.Idle;
            phaseTimer = 0f;
            phaseStartedAt = Time.time;
            hasLeftGround = false;
            wasAirborne = false;
            cooldownTimer = jumpCooldown;
            attackChain = false;

            if (body != null) body.linearVelocity = Vector3.zero;
            if (animator != null) animator.speed = 1f;

            Debug.LogWarning(
                $"[Kamikaze] Фаза {stuck} длилась дольше {maxPhaseDuration:0.##} с — вернулся в Idle.",
                this);
        }

        private Vector3 DesiredDirection(bool avoidObstacles)
        {
            Vector3 raw;

            if (HasTarget)
                raw = Vector3.ProjectOnPlane(Target.transform.position - FrontPosition, Vector3.up);
            else
                raw = Vector3.ProjectOnPlane(ResolveDestination() - transform.position, Vector3.up);

            if (raw.sqrMagnitude < 0.0001f) return Vector3.zero;
            if (raw.sqrMagnitude < 0.01f) return raw;

            if (ignoreObstacles || !avoidObstacles) return raw.normalized;
            return ComputeAvoidance(raw);
        }

        private float ReadyRange => Mathf.Max(attackRange, prepareRange);

        private float DistanceToTarget()
        {
            if (!HasTarget) return float.MaxValue;
            return Vector3.Distance(Target.transform.position, transform.position);
        }

        private void TrackAirborne()
        {
            if (IsGrounded()) return;

            hasLeftGround = true;
            wasAirborne = true;
        }

        /// <summary>
        /// Проверка земли — строго геометрическая, по расстоянию подошв до пола.
        ///
        /// Раньше луч бил на groundProbe + половина высоты капсулы, то есть
        /// на 1.2 м ниже подошв: робот, висящий в метре над полом, считался
        /// стоящим на земле. Вторая проверка по вертикальной скорости была
        /// хуже — на вершине прыжка скорость тоже около нуля, поэтому робот в
        /// воздухе проходил её как «стоящий» и отталкивался повторно.
        ///
        /// Теперь решает только луч из-под подошв, а скорость на землю не влияет.
        /// </summary>
        private bool IsGrounded()
        {
            if (body == null) return true;

            Bounds bounds = bodyCollider != null
                ? bodyCollider.bounds
                : new Bounds(transform.position, new Vector3(0.7f, 1.8f, 0.7f));

            // Луч бьётся из-под подошв, а не из центра робота: центр капсулы
            // висит выше пола, и луч из центра просто не доставал до земли.
            // Старт выше подошв наполовину зонда — иначе при небольшом
            // проникновении в пол луч шёл бы уже внутри него и ничего не находил.
            float half = groundProbe * 0.5f;

            Vector3 origin = new Vector3(
                bounds.center.x,
                bounds.min.y + half,
                bounds.center.z);

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundProbe, kamikazeGroundMask, QueryTriggerInteraction.Ignore)
                && hit.collider != null && !IsSelf(hit.collider))
                return true;

            // Запасной зонд: спасает на склонах и у самой кромки платформы, где
            // подошвы стоят на краю и точный луч промахивается.
            float slack = groundProbe + groundProbeSlack;

            return Physics.Raycast(origin, Vector3.down, out RaycastHit loose, slack, kamikazeGroundMask, QueryTriggerInteraction.Ignore)
                   && loose.collider != null && !IsSelf(loose.collider);
        }

        /// <summary>
        /// Удержание на земле. Обнуляет горизонтальную скорость каждый кадр:
        /// робот не ходит и не ползает, перемещение возможно только прыжком.
        /// </summary>
        private void HoldGround()
        {
            if (body == null) return;

            Vector3 velocity = body.linearVelocity;
            velocity.x = 0f;
            velocity.z = 0f;

            body.linearVelocity = velocity;
        }

        /// <summary>
        /// Касание чем угодно: игроком, стеной, полом, мусором. Пол из проверки
        /// НЕ исключается — по правилу робот взрывается при столкновении с любым
        /// коллайдером. От мгновенного взрыва на собственном полу защищает
        /// пауза attackFlightMinTime в начале боевого полёта.
        /// </summary>
        private bool TouchesAnything()
        {
            Collider[] hits = Physics.OverlapSphere(
                transform.position + Vector3.up * contactHeight,
                contactRadius,
                contactMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null) continue;
                if (IsSelf(hit)) continue;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Проверка попадания по самой цели. Сфера касания может не достать до
        /// игрока, если он выше или сбоку, поэтому дистанция до цели проверяется отдельно.
        /// </summary>
        private bool TouchesTarget()
        {
            if (!HasTarget) return false;

            float reach = contactRadius + targetTouchBonus;
            return DistanceToTarget() <= reach;
        }

        private void UpdateAnimation()
        {
            switch (phase)
            {
                case JumpPhase.Idle: UpdateAnimationState(idleState); break;
                case JumpPhase.ReadyToAttack: UpdateAnimationState(readyState); break;
                case JumpPhase.Attack: UpdateAnimationState(attackState); break;
                case JumpPhase.Landing: UpdateAnimationState(landingState); break;

                // Пауза в преследовании показывается наземным состоянием:
                // анимация бездействия в ветке атаки не используется.
                case JumpPhase.ChasePause: UpdateAnimationState(landingState); break;
            }
        }

        private void UpdateAnimationState(string state)
        {
            if (animator == null || string.IsNullOrEmpty(state)) return;

            if (currentState != state)
            {
                int hash = ResolveStateHash(state);

                if (hash != 0)
                {
                    if (crossFade <= 0.001f) animator.Play(hash, 0, 0f);
                    else animator.CrossFadeInFixedTime(hash, crossFade, 0, 0f);
                }
                else
                {
                    WarnMissingState(state);
                }

                currentState = state;
            }

            if (HasParameter("VerticalSpeed"))
                animator.SetFloat("VerticalSpeed", body != null ? body.linearVelocity.y : 0f);
        }

        /// <summary>
        /// Ищет состояние по точному имени, а если не нашёл — по близкому названию.
        /// Нужно из-за опечаток вида AttackFlying вместо AttackFly: раньше такое
        /// имя молча ломало анимацию боевого полёта, робот зависал в позе простоя.
        /// </summary>
        private int ResolveStateHash(string state)
        {
            int hash = FindStateHash(state);
            if (hash != 0) return hash;

            string wanted = NormalizeStateName(state);

            for (int i = 0; i < CanonicalStates.Length; i++)
            {
                string candidate = CanonicalStates[i];
                if (NormalizeStateName(candidate) != wanted) continue;

                int candidateHash = FindStateHash(candidate);
                if (candidateHash == 0) continue;

                Debug.LogWarning(
                    $"[Kamikaze] Вместо состояния '{state}' используется '{candidate}'. " +
                    "Исправь имя в инспекторе RobotKamikaze.",
                    this);

                return candidateHash;
            }

            return 0;
        }

        /// <summary>
        /// Animator.HasState принимает только ПОЛНЫЙ путь состояния, то есть
        /// "Base Layer.AttackFly", а не короткое имя "AttackFly". С коротким хешем
        /// проверка всегда возвращала false, поэтому Play ни разу не вызывался и робот
        /// навсегда оставался в состоянии по умолчанию — выглядел как застрявший.
        /// </summary>
        private int FindStateHash(string state)
        {
            if (animator == null || !animator.isActiveAndEnabled) return 0;
            if (animator.runtimeAnimatorController == null) return 0;
            if (string.IsNullOrEmpty(state)) return 0;

            for (int i = 0; i < animator.layerCount; i++)
            {
                int full = Animator.StringToHash(animator.GetLayerName(i) + "." + state);
                if (animator.HasState(i, full)) return full;
            }

            // Контроллеры с нестандартной структурой могут ответить и на короткий хеш.
            int shortHash = Animator.StringToHash(state);
            for (int i = 0; i < animator.layerCount; i++)
            {
                if (animator.HasState(i, shortHash)) return shortHash;
            }

            return 0;
        }

        private static string NormalizeStateName(string value)
        {
            string result = value.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();

            if (result.EndsWith("ing")) result = result.Substring(0, result.Length - 3);
            if (result.EndsWith("ed")) result = result.Substring(0, result.Length - 2);

            return result;
        }

        private void WarnMissingState(string state)
        {
            if (warnedStates.Add(state) == false) return;

            string controller = animator != null && animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.name
                : "нет";

            Debug.LogWarning(
                $"[Kamikaze] В контроллере '{controller}' нет состояния '{state}'. " +
                "Проверь имя в инспекторе RobotKamikaze.",
                this);
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

        private void Detonate()
        {
            if (exploded) return;
            exploded = true;

            if (animator != null && HasParameter("Dead")) animator.SetTrigger("Dead");

            DropLoot();

            SpawnExplosionEffect();

            Destroy(gameObject);
        }

        private void SpawnExplosionEffect()
        {
            if (explosionEffectPrefab == null) return;

            GameObject source = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);

            ExplosionEffect effect = source != null ? source.GetComponent<ExplosionEffect>() : null;
            if (effect != null) effect.Ignore(gameObject);
        }

        private void DropLoot()
        {
            if (loot == null || loot.Length == 0) return;

            for (int i = 0; i < loot.Length; i++)
            {
                LootEntry entry = loot[i];
                if (entry == null || entry.prefab == null) continue;
                if (Random.value > entry.chance) continue;

                int min = Mathf.Min(entry.countMin, entry.countMax);
                int max = Mathf.Max(entry.countMin, entry.countMax);
                int amount = Random.Range(min, max + 1);

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
