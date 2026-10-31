using System.Collections;
using CarGame.Audio;
using CarGame.Player;
using CarGame.Vehicle;
using UnityEngine;

namespace CarGame.Combat
{
    public enum TurretType
    {
        [InspectorName("Горошина")] Peashooter = 0,
        [InspectorName("Град")] Hail = 1,
        [InspectorName("Анегилятор")] Annihilator = 2
    }

    public enum ProcessorType
    {
        [InspectorName("Процессор M1")] M1 = 0,
        [InspectorName("Процессор M2")] M2 = 1,
        [InspectorName("Процессор M3")] M3 = 2
    }

    [DisallowMultipleComponent]
    public class PlayerTurret : MonoBehaviour
    {
        [Header("Тип турели")]
        [Tooltip("Горошина — 1 выстрел раз в 1.5 с. Град — 2 точки стрельбы, залп одновременно, 5 выстрелов в секунду. Анегилятор — красный луч раз в 2 с, убивает всё живое. Все три работают через рейкаст, без баллистики")]
        [SerializeField] private TurretType type = TurretType.Peashooter;

        [Header("Процессор")]
        [Tooltip("Чем лучше процессор, тем дальше турель видит цель и тем точнее стреляет")]
        [SerializeField] private ProcessorType processor = ProcessorType.M1;

[Header("Обзор")]
        [Tooltip("Зона обзора по горизонтали, градусы. Ноль — круговой обзор 360°")]
        [SerializeField] private float fieldOfView = 120f;
        [Tooltip("Использовать дальность и точность из процессора вместо значений ниже")]
        [SerializeField] private bool useProcessorStats = true;
        [Tooltip("Дальность обнаружения для M1, м")]
        [SerializeField] private float rangeM1 = 22f;
        [Tooltip("Дальность обнаружения для M2, м")]
        [SerializeField] private float rangeM2 = 32f;
        [Tooltip("Дальность обнаружения для M3, м")]
        [SerializeField] private float rangeM3 = 45f;
        [Tooltip("Разброс прицеливания для M1, градусы")]
        [SerializeField] private float spreadM1 = 6f;
        [Tooltip("Разброс прицеливания для M2, градусы")]
        [SerializeField] private float spreadM2 = 2.5f;
        [Tooltip("Разброс прицеливания для M3, градусы")]
        [SerializeField] private float spreadM3 = 0.5f;

        [Header("Цели")]
        [Tooltip("Слои, по которым ищутся роботы. Турель бьёт только врагов (Health.IsEnemy) и никогда — игрока или его машину")]
        [SerializeField] private LayerMask targetMask = ~0;

        [Header("Поворот")]
        [Tooltip("Скорость поворота башни, градусов/с")]
        [SerializeField] private float turretTurnSpeed = 200f;
        [Tooltip("Часть турели, которая поворачивается. Если пусто — берётся корень")]
        [SerializeField] private Transform turretHead;
        [Tooltip("Насколько развести выстрелы, если точки стрельбы не заданы, градусы")]
        [SerializeField] private float twinSpread = 0.9f;

        [Header("Холостой ход")]
        [Tooltip("На сколько градусов голова качается в стороны, когда нет цели")]
        [SerializeField] private float idleSweepAngle = 35f;
        [Tooltip("Скорость качания головы в покое")]
        [SerializeField] private float idleSweepSpeed = 1.2f;

        [Header("Точки стрельбы")]
        [Tooltip("Точки стрельбы. Все они дают залп одновременно, в один кадр. Если список пуст — выстрел идёт из головы турели")]
        [SerializeField] private Transform[] firePoints = new Transform[0];

