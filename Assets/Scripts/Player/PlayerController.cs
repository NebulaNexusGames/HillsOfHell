using CarGame.Buildings;
using CarGame.Combat;
using CarGame.Controls;
using CarGame.UI;
using CarGame.Vehicle;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CarGame.Player
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerRespawn))]
    [DisallowMultipleComponent]
    public class PlayerController : MonoBehaviour
    {
        [Header("Перемещение")]
        [Tooltip("Скорость ходьбы, м/с")]
        [SerializeField] private float walkSpeed = 4f;
        [Tooltip("Скорость бега (Shift), м/с")]
        [SerializeField] private float runSpeed = 7.5f;
        [Tooltip("Насколько быстро набирается скорость, м/с^2")]
        [SerializeField] private float acceleration = 16f;
        [Tooltip("Насколько быстро скорость падает до нуля, м/с^2")]
        [SerializeField] private float deceleration = 22f;
        [Tooltip("Ускорение свободного падения, м/с^2")]
        [SerializeField] private float gravity = -22f;
        [Tooltip("Высота прыжка, м. 0 — прыжок отключён (Пробел)")]
        [SerializeField] private float jumpHeight = 1.1f;

        [Header("Обзор")]
        [Tooltip("Пивото камеры. Если пусто — берётся первый Camera в детях")]
        [SerializeField] private Transform viewPivot;
        [Tooltip("Чувствительность по горизонтали")]
        [SerializeField] private float lookSensitivityX = 2.2f;
        [Tooltip("Чувствительность по вертикали")]
        [SerializeField] private float lookSensitivityY = 2.2f;
        [Tooltip("Сглаживание обзора. Больше — плавнее, 0 — мгновенный отклик")]
        [SerializeField] private float lookSmoothing = 14f;
        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;
        [Tooltip("Сколько можно довернуть голову в машине, градусы")]
        [SerializeField] private float inCarPitchLimit = 70f;
        [Tooltip("Сколько можно довернуть голову в машине по горизонтали, градусы")]
        [SerializeField] private float inCarYawLimit = 120f;
        [Tooltip("Прятать курсор и захватывать мышь")]
        [SerializeField] private bool lockCursor = true;

        [Header("Сглаживание камеры")]
        [Tooltip("Сглаживание камеры в машине, с. Больше — плавнее, меньше — отзывчивее")]
        [SerializeField] private float inCarSmoothing = 0.15f;

        [Header("Покачивание камеры при движении")]
        [Tooltip("Включить покачивание камеры в такт шагам. На своих двоих амплитуда зависит от скорости бега")]
        [SerializeField] private bool moveBobEnabled = true;
        [Tooltip("Амплитуда вертикального покачивания на обычной скорости ходьбы, м")]
        [SerializeField] private float bobAmplitude = 0.035f;
        [Tooltip("Скорость покачивания на шагу, шагов/с")]
        [SerializeField] private float bobFrequency = 1.9f;
        [Tooltip("Горизонтальное покачивание в долю от вертикального. 0 — только вверх-вниз")]
        [SerializeField] private float lateralBobRatio = 0.6f;
        [Tooltip("Скорость наката амплитуды. Больше — отзывчивее, меньше — плавнее")]
        [SerializeField] private float bobResponse = 8f;
        [Tooltip("Скорость, до которой камера не покачивается. Отсекает дрожание на месте и медленном шаге")]
        [SerializeField] private float bobDeadZone = 0.4f;
        [Tooltip("Скорость, на которой покачивание выходит на максимум, м/с")]
        [SerializeField] private float bobFullSpeed = 6f;

        [Header("Наклон камеры при стрейфе")]
        [Tooltip("Максимальный крен камеры при стрейфе, градусы. Знак задаёт сторону наклона")]
        [SerializeField] private float strafeTiltAngle = 2.5f;
        [Tooltip("Скорость наклона и возврата камеры, 1/с. Больше — резче, меньше — плавнее")]
        [SerializeField] private float strafeTiltSmooth = 7f;

        [Header("Покачивание в машине")]
        [Tooltip("Амплитуда дрожи в машине на максимальной скорости, м. Раньше здесь бралась ходовая синусоида, и камера в машине выглядела так, будто игрок идёт пешком")]
        [SerializeField] private float carBobAmplitude = 0.012f;
        [Tooltip("Частота дрожи в машине, Гц. Должна быть заметно выше ходовой, иначе снова читается как шаги")]
        [SerializeField] private float carBobFrequency = 7.5f;

        [Header("Тряска камеры от урона")]
        [Tooltip("Сила импульса тряски при получении урона")]
        [SerializeField] private float damageShakeStrength = 0.55f;
        [Tooltip("Насколько тряска направлена от угрозы, 0..1. 1 — целиком в сторону от источника урона")]
        [SerializeField] private float damageShakeDirectionWeight = 0.75f;
        [Tooltip("Максимальный сдвиг камеры от тряски, м")]
        [SerializeField] private float shakeMaxPosition = 0.22f;
        [Tooltip("Максимальный доворот камеры от тряски, градусы")]
        [SerializeField] private float shakeMaxAngle = 3.5f;

        [Header("Ускорение (энергетик)")]
        [Tooltip("Насколько быстро фактический множитель скорости догоняет заданный эффектом, 1/с")]
        [SerializeField] private float speedBoostResponse = 12f;

        [Header("Опьянение (пиво)")]
        [Tooltip("Максимальный наклон камеры от опьянения, градусы")]
        [SerializeField] private float drunkMaxAngle = 5f;
        [Tooltip("Скорость качания камеры, Гц")]
        [SerializeField] private float drunkFrequency = 1.7f;
        [Tooltip("Инвертировать управление, пока действует опьянение (и пешком, и за рулём)")]
        [SerializeField] private bool drunkInvertControls = true;
        [Tooltip("Минимальная пауза между случайными переворотами камеры, сек")]
        [SerializeField] private float drunkFlipMinInterval = 4f;
        [Tooltip("Максимальная пауза между случайными переворотами камеры, сек")]
        [SerializeField] private float drunkFlipMaxInterval = 9f;
        [Tooltip("За сколько секунд экран затемняется перед переворотом")]
        [SerializeField] private float drunkFlipFade = 0.55f;
        [Range(0f, 1f)]
        [Tooltip("Максимальная плотность затемнения перед переворотом")]
        [SerializeField] private float drunkFlipDarkness = 0.85f;
        [Tooltip("Насколько сильно поворачивается обзор за один переворот, градусы")]
        [SerializeField] private float drunkFlipTurn = 200f;
        [Tooltip("Скорость возврата повёрнутого обзора в норму после отрезвления, градусы/с")]
        [SerializeField] private float drunkFlipRecover = 40f;

        [Header("FOV от скорости")]
        [Tooltip("FOV камеры на месте, градусы")]
        [SerializeField] private float baseFov = 60f;
        [Tooltip("Насколько FOV растёт на максимальной скорости бега, градусы")]
        [SerializeField] private float runFovBoost = 8f;
        [Tooltip("Насколько FOV растёт на максимальной скорости машины, градусы")]
        [SerializeField] private float carFovBoost = 22f;
        [Tooltip("Скорость, на которой FOV выходит на максимум при беге, м/с")]
        [SerializeField] private float runFovFullSpeed = 7.5f;
        [Tooltip("Насколько быстро FOV догоняет цель")]
        [SerializeField] private float fovResponse = 6f;

        [Header("Аниматор (необязательно)")]
        [Tooltip("Если пусто — берётся первый Animator в детях")]
        [SerializeField] private Animator animator;
        [Tooltip("Триггер, который проигрывается при посадке")]
        [SerializeField] private string sitTrigger = "Sit";
        [Tooltip("Триггер, который проигрывается при высадке")]
        [SerializeField] private string standTrigger = "Stand";
        [Tooltip("Bool-параметр «сидит в машине»")]
        [SerializeField] private string inCarParameter = "InCar";

        [Header("Затемнение при посадке (без аниматора)")]
        [Tooltip("Если аниматор не указан, при посадке экран гаснет, игрок телепортируется в машину и свет возвращается")]
        [SerializeField] private float fadeOutDuration = 0.35f;
        [Tooltip("Сколько держать чёрный экран при телепорте, с")]
        [SerializeField] private float fadeHoldDuration = 0.15f;
        [Tooltip("Цвет затемнения")]
        [SerializeField] private Color fadeColor = Color.black;

        private CharacterController body;
        private Health health;
        private Vector3 horizontalVelocity;
        private float verticalSpeed;
        private float viewYaw;
        private float viewPitch;
        private Vector2 smoothLook;
        private float currentFadeAlpha;
        private float targetFadeAlpha;
        private CarController currentVehicle;
        private Transform vehicleViewAnchor;
        private Vector3 footLocalPosition;
        private Vector3 cameraPositionVelocity;
        private Vector3 bobOffset;
        private float bobPhase;
        private float bobRoll;
        private float currentBobAmplitude;
        private float strafeTilt;
        private CameraShake shake;
        private CameraDrunk drunk;
        private float speedBoostTarget = 1f;
        private float speedBoostMultiplier = 1f;
        private float speedBoostRemaining;
        private float carrySlowMultiplier = 1f;
        private bool carrySprintLocked;
        private Camera viewCamera;
        private float currentFov;
        private Quaternion initialRotation;
        private bool controlEnabled = true;
        private bool bodyEnabled = true;
        private bool lookEnabled = true;
        private float drunkFlipYaw;
        private float drunkFlipPitch;
        private Coroutine drunkFlipRoutine;
        private Image drunkDarken;
        private static bool controlsInverted;        private int sitTriggerHash;
        private int standTriggerHash;
        private int inCarParameterHash;

        private static Texture2D whiteTexture;

        /// <summary>Вызывается в момент прыжка. На него подписан звук игрока.</summary>
        public event Action Jumped;

        public static bool RunPressed => KeyBindings.IsPressed(GameAction.Sprint);

        public bool HasAnimator => animator != null;
        public bool IsInCar => currentVehicle != null;
        public CarController CurrentVehicle => currentVehicle;
        public bool ControlEnabled => controlEnabled;
        public bool LookEnabled => lookEnabled;
        public float Speed => horizontalVelocity.magnitude;
        public CharacterController Body => body;
        public Transform ViewPivot => viewPivot;
        public float FadeOutDuration => fadeOutDuration;
        public float FadeHoldDuration => fadeHoldDuration;
        public bool IsFaded => currentFadeAlpha > 0.01f;
        public Health Health => health;

        /// <summary>
        /// Управление инвертировано опьянением. Читается CarController,
        /// чтобы за рулём ввод тоже переворачивался.
        /// </summary>
        public static bool ControlsInverted => controlsInverted;

        /// <summary>
        /// Множитель чувствительности мыши из настроек. Умножается на базовую
        /// чувствительность каждого игрока, поэтому подходит и для меню, и для
        /// игры, и не требует трогать инспектор.
        /// </summary>
        public static float SensitivityMultiplier = 1f;

        /// <summary>Инверсия вертикального обзора из настроек.</summary>
        public static bool InvertY;

        /// <summary>
        /// Включена ли тряска и наклон камеры (настройка графики). Когда выключено,
        /// камера не дёргается от урона и не кренится при стрейфе, а предмет в руке
        /// не качается. Читается PlayerController и HandBob.
        /// </summary>
        public static bool CameraMotionEnabled = true;

        public void FadeOut()
        {
            targetFadeAlpha = 1f;
        }

        public void FadeIn()
        {
            targetFadeAlpha = 0f;
        }

        private void Awake()
        {
            // Создаём здесь, а не в инициализаторе поля: конструктор MonoBehaviour
            // не должен дергать Unity API, иначе компонент не поднимется вовсе.
            if (shake == null) shake = new CameraShake();
            shake.Initialize();

            if (drunk == null) drunk = new CameraDrunk();
            drunk.Initialize();

            body = GetComponent<CharacterController>();
            if (GetComponent<PlayerRespawn>() == null) gameObject.AddComponent<PlayerRespawn>();
            health = GetComponent<Health>();
            if (viewPivot == null)
            {
                Camera childCamera = GetComponentInChildren<Camera>();
                if (childCamera != null)
                {
                    Transform cameraParent = childCamera.transform.parent;
                    viewPivot = cameraParent != null && cameraParent != transform ? cameraParent : childCamera.transform;
                }
            }
            if (animator == null) animator = GetComponentInChildren<Animator>();
            footLocalPosition = viewPivot != null ? viewPivot.localPosition : Vector3.zero;
            initialRotation = transform.rotation;
            sitTriggerHash = Animator.StringToHash(sitTrigger);
            standTriggerHash = Animator.StringToHash(standTrigger);
            inCarParameterHash = Animator.StringToHash(inCarParameter);

            if (health != null) health.Damaged += OnDamaged;
        }

        private void OnDestroy()
        {
            if (health != null) health.Damaged -= OnDamaged;
        }

        private void Start()
        {
            SetCursorLocked(lockCursor);
        }

        private void OnDisable()
        {
            SetCursorLocked(false);
            controlsInverted = false;
            SetDrunkDarken(0f);
        }

        private void Update()
        {
            UpdateFade();
            UpdateSpeedBoost();
            UpdateDrunkState();

            if (bodyEnabled)
            {
                // Управление отключено (открыт инвентарь и т.п.) — ввод не
                // читается, но физика тела остаётся живой: игрок не должен
                // зависать в воздухе, если инвентарь открыли в момент падения.
                if (controlEnabled)
                {
                    UpdateHorizontalVelocity();
                }
                else
                {
                    horizontalVelocity = Vector3.MoveTowards(
                        horizontalVelocity, Vector3.zero, deceleration * Time.deltaTime);
                }

                UpdateVerticalSpeed();
            }
            else
            {
                horizontalVelocity = Vector3.zero;
                verticalSpeed = 0f;
            }

            if (body.enabled)
            {
                Vector3 motion = (horizontalVelocity + Vector3.up * verticalSpeed) * Time.deltaTime;
                body.Move(motion);
            }
        }

        private void OnGUI()
        {
            if (currentFadeAlpha <= 0.001f) return;

            Color previous = GUI.color;
            GUI.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, currentFadeAlpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), WhiteTexture, ScaleMode.StretchToFill);
            GUI.color = previous;
        }

        private void UpdateFade()
        {
            if (currentFadeAlpha == targetFadeAlpha) return;
            float rate = Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeOutDuration);
            currentFadeAlpha = Mathf.MoveTowards(currentFadeAlpha, targetFadeAlpha, rate);
        }

        private static Texture2D WhiteTexture
        {
            get
            {
                if (whiteTexture == null)
                {
                    whiteTexture = new Texture2D(1, 1);
                    whiteTexture.SetPixel(0, 0, Color.white);
                    whiteTexture.Apply();
                }
                return whiteTexture;
            }
        }

        private void LateUpdate()
        {
            UpdateViewBob();
            UpdateView();
            ApplyShake();
            ApplyDrunk();
            UpdateFov();
        }

        /// <summary>
        /// Тряска применяется после UpdateView, иначе обзор затирал бы её в тот же кадр.
        /// </summary>
        private void ApplyShake()
        {
            if (!CameraMotionEnabled) return;
            if (viewPivot == null || shake == null) return;

            Vector3 offset;
            float roll;
            shake.Evaluate(Time.deltaTime, shakeMaxPosition, shakeMaxAngle, out offset, out roll);

            if (offset.sqrMagnitude <= 0.000001f && Mathf.Abs(roll) <= 0.0001f) return;

            Quaternion rollRotation = Quaternion.Euler(0f, 0f, roll);

            if (currentVehicle == null)
            {
                viewPivot.localPosition = footLocalPosition + rollRotation * offset;
                viewPivot.localRotation = viewPivot.localRotation * rollRotation;
            }
            else
            {
                viewPivot.position += viewPivot.rotation * offset;
                viewPivot.rotation = viewPivot.rotation * rollRotation;
            }
        }

        /// <summary>
        /// Расширяет FOV с ростом скорости: на своих двоих — по бегу, в машине — по скорости.
        /// Ощущение скорости появляется само, тряску при этом трогать не нужно.
        /// </summary>
        private void UpdateFov()
        {
            ResolveViewCamera();
            if (viewCamera == null) return;

            float target = baseFov;

            if (currentVehicle != null)
            {
                float full = currentVehicle.MaxSpeed > 0.01f ? currentVehicle.MaxSpeed : 28f;
                target = baseFov + carFovBoost * Mathf.Clamp01(Mathf.Abs(currentVehicle.SpeedMs) / full);
            }
            else
            {
                float speed = new Vector2(horizontalVelocity.x, horizontalVelocity.z).magnitude;
                float full = runFovFullSpeed > 0.01f ? runFovFullSpeed : 1f;
                target = baseFov + runFovBoost * Mathf.Clamp01(speed / full);
            }

            float blend = fovResponse > 0.001f ? 1f - Mathf.Exp(-fovResponse * Time.deltaTime) : 1f;
            currentFov = Mathf.Lerp(currentFov, target, blend);

            viewCamera.fieldOfView = currentFov;
        }

        private void ResolveViewCamera()
        {
            if (viewCamera != null || viewPivot == null) return;

            viewCamera = viewPivot.GetComponent<Camera>();
            if (viewCamera == null) viewCamera = viewPivot.GetComponentInChildren<Camera>();

            // Стартуем от текущего FOV камеры, иначе первый кадр делает рывок с нуля.
            if (viewCamera != null) currentFov = viewCamera.fieldOfView;
        }

        /// <summary>
        /// Тряска от удара. awayFromThreat — направление ОТ источника урона,
        /// поэтому камера отталкивается от угрозы, а не к ней.
        /// </summary>
        public void ShakeFromDamage(Vector3 awayFromThreat, float strengthScale)
        {
            if (!CameraMotionEnabled) return;
            shake.AddImpulse(damageShakeStrength * strengthScale, awayFromThreat, damageShakeDirectionWeight);
        }

        public void ClearShake()
        {
            shake.Clear();
        }

        /// <summary>
        /// Ускоряет игрока на время действия энергетика. Множитель меньше 1
        /// тоже допустим — так эффект можно переиспользовать для замедления.
        /// Действие не складывается: если эффект уже идёт, берётся дольший срок.
        /// </summary>
        public void ApplySpeedBoost(float multiplier, float duration)
        {
            speedBoostTarget = Mathf.Max(0.1f, multiplier);
            speedBoostRemaining = Mathf.Max(speedBoostRemaining, Mathf.Max(0f, duration));
        }

        /// <summary>
        /// Замедление от груза в руках (строй-предмет). В отличие от энергетика,
        /// действует ровно пока его держат: кто выставил — тот и снимает.
        /// lockSprint дополнительно запрещает бег (режим стройки).
        /// </summary>
        public void SetCarrySlow(float multiplier, bool lockSprint)
        {
            carrySlowMultiplier = Mathf.Clamp(multiplier, 0.05f, 1f);
            carrySprintLocked = lockSprint;
        }

        public void ClearCarrySlow()
        {
            carrySlowMultiplier = 1f;
            carrySprintLocked = false;
        }

        /// <summary>Включает качание камеры от пива на заданное время.</summary>
        public void ApplyDrunk(float duration, float strength)
        {
            if (drunk == null) drunk = new CameraDrunk();
            drunk.Initialize();
            drunk.Trigger(duration, strength);

            if (isActiveAndEnabled)
            {
                if (drunkFlipRoutine != null) StopCoroutine(drunkFlipRoutine);
                drunkFlipRoutine = StartCoroutine(DrunkFlipLoop());
            }
        }

        private void UpdateDrunkState()
        {
            bool active = drunk != null && drunk.Active;
            controlsInverted = drunkInvertControls && active;

            if (!active)
            {
                // Корутины не идут, когда объект выключен, поэтому обзор и
                // затемнение возвращаем в норму здесь же, а не только в корутине.
                drunkFlipYaw = Mathf.MoveTowards(drunkFlipYaw, 0f, drunkFlipRecover * Time.deltaTime);
                drunkFlipPitch = Mathf.MoveTowards(drunkFlipPitch, 0f, drunkFlipRecover * Time.deltaTime);
                if (drunkFlipRoutine != null)
                {
                    StopCoroutine(drunkFlipRoutine);
                    drunkFlipRoutine = null;
                }
                SetDrunkDarken(0f);
            }
        }

        private IEnumerator DrunkFlipLoop()
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(1f, Mathf.Max(1.1f, drunkFlipMinInterval)));

            while (drunk != null && drunk.Active)
            {
                yield return StartCoroutine(DrunkFlipOnce());
                float wait = UnityEngine.Random.Range(drunkFlipMinInterval, Mathf.Max(drunkFlipMinInterval + 0.01f, drunkFlipMaxInterval));
                yield return new WaitForSeconds(wait);
            }

            drunkFlipRoutine = null;
        }

        private IEnumerator DrunkFlipOnce()
        {
            EnsureDrunkDarken();
            float half = Mathf.Max(0.05f, drunkFlipFade) * 0.5f;

            yield return FadeDrunkDarken(0f, drunkFlipDarkness, half);

            float sign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            drunkFlipYaw += drunkFlipTurn * sign;
            drunkFlipPitch = Mathf.Repeat(drunkFlipPitch + UnityEngine.Random.Range(-25f, 25f) + 180f, 360f) - 180f;

            yield return FadeDrunkDarken(drunkFlipDarkness, 0f, half);
        }

        private IEnumerator FadeDrunkDarken(float from, float to, float duration)
        {
            duration = Mathf.Max(0.0001f, duration);
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                SetDrunkDarken(Mathf.Lerp(from, to, Mathf.Clamp01(t / duration)));
                yield return null;
            }
            SetDrunkDarken(to);
        }

        private void SetDrunkDarken(float alpha)
        {
            if (drunkDarken == null) return;
            drunkDarken.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
        }

        private void EnsureDrunkDarken()
        {
            if (drunkDarken != null) return;

            Canvas canvas = ScreenOverlay.FindOrCreateScreenCanvas("DrunkCanvas");
            if (canvas == null) return;

            drunkDarken = ScreenOverlay.CreateFullScreenImage(canvas.transform, "DrunkDarken", -1);
            drunkDarken.rectTransform.SetAsLastSibling();
            SetDrunkDarken(0f);
        }

        private void OnDamaged(Health damaged, DamageInfo info)
        {
            // Урон приходит по info.direction: от атакующего к игроку. Уводим камеру
            // по тому же вектору — то есть прочь от угрозы. Если направление пустое
            // (источник взрыва может его не задать), берём вектор от источника.
            Vector3 away = info.direction;

            if (away.sqrMagnitude <= 0.0001f && info.source != null)
                away = transform.position - info.source.transform.position;

            float scale = damaged != null && damaged.Max > 0.01f
                ? Mathf.Clamp(info.amount / damaged.Max, 0.15f, 1.5f)
                : 1f;

            ShakeFromDamage(away.normalized, scale);
        }

        private void UpdateViewBob()
        {
            if (viewPivot == null) return;

            float speed = BobSpeed();
            float targetAmplitude = 0f;

            if (moveBobEnabled && speed > bobDeadZone)
            {
                if (currentVehicle != null)
                {
                    // В машине намеренно НЕ используется ходовая синусоида: на частоте
                    // шагов с доворотом камера выглядит так, будто игрок идёт пешком.
                    // Здесь короткая частая дрожь, привязанная к скорости, и без крена.
                    float full = currentVehicle.MaxSpeed > 0.01f ? currentVehicle.MaxSpeed : 28f;
                    targetAmplitude = carBobAmplitude * Mathf.Clamp01(speed / full);
                }
                else
                {
                    float normalized = Mathf.Clamp01((speed - bobDeadZone) / Mathf.Max(0.01f, bobFullSpeed));
                    targetAmplitude = bobAmplitude * normalized;
                }
            }

            float blend = bobResponse > 0.001f
                ? 1f - Mathf.Exp(-bobResponse * Time.deltaTime)
                : 1f;

            currentBobAmplitude = Mathf.Lerp(currentBobAmplitude, targetAmplitude, blend);

            if (currentBobAmplitude <= 0.00001f)
            {
                bobOffset = Vector3.zero;
                bobPhase = 0f;
                bobRoll = 0f;
                return;
            }

            float frequency = currentVehicle != null
                ? carBobFrequency
                : bobFrequency;

            bobPhase += frequency * Time.deltaTime * Mathf.PI * 2f;
            if (bobPhase > Mathf.PI * 2f) bobPhase -= Mathf.PI * 2f;

            float wave = Mathf.Sin(bobPhase);

            bobOffset = new Vector3(
                wave * currentBobAmplitude * lateralBobRatio,
                wave * currentBobAmplitude,
                0f);

            // Крен даём только на своих двоих: в машине он и превращал дрожь в
            // похожую на шаги покачивание головы.
            bobRoll = currentVehicle != null
                ? 0f
                : wave * currentBobAmplitude * lateralBobRatio * 4f;
        }

        private float BobSpeed()
        {
            if (currentVehicle != null)
                return Mathf.Abs(currentVehicle.SpeedMs);

            return new Vector2(horizontalVelocity.x, horizontalVelocity.z).magnitude;
        }

        private void ResetViewBob()
        {
            bobOffset = Vector3.zero;
            bobPhase = 0f;
            bobRoll = 0f;
            strafeTilt = 0f;
            currentBobAmplitude = 0f;
        }

        public void SetControlEnabled(bool enabled)
        {
            controlEnabled = enabled;
        }

        public void EnterVehicle(CarController vehicle, Transform viewAnchor)
        {
            currentVehicle = vehicle;
            vehicleViewAnchor = viewAnchor;
            if (vehicleViewAnchor != null && viewPivot != null) viewPivot.position = vehicleViewAnchor.position;
            cameraPositionVelocity = Vector3.zero;
            ResetViewBob();
            ClearShake();
            controlEnabled = false;
            bodyEnabled = false;
            body.enabled = false;
            horizontalVelocity = Vector3.zero;
            verticalSpeed = 0f;
            viewYaw = 0f;
            smoothLook = Vector2.zero;

            // В машине игрок неуязвим: урон от пуль и взрывов идёт по кузову,
            // а не по водителю.
            if (health != null) health.SetInvulnerable(true);

            Vector3 front = vehicle.FrontDirection;
            front.y = 0f;
            if (front.sqrMagnitude < 0.0001f) front = transform.forward;
            if (front.sqrMagnitude < 0.0001f) front = Vector3.forward;
            if (viewPivot != null) viewPivot.rotation = Quaternion.LookRotation(front.normalized, Vector3.up);
            SetAnimatorBool(inCarParameterHash, true);
            SetAnimatorTrigger(sitTriggerHash);
        }

        public void ExitVehicle()
        {
            currentVehicle = null;
            vehicleViewAnchor = null;
            if (viewPivot != null) viewPivot.localPosition = footLocalPosition;
            cameraPositionVelocity = Vector3.zero;
            ResetViewBob();
            ClearShake();
            controlEnabled = true;
            bodyEnabled = true;
            body.enabled = true;
            verticalSpeed = 0f;
            viewYaw = 0f;
            viewPitch = 0f;
            smoothLook = Vector2.zero;

            // Снова уязвим, как только вышел из машины.
            if (health != null) health.SetInvulnerable(false);

            transform.rotation = initialRotation;
            SetAnimatorBool(inCarParameterHash, false);
            SetAnimatorTrigger(standTriggerHash);
        }

        private void UpdateHorizontalVelocity()
        {
            Vector2 input = ReadMoveAxis();
            if (controlsInverted) input = -input;
            Vector3 desired = Vector3.zero;
            if (input.sqrMagnitude > 0.001f)
            {
                Vector3 forward = ViewForward;
                Vector3 right = ViewRight;
                bool running = RunPressed && !carrySprintLocked;
                float targetSpeed = (running ? runSpeed : walkSpeed) * speedBoostMultiplier * carrySlowMultiplier;
                desired = (forward * input.y + right * input.x) * targetSpeed;
            }

            float rate = desired.sqrMagnitude > 0.001f ? acceleration : deceleration;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, desired, rate * Time.deltaTime);
        }

        /// <summary>
        /// Держит множитель скорости от энергетика. Пока таймер идёт — множитель
        /// равен заданному, когда истёк — плавно возвращается к 1: FOV и покачивание
        /// успевают перестроиться, а не дёргаются на последнем кадре.
        /// </summary>
        private void UpdateSpeedBoost()
        {
            if (speedBoostRemaining > 0f)
            {
                speedBoostRemaining = Mathf.Max(0f, speedBoostRemaining - Time.deltaTime);
                if (speedBoostRemaining <= 0f) speedBoostTarget = 1f;
            }

            float blend = speedBoostResponse > 0.001f
                ? 1f - Mathf.Exp(-speedBoostResponse * Time.deltaTime)
                : 1f;

            speedBoostMultiplier = Mathf.Lerp(speedBoostMultiplier, speedBoostTarget, blend);
            if (Mathf.Abs(speedBoostMultiplier - 1f) <= 0.0005f && speedBoostTarget == 1f)
                speedBoostMultiplier = 1f;
        }

        /// <summary>
        /// Наклон камеры от пива. Применяется после UpdateView и ApplyShake,
        /// иначе обзор затирал бы доворот в тот же кадр.
        /// </summary>
        private void ApplyDrunk()
        {
            if (drunk == null || viewPivot == null) return;

            float roll;
            float yaw;
            drunk.Evaluate(Time.deltaTime, drunkMaxAngle, drunkFrequency, out roll, out yaw);

            if (Mathf.Abs(roll) <= 0.0001f && Mathf.Abs(yaw) <= 0.0001f) return;

            viewPivot.localRotation = viewPivot.localRotation * Quaternion.Euler(0f, yaw, roll);
        }

        private void UpdateVerticalSpeed()
        {
            if (body.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
            else verticalSpeed += gravity * Time.deltaTime;

            // Прыжок — это управление: без него игрок может открыть инвентарь,
            // но не должен подпрыгивать. Гравитация при этом продолжает работать.
            if (!controlEnabled) return;

            bool jumpPressed = KeyBindings.WasPressedThisFrame(GameAction.Jump);
            if (jumpHeight > 0f && jumpPressed && body.isGrounded)
            {
                verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
                Jumped?.Invoke();
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.rigidbody == null) return;

            BuildingDoor door = hit.rigidbody.GetComponent<BuildingDoor>();
            if (door == null) return;

            door.PushFromPlayer(body.velocity, hit.point);
        }

        private void UpdateView()
        {
            if (viewPivot == null) return;
            if (!lookEnabled) return;

            Mouse mouse = Mouse.current;
            Vector2 rawLook = mouse == null ? Vector2.zero : mouse.delta.ReadValue();
            rawLook.x *= lookSensitivityX * SensitivityMultiplier;
            rawLook.y *= lookSensitivityY * SensitivityMultiplier * (InvertY ? -1f : 1f);

            if (lookSmoothing > 0.001f)
            {
                float blend = 1f - Mathf.Exp(-lookSmoothing * Time.deltaTime);
                smoothLook.x += (rawLook.x - smoothLook.x) * blend;
                smoothLook.y += (rawLook.y - smoothLook.y) * blend;
            }
            else
            {
                smoothLook = rawLook;
            }

            if (currentVehicle == null)
            {
                viewYaw += smoothLook.x;
                viewPitch = Mathf.Clamp(viewPitch - smoothLook.y, minPitch, maxPitch);

                // Крен при стрейфе: стрейф вправо наклоняет камеру вправо (и
                // наоборот). Сглаживаем, чтобы наклон наезжал и уходил плавно, и
                // складываем с креном от покачивания.
                float strafeInput = controlEnabled ? ReadMoveAxis().x : 0f;
                float tiltTarget = CameraMotionEnabled ? -strafeInput * strafeTiltAngle : 0f;
                strafeTilt = Mathf.Lerp(strafeTilt, tiltTarget, 1f - Mathf.Exp(-strafeTiltSmooth * Time.deltaTime));

                Quaternion footRotation = Quaternion.Euler(viewPitch + drunkFlipPitch, viewYaw + drunkFlipYaw, 0f);
                viewPivot.localRotation = footRotation * Quaternion.Euler(0f, 0f, bobRoll + strafeTilt);
                viewPivot.localPosition = footLocalPosition + footRotation * bobOffset;
                return;
            }

            viewYaw = Mathf.Clamp(viewYaw + smoothLook.x, -inCarYawLimit, inCarYawLimit);
            viewPitch = Mathf.Clamp(viewPitch - smoothLook.y, -inCarPitchLimit, inCarPitchLimit);

            Vector2 neutral = GetVehicleNeutral();
            Quaternion targetRotation = currentVehicle.SeatRotation
                * Quaternion.Euler(neutral.x, neutral.y, 0f)
                * Quaternion.Euler(viewPitch + drunkFlipPitch, viewYaw + drunkFlipYaw, 0f);

            Quaternion targetBobbed = targetRotation * Quaternion.Euler(0f, 0f, bobRoll);

            if (vehicleViewAnchor != null)
            {
                // Позиция камеры в машине не сглаживается. Якорь жёстко привязан
                // к кузову, а SmoothDamp — фильтр низкой частоты, поэтому на
                // движущейся цели он даёт постоянный сдвиг примерно
                // скорость × время сглаживания: на 25 м/с это около четырёх
                // метров, и камера уезжала сквозь лобовое стекло. Плавность
                // посадки даёт EnterVehicle, который ставит камеру в якорь сразу.
                //
                // Дрожь приезжает в системе координат кузова, а не обзора: если
                // повернуть голову, вертикальная дрожь не должна превращаться в
                // боковую.
                viewPivot.position = vehicleViewAnchor.position + vehicleViewAnchor.rotation * bobOffset;
                cameraPositionVelocity = Vector3.zero;
            }

            if (inCarSmoothing > 0.001f)
            {
                float blend = 1f - Mathf.Exp(-(1f / inCarSmoothing) * Time.deltaTime);
                viewPivot.rotation = Quaternion.Slerp(viewPivot.rotation, targetBobbed, blend);
            }
            else
            {
                viewPivot.rotation = targetBobbed;
            }
        }

                private Vector2 GetVehicleNeutral()
        {
            if (currentVehicle == null) return Vector2.zero;

            Vector3 front = currentVehicle.FrontDirection;
            front.y = 0f;
            if (front.sqrMagnitude < 0.0001f) front = Vector3.forward;
            Quaternion targetRotation = Quaternion.LookRotation(front.normalized, Vector3.up);
            Quaternion relative = Quaternion.Inverse(currentVehicle.SeatRotation) * targetRotation;
            Vector3 euler = relative.eulerAngles;
            return new Vector2(NormalizeAngle(euler.x), NormalizeAngle(euler.y));
        }

        private static float NormalizeAngle(float degrees)
        {
            return Mathf.Repeat(degrees + 180f, 360f) - 180f;
        }

        private Vector3 ViewForward
        {
            get
            {
                Vector3 forward = viewPivot != null ? viewPivot.forward : transform.forward;
                forward = Vector3.ProjectOnPlane(forward, Vector3.up);
                return forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
            }
        }

        private Vector3 ViewRight
        {
            get
            {
                Vector3 right = viewPivot != null ? viewPivot.right : transform.right;
                right = Vector3.ProjectOnPlane(right, Vector3.up);
                return right.sqrMagnitude > 0.0001f ? right.normalized : transform.right;
            }
        }

        private static Vector2 ReadMoveAxis()
        {
            float x = 0f;
            float y = 0f;
            if (KeyBindings.IsPressed(GameAction.MoveRight)) x += 1f;
            if (KeyBindings.IsPressed(GameAction.MoveLeft)) x -= 1f;
            if (KeyBindings.IsPressed(GameAction.MoveForward)) y += 1f;
            if (KeyBindings.IsPressed(GameAction.MoveBack)) y -= 1f;
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        private void SetCursorLocked(bool locked)
        {
            if (!Application.isPlaying) return;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>
        /// Разрешает или запрещает обзор мышью. Выключается на время открытых
        /// меню, иначе клик по инвентарю одновременно доворачивал бы камеру.
        /// </summary>
        public void SetLookEnabled(bool value)
        {
            lookEnabled = value;
        }

        private void SetAnimatorTrigger(int hash)
        {
            if (!HasAnimatorParameter(hash, AnimatorControllerParameterType.Trigger)) return;
            animator.SetTrigger(hash);
        }

        private void SetAnimatorBool(int hash, bool value)
        {
            if (!HasAnimatorParameter(hash, AnimatorControllerParameterType.Bool)) return;
            animator.SetBool(hash, value);
        }

        private bool HasAnimatorParameter(int hash, AnimatorControllerParameterType type)
        {
            if (animator == null) return false;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == hash && parameter.type == type) return true;
            }
            return false;
        }
    }
}
