using System.Collections;
using CarGame.Audio;
using UnityEngine;

namespace CarGame.Combat
{
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(SphereCollider))]
    [DisallowMultipleComponent]
    public class RobotDrone : EnemyBase
    {
        [Header("Точка направления")]
        [Tooltip("Точка «морда» дрона. Направление взгляда берётся от центра дрона к ней, поэтому эта точка задаёт, какой стороной дрон смотрит на цель. Без неё берётся ось стволов")]
        [SerializeField] private Transform frontPoint;

        [Header("Характеристики")]
        [Tooltip("Максимальное здоровье дрона")]
        [SerializeField] private float maxHealth = 35f;
        [Tooltip("Скорость полёта, м/с")]
        [SerializeField] private float moveSpeed = 9f;
        [Tooltip("Скорость поворота, градусов/с")]
        [SerializeField] private float turnSpeed = 260f;
        [Tooltip("Сколько секунд дрон разгоняется и тормозит")]
        [SerializeField] private float accelerationTime = 0.6f;

        [Header("Полёт над целью")]
        [Tooltip("На какой высоте над машиной или игроком дрон кружит, м")]
        [SerializeField] private float hoverHeight = 7f;
        [Tooltip("Радиус кружения, м")]
        [SerializeField] private float orbitRadius = 9f;
        [Tooltip("Скорость кружения, радиан/с")]
        [SerializeField] private float orbitSpeed = 0.75f;
        [Tooltip("С какой стороны дрон держится относительно цели, градусы")]
        [SerializeField] private float orbitPhase;

        [Header("Физика полёта")]
        [Tooltip("Насколько быстро дрон догоняет желаемую скорость (инерция). Меньше — «тяжелее» и плавнее")]
        [SerializeField] private float velocityResponse = 6f;
        [Tooltip("Максимальный крен корпуса при разгоне, градусы. Корпус наклоняется в сторону поворота, как настоящий мультикоптер")]
        [SerializeField] private float maxTiltAngle = 28f;
        [Tooltip("Насколько быстро корпус возвращается в горизонт")]
        [SerializeField] private float tiltResponse = 5f;
        [Tooltip("Вертикальные колебания зависания (дрожание высоты), м")]
        [SerializeField] private float bobAmplitude = 0.14f;
        [Tooltip("Частота колебаний зависания, Гц")]
        [SerializeField] private float bobFrequency = 0.9f;
        [Tooltip("Проверять потолок над дроном и снижаться, чтобы протиснуться в низкий проход, м")]
        [SerializeField] private float ceilingProbe = 3.5f;
        [Tooltip("Запас от потолка, м")]
        [SerializeField] private float ceilingClearance = 0.45f;
        [Tooltip("Минимальная высота корпуса над землёй. Дрон не должен касаться пола: после спавна он это проверяет и поднимается, а ниже этой отметки не опускается никогда")]
        [SerializeField] private float minGroundHeight = 1.2f;
        [Tooltip("Штатная высота патрулирования над землёй, м. Выше minGroundHeight: в режиме патруля дрон держится заметно выше роста игрока и крыш машин. 0 — не поднимать принудительно")]
        [SerializeField] private float patrolAltitude = 6f;

        [Header("Стрельба")]
        [Tooltip("Дальность стрельбы, м")]
        [SerializeField] private float shootRange = 55f;
        [Tooltip("Пауза между выстрелами, с")]
        [SerializeField] private float fireInterval = 0.9f;
        [Tooltip("Точки стрельбы. Выстрелы идут по ним поочерёдно: сначала первая, потом вторая, и так далее. Если пусто — из центра дрона")]
        [SerializeField] private Transform[] firePoints = new Transform[0];
        [Tooltip("Урон выстрела")]
        [SerializeField] private float bulletDamage = 7f;
        [Tooltip("Разброс прицеливания, градусы")]
        [SerializeField] private float aimSpread = 2.5f;
        [Tooltip("Насколько широким лучом проверяется попадание, м. Больше — надёжнее ловит цель, но может задеть соседние объекты")]
        [SerializeField] private float assistRadius = 0.12f;
        [Tooltip("Слои, которые останавливают выстрел")]
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Множитель урона, если попали в машину")]
        [SerializeField] private float carDamageMultiplier = 1.4f;

        [Header("Эффект выстрела")]
        [Tooltip("Префаб эффекта. Один и тот же эффект ставится в точку попадания и у ствола, различается только направление")]
        [SerializeField] private GameObject shotEffectPrefab;

        [Header("Трассер")]
        [Tooltip("Показывать летящий луч от дрона к цели")]
        [SerializeField] private bool showTracer = true;
        [Tooltip("Толщина луча, м")]
        [SerializeField] private float tracerWidth = 0.04f;
        [Tooltip("Скорость полёта луча, м/с")]
        [SerializeField] private float tracerSpeed = 180f;
        [Tooltip("Цвет луча")]
        [SerializeField] private Color tracerColor = new Color(1f, 0.35f, 0.25f, 1f);

        [Header("Взрыв")]
        [Tooltip("Префаб взрыва, который срабатывает при ударе после смерти (Assets/Prefabs/Effects/Explosion.prefab)")]
        [SerializeField] private GameObject explosionEffectPrefab;
        [Tooltip("Скорость, с которой дрон бросается вперёд после смерти, м/с — по направлению носа. Дальше гравитация тянет его вниз")]
        [SerializeField] private float crashSpeed = 7f;
        [Tooltip("Скорость вращения волчком после смерти, градусов/с")]
        [SerializeField] private float crashSpinSpeed = 540f;
        [Tooltip("Слои, о которые дрон может врезаться после смерти. Слой самого дрона игнорируется")]
        [SerializeField] private LayerMask crashMask = ~0;

        [Header("Звук")]
        [Tooltip("Громкость выстрела")]
        [SerializeField] private float shotVolume = 0.5f;

        private Rigidbody body;
        private Transform tiltRig;
        private Quaternion tiltTarget = Quaternion.identity;
        private float bobPhase;
        private RobotVoice voice;
        private float nextFireTime;
        private int nextFirePoint = -1;
        private float orbitAngle;
        private Vector3 velocity;
        private Vector3 noseLocal = Vector3.forward;
        private Vector3 patrolTarget;
        private bool patrolTargetValid;
        private float patrolHoldUntil;
        private bool engaged;
        private bool returningHome;
        private bool crashing;
        private Vector3 crashVelocity;
        private float crashRadius = 0.5f;
        private static readonly Collider[] crashBuffer = new Collider[16];
        private static readonly RaycastHit[] crashHitBuffer = new RaycastHit[16];
        private AudioClip generatedShot;
        private AudioSource audioSource;

        /// <summary>Ускорение падения дрона после смерти, м/с².</summary>
        private const float CrashGravity = 22f;

        /// <summary>
        /// Куда дрон повёрнут носом. Задаётся точкой Front: направление от центра
        /// дрона к этой точке, так же как у машины. Если точка не задана, берётся
        /// ось стволов, а если нет и их — transform.forward.
        /// </summary>
        public Vector3 FrontDirection
        {
            get
            {
                if (frontPoint != null)
                {
                    Vector3 offset = frontPoint.position - transform.position;
                    if (offset.sqrMagnitude > 0.0001f) return offset.normalized;
                }

                Vector3 barrels = transform.TransformDirection(noseLocal);
                return barrels.sqrMagnitude > 0.0001f ? barrels : transform.forward;
            }
        }

        private void ApplyBloodMoonBuffs()
        {
            maxHealth *= BloodMoonHealthMultiplier;
            moveSpeed *= BloodMoonSpeedMultiplier;
            bulletDamage *= BloodMoonDamageMultiplier;
        }

        private void Awake()
        {
            ApplyBloodMoonBuffs();

            health = GetComponent<Health>();
            if (health != null)
            {
                health.Configure(maxHealth, Health.HpOwner.Enemy);

                // Без этой подписки дрон не знает, что умер: OnDied ни разу не
                // вызывался, Update выходил по health.IsDead, и дрон просто
                // зависал в воздухе вместо падения. Так же подписан и
                // RobotSwordsman.
                health.Died += OnDied;
            }

            body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            SphereCollider sphere = GetComponent<SphereCollider>();
            if (sphere != null) sphere.isTrigger = true;

            // Дрон — мультикоптер: корпус должен наклоняться в сторону движения,
            // но физический Rigidbody остаёт с заморозкой вращения. Крен крутится
            // на отдельном «риге»: если модель уже лежит дочерним объектом под
            // этим геймом, кренуем её, иначе создаём пустой rig и пересобираем
            // визуал под него. Коллайдеры остаются на корне — физика не плывёт.
            BuildTiltRig();

            voice = gameObject.AddComponent<RobotVoice>();

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.minDistance = 4f;
            audioSource.maxDistance = 60f;

            orbitAngle = orbitPhase * Mathf.Deg2Rad;
            BaseAwake();
            CacheNoseAxis();

            StartCoroutine(StartHoverCheck());
        }

        /// <summary>
        /// После спавна дрон проверяет, не касается ли он земли, и поднимается
        /// на безопасную высоту. Дальше касаться пола ему не даёт ClampAltitude
        /// в каждом кадре.
        /// </summary>
        private IEnumerator StartHoverCheck()
        {
            // Ждём физический кадр: в первый кадр после Instantiate коллайдеры
            // окружения ещё могут не попасть в broadphase, и зонд вниз
            // промахнётся.
            yield return new WaitForFixedUpdate();

            // Пока зонд вниз видит пол в пределах запаса minGroundHeight — дрон
            // лежит брюхом на земле (или заспавнился в полу): поднимаем его
            // ступенями вверх, пока не окажется на штатной высоте висения.
            for (int attempt = 0; attempt < 24 && IsGroundedBelow(); attempt++)
            {
                Vector3 position = transform.position;
                position.y += 0.5f;
                transform.position = position;
                if (body != null) body.linearVelocity = Vector3.zero;
                velocity = Vector3.zero;
                yield return new WaitForFixedUpdate();
            }
        }

        /// <summary>
        /// Есть ли твёрдая поверхность прямо под корпусом в пределах запаса
        /// minGroundHeight. Триггеры (зоны, датчики) поверхностью не считаются.
        /// </summary>
        private bool IsGroundedBelow()
        {
            float probe = minGroundHeight + 0.4f;
            return Physics.Raycast(transform.position, Vector3.down, probe, crashMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Полётный потолок высоты: ниже minGroundHeight над землёй дрон не
        /// опускается никогда — иначе он волочится брюхом по полу и цепляет
        /// наземные препятствия. Потолок наоборот: прижимается вниз, чтобы
        /// протиснуться в низкий проход, в который дронуallowed залететь.
        /// </summary>
        private void ClampAltitude()
        {
            if (crashing) return;

            float floorProbe = minGroundHeight + 0.6f;
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit ground, floorProbe, crashMask, QueryTriggerInteraction.Ignore))
            {
                float minY = ground.point.y + minGroundHeight;
                if (transform.position.y < minY)
                {
                    Vector3 position = transform.position;
                    position.y = Mathf.MoveTowards(position.y, minY, moveSpeed * Time.deltaTime);
                    transform.position = position;

                    if (velocity.y < 0f) velocity.y = 0f;
                    if (body != null && body.isKinematic == false) body.linearVelocity = velocity;
                }
            }

            if (ceilingProbe <= 0.05f) return;

            float radius = crashRadius * 0.8f;
            if (Physics.SphereCast(transform.position, radius, Vector3.up, out RaycastHit ceiling, ceilingProbe, crashMask, QueryTriggerInteraction.Ignore))
            {
                float maxY = ceiling.point.y - radius - ceilingClearance;
                if (transform.position.y > maxY)
                {
                    Vector3 position = transform.position;
                    position.y = Mathf.Max(position.y - moveSpeed * Time.deltaTime, maxY);
                    transform.position = position;

                    if (velocity.y > 0f) velocity.y = 0f;
                    if (body != null && !body.isKinematic) body.linearVelocity = velocity;
                }
            }
        }

        private void Update()
        {
            if (crashing)
            {
                UpdateCrash();
                return;
            }

            if (health == null || health.IsDead) return;

            BaseUpdate();

            // Жёсткий пол и потолок высоты: дрон никогда не волочится брюхом по
            // земле и сам протискивается в низкие проходы.
            ClampAltitude();

            if (HasTarget)
            {
                engaged = true;
                returningHome = false;
                patrolTargetValid = false;
                patrolHoldUntil = 0f;
                UpdateAttack();
                return;
            }

            if (engaged)
            {
                engaged = false;
                returningHome = true;
            }

            PatrolAltitudeAssist();

            if (returningHome) UpdateReturnHome();
            else UpdatePatrol();
        }

        /// <summary>
        /// Штатная высота патрулирования: над точкой спавна. Ниже patrolAltitude
        /// над землёй/спавном дрон в режиме патруля не опускается — так он
        /// держится выше крыш машин и игрока, пока никого не видит. В бою
        /// (hoverHeight над целью) эта подложка не действует.
        /// </summary>
        private void PatrolAltitudeAssist()
        {
            if (patrolAltitude <= 0.05f) return;

            float groundY = SpawnPoint.y;
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit ground,
                patrolAltitude + 60f, crashMask, QueryTriggerInteraction.Ignore))
            {
                groundY = ground.point.y;
            }

            float minY = groundY + patrolAltitude;
            if (transform.position.y >= minY - 0.02f) return;

            Vector3 position = transform.position;
            position.y = Mathf.MoveTowards(position.y, minY, moveSpeed * Time.deltaTime);
            transform.position = position;

            if (velocity.y < 0f) velocity.y = 0f;
            if (body != null && !body.isKinematic) body.linearVelocity = velocity;
        }

        /// <summary>
        /// После потери цели дрон возвращается на точку спавна и уже потом снова
        /// уходит патрулировать вокруг неё.
        /// </summary>
        private void UpdateReturnHome()
        {
            Vector3 drift = SpawnPoint - FrontPosition;
            Vector3 horizontal = Vector3.ProjectOnPlane(drift, Vector3.up);

            Vector3 direction = ComputeAvoidance(horizontal) + Vector3.Project(drift, Vector3.up);
            if (direction.sqrMagnitude > 0.0001f)
            {
                direction.Normalize();
                Move(direction, moveSpeed * 0.55f);
                FaceAxis(direction, turnSpeed, Time.deltaTime);
            }
            else
            {
                Move(Vector3.zero, 0f);
            }

            if (drift.sqrMagnitude <= PatrolPointTolerance * PatrolPointTolerance) returningHome = false;
        }

        /// <summary>
        /// После смерти дрон, как в ULTRAKILL, не ныряет камнем вниз: он летит
        /// туда, куда смотрел, и падение лишь постепенно тянет его вниз — так
        /// мёртвый дрон проносится вперёд, кувыркаясь, и взрывается, едва
        /// коснувшись чего угодно твёрдого. Взрыв — по первому касанию.
        /// </summary>
        private void UpdateCrash()
        {
            float dt = Time.deltaTime;
            crashVelocity += Vector3.down * (CrashGravity * dt);

            Vector3 from = transform.position;
            Vector3 delta = crashVelocity * dt;
            float distance = delta.magnitude;
            Vector3 direction = distance > 0.0001f ? delta / distance : Vector3.down;

            transform.position = from + delta;
            transform.rotation = Quaternion.AngleAxis(crashSpinSpeed * dt, Vector3.up) * transform.rotation;

            if (TryFindContact(from, direction, distance, out Vector3 point)) Detonate(point);
        }

        /// <summary>
        /// Ищет, задел ли упавший дрон что-нибудь твёрдое — и то, во что он уже
        /// воткнулся, и то, что лежит на пути за этот кадр (чтобы не проскочить
        /// тонкое). Учитываются любые коллайдеры, в том числе без Rigidbody:
        /// земля, стены, машина, игрок, другой робот. Триггеры (зоны, багажник,
        /// чужие датчики) не считаются — иначе дрон рвался бы ещё в воздухе.
        /// </summary>
        private bool TryFindContact(Vector3 from, Vector3 direction, float distance, out Vector3 point)
        {
            point = transform.position;

            // Берём не первый попавшийся коллайдер, а ближайший чужой: одиночный
            // SphereCast может вернуть собственный (устаревший) коллайдер дрона и
            // заслонить стену за ним.
            if (distance > 0.0001f)
            {
                int hits = Physics.SphereCastNonAlloc(from, crashRadius, direction, crashHitBuffer, distance, crashMask, QueryTriggerInteraction.Ignore);

                Collider best = null;
                float bestDistance = float.MaxValue;
                Vector3 bestPoint = point;

                for (int i = 0; i < hits; i++)
                {
                    Collider candidate = crashHitBuffer[i].collider;
                    if (IsSelf(candidate)) continue;
                    if (crashHitBuffer[i].distance >= bestDistance) continue;

                    bestDistance = crashHitBuffer[i].distance;
                    best = candidate;
                    bestPoint = crashHitBuffer[i].point;
                }

                if (best != null)
                {
                    point = bestPoint;
                    return true;
                }
            }

            int count = Physics.OverlapSphereNonAlloc(transform.position, crashRadius, crashBuffer, crashMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider candidate = crashBuffer[i];
                if (IsSelf(candidate)) continue;

                point = candidate.ClosestPoint(transform.position);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Страховка на случай, когда за кадр дрон переносится дальше своей
        /// сферы: физика сама сообщит о касании с любым твёрдым коллайдером,
        /// у которого нет Rigidbody (такие статические препятствия SphereCast
        /// иногда проскакивает на высокой скорости падения).
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            if (!crashing) return;
            if (other == null || other.isTrigger) return;
            if (IsSelf(other)) return;

            Detonate(other.ClosestPoint(transform.position));
        }

        private void Detonate(Vector3 point)
        {
            crashing = false;

            if (explosionEffectPrefab != null)
            {
                GameObject source = Instantiate(explosionEffectPrefab, point, Quaternion.identity);
                ExplosionEffect effect = source != null ? source.GetComponent<ExplosionEffect>() : null;
                if (effect != null) effect.Ignore(gameObject);
            }
            else
            {
                Debug.LogWarning($"[Drone] У дрона '{name}' не задан префаб взрыва — взрыва не будет.", this);
            }

            Destroy(gameObject, 0.05f);
        }

        /// <summary>
        /// Без цели дрон кружит рядом со спавном: долетает до точки, зависает на паузу,
        /// выбирает новую сторону и снова летит.
        /// </summary>
        private void UpdatePatrol()
        {
            Vector3 drift = ResolvePatrolPoint() - FrontPosition;
            Vector3 horizontal = Vector3.ProjectOnPlane(drift, Vector3.up);

            Vector3 direction = ComputeAvoidance(horizontal) + Vector3.Project(drift, Vector3.up);
            if (direction.sqrMagnitude < 0.0001f)
            {
                Move(Vector3.zero, 0f);
                return;
            }

            direction.Normalize();
            Move(direction, moveSpeed * 0.55f);
            FaceAxis(direction, turnSpeed, Time.deltaTime);
        }

        private void UpdateAttack()
        {
            orbitAngle += orbitSpeed * Time.deltaTime;

            Vector3 anchor = Target.transform.position + Vector3.up * hoverHeight;
            Vector3 offset = Quaternion.AngleAxis(Mathf.Rad2Deg * orbitAngle, Vector3.up) * Vector3.right * orbitRadius;
            Vector3 destination = anchor + offset;

            Vector3 toDestination = destination - FrontPosition;
            UpdateStuckCheck(velocity, toDestination.sqrMagnitude > 0.0001f);
            Move(toDestination, moveSpeed);
            FaceTarget(Target.transform.position + Vector3.up, turnSpeed, Time.deltaTime);

            if (Time.time < nextFireTime) return;

            float distance = Vector3.Distance(transform.position, Target.transform.position);
            if (distance > shootRange) return;
            if (!HasLineOfSight(Target)) return;

            nextFireTime = Time.time + fireInterval;
            Fire();
        }

        private void Move(Vector3 direction, float speed)
        {
            if (direction.sqrMagnitude < 0.0001f || speed <= 0.01f)
            {
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, moveSpeed * 2f * Time.deltaTime);
            }
            else
            {
                float throttle = IsPathClear(direction) ? 1f : 0.2f;

                // Инерция мультикоптера: дрон догоняет желаемую скорость через
                // velocityResponse, а не телепортируется в неё за кадр. Это и
                // есть «физика дрона» — плавный разгон и торможение с опережением.
                Vector3 desired = direction.normalized * speed * throttle;
                velocity = Vector3.Lerp(velocity, desired, Mathf.Clamp01(velocityResponse * Time.deltaTime));
            }

            // Вертикальное покачивание зависания: амплитуда из инспектора,
            // частота из bobFrequency. По горизонтали инерция, по вертикали —
            // мягкий дрейф, как у реального коптера на автопилоте.
            bobPhase += Time.deltaTime * bobFrequency * Mathf.PI * 2f;
            velocity.y += Mathf.Cos(bobPhase) * bobAmplitude * bobFrequency * Mathf.PI * 2f * Time.deltaTime * 6f;
            velocity.y = Mathf.Clamp(velocity.y, -moveSpeed * 0.5f, moveSpeed * 0.5f);

            if (body == null)
            {
                transform.position += velocity * Time.deltaTime;
                return;
            }

            body.linearVelocity = velocity;
            if (body.isKinematic) transform.position += velocity * Time.deltaTime;

            // Мультикоптер наклоняется туда, куда летит: риг крена догоняет
            // целевой наклон, посчитанный по текущей скорости.
            UpdateTiltTarget(new Vector3(velocity.x, 0f, velocity.z));
            ApplyTilt(Time.deltaTime);
        }

        /// <summary>
        /// Точка патруля рядом со спавном. Дрон долетает до неё, виснет patrolWaitTime,
        /// потом выбирает новую. Пока новая точка не выбрана — держит позицию.
        /// </summary>
        private Vector3 ResolvePatrolPoint()
        {
            if (!patrolTargetValid)
            {
                if (Time.time < patrolHoldUntil) return FrontPosition;

                Vector2 offset = Random.insideUnitCircle * PatrolRadius;
                patrolTarget = SpawnPoint + new Vector3(offset.x, 0f, offset.y);
                patrolTargetValid = true;
            }

            Vector3 flat = Vector3.ProjectOnPlane(patrolTarget - FrontPosition, Vector3.up);
            if (flat.magnitude <= PatrolPointTolerance)
            {
                patrolTargetValid = false;
                patrolHoldUntil = Time.time + PatrolWaitTime;
                return FrontPosition;
            }

            return patrolTarget;
        }

        /// <summary>
        /// Ось модели, вдоль которой смотрят стволы. Используется, только если
        /// не задана точка Front: у этой модели нос смотрит вверх, а не вперёд
        /// по transform.forward, поэтому ось берётся из точки стрельбы.
        /// </summary>
        private void CacheNoseAxis()
        {
            noseLocal = Vector3.forward;

            Transform muzzle = MuzzleTransform();
            if (muzzle == null) return;

            Vector3 local = transform.InverseTransformDirection(muzzle.forward);
            if (local.sqrMagnitude < 0.0001f) return;

            noseLocal = local.normalized;
        }

        private Transform MuzzleTransform()
        {
            if (firePoints == null) return null;

            for (int i = 0; i < firePoints.Length; i++)
            {
                if (firePoints[i] != null) return firePoints[i];
            }

            return null;
        }

        /// <summary>
        /// Доворачивает нос в заданную сторону, но по одной оси за раз: сначала
        /// влево/вправо до полного выравнивания, и только потом вверх/вниз.
        /// Одновременного поворота по двум осям («влево-вниз», «вправо-вверх»)
        /// не получается, крена тоже нет.
        /// </summary>
        private void FaceAxis(Vector3 worldDirection, float turnSpeed, float deltaTime)
        {
            if (worldDirection.sqrMagnitude < 0.0001f) return;

            Vector3 nose = FrontDirection;
            if (nose.sqrMagnitude < 0.0001f) return;

            Vector3 direction = worldDirection.normalized;
            float step = turnSpeed * deltaTime;

            Vector3 noseFlat = Vector3.ProjectOnPlane(nose, Vector3.up);
            Vector3 wantFlat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (noseFlat.sqrMagnitude > 0.0001f && wantFlat.sqrMagnitude > 0.0001f)
            {
                float yaw = Vector3.SignedAngle(noseFlat, wantFlat, Vector3.up);
                if (Mathf.Abs(yaw) > 0.01f)
                {
                    transform.rotation = Quaternion.AngleAxis(Mathf.Sign(yaw) * Mathf.Min(Mathf.Abs(yaw), step), Vector3.up) * transform.rotation;
                    return;
                }
            }

            Vector3 pitchAxis = Vector3.Cross(nose, direction);
            if (pitchAxis.sqrMagnitude <= 0.0001f)
            {
                // Цель лежит ровно на оси носа: подойдёт любая поперечная ось
                pitchAxis = Vector3.Cross(nose, Vector3.forward);
                if (pitchAxis.sqrMagnitude <= 0.0001f) pitchAxis = Vector3.Cross(nose, Vector3.right);
                if (pitchAxis.sqrMagnitude <= 0.0001f) return;
            }

            float pitch = Vector3.Angle(nose, direction);
            if (pitch <= 0.01f) return;

            transform.rotation = Quaternion.AngleAxis(Mathf.Min(pitch, step), pitchAxis.normalized) * transform.rotation;
        }

        /// <summary>
        /// Стреляет носом в цель: линия от дула до цели должна совпасть с осью стволов.
        /// </summary>
        private void FaceTarget(Vector3 targetPoint, float turnSpeed, float deltaTime)
        {
            Transform muzzle = MuzzleTransform();
            Vector3 origin = muzzle != null ? muzzle.position : FrontPosition;
            FaceAxis(targetPoint - origin, turnSpeed, deltaTime);
        }

        private void Fire()
        {
            if (Target == null) return;

            Transform point = NextFirePoint();
            Vector3 origin = point != null ? point.position : FrontPosition;
            Vector3 direction = Target.transform.position + Vector3.up - origin;
            if (direction.sqrMagnitude < 0.0001f) return;

            direction = Hitscan.ApplySpread(direction, aimSpread);

            HitscanResult shot = Hitscan.Cast(origin, direction, shootRange, assistRadius, hitMask, transform);
            Hitscan.ApplyDamage(shot, transform, bulletDamage, false, carDamageMultiplier);

            if (showTracer) SpawnTracer(origin, shot.Point, tracerWidth, tracerColor, tracerSpeed);

            SpawnShotEffect(shot, origin);
            PlayShotSound(origin);
        }

        /// <summary>
        /// Точки стрельбы чередуются: один выстрел — одна точка, следующий — другая.
        /// Так два ствола работают по очереди, а не одновременно.
        /// </summary>
        private Transform NextFirePoint()
        {
            if (firePoints == null || firePoints.Length == 0) return null;

            for (int i = 0; i < firePoints.Length; i++)
            {
                nextFirePoint = (nextFirePoint + 1) % firePoints.Length;
                if (firePoints[nextFirePoint] != null) return firePoints[nextFirePoint];
            }

            return null;
        }

        private void SpawnShotEffect(in HitscanResult shot, Vector3 origin)
        {
            if (shotEffectPrefab == null) return;

            ShotEffect.Spawn(shotEffectPrefab, shot.Point, -shot.Direction);
            ShotEffect.Spawn(shotEffectPrefab, origin, shot.Direction, true);
        }

        private const float TracerMinTravel = 0.02f;
        private const float TracerMaxTravel = 0.15f;
        private const float TracerFadeTime = 0.06f;

        private static Material tracerMaterial;
        private static MaterialPropertyBlock tracerBlock;

        /// <summary>
        /// Луч выстрела дрона: LineRenderer летит от дула к цели за доли секунды
        /// и гаснет. Живёт прямо на дроне — толщину и цвет задаёт он сам.
        /// </summary>
        private void SpawnTracer(Vector3 from, Vector3 to, float width, Color color, float speed)
        {
            if (width <= 0.0001f) return;

            GameObject beam = new GameObject("Tracer");
            beam.transform.position = from;

            LineRenderer line = beam.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, from);
            line.startWidth = width;
            line.endWidth = width * 0.5f;
            line.numCapVertices = 2;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = Color.white;
            line.endColor = new Color(1f, 1f, 1f, 0.35f);
            line.material = ResolveTracerMaterial();
            SetTracerColor(line, color);

            float travel = Mathf.Clamp(Vector3.Distance(from, to) / Mathf.Max(1f, speed), TracerMinTravel, TracerMaxTravel);
            StartCoroutine(AnimateTracer(line, from, to, color, travel));

            Destroy(beam, travel + TracerFadeTime + 0.3f);
        }

        private IEnumerator AnimateTracer(LineRenderer line, Vector3 from, Vector3 to, Color color, float travel)
        {
            float elapsed = 0f;
            while (elapsed < travel && line != null)
            {
                elapsed += Time.deltaTime;
                line.SetPosition(1, Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / travel)));
                yield return null;
            }

            if (line == null) yield break;
            line.SetPosition(1, to);

            float fade = 0f;
            while (fade < TracerFadeTime && line != null)
            {
                fade += Time.deltaTime;
                float alpha = 1f - Mathf.Clamp01(fade / TracerFadeTime);
                SetTracerColor(line, new Color(color.r, color.g, color.b, color.a * alpha));
                yield return null;
            }

            if (line != null) Destroy(line.gameObject);
        }

        private static void SetTracerColor(LineRenderer line, Color color)
        {
            if (tracerBlock == null) tracerBlock = new MaterialPropertyBlock();

            tracerBlock.SetColor("_BaseColor", color);
            tracerBlock.SetColor("_Color", color);
            line.SetPropertyBlock(tracerBlock);
        }

        private static Material ResolveTracerMaterial()
        {
            if (tracerMaterial != null) return tracerMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            tracerMaterial = new Material(shader);
            tracerMaterial.hideFlags = HideFlags.HideAndDontSave;

            if (tracerMaterial.HasProperty("_Surface")) tracerMaterial.SetFloat("_Surface", 1f);
            if (tracerMaterial.HasProperty("_Blend")) tracerMaterial.SetFloat("_Blend", 0f);
            if (tracerMaterial.HasProperty("_ZWrite")) tracerMaterial.SetFloat("_ZWrite", 0f);
            tracerMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            tracerMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            if (tracerMaterial.HasProperty("_BaseColor")) tracerMaterial.SetColor("_BaseColor", Color.white);
            if (tracerMaterial.HasProperty("_Color")) tracerMaterial.SetColor("_Color", Color.white);

            return tracerMaterial;
        }

        private void PlayShotSound(Vector3 origin)
        {
            if (generatedShot == null) generatedShot = ProceduralAudio.Gunshot(false);

            AudioSource source = audioSource != null ? audioSource : gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.PlayOneShot(generatedShot, shotVolume);
        }

        private void OnDied(Health dead)
        {
            SphereCollider sphere = GetComponent<SphereCollider>();
            if (sphere != null) crashRadius = Mathf.Max(0.05f, sphere.radius * sphere.transform.lossyScale.magnitude);

            // Смерть не гасит полёт: дрон летит туда, куда смотрел в последний
            // миг, а гравитация постепенно тянет его вниз. Так он проносится
            // вперёд по направлению взгляда и взрывается на первом твёрдом
            // касании, а не падает камнем строго вниз.
            Vector3 direction = FrontDirection;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.down;
            crashVelocity = direction.normalized * crashSpeed;

            if (body != null)
            {
                // Гасим прошлую скорость и снимаем Rigidbody с физики: иначе он
                // продолжит тащить дрон по инерции и тот пройдёт сквозь то, что
                // задел, вместо взрыва.
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            crashing = true;
        }

        /// <summary>
        /// Готовит объект «риг крена» (tiltRig) — дочерний трансформ, на котором
        /// крутится визуальный наклон дрона. Физический Rigidbody остаётся на
        /// корне с FreezeRotation, коллайдеры тоже на корне, поэтому физика не
        /// плывёт, а модель выглядит как настоящий мультикоптер: корпус
        /// наклоняется в сторону движения и покачивается на зависании.
        ///
        /// Если под геймом уже лежит дочерний визуал (модель из префаба), он
        /// пересобирается под риг. Если нет — риг создаётся пустым, и дальше
        /// ApplyTilt работает только с наклоном root-визуала через tiltTarget.
        /// </summary>
        private void BuildTiltRig()
        {
            // Уже есть — ничего не трогаем (защита от повторного вызова).
            if (tiltRig != null) return;

            Transform existing = transform.Find("TiltRig");
            if (existing != null)
            {
                tiltRig = existing;
                return;
            }

            tiltRig = new GameObject("TiltRig").transform;
            tiltRig.SetParent(transform, false);
            tiltRig.localPosition = Vector3.zero;
            tiltRig.localRotation = Quaternion.identity;
            tiltRig.localScale = Vector3.one;

            // Переносим весь визуальный дочерний контент под риг. Коллайдеры и
            // Rigidbody остаются на корне: риг нужен только для вращения модели.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child == tiltRig) continue;
                if (child.GetComponent<Collider>() != null && child.parent == transform)
                {
                    // Дочерние коллайдеры (например, hitbox'ы) оставляем на месте,
                    // чтобы не ломать физический профиль дрона.
                    continue;
                }

                bool isVisual = child.GetComponent<Renderer>() != null
                    || HasRendererInChildren(child)
                    || child.name.IndexOf("model", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || child.name.IndexOf("visual", System.StringComparison.OrdinalIgnoreCase) >= 0;

                if (isVisual) child.SetParent(tiltRig, true);
            }
        }

        private static bool HasRendererInChildren(Transform root)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.GetComponent<Renderer>() != null) return true;
                if (HasRendererInChildren(child)) return true;
            }

            return false;
        }

        /// <summary>
        /// Плавно доводит наклон рига к целевому значению. Вызывается из Update
        /// после Move(): tiltTarget пересчитывается там же по вектору скорости.
        /// </summary>
        private void ApplyTilt(float deltaTime)
        {
            if (tiltRig == null) return;

            tiltRig.localRotation = Quaternion.Slerp(
                tiltRig.localRotation,
                tiltTarget,
                Mathf.Clamp01(deltaTime * 6f));
        }

        /// <summary>
        /// Считает целевой наклон корпуса: тангаж/рыскание по вектору скорости
        /// (дрон наклоняется туда, куда летит) плюс лёгкое покачивание при
        /// зависании. Максимальный крен ограничен, чтобы модель не «лежала».
        /// </summary>
        private void UpdateTiltTarget(Vector3 planarVelocity)
        {
            float maxTilt = Mathf.Clamp(maxTiltAngle, 5f, 45f);
            float speedForTilt = moveSpeed > 0.01f ? moveSpeed : 4f;
            float t = Mathf.Clamp01(planarVelocity.magnitude / speedForTilt);

            float hoverBob = (planarVelocity.sqrMagnitude < 0.25f)
                ? Mathf.Sin(bobPhase) * 1.6f
                : 0f;

            if (planarVelocity.sqrMagnitude < 0.0001f)
            {
                tiltTarget = Quaternion.Euler(hoverBob, 0f, 0f);
                return;
            }

            Vector3 flat = planarVelocity.normalized;
            float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float pitch = -maxTilt * t;

            tiltTarget = Quaternion.Euler(pitch + hoverBob, 0f, 0f)
                * Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f));
        }
    }
}