        [Header("Стрельба")]
        [Tooltip("Урон одного луча")]
        [SerializeField] private float bulletDamage = 9f;
        [Tooltip("Мгновенное убийство цели (лазер Анегилятора)")]
        [SerializeField] private bool instantKill;
        [Tooltip("Насколько широким лучом проверяется попадание, м. Больше — надёжнее ловит цель, но может задеть соседние объекты")]
        [SerializeField] private float beamRadius = 0.06f;
        [Tooltip("Слои, которые останавливают луч")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Визуал выстрела")]
        [Tooltip("Показывать трассер луча. Для игрока это выглядит как пуля, по факту это рейкаст")]
        [SerializeField] private bool showTracer = true;
        [Tooltip("Толщина трассера у основания, м")]
        [SerializeField] private float tracerStartWidth = 0.035f;
        [Tooltip("Сколько живёт трассер, с")]
        [SerializeField] private float tracerDuration = 0.05f;
        [Tooltip("Цвет трассера")]
        [SerializeField] private Color tracerColor = new Color(1f, 0.85f, 0.3f, 1f);
        [Tooltip("Скорость полёта трассера, м/с")]
        [SerializeField] private float tracerSpeed = 220f;
        [Tooltip("Показывать красный луч Анегилятора")]
        [SerializeField] private bool showLaser = true;
        [Tooltip("Сколько живёт луч Анегилятора, с")]
        [SerializeField] private float laserDuration = 0.12f;
        [Tooltip("Толщина луча Анегилятора, м")]
        [SerializeField] private float laserWidth = 0.14f;

        [Header("Эффект выстрела")]
        [Tooltip("Префаб эффекта. Один и тот же эффект ставится в точку попадания и у ствола, различается только направление")]
        [SerializeField] private GameObject shotEffectPrefab;

        [Header("Звук")]
        [Tooltip("Громкость выстрела")]
        [SerializeField] private float shotVolume = 0.6f;
        [Tooltip("Звук обычного выстрела. Если пусто — генерируется")]
        [SerializeField] private AudioClip shotClip;
        [Tooltip("Звук лазера. Если пусто — генерируется")]
        [SerializeField] private AudioClip laserClip;

        [Header("Отладка")]
        [Tooltip("Писать в консоль, когда турель берёт и теряет цель")]
        [SerializeField] private bool logTargeting;

        private Health target;
        private Transform head;
        private Transform barrel;
        private float nextFireTime;
        private AudioSource audioSource;
        private AudioClip generatedShot;
        private AudioClip generatedLaser;
        private bool hadTarget;
        private Quaternion restLocalRotation;
        private float idlePhase;

        private enum HeadState { Idle, Engaged, Returning }
        private HeadState headState = HeadState.Idle;

        public TurretType Type => type;
        public ProcessorType Processor => processor;
        public Vector3 FrontPosition => barrel != null ? barrel.position : head != null ? head.position : transform.position;
        public Vector3 FrontForward => barrel != null ? barrel.forward : head != null ? head.forward : transform.forward;
        public Vector3 EyePosition => FrontPosition + Vector3.up * 0.35f;
        public Health Target => target;
        public float DetectRange => useProcessorStats ? RangeForProcessor(processor) : rangeM1;
        public float Spread => useProcessorStats ? SpreadForProcessor(processor) : spreadM1;
        public float FireInterval => type == TurretType.Annihilator ? 2f : type == TurretType.Hail ? 0.2f : 1.5f;

        /// <summary>
        /// Сколько точек стрельбы задано в сцене. Ноль — выстрел пойдёт из головы.
        /// </summary>
        public int FirePointCount
        {
            get
            {
                if (firePoints == null) return 0;

                int count = 0;
                for (int i = 0; i < firePoints.Length; i++)
                {
                    if (firePoints[i] != null) count++;
                }

                return count;
            }
        }

        private Transform FirstFirePoint()
        {
            if (firePoints == null) return null;

            for (int i = 0; i < firePoints.Length; i++)
            {
                if (firePoints[i] != null) return firePoints[i];
            }

            return null;
        }

        private void Awake()
        {
            head = turretHead != null ? turretHead : transform;
            barrel = FirstFirePoint();
            restLocalRotation = head.localRotation;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.minDistance = 4f;
            audioSource.maxDistance = 70f;
        }

        private void Update()
        {
            target = FindTarget();
            if (target == null)
            {
                if (hadTarget && logTargeting) Debug.Log($"[Turret] '{name}' потеряла цель", this);
                if (hadTarget) headState = HeadState.Returning;
                hadTarget = false;
                IdleHead();
                return;
            }

            if (!hadTarget && logTargeting) Debug.Log($"[Turret] '{name}' взяла цель: {target.name}", this);
            hadTarget = true;
            headState = HeadState.Engaged;

            AimAt(TargetPoint());

            if (Time.time < nextFireTime) return;
            if (!IsAimed(TargetPoint())) return;

            nextFireTime = Time.time + FireInterval;
            Fire();
        }

        // В покое голова качается в стороны, а после боя плавно возвращается
        // в исходное положение, которое было до начала движения.
        private void IdleHead()
        {
            if (head == null) return;

            if (headState == HeadState.Returning)
            {
                head.localRotation = Quaternion.RotateTowards(
                    head.localRotation,
                    restLocalRotation,
                    turretTurnSpeed * Time.deltaTime);

                if (Quaternion.Angle(head.localRotation, restLocalRotation) <= 0.5f)
                {
                    head.localRotation = restLocalRotation;
                    idlePhase = 0f;
                    headState = HeadState.Idle;
                }

                return;
            }

            idlePhase += Time.deltaTime * idleSweepSpeed;
            float yaw = Mathf.Sin(idlePhase) * idleSweepAngle;
            head.localRotation = Quaternion.Euler(0f, yaw, 0f) * restLocalRotation;
        }

        private Vector3 TargetPoint()
        {
            return target.transform.position + Vector3.up * 0.8f;
        }

        private Health FindTarget()
        {
            // Турель помогает игроку: ищет ближайшего робота и никогда не
            // стреляет по игроку или его машине (они не помечены как IsEnemy).
            Collider[] hits = Physics.OverlapSphere(transform.position, DetectRange, targetMask, QueryTriggerInteraction.Collide);
            Health best = null;
            float bestSquare = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i];
                if (c == null) continue;
                if (c.isTrigger) continue;

                Health candidate = c.GetComponentInParent<Health>();
                if (candidate == null) candidate = c.GetComponent<Health>();
                if (candidate == null || !candidate.IsEnemy || candidate.IsDead) continue;
                if (candidate.GetComponentInParent<PlayerController>() != null) continue;
                if (candidate.GetComponent<CarGame.Vehicle.CarController>() != null) continue;
                if (!IsInFieldOfView(candidate.transform.position)) continue;
                if (!ClearLineOfSight(candidate)) continue;

                float square = (candidate.transform.position - transform.position).sqrMagnitude;
                if (square >= bestSquare) continue;

                bestSquare = square;
                best = candidate;
            }

