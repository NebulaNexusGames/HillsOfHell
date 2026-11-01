using CarGame.Effects;
using CarGame.Player;
using CarGame.Vehicle;
using UnityEngine;

namespace CarGame.Combat
{
    public abstract class EnemyBase : MonoBehaviour
    {
[Header("Зрение")]
        [Tooltip("С какой дистанции робот вообще замечает цель, м. Дальше он ничего не делает и стоит на патруле")]
        [SerializeField] private float detectRange = 28f;
        [Tooltip("Жёсткий потолок зрения. Даже если detectRange в сцене забыли поправить, робот не увидит цель дальше этого расстояния")]
        [SerializeField] private float maxDetectRange = 30f;
        [Tooltip("Проверять ли, что цель видно напрямую. Выключи, если нужно, чтобы робот реагировал сквозь стены")]
        [SerializeField] private bool requireLineOfSight = true;
        [Tooltip("Слои, которые заслоняют обзор")]
        [SerializeField] private LayerMask sightBlockers = ~0;
        [Tooltip("Сколько секунд не нужно видеть цель, чтобы забыть её")]
        [SerializeField] private float loseTargetMemory = 4f;
        [Tooltip("Помнить ли цель после потери прямой видимости (спряталась за объектом) и преследовать последнюю виденную позицию. Дрон: да — иначе он «забывает» игрока из-за любой преграды")]
        [SerializeField] private bool rememberTargetWhenLost = true;
        [Tooltip("Требовать для захвата и удержания цели сектор обзора перед роботом. Мечник: погоня только если реально увидел. 0 — без ограничения угла")]
        [SerializeField] private float sightAngleDegrees = 120f;
        [Tooltip("Радиус зоны вокруг робота, внутри которой цель замечается с любой стороны (даже за спиной). 0 — отключено")]
        [SerializeField] private float nearDetectionRadius = 8f;
        [Tooltip("Проверять, дотягивается ли робот до цели по высоте и расстоянию. Выключи — будет преследовать любую цель в зоне видимости")]
        [SerializeField] private bool requireReachableTarget = true;
        [Tooltip("Высота цели над её точкой отсчёта, м. Обычно центр груди или капота")]
        [SerializeField] private float targetHeightOffset = 1f;
        [Tooltip("Максимальная разница высот между роботом и целью, м. Выше — цель считается недостижимой и робот прекращает преследование")]
        [SerializeField] private float maxTargetHeight = 6f;
        [Tooltip("На сколько метров робот может отойти от цели по горизонтали, считая это досягаемым")]
        [SerializeField] private float attackReach = 3.5f;
        [Tooltip("Максимальный уклон, который робот способен забраться, градусы. Круче — считается стеной")]
        [SerializeField] private float maxClimbSlope = 40f;
        [Tooltip("Допуск по высоте, в пределах которого цель считается на своём уровне, м")]
        [SerializeField] private float verticalReach = 2.2f;

        [Header("Патруль")]
        [Tooltip("Точки, по которым ходит робот, пока никого не видит. Если пусто — патрулирует вокруг места спавна")]
        [SerializeField] private Transform[] patrolPoints = new Transform[0];
        [Tooltip("На каком расстоянии считать точку патруля достигнутой, м")]
        [SerializeField] private float patrolPointTolerance = 1.6f;
[Tooltip("Радиус блуждания вокруг спавна, если точки патруля не заданы, м")]
        [SerializeField] private float patrolRadius = 10f;
        [Tooltip("Скорость патрулирования, м/с")]
        [SerializeField] private float patrolSpeed = 1.9f;
        [Tooltip("Стоять на месте после достижения точки патруля, с. 0 — не останавливаться")]
        [SerializeField] private float patrolWaitTime = 1f;
        [Tooltip("Стоять на точке ровно это время, с. 0 — использовать patrolWaitTime")]
        [SerializeField] private float restDuration = 1f;
        [Tooltip("Сколько шагов пройти на патруле, прежде чем остановиться на паузу и сменить направление (режим блуждания без точек). 0 — считать дистанцию вместо шагов")]
        [SerializeField] private int patrolStepsBeforeStop = 8;
        [Tooltip("Длина одного шага для подсчёта, м")]
        [SerializeField] private float patrolStepLength = 0.7f;
        [Tooltip("Если patrolStepsBeforeStop = 0: сколько метров пройти, прежде чем остановиться и сменить направление")]
        [SerializeField] private float patrolDistanceBeforeStop = 6f;
        [Tooltip("Сколько секунд робот стоит на патруле, прежде чем сменить маршрут. Короткая плановая стоянка: робот в основном ходит, а не стоит")]
        [SerializeField] private float patrolStopDuration = 1f;

        [Header("Обход препятствий")]
        [Tooltip("Насколько далеко робот проверяет путь перед собой, м")]
        [SerializeField] private float obstacleProbe = 2.4f;
        [Tooltip("Слои, которые считаются препятствиями для обхода")]
        [SerializeField] private LayerMask obstacleMask = ~0;
        [Tooltip("Обходить ли низкий мусор под ногами, который цепляет корпус и тормозит робота")]
        [SerializeField] private bool avoidLowDebris = true;
        [Tooltip("Шаг перебора направлений при обходе, градусы. Чем меньше — тем плотнее сканирование")]
        [SerializeField] private float avoidAngleStep = 20f;
        [Tooltip("Насколько шире самого робота проверять проход, м. Больше запас — меньше застреваний в углу")]
        [SerializeField] private float avoidClearance = 0.12f;
        [Tooltip("Доля от высоты робота, на которой проверяется путь. 0.5 — середина корпуса")]
        [SerializeField] private float avoidHeightFactor = 0.55f;
        [Tooltip("Высота проверки мусора под ногами, м")]
        [SerializeField] private float debrisHeight = 0.3f;

        [Header("Поверхность под ногами")]
        [Tooltip("Слои пола. По ним определяется, ровная поверхность под роботом или подъём")]
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("Насколько далеко вперёд проверять уклон, м")]
        [SerializeField] private float slopeProbeDistance = 1.6f;
        [Tooltip("Высота, с которой опускается зонд на землю, м")]
        [SerializeField] private float slopeProbeHeight = 2.5f;
        [Tooltip("Перепад высот, который робот переступает автоматически: бордюры, ступеньки, мелкие неровности, м")]
        [SerializeField] private float stepUpHeight = 0.35f;

        [Header("Застревание")]
        [Tooltip("Скорость, ниже которой робот считается буксующим на месте, м/с")]
        [SerializeField] private float stuckSpeed = 0.4f;
        [Tooltip("Сколько секунд робот должен буксовать, прежде чем включить принудительный объезд")]
        [SerializeField] private float stuckDelay = 0.7f;
        [Tooltip("Сколько секунд держать объезд в выбранную сторону, с")]
        [SerializeField] private float detourDuration = 1.3f;

