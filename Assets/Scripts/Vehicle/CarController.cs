using CarGame.Combat;
using CarGame.Controls;
using CarGame.Items;
using CarGame.Player;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarGame.Vehicle
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(CarAudio))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class CarController : MonoBehaviour
    {
        private const float AirLevelGain = 6f;
        private const float AirLevelMaxRadians = 0.7853982f;
        private const float RightingFullAngle = 75f;

        [System.Serializable]
        public class WheelSetup
        {
            [Tooltip("Физическое колесо (WheelCollider)")]
            public WheelCollider collider;
            [Tooltip("Мес-колесо — ребёнок своего WheelCollider. Если пусто — ищется под коллайдером")]
            public Transform visual;
            [Tooltip("Крутится мотором (используется, если выключено автоопределение приводных колёс)")]
            public bool motors = true;
            [System.NonSerialized] public bool frontWheel;
            [System.NonSerialized] public float forwardSign = 1f;
            [System.NonSerialized] public float steerSign = 1f;
            [System.NonSerialized] public Quaternion baseLocalRotation;
            [System.NonSerialized] public float roll;
        }

        [Header("Точки взаимодействия")]
        [Tooltip("Точка, из которой смотрит камера игрока, пока он в машине")]
        [SerializeField] private Transform lookPoint = null;
        [Tooltip("Точка, куда садится игрок (сиденье)")]
        [SerializeField] private Transform seatPoint = null;
        [Tooltip("Точка высадки. Если пусто — вычисляется сбоку от машины")]
        [SerializeField] private Transform exitPoint = null;
        [Tooltip("Смещение высадки, если exitPoint не задан, м")]
        [SerializeField] private float exitSideOffset = 1.8f;

        [Header("Направление вперёд")]
        [Tooltip("Точка носа машины, задаёт направление «вперёд» (W). Если пусто — transform.forward. Позволяет корректно ездить, даже если модель развёрнута не вдоль +Z")]
        [SerializeField] private Transform frontPoint = null;

        [Header("Колёса и движение")]
        [Tooltip("Если пусто — собираются из детей. motors=все")]
        [SerializeField] private WheelSetup[] wheels;
        [Tooltip("Автоопределение приводных колёс: мотор вращает только колёса, ближайшие к передней точке (frontPoint). Выключено — привод задаётся флагом motors в каждом колесе")]
        [SerializeField] private bool autoDriveFrontWheels = true;
        [Tooltip("Максимальный угол поворота колёс, градусы")]
        [SerializeField] private float maxSteerAngle = 30f;
        [Tooltip("Скорость плавного доворота руля: полный диапазон -1..1 за секунду")]
        [SerializeField] private float steeringSpeed = 5f;
        [Tooltip("Инвертировать направление разгона/поворота, если W едет назад или руль крутит не туда")]
        [SerializeField] private bool invertDriveDirection = false;
        [Tooltip("Максимальная скорость, м/с")]
        [SerializeField] private float maxSpeed = 28f;
        [Tooltip("Моторное усилие, N")]
        [SerializeField] private float motorTorque = 1600f;
        [Tooltip("Усилие тормоза, N")]
        [SerializeField] private float brakeTorque = 2600f;
        [Tooltip("Усилие ручника (Пробел), N")]
        [SerializeField] private float handbrakeTorque = 4200f;
        [Tooltip("Смещение центра масс относительно корпуса — для устойчивости")]
        [SerializeField] private Vector3 centerOfMassOffset = Vector3.zero;

        [Header("Антидрифление")]
        [Tooltip("Подавление бокового скольжения вектора скорости на высоких скоростях. 0 — выкл, больше — сильнее грип. Гасит боковую составляющую, чтобы машина не держала курс сквозь резкие манёвры.")]
        [SerializeField] private float antiDriftGrip = 6f;

        [Header("Кузов")]
        [Tooltip("Коллайдеры кузова. Колёса игнорируют их все, сидящий игрок — тоже. Если массив пуст, кузов собирается из нетриггерных коллайдеров детей")]
        [SerializeField] private Collider[] bodyColliders;

        [Header("Прочность")]
        [Tooltip("Максимальное здоровье машины")]
        [SerializeField] private float maxHealth = 250f;

        [Header("Топливо")]
        [Tooltip("Объём полного бака, единицы")]
        [SerializeField] private float maxFuel = 100f;
        [Tooltip("Сколько топлива в баке на старте сцены, единицы")]
        [SerializeField] private float startFuel = 100f;
        [Tooltip("Расход топлива в секунду на холостом ходу (пока игрок за рулём)")]
        [SerializeField] private float idleFuelBurn = 0.4f;
        [Tooltip("Дополнительный расход топлива в секунду за каждый м/с скорости")]
        [SerializeField] private float speedFuelBurn = 0.08f;

        [Header("Фары")]
        [Tooltip("Источники света фар. Можно назначить сами в сцене/префабе")]
        [SerializeField] private Light[] headlights;
        [Tooltip("Включать фары автоматически при посадке в машину")]
        [SerializeField] private bool lightsOnAtEnter = false;

        [Header("Багажник")]
        [Tooltip("Зона багажника. Именно из неё ключ берёт металл на ремонт. Задаётся вручную: зона лежит на отдельном объекте Cargo, автопоиск не нужен")]
        [SerializeField] private CargoZone cargoZone = null;

        [Header("Посадка")]
        [SerializeField] private float seatDuration = 0.55f;
        [Tooltip("После нажатия E машина становится прозрачной для игрока (коллайдеры не считаются)")]
        [SerializeField] private bool ignorePlayerCollisionWhileInside = true;

        private Rigidbody body;

        [Header("Антипереворот (как в Half-Life 2)")]
        [Tooltip("Включить: машина всегда возвращается на колёса")]
        [SerializeField] private bool antiRollEnabled = true;
        [Tooltip("С какого наклона машина начинает вставать на колёса, градусы")]
        [SerializeField] private float rightingAngle = 20f;
        [Tooltip("Запас момента сверх нужного для преодоления тяжести. 1 — впритык, 2 — вдвое с запасом")]
        [SerializeField] private float rightingGain = 2.5f;
        [Tooltip("Дополнительное мягкое выравнивание в полёте, чтобы машина садилась на колёса, 1/с")]
        [SerializeField] private float airLevelSpeed = 2.5f;
        [Tooltip("Сколько секунд машина может не двигаться к ровному положению, прежде чем доворот станет принудительным, с")]
        [SerializeField] private float rightingStallTime = 0.4f;
        [Tooltip("С какого наклона разрешён принудительный доворот, градусы. Ниже — только момент, чтобы не воевать со склонами")]
        [SerializeField] private float rightingForceAngle = 60f;
        [Tooltip("Скорость принудительного доворота, 1/с")]
        [SerializeField] private float rightingTurnSpeed = 1.5f;
        [Tooltip("Толкающая сила при принудительном довороте, м/с². Помогает перекатиться через препятствие")]
        [SerializeField] private float rightingPush = 3f;

        private PlayerController driver;
        private Health health;
        private CargoZone cargo;
        private bool seated;
        private bool transitioning;
        private float throttle;
        private float steer;
        private bool handbrake;
        private Collider[] carColliders;
        private WheelCollider[] carWheelColliders;
        private Collider[] bodyCollidersCache;
        private int groundedWheels;
        private Vector3 balanceReference = Vector3.up;
        private float lastTilt;
        private float rightingStallTimer;
        private bool warnedAboutCargo;
        private float fuel;
        private bool fuelEmptyNotified;
        private bool headlightsOn;
        private readonly List<HeadlightIntensity> headlightIntensities = new List<HeadlightIntensity>();

        public bool HeadlightsOn => headlightsOn;

        public PlayerController Driver => driver;
        public Health Health => health;
        public CargoZone Cargo => cargo;
        public bool IsOccupied => driver != null;
        public bool IsSeated => seated;
        public bool IsTransitioning => transitioning;
        public float SteerInput => steer;
        public float MaxSteerAngle => maxSteerAngle;
        public float SteerAngle => steer * maxSteerAngle;
        public int GroundedWheelCount => groundedWheels;
        public bool IsAirborne => wheels != null && wheels.Length > 0 && groundedWheels == 0;
        public Vector3 BalanceReference => balanceReference;
        public float TiltAngle => Vector3.Angle(transform.up, balanceReference);
        public Collider[] BodyColliders
        {
            get
            {
                if (bodyColliders != null && bodyColliders.Length > 0) return bodyColliders;
                return bodyCollidersCache ?? (bodyCollidersCache = FindBodyColliders());
            }
        }
        public float SpeedMs => body != null ? Vector3.Dot(FrontDirection, body.linearVelocity) : 0f;
        public float MaxSpeed => maxSpeed;
        public void ToggleHeadlights()
        {
            SetHeadlights(!headlightsOn);
        }

        public void SetHeadlights(bool on)
        {
            headlightsOn = on;

            Light[] list = ResolveHeadlights();
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
            {
                Light light = list[i];
                if (light == null) continue;

                light.enabled = on;

                // Яркость из инспектора восстанавливаем при включении: если
                // источник выключали вручную нулевой яркостью, F должен его
                // действительно зажечь, а не просто включить пустой источник.
                if (on)
                {
                    float original = GetOriginalIntensity(light);
                    if (original > 0.0001f) light.intensity = original;
                }
            }
        }

        private float GetOriginalIntensity(Light light)
        {
            int id = light.GetInstanceID();
            for (int i = 0; i < headlightIntensities.Count; i++)
            {
                if (headlightIntensities[i].id != id) continue;
                return headlightIntensities[i].intensity;
            }

            float value = light.intensity;
            headlightIntensities.Add(new HeadlightIntensity { id = id, intensity = value });
            return value;
        }

        private struct HeadlightIntensity
        {
            public int id;
            public float intensity;
        }

        /// <summary>
        /// Список фар: заданный в инспекторе, а если он пуст — все источники
        /// света внутри машины.
        /// </summary>
        private Light[] ResolveHeadlights()
        {
            if (headlights != null && headlights.Length > 0) return headlights;

            if (headlights == null) headlights = new Light[0];
            if (headlights.Length == 0)
            {
                headlights = GetComponentsInChildren<Light>(true);
            }

            return headlights;
        }

        /// <summary>
        /// Нажатие клавиши фар. Кроме привязки из настроек проверяем и саму
        /// клавиатуру напрямую: иначе новая привязка молча не работала бы.
        /// </summary>
        private static bool LightsPressedThisFrame()
        {
            if (KeyBindings.WasPressedThisFrame(GameAction.CarLights)) return true;

            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.fKey.wasPressedThisFrame;
        }

        /// <summary>Текущее топливо в баке, единицы.</summary>
        public float Fuel => fuel;
        /// <summary>Объём полного бака, единицы.</summary>
        public float MaxFuel => maxFuel;
        /// <summary>Топливо, доля полного бака 0..1 — удобно для HUD.</summary>
        public float FuelNormalized => maxFuel > 0.0001f ? Mathf.Clamp01(fuel / maxFuel) : 0f;
        /// <summary>В баке ещё есть топливо.</summary>
        public bool HasFuel => fuel > 0.0001f;
        /// <summary>Бак пуст, мотор заглох.</summary>
        public bool IsFuelEmpty => fuel <= 0.0001f;

        /// <summary>Бак впервые опустел (для HUD/предметов).</summary>
        public event System.Action FuelEmptied;

        /// <summary>Доливает топливо. Доза больше свободного места — просто полный бак.</summary>
        public void Refuel(float amount)
        {
            if (amount <= 0f) return;

            fuel = Mathf.Clamp(fuel + amount, 0f, maxFuel);
            if (fuel > 0.0001f) fuelEmptyNotified = false;
        }

        /// <summary>Жёстко задаёт уровень топлива (для загрузки сейва/отладки).</summary>
        public void SetFuel(float amount)
        {
            fuel = Mathf.Clamp(amount, 0f, maxFuel);
            if (fuel > 0.0001f) fuelEmptyNotified = false;
            else fuelEmptyNotified = true;
        }
        public Vector3 SeatPosition => seatPoint != null ? seatPoint.position : transform.position;
        public Quaternion SeatRotation
        {
            get
            {
                Quaternion baseRotation = seatPoint != null ? seatPoint.rotation : transform.rotation;
                Vector3 seatForward = baseRotation * Vector3.forward;
                seatForward.y = 0f;
                if (seatForward.sqrMagnitude < 0.0001f) seatForward = Vector3.forward;
                else seatForward.Normalize();

                float deltaYaw = Vector3.SignedAngle(seatForward, FrontDirection, Vector3.up);
                return baseRotation * Quaternion.Euler(0f, deltaYaw, 0f);
            }
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            // Машина из префаба чертежа должна быть физичной: гравитация
            // включена, тело не должно быть кинематическим и ограничения физики
            // должны быть сняты — иначе машина «весит в воздухе».
            body.useGravity = true;
            body.isKinematic = false;
            body.constraints = RigidbodyConstraints.None;
            body.centerOfMass = centerOfMassOffset;
            carColliders = GetComponentsInChildren<Collider>(true);
            bodyCollidersCache = FindBodyColliders();
            fuel = Mathf.Clamp(startFuel, 0f, maxFuel);
            fuelEmptyNotified = fuel <= 0.0001f;
            SetHeadlights(lightsOnAtEnter);
            headlightsOn = lightsOnAtEnter;

            // WheelCollider не наследуется от Collider, поэтому в carColliders его нет,
            // а GetComponentsInChildren<Collider> его не вернёт. Без отдельного списка
            // сидящий игрок продолжает упираться в колёса, а предметы багажника
            // могут цеплять их при погрузке.
            carWheelColliders = GetComponentsInChildren<WheelCollider>(true);

            AutoWireWheels();
            IgnoreWheelsWithBodyColliders();
            IgnoreWheelsWithPlayer();
            EvaluateDriveWheels();
            ResolveSupportComponents();
        }

        private void ResolveSupportComponents()
        {
            health = GetComponent<Health>();
            if (health == null) health = gameObject.AddComponent<Health>();
            if (maxHealth > 0.5f) health.Configure(maxHealth, Health.HpOwner.Car);

            // Зона багажника берётся только из поля. Автопоиск тут был бесполезен и вредил: зона лежит на отдельном объекте Cargo, поэтому ни
            // GetComponentInChildren, ни GetComponentInParent её не находили, а вложенная или родительская зона подхватывалась вместо нужной.
            cargo = cargoZone;

            if (cargo == null && !warnedAboutCargo)
            {
                warnedAboutCargo = true;
                Debug.LogWarning(
                    $"[Car] У машины '{name}' не задана зона багажника (поле Cargo Zone). " +
                    "Ключ не сможет брать металл на ремонт.", this);
            }

            if (GetComponent<CarAudio>() == null) gameObject.AddComponent<CarAudio>();

            // Страховка: без CarDestruction машина с нулевым здоровьем просто
            // продолжала бы ездить и стрелять. Если компонент уже стоит в сцене
            // (с префабом взрыва и лутом) — он и используется.
            if (GetComponent<CarDestruction>() == null) gameObject.AddComponent<CarDestruction>();
        }
        private void Update()
        {
            if (driver == null)
            {
                throttle = 0f;
                steer = 0f;
                handbrake = false;
                return;
            }

            if (!transitioning && PlayerInteractor.InteractPressed)
            {
                StartExit();
                return;
            }

            if (!transitioning && LightsPressedThisFrame())
            {
                ToggleHeadlights();
                return;
            }

            ReadInput();
        }

        private void FixedUpdate()
        {
            UpdateBalanceState();
            BurnFuel();
            Drive();
            AntiDrift();
            UpdateAntiRoll();
        }

        /// <summary>
        /// Антидрифление: гасим боковую (латеральную) составляющую вектора
        /// скорости машины, чтобы при резких манёврах и высоких скоростях не
        /// возникало бокового скольжения/дрейфа. Силу применяем через физику
        /// (AddForce), поэтому тело само само остановит скольжение — без прямого
        /// перезаписи velocity, которая может вызвать коллизии.
        /// </summary>
        private void AntiDrift()
        {
            if (body == null) return;
            if (driver == null) return;

            Vector3 forwardVelocity = FrontDirection * Vector3.Dot(FrontDirection, body.linearVelocity);
            Vector3 lateralVelocity = body.linearVelocity - forwardVelocity;
            if (lateralVelocity.sqrMagnitude < 0.0001f) return;

            body.AddForce(-lateralVelocity * antiDriftGrip, ForceMode.Force);
        }

        private void LateUpdate()
        {
            UpdateWheelVisuals();

            if (seated && driver != null && seatPoint != null && !transitioning)
            {
                driver.transform.SetPositionAndRotation(SeatPosition, SeatRotation);
            }
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;

            PlayerController player = interactor.GetComponent<PlayerController>();
            if (player == null) player = interactor.GetComponentInParent<PlayerController>();
            if (player == null) return;

            if (driver == null) StartEnter(player);
            else StartExit();
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null || transitioning) return false;

            PlayerController player = interactor.GetComponent<PlayerController>();
            if (player == null) player = interactor.GetComponentInParent<PlayerController>();
            if (player == null) return false;

            if (driver == null) return true;

            return driver == player && interactor.Hand != null && !interactor.Hand.IsHolding;
        }

        public void SetCollisionWithPlayer(PlayerController player, bool collide)
        {
            if (player == null || carColliders == null) return;

            Collider[] playerColliders = player.GetComponentsInChildren<Collider>(true);
            bool ignore = !collide && ignorePlayerCollisionWhileInside;

            for (int i = 0; i < carColliders.Length; i++)
            {
                Collider carCollider = carColliders[i];
                if (carCollider == null) continue;

                for (int j = 0; j < playerColliders.Length; j++)
                {
                    Collider playerCollider = playerColliders[j];
                    if (playerCollider == null) continue;

                    Physics.IgnoreCollision(carCollider, playerCollider, ignore);
                }
            }

            // Колёса всегда игнорируют игрока — иначе он упирается в них, даже когда не в машине.
            IgnoreWheelCollisions(playerColliders, true);
        }

        /// <summary>
        /// Колёса игнорируются отдельно от кузова: WheelCollider не Collider,
        /// поэтому в общий цикл они не попадают.
        /// </summary>
        private void IgnoreWheelCollisions(Collider[] otherColliders, bool ignore)
        {
            if (carWheelColliders == null || otherColliders == null) return;

            for (int i = 0; i < carWheelColliders.Length; i++)
            {
                WheelCollider wheel = carWheelColliders[i];
                if (wheel == null) continue;

                for (int j = 0; j < otherColliders.Length; j++)
                {
                    Collider other = otherColliders[j];
                    if (other == null) continue;

                    Physics.IgnoreCollision(wheel, other, ignore);
                }
            }
        }

        private void StartEnter(PlayerController player)
        {
            if (player == null || player.IsInCar || transitioning || seatPoint == null) return;

            transitioning = true;
            driver = player;
            SetCollisionWithPlayer(player, false);
            player.EnterVehicle(this, lookPoint);

            if (player.HasAnimator)
            {
                StartCoroutine(EnterRoutine(player));
            }
            else
            {
                StartCoroutine(EnterTeleportRoutine(player));
            }
        }

        private IEnumerator EnterTeleportRoutine(PlayerController player)
        {
            if (player == null)
            {
                transitioning = false;
                yield break;
            }

            player.FadeOut();
            yield return new WaitForSecondsRealtime(player.FadeOutDuration);

            player.transform.SetPositionAndRotation(SeatPosition, SeatRotation);
            seated = true;
            transitioning = false;

            yield return new WaitForSecondsRealtime(player.FadeHoldDuration);
            player.FadeIn();
            yield return new WaitForSecondsRealtime(player.FadeOutDuration);
        }

        private void StartExit()
        {
            if (driver == null || transitioning) return;

            transitioning = true;
            seated = false;
            PlayerController player = driver;
            StartCoroutine(ExitRoutine(player));
        }

        public void ForceExit()
        {
            StartExit();
        }

        /// <summary>
        /// Мгновенный выход водителя — без пересадки. Нужен, когда машина
        /// уничтожается: ждать плавную анимацию некогда, игрока надо выставить
        /// наружу до того, как корпус исчезнет. Анимацию входа/выхода обрываем,
        /// иначе она дописала бы себя сама и вернула игрока в машину.
        /// </summary>
        public void EjectNow()
        {
            if (driver == null) return;

            StopAllCoroutines();
            transitioning = false;
            seated = false;

            PlayerController player = driver;
            driver = null;

            Vector3 position = GetExitPosition(out Quaternion rotation);
            player.transform.SetPositionAndRotation(position, rotation);
            player.ExitVehicle();
            SetCollisionWithPlayer(player, true);
        }

        private IEnumerator EnterRoutine(PlayerController player)
        {
            Vector3 startPosition = player.transform.position;
            Quaternion startRotation = player.transform.rotation;
            Vector3 endPosition = SeatPosition;
            Quaternion endRotation = SeatRotation;
            float elapsed = 0f;

            while (elapsed < seatDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / seatDuration);
                t = t * t * (3f - 2f * t);
                player.transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, endPosition, t),
                    Quaternion.Slerp(startRotation, endRotation, t));
                yield return null;
            }

            player.transform.SetPositionAndRotation(endPosition, endRotation);
            seated = true;
            transitioning = false;
        }

        private IEnumerator ExitRoutine(PlayerController player)
        {
            Vector3 startPosition = player.transform.position;
            Quaternion startRotation = player.transform.rotation;
            Vector3 endPosition = GetExitPosition(out Quaternion endRotation);
            float elapsed = 0f;

            while (elapsed < seatDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / seatDuration);
                t = t * t * (3f - 2f * t);
                player.transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, endPosition, t),
                    Quaternion.Slerp(startRotation, endRotation, t));
                yield return null;
            }

            player.transform.SetPositionAndRotation(endPosition, endRotation);
            driver = null;
            transitioning = false;
            player.ExitVehicle();
            SetCollisionWithPlayer(player, true);
        }

        private Vector3 GetExitPosition(out Quaternion rotation)
        {
            if (exitPoint != null)
            {
                rotation = exitPoint.rotation;
                return exitPoint.position;
            }

            rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(FrontDirection, Vector3.up), Vector3.up);
            Transform reference = seatPoint != null ? seatPoint : transform;
            Vector3 position = reference.position + transform.right * exitSideOffset;

            if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore))
            {
                position = hit.point + Vector3.up * 0.05f;
            }

            return position;
        }

        private void ReadInput()
        {
            if (driver == null)
            {
                throttle = 0f;
                steer = 0f;
                handbrake = false;
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                throttle = 0f;
                steer = 0f;
                handbrake = false;
                return;
            }

            float inputThrottle = 0f;
            float inputSteer = 0f;
            if (KeyBindings.IsPressed(GameAction.CarForward)) inputThrottle += 1f;
            if (KeyBindings.IsPressed(GameAction.CarBack)) inputThrottle -= 1f;
            if (KeyBindings.IsPressed(GameAction.CarRight)) inputSteer += 1f;
            if (KeyBindings.IsPressed(GameAction.CarLeft)) inputSteer -= 1f;

            // Опьянение инвертирует управление (и пешком, и за рулём).
            if (PlayerController.ControlsInverted)
            {
                inputThrottle = -inputThrottle;
                inputSteer = -inputSteer;
            }

            throttle = Mathf.Clamp(inputThrottle, -1f, 1f);
            steer = Mathf.MoveTowards(steer, Mathf.Clamp(inputSteer, -1f, 1f), steeringSpeed * Time.deltaTime);
            handbrake = KeyBindings.IsPressed(GameAction.Handbrake);
        }

        public Vector3 FrontDirection
        {
            get
            {
                if (frontPoint != null)
                {
                    Vector3 direction = frontPoint.position - transform.position;
                    direction.y = 0f;
                    if (direction.sqrMagnitude > 0.0001f) return direction.normalized;
                }
                return transform.forward;
            }
        }

        /// <summary>
        /// Расход топлива: только пока за рулём. Холостой расход плюс расход,
        /// пропорциональный скорости. Когда бак опустел — мотор глохнет
        /// (см. Drive), а наружу один раз уходит FuelEmptied.
        /// </summary>
        private void BurnFuel()
        {
            if (driver == null || fuel <= 0.0001f) return;

            float burn = idleFuelBurn + Mathf.Abs(SpeedMs) * speedFuelBurn;
            fuel -= burn * Time.fixedDeltaTime;

            if (fuel > 0.0001f) return;

            fuel = 0f;
            if (fuelEmptyNotified) return;

            fuelEmptyNotified = true;
            FuelEmptied?.Invoke();
        }

        private void Drive()
        {
            if (wheels == null) return;

            float forwardSpeed = SpeedMs;
            bool brakingOppositeInput = throttle != 0f
                && Mathf.Sign(throttle) != Mathf.Sign(forwardSpeed)
                && Mathf.Abs(forwardSpeed) > 0.5f;

            for (int i = 0; i < wheels.Length; i++)
            {
                WheelSetup wheel = wheels[i];
                if (wheel == null || wheel.collider == null) continue;

                wheel.collider.steerAngle = wheel.frontWheel ? steer * maxSteerAngle * wheel.steerSign : 0f;

                float wheelBrakeTorque = 0f;
                float wheelMotorTorque = 0f;

                if (driver == null)
                {
                    wheelBrakeTorque = brakeTorque;
                }
                else if (handbrake)
                {
                    wheelBrakeTorque = handbrakeTorque;
                }
                else if (brakingOppositeInput)
                {
                    wheelBrakeTorque = brakeTorque;
                }
                else if (HasFuel)
                {
                    bool drive = autoDriveFrontWheels ? wheel.frontWheel : wheel.motors;
                    if (drive)
                    {
                        float speedFactor = Mathf.Clamp01(1f - Mathf.Abs(forwardSpeed) / maxSpeed);
                        wheelMotorTorque = throttle * motorTorque * speedFactor * wheel.forwardSign;
                    }
                }
                else
                {
                    wheelMotorTorque = 0f;
                }

                wheel.collider.brakeTorque = wheelBrakeTorque;
                wheel.collider.motorTorque = wheelMotorTorque;
            }
        }

        private void EvaluateDriveWheels()
        {
            if (wheels == null || wheels.Length == 0) return;

            Vector3 center = transform.position;
            Vector3 front = FrontDirection;

            for (int i = 0; i < wheels.Length; i++)
            {
                WheelSetup wheel = wheels[i];
                if (wheel == null || wheel.collider == null) continue;

                Vector3 wheelForward = wheel.collider.transform.forward;
                wheelForward.y = 0f;
                if (wheelForward.sqrMagnitude < 0.0001f) wheelForward = front;

                float align = Vector3.Dot(wheelForward.normalized, front);
                float flip = invertDriveDirection ? -1f : 1f;
                wheel.steerSign = (align >= 0f ? 1f : -1f) * flip;
                wheel.forwardSign = (align >= 0f ? 1f : -1f) * flip;

                if (wheel.visual != null) wheel.baseLocalRotation = wheel.visual.localRotation;
            }

            float[] projections = new float[wheels.Length];
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < wheels.Length; i++)
            {
                WheelSetup wheel = wheels[i];
                if (wheel == null || wheel.collider == null) continue;

                Vector3 offset = wheel.collider.transform.position - center;
                offset.y = 0f;
                projections[i] = Vector3.Dot(offset, front);
                sum += projections[i];
                count++;
            }

            float average = count > 0 ? sum / count : 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                WheelSetup wheel = wheels[i];
                if (wheel == null) continue;
                wheel.frontWheel = projections[i] >= average;
            }
        }

        private void UpdateWheelVisuals()
        {
            if (wheels == null) return;

            for (int i = 0; i < wheels.Length; i++)
            {
                WheelSetup wheel = wheels[i];
                if (wheel == null || wheel.visual == null || wheel.collider == null) continue;
                if (wheel.visual == wheel.collider.transform) continue;

                wheel.collider.GetWorldPose(out Vector3 position, out _);
                wheel.visual.position = position;

                wheel.roll += wheel.collider.rpm * 6f * Time.deltaTime;

                float steerAngleDegrees = wheel.frontWheel ? steer * maxSteerAngle * wheel.steerSign : 0f;
                wheel.visual.localRotation = Quaternion.Euler(0f, steerAngleDegrees, 0f)
                    * wheel.baseLocalRotation
                    * Quaternion.AngleAxis(wheel.roll, Vector3.forward);
            }
        }

        private void UpdateBalanceState()
        {
            groundedWheels = 0;
            Vector3 sum = Vector3.zero;

            if (wheels != null)
            {
                for (int i = 0; i < wheels.Length; i++)
                {
                    WheelSetup wheel = wheels[i];
                    if (wheel == null || wheel.collider == null) continue;
                    if (!wheel.collider.GetGroundHit(out WheelHit hit)) continue;

                    groundedWheels++;
                    sum += hit.normal;
                }
            }

            balanceReference = sum.sqrMagnitude < 0.0001f ? Vector3.up : sum.normalized;
        }

        private void UpdateAntiRoll()
        {
            if (!antiRollEnabled) return;
            if (body == null || wheels == null || wheels.Length == 0) return;

            float tilt = TiltAngle;
            if (tilt <= 0.01f)
            {
                rightingStallTimer = 0f;
                lastTilt = tilt;
                return;
            }

            if (!TryGetRightingDirection(transform.up, balanceReference, out Vector3 direction)) return;

            if (tilt <= rightingAngle)
            {
                rightingStallTimer = 0f;
                lastTilt = tilt;
                if (groundedWheels == 0 && airLevelSpeed > 0.01f) AddAirLevelTorque(direction, tilt);
                return;
            }

            body.AddTorque(direction * ComputeRightingTorque(tilt), ForceMode.Force);
            if (groundedWheels == 0 && airLevelSpeed > 0.01f) AddAirLevelTorque(direction, tilt);
            UpdateRightingStall(tilt, direction);
        }

        private float ComputeRightingTorque(float tilt)
        {
            Vector3 centerOfMass = body.worldCenterOfMass;
            Vector3 pivot = FindSupportPoint();

            float comHeight = Mathf.Max(0.05f, Vector3.Dot(centerOfMass - pivot, balanceReference));
            float rollRadius = GetRollRadius();

            float radians = tilt * Mathf.Deg2Rad;
            float lever = comHeight * Mathf.Abs(Mathf.Sin(radians))
                + rollRadius * (1f - Mathf.Abs(Mathf.Cos(radians)));

            float ramp = Mathf.Pow(Mathf.InverseLerp(rightingAngle, RightingFullAngle, tilt), 2f);
            return lever * body.mass * Physics.gravity.magnitude * Mathf.Max(0.01f, rightingGain) * ramp;
        }

        private Vector3 FindSupportPoint()
        {
            if (wheels != null && groundedWheels > 0)
            {
                Vector3 sum = Vector3.zero;
                int count = 0;

                for (int i = 0; i < wheels.Length; i++)
                {
                    WheelSetup wheel = wheels[i];
                    if (wheel == null || wheel.collider == null) continue;
                    if (!wheel.collider.GetGroundHit(out _)) continue;

                    sum += wheel.collider.transform.position;
                    count++;
                }

                if (count > 0) return sum / count;
            }

            if (TryGetBodyBounds(out Bounds bodyBounds))
            {
                Vector3 support = new Vector3(
                    Mathf.Abs(balanceReference.x),
                    Mathf.Abs(balanceReference.y),
                    Mathf.Abs(balanceReference.z));
                return bodyBounds.center - balanceReference * Vector3.Dot(bodyBounds.extents, support);
            }

            return transform.position;
        }

        private float GetRollRadius()
        {
            if (!TryGetBodyBounds(out Bounds bodyBounds)) return 0.5f;

            Vector3 extents = bodyBounds.extents;
            return Mathf.Max(0.2f, Mathf.Min(extents.x, extents.z));
        }

        /// <summary>Объединённые границы всех коллайдеров кузова (массив).</summary>
        private bool TryGetBodyBounds(out Bounds bounds)
        {
            bounds = default;
            Collider[] bodies = BodyColliders;
            if (bodies == null || bodies.Length == 0) return false;

            bool has = false;
            for (int i = 0; i < bodies.Length; i++)
            {
                Collider collider = bodies[i];
                if (collider == null) continue;

                if (!has)
                {
                    bounds = collider.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return has;
        }

        private void UpdateRightingStall(float tilt, Vector3 direction)
        {
            if (tilt < lastTilt - 0.05f)
            {
                lastTilt = tilt;
                rightingStallTimer = 0f;
                return;
            }

            lastTilt = Mathf.Min(lastTilt, tilt);
            if (tilt < rightingForceAngle) return;

            rightingStallTimer += Time.fixedDeltaTime;
            if (rightingStallTimer < rightingStallTime) return;

            ForceUpright(direction, tilt);
        }

        private void ForceUpright(Vector3 direction, float tilt)
        {
            Quaternion level = Quaternion.FromToRotation(transform.up, balanceReference) * body.rotation;
            float turn = Mathf.Clamp01(Mathf.Max(0.01f, rightingTurnSpeed) * Time.fixedDeltaTime);
            body.MoveRotation(Quaternion.Slerp(body.rotation, level, turn));

            if (rightingPush <= 0.01f) return;

            float push = rightingPush * body.mass * Mathf.Sin(Mathf.Clamp(tilt, 0f, 90f) * Mathf.Deg2Rad);
            body.AddForce(direction * push, ForceMode.Force);
        }

        private void AddAirLevelTorque(Vector3 direction, float tilt)
        {
            float amount = airLevelSpeed * Mathf.Min(tilt * Mathf.Deg2Rad, AirLevelMaxRadians) * AirLevelGain;
            if (amount <= 0.0001f) return;

            body.AddTorque(direction * (amount * body.mass), ForceMode.Force);
        }

        private static bool TryGetRightingDirection(Vector3 up, Vector3 reference, out Vector3 direction)
        {
            Vector3 axis = Vector3.Cross(up, reference);
            if (axis.sqrMagnitude > 0.0001f)
            {
                direction = axis.normalized;
                return true;
            }

            Vector3 referenceUp = reference.sqrMagnitude < 0.0001f ? Vector3.up : reference.normalized;
            axis = Vector3.Cross(referenceUp, Vector3.right);
            if (axis.sqrMagnitude <= 0.0001f) axis = Vector3.Cross(referenceUp, Vector3.forward);
            if (axis.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.zero;
                return false;
            }

            direction = axis.normalized;
            return true;
        }

        public void SetStoredItemCollision(PickupItem item, bool ignore)
        {
            if (item == null || carColliders == null) return;

            Collider[] itemColliders = item.GetComponentsInChildren<Collider>(true);
            Collider[] bodies = BodyColliders;

            if (bodies != null)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    Collider bodyCollider = bodies[i];
                    if (bodyCollider == null) continue;

                    for (int j = 0; j < itemColliders.Length; j++)
                    {
                        Collider itemCollider = itemColliders[j];
                        if (itemCollider == null) continue;

                        Physics.IgnoreCollision(bodyCollider, itemCollider, ignore);
                    }
                }
            }

            // Предмет в багажнике не должен цеплять и колёса: они не Collider,
            // поэтому основной цикл их не охватывает.
            IgnoreWheelCollisions(itemColliders, ignore);
        }

        private void IgnoreWheelsWithBodyColliders()
        {
            if (wheels == null) return;

            Collider[] bodies = BodyColliders;
            if (bodies == null || bodies.Length == 0) return;

            for (int i = 0; i < wheels.Length; i++)
            {
                WheelSetup wheel = wheels[i];
                if (wheel == null || wheel.collider == null) continue;

                for (int j = 0; j < bodies.Length; j++)
                {
                    Collider bodyCollider = bodies[j];
                    if (bodyCollider == null) continue;
                    Physics.IgnoreCollision(wheel.collider, bodyCollider, true);
                }
            }
        }

        /// <summary>Колёса не должны упираться в игрока на улице — только в кузов.</summary>
        private void IgnoreWheelsWithPlayer()
        {
            PlayerController player = FindPlayer();
            if (player == null) return;

            Collider[] playerColliders = player.GetComponentsInChildren<Collider>(true);
            IgnoreWheelCollisions(playerColliders, true);
        }

        private static PlayerController FindPlayer()
        {
            return Object.FindFirstObjectByType<PlayerController>();
        }

        private Collider[] FindBodyColliders()
        {
            if (carColliders == null) return System.Array.Empty<Collider>();

            int count = 0;
            for (int i = 0; i < carColliders.Length; i++)
            {
                Collider collider = carColliders[i];
                if (collider == null || collider.isTrigger) continue;
                count++;
            }

            if (count == 0) return System.Array.Empty<Collider>();

            Collider[] bodies = new Collider[count];
            int index = 0;
            for (int i = 0; i < carColliders.Length; i++)
            {
                Collider collider = carColliders[i];
                if (collider == null || collider.isTrigger) continue;
                bodies[index++] = collider;
            }

            return bodies;
        }

        private void AutoWireWheels()
        {
            if (wheels == null || wheels.Length == 0)
            {
                WheelCollider[] colliders = GetComponentsInChildren<WheelCollider>();
                wheels = new WheelSetup[colliders.Length];
                for (int i = 0; i < colliders.Length; i++)
                {
                    wheels[i] = new WheelSetup
                    {
                        collider = colliders[i],
                        motors = true,
                    };
                }
            }

            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;

                if (wheels[i].collider == null)
                {
                    wheels[i].collider = GetComponentInChildren<WheelCollider>();
                }

                if (wheels[i].visual == null && wheels[i].collider != null)
                {
                    MeshFilter meshFilter = wheels[i].collider.GetComponentInChildren<MeshFilter>();
                    wheels[i].visual = meshFilter != null ? meshFilter.transform : null;
                }
            }
        }

        private void OnDestroy()
        {
            if (driver == null) return;
            driver.ExitVehicle();
        }
    }
}