            return best;
        }

        private bool ClearLineOfSight(Health candidate)
        {
            if (Physics.Linecast(EyePosition, candidate.transform.position + Vector3.up * 0.8f, out RaycastHit hit, hitMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null) return true;
                if (hit.collider.isTrigger) return false;
                Health hitHealth = hit.collider.GetComponentInParent<Health>();
                if (hitHealth == null) return false;
                return hitHealth == candidate;
            }

            return true;
        }

        private bool IsInFieldOfView(Vector3 point)
        {
            if (fieldOfView <= 0.01f) return true;

            Vector3 toTarget = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
            if (toTarget.sqrMagnitude < 0.0001f) return true;

            Vector3 forward = Vector3.ProjectOnPlane(FrontForward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) return true;

            return Vector3.Angle(forward, toTarget) <= fieldOfView * 0.5f;
        }

        private void AimAt(Vector3 point)
        {
            Vector3 origin = FrontPosition;
            Vector3 direction = point - origin;
            if (direction.sqrMagnitude < 0.0001f) return;

            Transform aimPoint = head != null ? head : transform;
            Quaternion delta = Quaternion.FromToRotation(FrontForward, direction.normalized);
            aimPoint.rotation = Quaternion.RotateTowards(
                aimPoint.rotation,
                delta * aimPoint.rotation,
                turretTurnSpeed * Time.deltaTime);
        }

        private bool IsAimed(Vector3 point)
        {
            Vector3 direction = (point - FrontPosition).normalized;
            return Vector3.Angle(FrontForward, direction) <= 6f;
        }

        private void Fire()
        {
            if (type == TurretType.Annihilator)
            {
                FireAnnihilator();
                return;
            }

            Vector3 aimOrigin = barrel != null ? barrel.position : (head != null ? head.position : transform.position);
            Vector3 targetDir = target != null ? (TargetPoint() - aimOrigin).normalized : FrontForward;

            if (FirePointCount > 0)
            {
                for (int i = 0; i < firePoints.Length; i++)
                {
                    if (firePoints[i] != null) Shoot(firePoints[i], 0f, targetDir);
                }
            }
            else
            {
                int barrels = type == TurretType.Hail ? 2 : 1;
                for (int i = 0; i < barrels; i++)
                {
                    float yaw = barrels > 1 ? twinSpread * (i - (barrels - 1) * 0.5f) : 0f;
                    Shoot(null, yaw, targetDir);
                }
            }

            PlayShotSound(generatedShotGet(), 0.8f);
        }