        protected Health health;
        protected PlayerController player;

        /// <summary>Звуковой движок робота: создаётся автоматически на Awake.
        /// Клипы и громкости лежат НЕ здесь, а в полях каждого конкретного
        /// бота (RobotKamikaze / RobotDrone / RobotSwordsman) — они передаются
        /// в Voice через ApplySoundClips().</summary>
        protected RobotVoice Voice { get; private set; }

        /// <summary>Наследники вызывают это из своего Awake: их инспекторные
        /// поля со звуками настраивают общий звуковой компонент.</summary>
        protected void ApplySoundClips(AudioClip stepClip, AudioClip jumpClip,
            AudioClip flightLoopClip, AudioClip landClip, AudioClip alertClip, AudioClip shotClip,
            float stepVolume = -1f, float jumpVolume = -1f, float landVolume = -1f,
            float alertVolume = -1f, float flightLoopVolume = -1f, float shotVolume = -1f)
        {
            if (Voice == null) return;
            Voice.AssignClips(stepClip, jumpClip, flightLoopClip, landClip, alertClip, shotClip);
            Voice.AssignVolumes(stepVolume, jumpVolume, landVolume, alertVolume, flightLoopVolume, shotVolume);
        }

        private Transform currentPatrolPoint;
        private float pauseUntil;
        private float patrolWaitUntil => pauseUntil;
        // До этого момента робот обязан стоять (замах атаки). Ставится через
        // LockMovement(), читается в CanMoveNow().
        private float attackLockUntil;
        private float lastSeenTime = -100f;
        // Последняя позиция цели, зафиксированная в момент захвата/видимости.
        // Используется при «памяти цели»: потеряв игрока из виду (за стеной),
        // робот бежит сюда, а не стоит на месте.
        private Vector3 lastKnownTargetPosition;
        private bool hasLastKnownTargetPosition;
        // Флаг «бежим к последней виденной точке» (цель помнится, но не видна).
        // Ставится в BaseUpdate, читается в ResolveDestination.
        private bool chaseMemoryDestinationActive;
        private float repathTime;
        private Vector3 wanderTarget;
        private Vector3 spawnPoint;

        private Vector3 halfExtents = new Vector3(0.35f, 0.9f, 0.35f);
        private float bodyHalfHeight = 1.8f;
        private Rigidbody selfBody;
        // Счётчик «пройденного на патруле» для остановки через N шагов и смены
        // направления. Работает в режиме блуждания (без точек патруля).
        private float patrolWalked;
        private Vector3 lastPatrolPosition;
        private bool detourActive;
        private float detourTimer;
        private Vector3 detourDirection;
        private float stuckTimer;
        private Vector3 lastSweepNormal;
        private bool reverseFlipped;
        protected bool climbing;
        private RaycastHit lastSweepHit;
        private Collider overlapHit;
        private static readonly Collider[] sweepBuffer = new Collider[16];
        private static readonly RaycastHit[] groundBuffer = new RaycastHit[16];
        private static readonly RaycastHit[] sightBuffer = new RaycastHit[16];

        public Health Health => health;
        public float DetectRange => Mathf.Clamp(detectRange, 0.5f, Mathf.Max(0.5f, maxDetectRange)) * BloodMoonVisionMultiplier;
        public Vector3 FrontPosition => transform.position + FrontOffset;
        public Vector3 FrontForward => transform.forward;
        protected Vector3 FrontOffset { get; private set; }
        protected float PatrolSpeed => patrolSpeed;
        /// <summary>
        /// Держать ли цель после потери видимости (loseTargetMemory секунд).
        /// Мечник переопределяет в false: погоня только пока реально видит.
        /// </summary>
        protected virtual bool HasSightMemory => true;
        protected float PatrolRadius => patrolRadius;
        protected float PatrolWaitTime => patrolWaitTime;
        protected float PatrolPointTolerance => patrolPointTolerance;
        protected Vector3 SpawnPoint => spawnPoint;
        public Health Target { get; protected set; }
        public bool HasTarget => Target != null && !Target.IsDead;

        /// <summary>Последняя виденная позиция цели (для преследования «по памяти»).
        /// Актуальна, пока цель не сброшена после истечения памяти.</summary>
        protected Vector3 LastKnownTargetPosition => lastKnownTargetPosition;

        /// <summary>Есть ли зафиксированная последняя позиция цели.</summary>
        protected bool HasLastKnownTargetPosition => hasLastKnownTargetPosition;

        /// <summary>Сколько секунд робот помнит цель после потери видимости.
        /// По умолчанию — сериализованный loseTargetMemory; наследник может
        /// переопределить (мечник: 10 секунд по ТЗ).</summary>
        protected virtual float LoseTargetMemoryOverride => -1f;

        /// <summary>Помнить ли цель после потери прямой видимости (игрок
        /// спрятался за объектом) и бежать к последней виденной позиции.
        /// Выключено — цель забывается мгновенно.</summary>
        protected bool RemembersTargetWhenLost => rememberTargetWhenLost;

        /// <summary>Преследование «по памяти»: есть активная цель, она прямо
        /// сейчас не видна, но память о ней ещё жива и её позиция известна.
        /// Наследники используют это для облёта препятствий сменой высоты.</summary>
        protected bool ChasingByMemory =>
            HasTarget && rememberTargetWhenLost && hasLastKnownTargetPosition && !IsTargetVisible(Target);

        /// <summary>Сбрасывать ли цель, если проверка досягаемости (высота/уклон)
        /// провалилась. Наследник может позволить «пробовать достать» сквозь
        /// препятствие, чтобы робот не забывал игрока из-за преграды на пути.</summary>
        protected virtual bool DropTargetWhenUnreachable => true;

        /// <summary>Разрешена ли прямая линия до цели без проверки сектора обзора
        /// (используется дочерними классами для движения к последней точке памяти).</summary>
        protected bool HasClearLineOfSightToTarget()
        {
            return Target != null && HasLineOfSight(Target);
        }

        /// <summary>Принудительный объезд препятствия при преследовании: включает
        /// detour даже если UpdateStuckCheck не подтвердил физический контакт.</summary>
        protected void StartForcedDetour()
        {
            StartDetour();
        }

        protected virtual void BaseAwake()
        {
            spawnPoint = transform.position;
            selfBody = GetComponent<Rigidbody>();
            ReadOwnSize();
            PickNextPatrolPoint();

            // Звуки робота вешаются автоматически на ЛЮБОГО EnemyBase: сигнал
            // обнаружения цели звучит в базовом Update(), а шаги/прыжки/
            // приземления дергают Voice из наследников. Клипы — из полей
            // инспектора (stepClip/jumpClip/...), пусто — процедурные звуки.
            if (Voice == null) Voice = GetComponent<RobotVoice>();
            if (Voice == null) Voice = gameObject.AddComponent<RobotVoice>();
        }

        private void ReadOwnSize()
        {
            Collider own = GetComponent<Collider>();
            if (own == null)
            {
                halfExtents = new Vector3(0.35f, 0.9f, 0.35f) + Vector3.one * avoidClearance;
                bodyHalfHeight = 0.9f;
                return;
            }

            Vector3 scale = Vector3.one;
            float radius = 0.35f;
            float height = 1.8f;
            Vector3 center = Vector3.zero;
            bool resolved = false;

            if (own is CapsuleCollider capsule)
            {
                scale = capsule.transform.lossyScale;
                radius = capsule.radius * Mathf.Abs(scale.x);
                height = capsule.height * Mathf.Abs(scale.y);
                center = Vector3.Scale(capsule.center, scale);
                resolved = true;
            }
            else if (own is BoxCollider box)
            {
                scale = box.transform.lossyScale;
                radius = 0.5f * (Mathf.Abs(box.size.x * scale.x) + Mathf.Abs(box.size.z * scale.z));
                height = Mathf.Abs(box.size.y * scale.y);
                center = Vector3.Scale(box.center, scale);
                resolved = true;
            }
            else if (own is SphereCollider sphere)
            {
                scale = sphere.transform.lossyScale;
                radius = Mathf.Max(Mathf.Abs(sphere.radius * scale.x), Mathf.Abs(sphere.radius * scale.z));
                height = radius * 2f;
                center = Vector3.Scale(sphere.center, scale);
                resolved = true;
            }

            if (!resolved)
            {
                Bounds fallback = own.bounds;
                float sy = Mathf.Max(0.1f, Mathf.Abs(fallback.size.y) * 0.5f);

                radius = Mathf.Max(0.05f, Mathf.Max(Mathf.Abs(fallback.size.x), Mathf.Abs(fallback.size.z)) * 0.4f);
                height = sy * 2f;
                center = Vector3.zero;
            }

            radius = Mathf.Max(0.05f, radius);
            height = Mathf.Max(radius * 2f, height);

            bodyHalfHeight = height * 0.5f;
            halfExtents = new Vector3(radius, bodyHalfHeight, radius) + Vector3.one * avoidClearance;
            FrontOffset = center;
        }