        private void FireAnnihilator()
        {
            // Выстрел начинается у точки огня (ствол/голова), а не от «глаза»
            // над ней: луч должен лететь от FirePoint, а не от позиции выше.
            Vector3 origin = FrontPosition;
            Vector3 direction = FrontForward;

            HitscanResult shot = Hitscan.Cast(origin, direction, DetectRange, beamRadius, hitMask, transform);
            if (shot.Target != null && shot.Target.IsEnemy)
                Hitscan.ApplyDamage(shot, transform, bulletDamage * 3f, instantKill, 1f);

            SpawnShotEffect(shot, origin);

            if (showLaser) SpawnTracer(origin, shot.Point, laserWidth, new Color(1f, 0.12f, 0.12f, 0.95f), tracerSpeed, laserDuration);

            PlayShotSound(generatedLaserGet(), 1f);
        }

        /// <summary>
        /// Один выстрел. Точка стрельбы задаёт позицию вылета, направление идёт к цели.
        /// </summary>
        private void Shoot(Transform point, float yaw, Vector3 targetDir)
        {
            Vector3 origin = point != null ? point.position : EyePosition;
            Vector3 direction = targetDir.sqrMagnitude > 0.0001f ? targetDir : (point != null ? point.forward : FrontForward);

            if (point != null && target != null)
            {
                direction = (TargetPoint() - point.position).normalized;
            }
            else if (Mathf.Abs(yaw) > 0.001f && point == null) direction = Quaternion.AngleAxis(yaw, Vector3.up) * direction;

            direction = Hitscan.ApplySpread(direction, Spread);

            HitscanResult shot = Hitscan.Cast(origin, direction, DetectRange, beamRadius, hitMask, transform);
            if (shot.Target != null && shot.Target.IsEnemy)
                Hitscan.ApplyDamage(shot, transform, bulletDamage, instantKill, 1f);

            SpawnShotEffect(shot, origin);

            if (showTracer) SpawnTracer(origin, shot.Point, tracerStartWidth, tracerColor, tracerSpeed, tracerDuration);
        }

        /// <summary>
        /// Эффект в точке попадания смотрит туда, откуда прилетел выстрел, а у
        /// ствола — по направлению выстрела.
        /// </summary>
        private void SpawnShotEffect(in HitscanResult shot, Vector3 origin)
        {
            if (shotEffectPrefab == null) return;

            ShotEffect.Spawn(shotEffectPrefab, shot.Point, -shot.Direction);
            ShotEffect.Spawn(shotEffectPrefab, origin, shot.Direction, true);
        }

        private const float TracerMinTravel = 0.02f;
        private const float TracerMaxTravel = 0.15f;

        private static Material tracerMaterial;
        private static MaterialPropertyBlock tracerBlock;

        /// <summary>
        /// Луч выстрела турели: LineRenderer летит от ствола к цели за доли
        /// секунды, задерживается и гаснет за fade. Живёт прямо на турели —
        /// толщину и цвет задаёт она сама.
        /// </summary>
        private void SpawnTracer(Vector3 from, Vector3 to, float width, Color color, float speed, float fade)
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

            float hold = Mathf.Max(0.01f, fade);
            float travel = Mathf.Clamp(Vector3.Distance(from, to) / Mathf.Max(1f, speed), TracerMinTravel, TracerMaxTravel);
            StartCoroutine(AnimateTracer(line, from, to, color, travel, hold));

            Destroy(beam, travel + hold + 0.3f);
        }

        private IEnumerator AnimateTracer(LineRenderer line, Vector3 from, Vector3 to, Color color, float travel, float fade)
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

            float faded = 0f;
            while (faded < fade && line != null)
            {
                faded += Time.deltaTime;
                float alpha = 1f - Mathf.Clamp01(faded / fade);
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

        private AudioClip generatedShotGet()
        {
            if (shotClip != null) return shotClip;
            if (generatedShot == null) generatedShot = ProceduralAudio.Gunshot(false);
            return generatedShot;
        }

        private AudioClip generatedLaserGet()
        {
            if (laserClip != null) return laserClip;
            if (generatedLaser == null) generatedLaser = ProceduralAudio.Laser();
            return generatedLaser;
        }

        private void PlayShotSound(AudioClip clip, float gain)
        {
            if (clip == null || audioSource == null) return;
            audioSource.PlayOneShot(clip, shotVolume * gain);
        }

        private float RangeForProcessor(ProcessorType value)
        {
            switch (value)
            {
                case ProcessorType.M1: return rangeM1;
                case ProcessorType.M2: return rangeM2;
                case ProcessorType.M3: return rangeM3;
                default: return rangeM1;
            }
        }

        private float SpreadForProcessor(ProcessorType value)
        {
            switch (value)
            {
                case ProcessorType.M1: return spreadM1;
                case ProcessorType.M2: return spreadM2;
                case ProcessorType.M3: return spreadM3;
                default: return spreadM1;
            }
        }
    }
}