        protected void FaceDirection(Vector3 direction, float turnSpeed, float deltaTime)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(flat.normalized, Vector3.up),
                turnSpeed * deltaTime);
        }

        protected virtual void BaseUpdate()
        {
            climbing = false;
            UpdateTarget();

            // Преследование «по памяти»: цель ещё активна (loseTargetMemory не
            // истёк), но прямо сейчас не видна. Идём к последней виденной
            // позиции — иначе робот упирался бы в стену/препятствие и стоял,
            // хотя цель «помнится».
            if (HasTarget && hasLastKnownTargetPosition && !IsTargetVisible(Target))
            {
                chaseMemoryDestinationActive = true;
                return;
            }

            chaseMemoryDestinationActive = false;
        }

        private void UpdateTarget()
        {
            if (player == null) player = FindPlayer();

            // Сколько секунд робот помнит цель после потери видимости.
            // Наследник может переопределить (мечник — 10 секунд по ТЗ).
            // Флаг rememberTargetWhenLost = false выключает память целиком.
            float memoryTime = !rememberTargetWhenLost
                ? 0f
                : (LoseTargetMemoryOverride > 0f ? LoseTargetMemoryOverride : loseTargetMemory);

            // Пока цель есть, каждый кадр проверяем, видно ли её. Так робот не
            // уходит в стену, преследуя позицию, которую потерял из виду: как
            // только зрение пропало и память истекла — цель сбрасывается.
            if (HasTarget)
            {
                // Мёртвый или уничтоженный объект цели — сброс мгновенно,
                // иначе HasTarget ещё долго даёт true и робот «бежит» к нулю.
                if (Target == null || Target.Equals(null) || Target.IsDead)
                {
                    Target = null;
                    detourActive = false;
                    BeginPause(0.8f);
                    return;
                }

                bool visible = IsTargetVisible(Target);
                if (visible)
                {
                    lastSeenTime = Time.time;
                    // Свежая видимая позиция — запоминаем её для погони «по памяти».
                    lastKnownTargetPosition = Target.transform.position;
                    hasLastKnownTargetPosition = true;
                }
                else if (Time.time - lastSeenTime > memoryTime)
                {
                    // Память истекла: цель забыта, возврат к патрулю.
                    Target = null;
                    detourActive = false;

                    // Цель потеряли не «на ровном месте»: робот реально шёл к ней.
                    // После сброса цели он обязан вернуться в нормальный цикл
                    // патруля, а не залипнуть в позе преследования: короткий
                    // отдых на месте, новый точку маршрута берёт ResolvePatrolDestination.
                    BeginPause(0.8f);
                    return;
                }
                else if (!IsTargetReachable(Target) && DropTargetWhenUnreachable)
                {
                    // Цель стала геометрически недосягаемой (например игрок на
                    // крыше) — забываем сразу. Мечник этот сброс отключил: упор
                    // в препятствие не должен быть причиной «забыть» игрока.
                    Target = null;
                    detourActive = false;
                    BeginPause(0.8f);
                    return;
                }
            }

            if (!HasTarget) TryAcquireTarget();
        }

        private bool TryAcquireTarget()
        {
            if (player == null) player = FindPlayer();
            if (player == null) return false;

            Health onFoot = player.Health;
            Health vehicle = player.CurrentVehicle != null ? player.CurrentVehicle.Health : null;

            Health best = null;
            float bestDistance = float.MaxValue;

            // Все роботы считают целью не только игрока: если игрок в машине
            // (или его рядом нет, а машина есть) — целью становится машина,
            // и боты damages её. Пешего игрока тоже никто не отменял: из двух
            // доступных целей берётся та, что ближе.
            TryConsiderTarget(onFoot, ref best, ref bestDistance);
            TryConsiderTarget(vehicle, ref best, ref bestDistance);

            if (best == null) return false;

            lastSeenTime = Time.time;
            Target = best;
            return true;
        }

        /// <summary>
        /// Проверяет кандидата на роль цели (игрок или машина) и, если он
        /// проходит все проверки и ближе текущего лучшего, делает его целью.
        /// </summary>
        private void TryConsiderTarget(Health candidate, ref Health best, ref float bestDistance)
        {
            if (candidate == null || candidate.Equals(null) || candidate.IsDead) return;

            Vector3 toTarget = candidate.transform.position - transform.position;
            if (toTarget.magnitude > DetectRange) return;
            if (toTarget.magnitude >= bestDistance) return;
            if (!IsTargetReachable(candidate)) return;

            // Захват цели — только если она в секторе обзора перед роботом.
            if (!IsWithinSightCone(candidate)) return;

            if (requireLineOfSight && !HasLineOfSight(candidate)) return;

            best = candidate;
            bestDistance = toTarget.magnitude;
        }

        protected virtual bool IsTargetReachable(Health candidate)
        {
            if (!requireReachableTarget || candidate == null) return true;

            Vector3 targetCenter = candidate.transform.position + Vector3.up * targetHeightOffset;
            Vector3 selfCenter = transform.position + Vector3.up * targetHeightOffset;
            Vector3 toTarget = targetCenter - selfCenter;

            float height = Mathf.Abs(toTarget.y);
            float flat = new Vector2(toTarget.x, toTarget.z).magnitude;

            if (height > maxTargetHeight) return false;
            if (height <= verticalReach) return true;

            float slopeLimit = maxClimbSlope;
            if (slopeLimit <= 0.01f) return flat <= attackReach;

            float slope = Mathf.Atan2(height, Mathf.Max(0.05f, flat)) * Mathf.Rad2Deg;
            if (slope <= slopeLimit) return true;

            return flat <= attackReach;
        }

        /// <summary>
        /// Видна ли цель прямо сейчас: в пределах дальности зрения и без преград.
        /// Проверяется каждый кадр, пока цель живёт, чтобы робот не шёл сквозь
        /// стену к цели, которую давно потерял из виду.
        /// </summary>
        protected bool IsTargetVisible(Health candidate)
        {
            if (candidate == null || candidate.IsDead) return false;

            Vector3 flat = Vector3.ProjectOnPlane(candidate.transform.position - transform.position, Vector3.up);
            if (flat.magnitude > DetectRange) return false;

            // «Увидел» значит и в секторе обзора: цель за спиной не считается видимой.
            if (!IsWithinSightCone(candidate)) return false;

            if (requireLineOfSight && !HasLineOfSight(candidate)) return false;

            return true;
        }

        /// <summary>
        /// Находится ли цель в горизонтальном секторе обзора перед роботом
        /// (sightAngleDegrees — полный угол). 0 или больше 179 — без ограничения.
        /// </summary>
        protected virtual bool IsWithinSightCone(Health candidate)
        {
            if (candidate == null) return false;

            // «Зона видимости» робота: цель внутри этого радиуса вокруг него
            // замечается с любой стороны, даже за спиной. Вне зоны работает
            // обычный сектор обзора.
            if (nearDetectionRadius > 0f)
            {
                Vector3 toCandidate = candidate.transform.position - transform.position;
                if (toCandidate.sqrMagnitude <= nearDetectionRadius * nearDetectionRadius) return true;
            }

            if (sightAngleDegrees <= 0f || sightAngleDegrees >= 179f) return true;

            Vector3 toTarget = candidate.transform.position - transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(toTarget, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) return true;

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) return true;

            float angle = Vector3.Angle(forward.normalized, flat.normalized);
            return angle <= sightAngleDegrees * 0.5f;
        }

        protected bool HasLineOfSight(Health target)
        {
            if (target == null) return false;

            Vector3 origin = FrontPosition + Vector3.up * 1.2f;
            Vector3 destination = target.transform.position + Vector3.up * 0.9f;
            Vector3 direction = destination - origin;
            float distance = direction.magnitude;
            if (distance < 0.05f) return true;
            direction /= distance;

            int count = Physics.RaycastNonAlloc(origin, direction, sightBuffer, distance, sightBlockers, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            Collider nearestCollider = null;

            for (int i = 0; i < count; i++)
            {
                Collider candidate = sightBuffer[i].collider;
                if (candidate == null || IsSelf(candidate)) continue;
                if (sightBuffer[i].distance >= nearest) continue;

                nearest = sightBuffer[i].distance;
                nearestCollider = candidate;
            }

            if (nearestCollider == null) return true;
            return IsColliderOf(target, nearestCollider);
        }

        private static bool IsColliderOf(Health target, Collider candidate)
        {
            if (target == null || candidate == null) return false;

            Transform hit = candidate.transform;
            return hit == target.transform || hit.IsChildOf(target.transform);
        }

        protected Vector3 ResolveDestination()
        {
            if (HasTarget && Target != null && !Target.Equals(null))
                return Target.transform.position;
            return ResolvePatrolDestination();
        }

        /// <summary>
        /// Разрешено ли роботу сейчас двигаться. Возвращает false в паузе на
        /// точке патруля — раньше destination в этот момент указывал на текущую
        /// позицию, но скорость не обнулялась, и робот продолжал «идти» на месте.
        /// С целью (режим преследования) движение запрещено только во время
        /// удара (attackLockUntil) — см. CanMoveNow().
        /// </summary>
        protected bool PatrolAllowsMove()
        {
            if (HasTarget) return true;
            return Time.time >= pauseUntil;
        }

        /// <summary>
        /// Единая точка истины: можно ли робиту ехать прямо сейчас.
        /// - Преследование (есть цель): НЕ останавливаться никогда — бежать к
        ///   цели без пауз; исключение только сам замах атаки (attackLockUntil),
        ///   иначе робот «ездит» во время удара и атака нечитаема.
        /// - Патруль: идти всегда, кроме короткой плановой стоянки на маршруте
        ///   (pauseUntil), которая нужна лишь чтобы сменить точку маршрута.
        /// Это переопределяет старое поведение PatrolAllowsMove(), которое при
        /// наличии цели возвращало true даже во время блокировки удара, из-за
        /// чего мечник продолжал шагать в момент атаки.
        /// </summary>
        protected bool CanMoveNow()
        {
            if (Time.time < attackLockUntil) return false;
            if (HasTarget) return true;
            return Time.time >= pauseUntil;
        }

        /// <summary>
        /// Заставляет робота немедленно сменить маршрут: сбрасывает счётчик
        /// пройденного и текущую точку блуждания, так что в следующем кадре
        /// ResolvePatrolDestination выберет новую случайную точку рядом со
        /// спавном и робот сразу продолжит движение к ней.
        /// </summary>
        protected void ForceNewPatrolRoute()
        {
            patrolWalked = 0f;
            lastPatrolPosition = transform.position;
            wanderTarget = Vector3.zero; // выбор новой точки в след. кадре
        }

        private Vector3 ResolvePatrolDestination()
        {
            if (patrolPoints != null && patrolPoints.Length > 0)
            {
                if (currentPatrolPoint == null) PickNextPatrolPoint();

                Transform point = currentPatrolPoint;
                if (point == null)
                {
                    PickNextPatrolPoint();
                    return ResolvePatrolDestination();
                }

                Vector3 flat = Vector3.ProjectOnPlane(point.position - transform.position, Vector3.up);
                if (flat.magnitude <= patrolPointTolerance)
                {
                    // Дошёл до точки: короткая плановая стоянка (по умолчанию 1 с),
                    // затем сразу новая точка маршрута. Долго стоять на патруле
                    // нельзя — робот должен ходить.
                    if (Time.time >= patrolWaitUntil)
                    {
                        PickNextPatrolPoint();
                        BeginPatrolStop(patrolStopDuration > 0f ? patrolStopDuration : Mathf.Min(1f, patrolWaitTime));
                    }

                    return transform.position;
                }

                return point.position;
            }

            // Режим блуждания без точек. Робот ХОДИТ по маршруту и останавливается
            // только иногда — чтобы сменить точку маршрута (короткая стоянка
            // patrolStopDuration, по умолчанию 1 с). Долго «стоять на патруле»
            // недопустимо: это было причиной залипания меча в Idle.
            float stop = patrolStopDuration > 0f ? patrolStopDuration : Mathf.Min(1f, patrolWaitTime);

            if ((wanderTarget - transform.position).sqrMagnitude < 1f)
            {
                // Первая точка после спавна / принудительная смена маршрута:
                // выбираем новую цель блуждания и сразу идём к ней (без паузы).
                patrolWalked = 0f;
                lastPatrolPosition = transform.position;

                Vector2 offset = Random.insideUnitCircle * patrolRadius;
                wanderTarget = new Vector3(spawnPoint.x + offset.x, spawnPoint.y, spawnPoint.z + offset.y);
            }

            // Прошёл «N шагов» (или метров) — встаёт на короткую стоянку и
            // меняет направление: новая точка блуждания выбирается заново.
            float stepLimit = patrolStepsBeforeStop > 0
                ? patrolStepsBeforeStop * Mathf.Max(0.1f, patrolStepLength)
                : Mathf.Max(0.5f, patrolDistanceBeforeStop);

            Vector3 flatDelta = Vector3.ProjectOnPlane(transform.position - lastPatrolPosition, Vector3.up);
            patrolWalked += flatDelta.magnitude;
            lastPatrolPosition = transform.position;

            if (patrolWalked >= stepLimit && Time.time >= pauseUntil)
            {
                patrolWalked = 0f;
                Vector2 nextOffset = Random.insideUnitCircle * patrolRadius;
                wanderTarget = new Vector3(spawnPoint.x + nextOffset.x, spawnPoint.y, spawnPoint.z + nextOffset.y);
                BeginPatrolStop(stop);
                return transform.position;
            }

            return wanderTarget;
        }

        protected void BeginPause(float duration)
        {
            // Длительность паузы берётся ровно из параметра: отдых на точке
            // патруля длится restDuration секунд, а не «сколько повезёт».
            pauseUntil = Time.time + Mathf.Max(0.1f, duration > 0f ? duration : patrolWaitTime);
        }

        /// <summary>
        /// Короткая плановая стоянка на маршруте (по умолчанию ~1 с): робот
        /// останавливается, чтобы затем сменить точку маршрута. Использует тот
        /// же механизм pauseUntil, что и обычный отдых патруля, но значительно
        /// короче restDuration — патруль должен ХОДИТЬ, а стоять лишь иногда.
        /// </summary>
        protected void BeginPatrolStop(float duration)
        {
            pauseUntil = Time.time + Mathf.Max(0.1f, duration);
        }

        /// <summary>
        /// Заблокировать движение робота на duration секунд (замах атаки).
        /// В это время CanMoveNow() == false даже при активной цели.
        /// </summary>
        protected void LockMovement(float duration)
        {
            attackLockUntil = Time.time + Mathf.Max(0f, duration);
        }

        private void PickNextPatrolPoint()
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
            {
                currentPatrolPoint = null;
                return;
            }

            int start = Random.Range(0, patrolPoints.Length);
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                Transform candidate = patrolPoints[(start + i) % patrolPoints.Length];
                if (candidate == null) continue;
                currentPatrolPoint = candidate;
                return;
            }

            currentPatrolPoint = null;
        }

        protected Vector3 ComputeAvoidance(Vector3 desiredDirection)
        {
            Vector3 flat = Vector3.ProjectOnPlane(desiredDirection, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) return desiredDirection;

            flat.Normalize();

            if (detourActive)
            {
                detourTimer -= Time.deltaTime;
                if (detourTimer <= 0f) detourActive = false;
                else return detourDirection;
            }

            Vector3 bodyOrigin = FrontPosition + Vector3.up * (bodyHalfHeight * 2f * (avoidHeightFactor - 0.5f));
            if (!IsBlocked(bodyOrigin, flat, halfExtents)) return flat;

            repathTime = Time.time + 0.35f;
            Vector3 contactNormal = Vector3.ProjectOnPlane(lastSweepNormal, Vector3.up).normalized;
            if (contactNormal.sqrMagnitude < 0.5f) contactNormal = -flat;

            if (TrySlideAlongWall(bodyOrigin, flat, contactNormal, out Vector3 slide)) return slide;

            if (TryScan(bodyOrigin, flat, out Vector3 free)) return free;

            StartReversal();
            return detourDirection;
        }

        private bool TrySlideAlongWall(Vector3 origin, Vector3 forward, Vector3 normal, out Vector3 result)
        {
            result = forward;

            Vector3 along = Vector3.Cross(Vector3.up, normal);
            if (along.sqrMagnitude < 0.0001f) return false;
            along.Normalize();

            Vector3 first = Vector3.Dot(along, forward) >= 0f ? along : -along;
            Vector3 second = -first;

            if (TrySlideAlong(origin, first, forward, normal, out result)) return true;
            if (TrySlideAlong(origin, second, forward, normal, out result)) return true;

            return false;
        }

        private bool TrySlideAlong(Vector3 origin, Vector3 along, Vector3 forward, Vector3 normal, out Vector3 result)
        {
            result = forward;

            if (IsBlocked(origin, along, halfExtents))
            {
                result = forward;
                return false;
            }

            detourDirection = (along - normal * Vector3.Dot(along, normal)).normalized;
            if (detourDirection.sqrMagnitude < 0.0001f) detourDirection = along;

            result = detourDirection;
            detourTimer = detourDuration;
            detourActive = true;

            return true;
        }

        private void StartReversal()
        {
            Vector3 reference = HasTarget && Target != null ? Target.transform.position - FrontPosition : wanderTarget - FrontPosition;
            Vector3 forward = Vector3.ProjectOnPlane(reference, Vector3.up);

            if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
            forward.Normalize();

            detourDirection = -forward;
            detourTimer = detourDuration * 2f;
            detourActive = true;
        }

        protected bool IsPathClear(Vector3 direction)
        {
            if (obstacleProbe <= 0.05f) return true;

            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) return true;
            flat.Normalize();

            Vector3 origin = FrontPosition + Vector3.up * (bodyHalfHeight * 2f * (avoidHeightFactor - 0.5f));
            if (IsBlocked(origin, flat, halfExtents * 0.5f)) return false;

            Vector3 debrisExtents = new Vector3(halfExtents.x, debrisHeight * 0.5f, halfExtents.z);
            Vector3 debrisOrigin = new Vector3(origin.x, FrontPosition.y + debrisHeight * 0.5f, origin.z);
            if (avoidLowDebris
                && Sweep(debrisOrigin, debrisExtents, flat, bodyHalfHeight)
                && !IsSelf(BlockingCollider)
                && !IsGroundClimbable(flat))
                return false;

            return true;
        }

        private bool TryScan(Vector3 origin, Vector3 forward, out Vector3 result)
        {
            result = forward;
            if (obstacleProbe <= 0.05f) return true;

            float step = Mathf.Clamp(avoidAngleStep, 5f, 90f);

            for (float offset = step; offset <= 150f; offset += step)
            {
                float radians = offset * Mathf.Deg2Rad;

                Vector3 right = Quaternion.AngleAxis(-radians, Vector3.up) * forward;
                if (!IsBlocked(origin, right, halfExtents)) { result = right; return true; }

                Vector3 left = Quaternion.AngleAxis(radians, Vector3.up) * forward;
                if (!IsBlocked(origin, left, halfExtents)) { result = left; return true; }
            }

            return false;
        }

        private void StartDetour()
        {
            Vector3 reference = HasTarget && Target != null ? Target.transform.position - FrontPosition : wanderTarget - FrontPosition;
            Vector3 forward = Vector3.ProjectOnPlane(reference, Vector3.up);

            if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
            forward.Normalize();

            int sign = reverseFlipped ? -1 : 1;
            reverseFlipped = !reverseFlipped;

            // Боковой объезд допустим только там, где робот физически пролезет:
            // раньше направление выбиралось вслепую и прижимало робота к стене.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3 candidate = Quaternion.AngleAxis(sign * (90f + attempt * 15f), Vector3.up) * forward;
                if (IsPathClear(candidate))
                {
                    detourDirection = candidate;
                    detourTimer = detourDuration;
                    detourActive = true;
                    return;
                }
            }

            // Все боковые пути заняты — разворот назад единственный выход из угла.
            detourDirection = -forward;
            detourTimer = detourDuration;
            detourActive = true;
        }

        /// <summary>
        /// Признак того, что робот реально упёрся, а не «буксует» на месте по
        /// собственной воле: стоит в паузе патруля или держит цель на дистанции
        /// атаки. Настоящее застревание подтверждается контактом с препятствием
        /// вплотную к корпусу.
        /// </summary>
        private bool InPhysicalContact()
        {
            Vector3 origin = FrontPosition + Vector3.up * (bodyHalfHeight * 2f * (avoidHeightFactor - 0.5f));
            Vector3 probe = halfExtents * 0.6f;

            int count = Physics.OverlapBoxNonAlloc(origin, probe, sweepBuffer, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider candidate = sweepBuffer[i];
                if (IsSelf(candidate)) continue;
                if (IsTargetCollider(candidate)) continue;
                return true;
            }

            return false;
        }

        protected void UpdateStuckCheck(Vector3 velocity, bool wantsToMove)
        {
            if (climbing) return;

            // Не хочет идти (пауза патруля, удержание позиции, стойка у цели) —
            // нулевая скорость не признак застревания. Раньше мечник в такой
            // момент «зацикливался»: таймер рос, включался объезд, анимация
            // переходила в бег, а робот продолжал стоять на месте.
            if (!wantsToMove || !PatrolAllowsMove())
            {
                stuckTimer = 0f;
                detourActive = false;
                return;
            }

            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            if (speed >= stuckSpeed) stuckTimer = 0f;
            else stuckTimer += Time.deltaTime;

            if (stuckTimer < stuckDelay) return;

            stuckTimer = 0f;

            // Принудительный объезд включаем только при реальном контакте со
            // стеной — иначе это просто остановка, а не застревание.
            if (!InPhysicalContact()) return;

            if (detourActive) detourTimer = Mathf.Max(detourTimer, detourDuration * 0.6f);
            else StartDetour();
        }

        private bool IsBlocked(Vector3 origin, Vector3 direction, Vector3 extents)
        {
            if (obstacleProbe <= 0.05f) return false;

            if (Sweep(origin, extents, direction, obstacleProbe) && !IsSelf(BlockingCollider) && !IsTargetCollider(BlockingCollider))
            {
                if (IsGroundClimbable(direction))
                {
                    climbing = true;
                    return false;
                }

                return true;
            }

            if (!avoidLowDebris || debrisHeight <= 0.02f) return false;

            Vector3 debrisExtents = new Vector3(extents.x, debrisHeight * 0.5f, extents.z);
            Vector3 debrisOrigin = new Vector3(origin.x, GroundLevel + debrisHeight * 0.5f, origin.z);

            if (Sweep(debrisOrigin, debrisExtents, direction, obstacleProbe * 0.6f) && !IsSelf(BlockingCollider) && !IsTargetCollider(BlockingCollider))
                return true;

            return false;
        }

        /// <summary>
        /// Цель не считается препятствием, иначе робот начинает обходить того,
        /// к кому сам бежит, и это выглядит как попытка уклониться от игрока.
        /// </summary>
        protected bool IsTargetCollider(Collider candidate)
        {
            if (candidate == null || Target == null) return false;

            Transform hit = candidate.transform;
            return hit == Target.transform || hit.IsChildOf(Target.transform);
        }

        private float GroundLevel => transform.position.y;

        private bool Sweep(Vector3 origin, Vector3 extents, Vector3 direction, float distance)
        {
            if (distance <= 0.05f)
            {
                int count = Physics.OverlapBoxNonAlloc(origin, extents, sweepBuffer, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);

                for (int i = 0; i < count; i++)
                {
                    if (IsSelf(sweepBuffer[i])) continue;

                    overlapHit = sweepBuffer[i];
                    lastSweepNormal = MeasureContactNormal(sweepBuffer[i], origin, direction);

                    return true;
                }

                return false;
            }

            Physics.BoxCast(origin, extents, direction, out RaycastHit hit, Quaternion.identity, distance, obstacleMask, QueryTriggerInteraction.Ignore);

            lastSweepHit = hit;
            lastSweepNormal = hit.collider != null ? MeasureContactNormal(hit.collider, hit.point, direction) : Vector3.zero;

            return hit.collider != null;
        }

        private Vector3 MeasureContactNormal(Collider blocker, Vector3 contact, Vector3 sweepDirection)
        {
            if (blocker == null) return Vector3.zero;

            Vector3 back = new Vector3(contact.x, 0f, contact.z) - new Vector3(sweepDirection.x, 0f, sweepDirection.z) * 0.08f;
            Vector3 probeOrigin = new Vector3(back.x, GroundLevel + slopeProbeHeight * 0.75f, back.z);

            if (Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit face, slopeProbeHeight * 1.6f, obstacleMask, QueryTriggerInteraction.Ignore)
                && face.collider == blocker
                && !IsSelf(face.collider))
                return face.normal;

            Vector3 boxNormal = Vector3.ProjectOnPlane(blocker.transform.up, Vector3.up);
            if (boxNormal.sqrMagnitude > 0.01f) return boxNormal.normalized;

            return Vector3.ProjectOnPlane(-sweepDirection, Vector3.up).normalized;
        }

        protected bool IsGroundClimbable(Vector3 direction)
        {
            float rise = MeasureStepAhead(direction);
            return rise >= 0f && rise <= MaxClimbableRise();
        }

        private float MaxClimbableRise()
        {
            float slopeRise = Mathf.Tan(maxClimbSlope * Mathf.Deg2Rad) * slopeProbeDistance;
            return slopeRise + stepUpHeight;
        }

        protected float ClimbSpeed(Vector3 direction, float speed)
        {
            if (!climbing) return 0f;

            float rise = MeasureStepAhead(direction);
            if (rise <= stepUpHeight) return 0f;

            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) return 0f;

            float run = Mathf.Max(slopeProbeDistance, flat.magnitude);
            float slope = rise / run;
            float maxSlope = Mathf.Tan(maxClimbSlope * Mathf.Deg2Rad);
            if (slope <= maxSlope) return 0f;

            return Mathf.Min(speed * 0.5f, speed * slope);
        }

        private float MeasureStepAhead(Vector3 direction)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) return -1f;
            flat.Normalize();

            float here = GroundHeightAt(FrontPosition);
            float ahead = GroundHeightAt(FrontPosition + flat * slopeProbeDistance);

            if (here < -900f || ahead < -900f) return -1f;

            return ahead - here;
        }

        private float GroundHeightAt(Vector3 origin)
        {
            Vector3 from = new Vector3(origin.x, GroundLevel + slopeProbeHeight, origin.z);
            float distance = slopeProbeHeight * 2f + 1f;

            int count = Physics.RaycastNonAlloc(
                from,
                Vector3.down,
                groundBuffer,
                distance,
                groundMask,
                QueryTriggerInteraction.Ignore);

            float best = -1000f;

            for (int i = 0; i < count; i++)
            {
                Collider candidate = groundBuffer[i].collider;
                if (candidate == null) continue;
                if (IsSelf(candidate)) continue;

                float y = groundBuffer[i].point.y;
                if (y > best) best = y;
            }

            return best;
        }

        protected bool IsSelf(Collider candidate)
        {
            // Сравнение тел идёт только когда своё тело вообще есть: у врагов
            // без Rigidbody selfBody пуст, и check "attachedRigidbody == selfBody"
            // превращался в "null == null" — тогда «своими» оказывались ВСЕ
            // статические коллайдеры без Rigidbody (земля, стены, машина), и
            // запросы их молча пропускали. Из-за этого дрон не взрывался о
            // препятствия в полёте смерти.
            return candidate == null
                || candidate.transform.IsChildOf(transform)
                || (selfBody != null && candidate.attachedRigidbody == selfBody);
        }

        private Collider BlockingCollider
        {
            get
            {
                Collider found = lastSweepHit.collider;
                return found != null ? found : overlapHit;
            }
        }

        protected bool RepathExpired => Time.time >= repathTime;

        /// <summary>Множитель зрения от кровавой луны (1, если событие неактивно).</summary>
        protected static float BloodMoonVisionMultiplier =>
            BloodMoonState.IsActive ? Mathf.Max(0.01f, BloodMoonState.EnemyVisionMultiplier) : 1f;

        /// <summary>Множитель скорости от кровавой луны.</summary>
        protected static float BloodMoonSpeedMultiplier =>
            BloodMoonState.IsActive ? Mathf.Max(0.01f, BloodMoonState.EnemySpeedMultiplier) : 1f;

        /// <summary>Множитель урона от кровавой луны.</summary>
        protected static float BloodMoonDamageMultiplier =>
            BloodMoonState.IsActive ? Mathf.Max(0.01f, BloodMoonState.EnemyDamageMultiplier) : 1f;

        /// <summary>Множитель здоровья от кровавой луны (применяется один раз при спавне).</summary>
        protected static float BloodMoonHealthMultiplier =>
            BloodMoonState.IsActive ? Mathf.Max(0.01f, BloodMoonState.EnemyHealthMultiplier) : 1f;

        // ---- Диагностика застревания для дочерних классов -------------------
        // Мечник использует эти значения, чтобы самому засекать «упёрся в
        // препятствие» и включать принудительный объезд, не дожидаясь мягких
        // условий UpdateStuckCheck (там обязателен физический контакт вплотную).
        protected float StuckSpeedValue => stuckSpeed;
        protected float StuckDelayValue => stuckDelay;
        protected bool IsDetourActive => detourActive;

        protected static PlayerController FindPlayer()
        {
            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            return players.Length > 0 ? players[0] : null;
        }

        /// <summary>
        /// Звуки роботов: шаги мечника, прыжок/полёт/приземление динобота,
        /// гул винтов дрона и сигнал обнаружения цели. Клипы генерируются
        /// процедурно (CarGame.Audio), поэтому ассеты не нужны; свои AudioClip
        /// можно подкинуть в инспекторе — они имеют приоритет.
        /// Вешается автоматически на любого EnemyBase при Awake.
        /// </summary>
        [DisallowMultipleComponent]
        public class RobotVoice : MonoBehaviour
        {
            // Клипы и громкости НЕ настраиваются здесь: их передаёт конкретный
            // бот через AssignClips/AssignVolumes (поля инспектора живут в
            // RobotKamikaze / RobotDrone / RobotSwordsman). Пустой клип —
            // процедурный звук по умолчанию.
            private AudioClip stepClip;
            private AudioClip jumpClip;
            private AudioClip flightLoopClip;
            private AudioClip landClip;
            private AudioClip alertClip;
            private AudioClip shotClip;

            private float stepVolume = 0.35f;
            private float jumpVolume = 0.6f;
            private float landVolume = 0.5f;
            private float alertVolume = 0.8f;
            private float flightLoopVolume = 0.25f;
            private float shotVolume = 0.5f;

            /// <summary>Настройка клиентских клипов из скрипта конкретного бота.</summary>
            public void AssignClips(AudioClip steps, AudioClip jump, AudioClip flightLoop,
                AudioClip land, AudioClip alert, AudioClip shot)
            {
                stepClip = steps;
                jumpClip = jump;
                landClip = land;
                alertClip = alert;
                shotClip = shot;

                bool loopChanged = flightLoopClip != flightLoop;
                flightLoopClip = flightLoop;
                if (loopChanged && hover != null)
                {
                    hover.clip = flightLoopClip != null ? flightLoopClip : CarGame.Audio.ProceduralAudio.HoverLoop();
                }
            }

            /// <summary>Настройка громкостей; значение меньше нуля — оставить дефолт.</summary>
            public void AssignVolumes(float steps, float jump, float land,
                float alert, float flightLoop, float shot)
            {
                if (steps >= 0f) stepVolume = steps;
                if (jump >= 0f) jumpVolume = jump;
                if (land >= 0f) landVolume = land;
                if (alert >= 0f) alertVolume = alert;
                if (flightLoop >= 0f) flightLoopVolume = flightLoop;
                if (shot >= 0f) shotVolume = shot;
                if (hover != null) hover.volume = flightLoopVolume;
            }

            [Header("Интервалы")]
            [Tooltip("Интервал шагов на патруле / беге, с")]
            [SerializeField] private float patrolStepInterval = 0.55f;
            [SerializeField] private float runStepInterval = 0.34f;

            private AudioSource voice;
            private AudioSource hover;
            private EnemyBase owner;
            private RobotDrone droneOwner;
            private RobotKamikaze kamikazeOwner;
            private float stepTimer;
            private bool wasEngaged;
            private bool wasAirborne;

            /// <summary>Один процедурный клип удара приземления на весь проект.</summary>
            private static AudioClip sharedLandClip;

            private void Awake()
            {
                owner = GetComponent<EnemyBase>();
                droneOwner = GetComponent<RobotDrone>();
                kamikazeOwner = GetComponent<RobotKamikaze>();

                voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 1f;
                voice.minDistance = 2f;
                voice.maxDistance = 45f;

                // Петля полёта: у дрона — постоянный гул винтов; у динобота —
                // жужжание только ПОКА он в воздухе (см. Update).
                if (droneOwner != null || kamikazeOwner != null)
                {
                    hover = gameObject.AddComponent<AudioSource>();
                    hover.playOnAwake = false;
                    hover.loop = true;
                    hover.spatialBlend = 1f;
                    hover.minDistance = 3f;
                    hover.maxDistance = 70f;
                    hover.clip = flightLoopClip != null ? flightLoopClip : CarGame.Audio.ProceduralAudio.HoverLoop();
                    hover.volume = flightLoopVolume;

                    // Дрон летает всегда — петля стартует сразу.
                    if (droneOwner != null) hover.Play();
                    else hover.Stop();
                }
            }

            private void Update()
            {
                if (owner == null) return;

                // Сигнал обнаружения — ровно в момент захвата цели.
                bool engaged = owner.HasTarget;
                if (engaged && !wasEngaged) Play(alertClip, CachedAlert(), alertVolume, pitchJitter: 0.08f);
                wasEngaged = engaged;

                if (hover == null) return;

                if (kamikazeOwner != null)
                {
                    // Динобот: звук полёта звучит всё время, пока робот в воздухе,
                    // и гаснет сразу после приземления.
                    bool airborne = kamikazeOwner.IsAirborneForSound;
                    if (airborne && !wasAirborne) hover.Play();
                    if (!airborne && wasAirborne) hover.Stop();
                    wasAirborne = airborne;
                    if (airborne) hover.pitch = Mathf.Clamp(0.9f + Random.value * 0.1f, 0.85f, 1.15f);
                    return;
                }

                // Чем быстрее летит дрон — тем выше тон винтов.
                Rigidbody body = owner.GetComponent<Rigidbody>();
                float speed = body != null ? body.linearVelocity.magnitude : 0f;
                hover.pitch = Mathf.Clamp(0.85f + speed * 0.03f, 0.8f, 1.5f);
                hover.volume = flightLoopVolume * Mathf.Clamp01(0.4f + speed * 0.12f);
            }

            /// <summary>Шаги мечника: вызывать каждый кадр, интервал держит сам.</summary>
            public void Step(Vector3 horizontalVelocity, bool engaged)
            {
                float speed = horizontalVelocity.magnitude;
                if (speed < 0.5f)
                {
                    stepTimer = 0f;
                    return;
                }

                stepTimer -= Time.deltaTime;
                if (stepTimer > 0f) return;

                stepTimer = engaged ? runStepInterval : patrolStepInterval;
                float tone = engaged ? 150f : 118f;
                Play(stepClip, CachedFootstep(0.5f, tone), stepVolume, pitchJitter: 0.12f);
            }

            /// <summary>Прыжок динобота: толчок вверх.</summary>
            public void Jump()
            {
                Play(jumpClip, CachedJump(), jumpVolume, pitchJitter: 0.1f);
            }

            /// <summary>Приземление динобота: глухой тяжёлый удар.</summary>
            public void Land()
            {
                if (landClip == null && sharedLandClip == null)
                    sharedLandClip = CarGame.Audio.ProceduralAudio.Impact(true);

                Play(landClip, sharedLandClip, landVolume, pitchJitter: -0.15f);
            }

            /// <summary>Выстрел дрона.</summary>
            public void Shot()
            {
                Play(shotClip, CachedShot(), shotVolume, pitchJitter: 0.05f);
            }

            // Процедурные клипы кешируются: иначе на каждом событии создавался
            // бы новый AudioClip (те же шаги звучат несколько раз в секунду).
            private static AudioClip cachedAlert;
            private static AudioClip cachedJump;
            private static AudioClip cachedShot;
            private static AudioClip cachedStepPatrol;
            private static AudioClip cachedStepRun;

            private static AudioClip CachedAlert()
            {
                if (cachedAlert == null) cachedAlert = CarGame.Audio.ProceduralAudio.Alert();
                return cachedAlert;
            }

            private static AudioClip CachedJump()
            {
                if (cachedJump == null) cachedJump = CarGame.Audio.ProceduralAudio.Jump();
                return cachedJump;
            }

            private static AudioClip CachedShot()
            {
                if (cachedShot == null) cachedShot = CarGame.Audio.ProceduralAudio.Gunshot(false);
                return cachedShot;
            }

            private static AudioClip CachedFootstep(float grit, float tone)
            {
                if (tone > 130f)
                {
                    if (cachedStepRun == null) cachedStepRun = CarGame.Audio.ProceduralAudio.Footstep(grit, tone);
                    return cachedStepRun;
                }

                if (cachedStepPatrol == null) cachedStepPatrol = CarGame.Audio.ProceduralAudio.Footstep(grit, tone);
                return cachedStepPatrol;
            }

            private void Play(AudioClip custom, AudioClip procedural, float volume, float pitchJitter)
            {
                AudioClip clip = custom != null ? custom : procedural;
                if (clip == null || voice == null) return;

                voice.pitch = Mathf.Clamp(1f + Random.Range(-pitchJitter, pitchJitter), 0.6f, 1.6f);
                voice.PlayOneShot(clip, volume);
            }
        }
    }
}
