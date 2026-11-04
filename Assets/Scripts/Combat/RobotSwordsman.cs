using CarGame.Player;
using CarGame.Vehicle;
using UnityEngine;
using UnityEngine.Animations;

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
        [Tooltip("Звук атаки — проигрывается ОДИН РАЗ в момент каждого замаха. " +
                 "Пусто — процедурный свист удара")]
        [SerializeField] private AudioClip attackSound;
        [Tooltip("Громкость шагов")]
        [SerializeField][Range(0f, 2f)] private float stepVolume = 0.35f;
        [Tooltip("Громкость сигнала обнаружения")]
        [SerializeField][Range(0f, 2f)] private float alertVolume = 0.8f;
        [Tooltip("Громкость звука атаки")]
        [SerializeField][Range(0f, 2f)] private float attackVolume = 1f;

        [Header("Память цели")]
        [Tooltip("Сколько секунд мечник помнит игрока после потери видимости и " +
                 "продолжает преследовать последнюю известную позицию. По ТЗ — 10 секунд.")]
        [SerializeField] private float targetMemoryDuration = 10f;

        private Rigidbody body;
        private string currentStateName;
        // Факт движения робота для аниматора: Walk/Run играются ТОЛЬКО когда
        // робот реально едет (скорость выше порога), Idle — только когда стоит.
        private const float MovingSpeedThreshold = 0.2f;
        // Небольшая гистерезис-задержка (секунды): робот «едет» для анимации,
        // только если скорость держится выше порога дольше этого времени.
        // Гасит дёрганье Walk↔Idle на коротких провалах скорости и не даёт
        // показать Walk в момент фактической остановки.
        private float movingSinceTime = -1f;
        private bool animMovingState;
        // ===== ПАТРУЛЬ: ходим ВСЕГДА, стоим ТОЛЬКО чтобы повернуть =====
        // Флаг «робот на плановой поворотной стоянке». Ставится в момент, когда
        // патруль дошёл до точки маршрута (или упёрся в препятствие): робот
        // честно встаёт в Idle и дожидается, пока поворот к новой точке будет
        // завершён — после чего СРАЗУ идёт дальше. Никаких длинных стоянок:
        // если по какой-то причине поворот не фиксируется, срабатывает жёсткий
        // предохранитель patrolTurnMaxWait, и робот трогается в любом случае.
        private bool patrolTurning;
        // Момент, когда поворотная стоянка обязана закончиться (страховка).
        private float patrolTurnEndsAt;
        // Целевое направление после поворота (к новой точке маршрута) — то,
        // куда указала «выбравшаяся» линия-сканер.
        private Vector3 patrolTurnTo;

        // ===== СКАНЕР НАПРАВЛЕНИЯ НА ПОВОРОТНОЙ СТОЯНКЕ =====
        // Пока робот стоит и выбирает направление, вокруг него крутится прямая
        // рейкаст-линия (видна как Gizmos в Scene-вью): она сканирует пространство,
        // сама прогоняет рейкасты и ищет свободное направление. Через случайное
        // время сканирование останавливается, линия мигает зелёным и «замораживается»
        // — ровно по ней робот и идёт дальше.
        private const int ScanPhaseScanning = 0;   // линия крутится, ищем направление
        private const int ScanPhaseLocked = 1;     // замирание с зелёным миганием
        private const int ScanPhaseGo = 2;         // выбор зафиксирован, можно идти
        private int scanPhase = ScanPhaseScanning;
        // Текущий угол луча сканера (градусы, относительно forward робота).
        private float scanAngleDegrees;
        // Направление, выбранное сканером (в мировых координатах, плоское).
        private Vector3 scanChosenDirection = Vector3.forward;
        // Накопитель фазы сканирования (секунды реального времени).
        private float scanElapsed;
        // Сколько секунд сканер крутится перед заморозкой (рандом на каждую стоянку).
        private float scanDuration = 1.5f;
        // Когда сканер впервые увидел свободное направление (для ранней остановки).
        private float scanFirstClearTime = -1f;
        // Накопитель мигания зелёным в фазе Locked.
        private float blinkElapsed;
        // Фиксированная точка вращения луча (позиция, где робот встал на
        // стоянку). Пока луч крутится — сам робот НЕ вращается и не двигается.
        // ВАЖНО: после заморозки луч остаётся ровно там, где был создан —
        // его основание НЕ закрепляется за роботом и никуда не скользит.
        // Робот идёт к неподвижному концу луча; дошёл — «удаляет» старый луч
        // (гасит подсветку) и создаёт новый на месте новой стоянки.
        private Vector3 scanOrigin;
        // Фактическая длина луча после «самосжатия»: если во все стороны
        // стены, сканер уменьшает длину луча, пока не найдёт свободное
        // направление. Восстанавливается до patrolScanLength на новой стоянке.
        private float currentScanLength = 6f;
        // Страховочный таймер: робот обязан ехать; если он почти стоит дольше
        // секунды при свободном пути — принудительно встаём на поворот.
        private float stuckWhileWalking;
        // Флаг текущего кадра: линия-сканер активна (рисуем Gizmos-линию).
        private bool gizmoLineActive;
        // Линия-сканер видна и после завершения стоянки ещё несколько секунд —
        // «выбранный курс» подсвечивается зелёным, затем гаснет.
        private float scanFadeUntil;
        // Концы линии для отрисовки в текущем кадре.
        private Vector3 gizmoLineStart;
        private Vector3 gizmoLineEnd;
        // Цвет линии в текущем кадре (белый — крутится, зелёный — замерла).
        private Color gizmoLineColor = Color.white;

        [Tooltip("Сколько секунд максимум крутится линия-сканер, прежде чем " +
                 "замереть и выбрать направление. Фактически берётся случайно " +
                 "из диапазона [половина значения, полное значение].")]
        [SerializeField] private float patrolScanDuration = 3f;
        [Tooltip("Скорость вращения линии-сканера, градусов/с")]
        [SerializeField] private float patrolScanSpeed = 160f;
        [Tooltip("Минимальная длительность зелёного мигания заморозки, с. " +
                 "Как только сканер нашёл свободное направление, он мигает " +
                 "зелёным минимум столько секунд, затем робот трогается.")]
        [SerializeField] private float patrolBlinkMinDuration = 1f;
        [Tooltip("Длина линии-сканера (и глубина проверки рейкастом), м")]
        [SerializeField] private float patrolScanLength = 6f;
        [Tooltip("Максимальная длительность всей поворотной стоянки (скан + " +
                 "мигание), с. Жёсткий предохранитель против вечных остановок.")]
        [SerializeField] private float patrolTurnMaxWait = 7f;
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
        // Таймер deathExplodeDelay — в РЕАЛЬНЫХ секундах: накопленная
        // Time.unscaledDeltaTime (паузы с timeScale = 0 его не растягивают).
        private float deathElapsed;
        // Момент последнего тика состояния смерти (реальное время). Нужен для
        // самовосстановления: если Update() мечника перестали вызывать
        // (компонент выключили/удалили, скрипт упал с исключением), Unity
        // также перестаёт обновлять Animator — тряска залипает на первом кадре,
        // а взрыв не наступает никогда. OnDisable и OnAnimatorMove замечают
        // это и возвращают состояние смерти в работу.
        private float lastDeathTickUnscaledTime = -1f;
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

            // КЛЮЧЕВОЕ для анимации смерти: Unity обновляет (evaluator-тик)
            // компонент Animator только пока на объекте живёт ХОТЬ ОДИН
            // активный MonoBehaviour. Если в момент смерти скрипт мечника
            // окажется выключен/удалён (сторонние системы «убирают мёртвых»,
            // отключают компоненты), аниматор мгновенно замирает ровно на
            // первом кадре Death — ровно тот симптом, что наблюдался.
            // keepAnimatorControllerStateOnDisable сохраняет состояние
            // контроллера при случайном выключении Animator'а.
            if (animator != null) animator.keepAnimatorControllerStateOnDisable = true;

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
            // после выхода на строку «IsDead — выходи» вообще переставал тикать,
            // поэтому смерть выглядела как «залипший первый кадр», а взрыв не
            // наступал никогда. Теперь мёртвый, но ещё не взорвавшийся мечник
            // продолжает крутить луп тряски и отсчёт до взрыва.
            if (dying)
            {
                // Отсчёт в реальных секундах накоплением unscaledDeltaTime:
                // паузы/меню с timeScale = 0 его не растягивают, а на обычной
                // скорости 10 в инспекторе = ровно 10 секунд. Дельту кадра
                // ограничиваем сверху — иначе редкий просадочный кадр с
                // гигантским deltaTime «съест» часть задержки.
                deathElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.5f);
                lastDeathTickUnscaledTime = Time.unscaledTime;
                if (deathElapsed >= Mathf.Max(0f, deathExplodeDelay)) Explode();
                ForceDeathAnimation();
                return;
            }

            if (health == null || health.IsDead) return;

            // Сброс флага поворотной стоянки при смене режима: если робот
            // стоит в Idle и ждёт завершения поворота, а цель появилась —
            // немедленно снимаем patrolTurning, иначе движение заблокируется
            // навсегда.
            BaseUpdate();
            if (HasTarget && Target != null && !Target.Equals(null))
            {
                // Сбрасываем и сканер: иначе gizmoLineActive останется true,
                // а patrolTurning — false, и «призрак» замороженной линии
                // будет висеть над роботом до следующей стоянки. rayFrozen
                // сбрасываем тоже — старый луч полностью «удаляется».
                patrolTurning = false;
                gizmoLineActive = false;
                rayFrozen = false;
            }

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
            // ДВИЖЕНИЕ РАЗРЕШЕНО ВСЕГДА, кроме трёх случаев:
            //   • замах атаки (attackPlaying);
            //   • пост-ударная стойка (strikeHoldTimer);
            //   • поворотная стоянка патруля (patrolTurning) — робот встаёт,
            //     доворачивается к новой точке маршрута и СРАЗУ идёт дальше.
            // Старые паузы базового класса (pauseUntil/PatrolAllowsMove) в
            // расчёт НЕ берутся: раньше именно они сажали робота в Idle
            // посреди чистого маршрута или держали Walk на остановившемся
            // боте. Преследование — бессрочное, без остановок; патруль —
            // ходит всегда, стоит только чтобы повернуть.
            bool holdingAfterStrike = strikeHoldTimer > 0f;

            // Активность поворотной стоянки управляется только этим блоком.
            if (!engaged)
            {
                UpdatePatrolTurnStop(ref direction);

                // Луч НЕ закрепляется за роботом: пока мечник идёт по выбранному
                // курсу, «замороженная» линия остаётся висеть ровно там, где была
                // создана (gizmoLineStart/gizmoLineEnd заданы один раз при
                // заморозке сканера и больше не сдвигаются). Робот доходит до её
                // конца — старый луч гасится («удаляется»), на новой стоянке
                // сканер создаёт новый луч из актуальной позиции робота.
            }
            else
            {
                // Цель появилась — любая патрульная стоянка мгновенно снимается,
                // старый луч «удаляется» (он больше не рисуется).
                patrolTurning = false;
                gizmoLineActive = false;
                rayFrozen = false;
            }

            bool mayMove = !holdingAfterStrike && !attackPlaying && !patrolTurning;

            UpdateStuckCheck(body != null ? body.linearVelocity : Vector3.zero, mayMove);

            // Поворот выполняется во время ХОДЬБЫ и преследования. Во время
            // поворотной стоянки патруля (patrolTurning) робот НЕ вращается:
            // он стоит ровно там, где дошёл, а направление выбирает только
            // крутящийся луч-сканер. Разворот — уже после заморозки линии,
            // когда робот трогается строго по выбранному курсу.
            if (!patrolTurning && direction.sqrMagnitude > 0.0001f)
                FaceDirection(direction, turnSpeed, Time.deltaTime);

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

                // ЖЁСТКАЯ СВЯЗЬ АНИМАЦИИ С ДВИЖЕНИЕМ (устраняет баги «стоит на
                // месте в анимации Walk» и «идёт в анимации Idle»):
                // состояние выбирается по ФАКТИЧЕСКОЙ горизонтальной скорости
                // робота, а не по таймеру патруля.
                //   • робот едет  (скорость > порога) → Walk (патруль) / Run (цель);
                //   • робот стоит (скорость ~ 0)      → Idle всегда.
                // Таймер patrolWaitUntil больше НЕ участвует в выборе анимации:
                // раньше он мог показать Idle, когда робот ещё тормозил после
                // точки маршрута (ехал в Idle), или наоборот держать Walk, когда
                // движение затормозилось препятствием (стоял в Walk).
                Vector3 actualVelocity = body != null
                    ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up)
                    : Vector3.zero;
                bool actuallyMoving = UpdateMovementHysteresis(actualVelocity.magnitude);

                string desired;
                if (!actuallyMoving)
                {
                    desired = idleState;                 // полный покой — только Idle
                }
                else if (engaged)
                {
                    desired = runState;                  // преследование в движении — Run
                }
                else
                {
                    desired = walkState;                 // патруль в движении — Walk
                    // Робот реально идёт, а базовый класс успел поставить
                    // плановую паузу (pauseUntil) — снимаем её немедленно:
                    // движение и анимация не должны расходиться с таймерами
                    // EnemyBase. Иначе сразу после Walk прыгнул бы Idle,
                    // хотя робот едет (симптом «идёт в анимации Idle»).
                    if (Time.time < patrolWaitUntil) pauseUntil = Time.time;
                }
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

        /// <summary>
        /// ПАТРУЛЬ: робот ходит ВСЕГДА; встать он имеет право только чтобы
        /// повернуть. Поворот работает как «выбор направления лучом-сканером»:
        ///   1) Робот идёт по выбранному направлению (анимация Walk), пока не
        ///      дойдёт до конца луча (дистанция сканирования) — без остановок,
        ///      если на пути ничего не мешает.
        ///   2) Дошёл или упёрся в препятствие/стену — честно встаёт в Idle и
        ///      НЕ вращается: вокруг стоящего робота крутится прямая
        ///      рейкаст-линия (Gizmos в Scene-вью). Коснулась линия стены —
        ///      это не повод застревать: сканер просто ищет другое направление.
        ///      Если луч везде упирается в стены — он сам укорачивается, пока
        ///      не найдёт свободный коридор.
        ///   3) Через случайное время линия замирает на свободном направлении,
        ///      мигает зелёным — робот разворачивается и идёт ровно по ней,
        ///      пока снова не дойдёт до конца луча.
        /// Долгих беспричинных остановок не бывает: предельная длительность
        /// всей стоянки — patrolTurnMaxWait секунд (жёсткий предохранитель).
        /// </summary>
        private void UpdatePatrolTurnStop(ref Vector3 direction)
        {
            if (patrolTurning)
            {
                UpdateDirectionScanner();

                if (scanPhase == ScanPhaseGo)
                {
                    // Выбор зафиксирован: трогаемся ВДОЛЬ замороженной линии.
                    patrolTurning = false;
                    // Замороженная линия остаётся видимой (зелёная) ровно там,
                    // где была создана — основание НЕ привязано к роботу.
                    // Координаты gizmoLineStart/gizmoLineEnd фиксируются один
                    // раз в момент заморозки и до конца пути не меняются.
                    scanFadeUntil = Time.time + Mathf.Max(1f, currentScanLength) /
                                    Mathf.Max(0.5f, PatrolSpeed) + 1f;
                    SetPatrolRouteAlongChosenDirection();
                    // Идём строго по выбранному лучу (не к старой точке!).
                    direction = FlatTowards(scanChosenDirection);
                    patrolTurnTo = direction;
                }
                else
                {
                    // Ещё стоим: робот НЕ вращается — стоит ровно там, где
                    // дошёл, а направление выбирает только крутящийся луч.
                    // Движение запрещено — анимация честно в Idle.
                    direction = Vector3.zero;
                }

                return;
            }

            // --- Обычная ходьба патруля ---
            // Целевая точка маршрута пересчитывается каждый кадр: она лежит
            // ВДОЛЬ выбранного луча на расстоянии фактической длины скана.
            // Робот идёт, пока не дойдёт до конца луча, — затем встаёт и
            // снова выбирает направление.
            Vector3 dest = PatrolDestinationNow();
            Vector3 toDest = FlatTowards(dest - transform.position);

            // Страховка от «съезда» с курса: базовый класс на короткой плановой
            // стоянке возвращает в качестве цели текущую позицию робота — если
            // поймать этот кадр, робот встал бы посреди чистого маршрута без
            // выбора направления. В такой кадр идём по прежнему курсу; если
            // курс неизвестен — принудительно обновляем wander-точку.
            if (toDest.sqrMagnitude < 0.25f)
            {
                if (patrolTurnTo.sqrMagnitude > 0.0001f)
                {
                    toDest = FlatTowards(patrolTurnTo);
                    dest = transform.position + toDest;
                }
                else
                {
                    ForceNewPatrolRoute();
                    direction = Vector3.zero;
                    return;
                }
            }

            float arriveTolerance = Mathf.Max(0.4f, PatrolPointTolerance);
            if ((dest - transform.position).sqrMagnitude
                <= arriveTolerance * arriveTolerance)
            {
                // Дошли до конца луча — встаём и сканируем новое направление.
                BeginPatrolTurnStop();
                direction = Vector3.zero;
                return;
            }

            // Жёсткая проверка: робот обязан ЕХАТЬ. Если он почти стоит, а
            // путь к цели явно перекрыт (упёрся в стену/препятствие) — это
            // законная поворотная стоянка: встаём и выбираем другое
            // направление. Если же путь свободен — топтания быть не должно:
            // ждём секундный страховочный таймер залипания и перегенерируем
            // точку маршрута (луч всё равно покажет корректное направление).
            float horizontalSpeed = body != null
                ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude
                : 0f;
            if (horizontalSpeed < MovingSpeedThreshold * 0.5f)
            {
                if (!IsPathClear(toDest))
                {
                    BeginPatrolTurnStop();
                    direction = Vector3.zero;
                    return;
                }

                stuckWhileWalking += Time.deltaTime;
                if (stuckWhileWalking > 1f)
                {
                    stuckWhileWalking = 0f;
                    BeginPatrolTurnStop();
                    direction = Vector3.zero;
                    return;
                }
            }
            else
            {
                stuckWhileWalking = 0f;
            }

            direction = toDest;
        }

        /// <summary>
        /// Текущая активная точка патрульного маршрута в flat-режиме мечника:
        /// wander-точка базового класса (когда patrolPoints не назначены) либо
        /// ближайшая назначенная точка маршрута. Используется только для
        /// направления движения во время ходьбы — выбор точек по-прежнему
        /// делает EnemyBase.ResolvePatrolDestination().
        /// </summary>
        private Vector3 PatrolDestinationNow()
        {
            if (!HasPatrolPoints) return WanderDestination;

            Transform best = null;
            float bestDist = float.MaxValue;
            foreach (Transform point in PatrolPoints)
            {
                if (point == null) continue;
                float dist = Vector3.ProjectOnPlane(
                    point.position - transform.position, Vector3.up).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = point;
                }
            }

            return best != null ? best.position : transform.position;
        }

        /// <summary>
        /// Запустить поворотную стоянку со сканером направления: робот встаёт
        /// в Idle (сам НЕ вращается), вокруг него начинает крутиться прямая
        /// рейкаст-линия. Длина луча восстанавливается до полной — на новой
        /// стоянке сканер снова «щупает» пространство целиком; если луч везде
        /// упрётся в стены, он сам укоротится прямо во время вращения.
        /// </summary>
        private void BeginPatrolTurnStop()
        {
            patrolTurning = true;
            scanPhase = ScanPhaseScanning;
            scanElapsed = 0f;
            blinkElapsed = 0f;
            scanFirstClearTime = -1f;
            stuckWhileWalking = 0f;
            // Старый луч «удаляется»: флаг заморозки сбрасывается, новая
            // стоянка создаст собственный луч из актуальной позиции робота.
            rayFrozen = false;
            // Точка вращения луча фиксируется: робот стоит и не двигается.
            scanOrigin = transform.position + Vector3.up * 0.5f;
            currentScanLength = Mathf.Max(1f, patrolScanLength);
            // Случайная длительность сканирования на каждую стоянку:
            // от половины patrolScanDuration до полного значения.
            float maxScan = Mathf.Max(0.6f, patrolScanDuration);
            scanDuration = Random.Range(maxScan * 0.5f, maxScan);
            // Стартуем с произвольного угла, чтобы линия не «привязывалась»
            // к предыдущему выбору.
            scanAngleDegrees = Random.Range(0f, 360f);
            scanChosenDirection = FlatTowards(transform.forward);
            if (scanChosenDirection.sqrMagnitude < 0.0001f)
                scanChosenDirection = Vector3.forward;
            patrolTurnTo = scanChosenDirection;
            // Жёсткий предел всей стоянки (скан + мигание): дольше робот
            // не имеет права стоять ни при каких обстоятельствах.
            patrolTurnEndsAt = Time.time + Mathf.Max(
                scanDuration + Mathf.Max(0.4f, patrolBlinkMinDuration) + 0.5f,
                patrolTurnMaxWait);
        }

        /// <summary>
        /// Шаг сканера направления (вызывается каждый кадр стоянки). Машинка
        /// состояний: Scanning → Locked (зелёное мигание) → Go (поехали).
        /// Касание стены лучом НЕ означает остановку робота — это лишь сигнал
        /// сканеру искать другое направление; если свободных направлений нет
        /// вовсе, луч сам укорачивается (шагами), пока коридор не найдётся.
        /// </summary>
        private void UpdateDirectionScanner()
        {
            float dt = Time.deltaTime;

            // Страховка от вечной остановки: если фазы по какой-то причине не
            // проходят, по истечении patrolTurnMaxWait идём по текущему лучу.
            bool failsafe = Time.time >= patrolTurnEndsAt;

            switch (scanPhase)
            {
                case ScanPhaseScanning:
                {
                    scanElapsed += dt;
                    // Линия крутится вокруг СТОЯЩЕГО робота с постоянной
                    // скоростью (сам робот не вращается).
                    scanAngleDegrees += Mathf.Max(30f, patrolScanSpeed) * dt;
                    if (scanAngleDegrees >= 360f) scanAngleDegrees -= 360f;

                    Vector3 rayDir = CurrentScanWorldDirection();
                    bool clear = IsScanRayClear(rayDir, currentScanLength);

                    // Заморозка: истекло случайное время сканирования И луч
                    // сейчас свободен (не смотрит в стену). Если свободных
                    // лучей долго нет — ждём следующий сектор.
                    bool timeUp = scanElapsed >= scanDuration;
                    if ((timeUp && clear) || failsafe)
                    {
                        scanChosenDirection = rayDir;
                        blinkElapsed = 0f;
                        scanPhase = ScanPhaseLocked;
                    }
                    else if (!clear && scanElapsed > scanDuration)
                    {
                        // Время вышло, но все направления (за целый оборот)
                        // упираются в стены — УКОРАЧИВАЕМ луч и пробуем снова:
                        // так робот выберет короткий, но свободный проход
                        // вместо вечной стоянки в углу.
                        currentScanLength = Mathf.Max(
                            MinScanLength, currentScanLength * 0.7f);
                        // Даем ещё один оборот уже с коротким лучом.
                        scanDuration = scanElapsed + Mathf.Max(0.6f, patrolScanDuration);
                    }
                    break;
                }

                case ScanPhaseLocked:
                    blinkElapsed += dt;
                    // Зелёное мигание длится минимум patrolBlinkMinDuration
                    // секунд — робот «показывает» выбранный курс, затем идёт.
                    if (blinkElapsed >= Mathf.Max(0.4f, patrolBlinkMinDuration) || failsafe)
                        scanPhase = ScanPhaseGo;
                    break;

                default: // ScanPhaseGo
                    break;
            }

            // Обновляем параметры отрисовки линии для Gizmos (текущий кадр).
            gizmoLineActive = true;

            if (scanPhase == ScanPhaseScanning)
            {
                // Луч ещё крутится вокруг стоящего робота — линия следует за
                // текущим углом сканирования, основание фиксировано на точке
                // стоянки (scanOrigin).
                Vector3 shown = CurrentScanWorldDirection();
                gizmoLineStart = scanOrigin;
                gizmoLineEnd = scanOrigin + shown.normalized * currentScanLength;
                // Пока линия крутится — она белая.
                gizmoLineColor = Color.white;
            }
            else
            {
                // Направление заморозлено. Линия «создана» один раз и больше
                // НИКУДА не сдвигается: начало — ровно точка стоянки, конец —
                // точка на расстоянии длины луча по выбранному направлению.
                // Именно к этой неподвижной конечной точке робот и пойдёт.
                if (!rayFrozen)
                {
                    rayFrozen = true;
                    frozenRayStart = scanOrigin;
                    frozenRayEnd = scanOrigin +
                        FlatTowards(scanChosenDirection).normalized * currentScanLength;
                }

                gizmoLineStart = frozenRayStart;
                gizmoLineEnd = frozenRayEnd;

                if (scanPhase == ScanPhaseLocked)
                {
                    // Замороженная линия мигает зелёным (~2 вспышки в секунду).
                    float pulse = 0.55f + 0.45f * Mathf.Sin(blinkElapsed * Mathf.PI * 4f);
                    gizmoLineColor = new Color(0.1f, 1f, 0.25f, pulse);
                }
                else
                {
                    // Робот уже идёт по лучу — он остаётся видимым, но ровно
                    // там, где был создан (без «прилипания» к позиции мечника).
                    gizmoLineColor = new Color(0.1f, 1f, 0.25f, 0.6f);
                }
            }
        }

        // ===== НЕПОДВИЖНЫЙ ЛУЧ ПОСЛЕ ЗАМОРОЗКИ =====
        // Флаг «луч зафиксирован в мире»: координаты ниже ставятся ровно один
        // раз — в момент выбора направления — и более не пересчитываются.
        private bool rayFrozen;
        private Vector3 frozenRayStart;
        private Vector3 frozenRayEnd;

        /// <summary>Минимальная длина луча при самосжатии сканера.</summary>
        private const float MinScanLength = 1f;

        /// <summary>Мировое плоское направление текущего луча сканера.</summary>
        private Vector3 CurrentScanWorldDirection()
        {
            Vector3 flatForward = FlatTowards(transform.forward);
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
            Quaternion yaw = Quaternion.AngleAxis(scanAngleDegrees, Vector3.up);
            return yaw * flatForward.normalized;
        }

        /// <summary>
        /// Проверка луча сканера рейкастом: свободно ли направление на длину
        /// length. Свои коллайдеры и коллайдеры цели игнорируются — как в
        /// штатной проверке базового класса.
        /// </summary>
        private bool IsScanRayClear(Vector3 worldDirection, float length)
        {
            Vector3 flat = FlatTowards(worldDirection);
            if (flat.sqrMagnitude < 0.0001f) return false;
            flat.Normalize();

            Vector3 origin = FrontPosition + Vector3.up * 0.5f;
            float dist = Mathf.Max(MinScanLength, length);
            const float skin = 0.18f; // запас, чтобы не цеплять углы коллайдеров

            if (Physics.SphereCast(origin, skin, flat, out RaycastHit hit, dist,
                                   Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                Collider other = hit.collider;
                if (other == null) return true;
                if (other.transform.root == transform.root) return true; // сам робот
                if (Target != null && !Target.Equals(null)
                    && other.transform.root == Target.transform.root) return true; // цель
                return false;
            }

            return true;
        }

        /// <summary>
        /// Фиксация маршрута ВДОЛЬ замороженного луча: новая точка патруля
        /// ставится ровно на конце луча (на фактической дистанции скана),
        /// поэтому робот после стоянки идёт по линии и останавливается,
        /// только когда дойдёт до её конца.
        /// </summary>
        private void SetPatrolRouteAlongChosenDirection()
        {
            ForceNewPatrolRoute(); // сброс старой точки и счётчика пройденного

            // Снимаем любые плановые паузы базового класса: робот выбрал
            // направление и должен ТРОГУТЬСЯ немедленно, а не досидевать
            // чужую стоянку в Idle (иначе Walk появится с задержкой).
            pauseUntil = Time.time;

            Vector3 dir = FlatTowards(scanChosenDirection);
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            // Идём ровно до конца луча — на его фактическую (возможно,
            // укороченную сканером) длину. Отправная точка — НЕ текущая
            // позиция робота, а основание замороженного луча (scanOrigin):
            // робот в этот момент стоит ровно там же, но при малом смещении
            // за время мигания цель остаётся привязана к созданному лучу,
            // а не «едет» вместе с роботом.
            float distance = currentScanLength;
            Vector3 rayBase = rayFrozen ? frozenRayStart : transform.position + Vector3.up * 0.5f;

            if (!HasPatrolPoints)
            {
                // Wander-режим: выставляем активную точку блуждания прямо по
                // лучу — ResolvePatrolDestination вернёт её и робот пойдёт туда.
                Vector3 destination = rayBase + dir * distance;
                destination.y = transform.position.y;
                // Держим точку в разумной близости к спавну, чтобы не убежать.
                Vector3 flatFromSpawn = FlatTowards(destination - SpawnPoint);
                if (flatFromSpawn.magnitude > PatrolRadius)
                {
                    destination = SpawnPoint + flatFromSpawn.normalized * PatrolRadius * 0.9f;
                    destination.y = SpawnPoint.y;
                }
                WanderDestination = destination;
            }
            else
            {
                // Режим точек маршрута: тоже ведём робота по лучу — иначе
                // PickNextPatrolPoint возьмёт ближайшую точку в другом
                // направлении и выбор сканера будет проигнорирован.
                Vector3 routeDestination = rayBase + dir * distance;
                routeDestination.y = transform.position.y;
                WanderDestination = routeDestination;
            }
        }

        /// <summary>Отрисовка линии-сканера (Scene-вью, включённые Gizmos).</summary>
        private void OnDrawGizmos()
        {
            DrawScannerGizmo();
        }

        private void DrawScannerGizmo()
        {
            // Во время ходьбы по выбранному курсу показываем замороженную
            // линию зелёным РОВНО там, где она была создана: начало и конец
            // луча неподвижны в мире (frozenRayStart/frozenRayEnd) — линия НЕ
            // следует за роботом. Гаснет («удаляется»), когда робот дошёл до
            // её конца (или истёк срок подсветки) — тогда начинается новая
            // стоянка и сканер создаст новый луч из актуальной позиции.
            if (!patrolTurning)
            {
                if (!gizmoLineActive || Time.time >= scanFadeUntil) return;
                if (!rayFrozen) return;
                Gizmos.color = gizmoLineColor;
                Gizmos.DrawLine(frozenRayStart, frozenRayEnd);
                Gizmos.DrawSphere(frozenRayEnd, 0.08f);
                return;
            }

            if (!gizmoLineActive) return;

            Gizmos.color = gizmoLineColor;
            Gizmos.DrawLine(gizmoLineStart, gizmoLineEnd);
            // Маленькая сфера на конце — лучше видна «голова» луча.
            Gizmos.DrawSphere(gizmoLineEnd, 0.08f);
        }

        private static Vector3 FlatTowards(Vector3 vector)
        {
            return Vector3.ProjectOnPlane(vector, Vector3.up);
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

        /// <summary>
        /// Ужестённая проверка «едет ли робот» для выбора анимации: состояние
        /// «движение» включается только когда горизонтальная скорость держится
        /// выше MovingSpeedThreshold дольше короткого порога, и выключается
        /// СРАЗУ при падении скорости ниже порога. Так Walk/Run никогда не
        /// остаются на остановившемся роботе, а Idle не показывается во время
        /// честной езды (короткие провалы скорости внутри цикла шагов не
        /// вызывают дёрганья Walk↔Idle).
        /// </summary>
        private bool UpdateMovementHysteresis(float horizontalSpeed)
        {
            if (horizontalSpeed > MovingSpeedThreshold)
            {
                if (movingSinceTime < 0f) movingSinceTime = Time.time;
                if (!animMovingState && Time.time - movingSinceTime >= 0.12f)
                    animMovingState = true;
            }
            else
            {
                movingSinceTime = -1f;
                animMovingState = false; // стоп мгновенно → Idle
            }

            return animMovingState;
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
            PlayAttackSound();

            if (!animateStrike || animator == null || string.IsNullOrEmpty(attackState)) return;

            // SwordBot.controller переключает атаку триггером "Attack":
            // Idle/Walk/Run → Attack; из Attack есть выход в Idle. Удар
            // должен доиграться ЦЕЛИКОМ — дальше код держит Idle-стойку.
            attackPlaying = true;
            attackPlayUntil = Time.time + AttackStateLength() + 0.25f;
            ApplyState(attackState);
        }

        /// <summary>
        /// Звук замаха: ОДИН РАЗ в момент начала атаки (вызывается из PlayAttack,
        /// то есть ровно на каждый удар). Если в инспекторе назначен attackSound —
        /// играется он; иначе — процедурный свист удара. Громкость — attackVolume.
        /// </summary>
        private void PlayAttackSound()
        {
            AudioClip clip = attackSound != null ? attackSound : CarGame.Audio.ProceduralAudio.Whiff();
            if (clip == null) return;
            AudioSource.PlayClipAtPoint(clip, transform.position, attackVolume);
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

            // Гарантии жёсткой связи «анимация ↔ движение»:
            //   • speed <= 0 или пустое направление — робот СТОИТ: скорость
            //     гасится до нуля быстро (ускоренным торможением), иначе он по
            //     инерции ещё долго едет и аниматор честно держит Walk, хотя
            //     план — стоять (симптом «стоит, но играет Walk»);
            //   • speed > 0 — целевая скорость достигается тем же быстрым
            //     ускорением, без «вязкого» разгона на доли метра.
            float brakeRate = moveSpeed * 12f;
            float accelRate = moveSpeed * 16f;

            Vector3 velocity = body.linearVelocity;
            Vector3 flatVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);

            if (direction.sqrMagnitude < 0.0001f || speed <= 0.01f)
                flatVelocity = Vector3.MoveTowards(flatVelocity, Vector3.zero, brakeRate * Time.deltaTime);
            else if (IsPathClear(direction) || climbing)
                flatVelocity = Vector3.MoveTowards(flatVelocity, direction.normalized * speed, accelRate * Time.deltaTime);
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
            lastDeathTickUnscaledTime = Time.unscaledTime;

            // Скрипт должен остаться АКТИВНЫМ до самого взрыва: Update()
            // гоняет таймер и луп тряски. Если компонент был выключен (или его
            // выключила сторонняя система), Unity перестала бы вызывать Update,
            // аниматор замер бы на первом кадре, а взрыв не наступил бы никогда.
            enabled = true;

            // Animator обязан тикаться даже если сам объект временно уйдёт в
            // deactivate или скрипт будет выключен в том же кадре.
            EnsureAnimatorAwake();

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
        /// Страховка состояния смерти: вызывается даже когда Update() мечника
        /// больше не крутится. OnDisable срабатывает в момент выключения или
        /// удаления компонента — Unity к этому кадру ещё успевает обновлять
        /// Animator, поэтому тряска не залипает навечно. Если скрипт умерщвлён
        /// полностью (Destroy), событие всё равно догоняет таймер и взрывает
        /// робота ровно через deathExplodeDelay реальных секунд.
        /// </summary>
        private void OnDisable()
        {
            CatchUpDeathTimer();
        }

        private void CatchUpDeathTimer()
        {
            if (!dying || exploded) return;

            // Догоняем время, прошедшее с последнего засчитанного тика.
            if (lastDeathTickUnscaledTime >= 0f)
            {
                deathElapsed += Mathf.Clamp(
                    Time.unscaledTime - lastDeathTickUnscaledTime, 0f, 30f);
            }

            if (deathElapsed >= Mathf.Max(0f, deathExplodeDelay)) Explode();
        }

        /// <summary>
        /// Гарантирует, что Animator точно существует и включён. Включает
        /// keepAnimatorControllerStateOnDisable, чтобы состояние Death не
        /// сбрасывалось при случайном выключении компонента.
        /// </summary>
        private void EnsureAnimatorAwake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
            if (!animator.enabled) animator.enabled = true;
            animator.keepAnimatorControllerStateOnDisable = true;
        }

        /// <summary>
        /// Поддержание лупа анимации смерти каждый кадр: гарантируем
        /// dieParameter == true и находимся в состоянии Death.
        ///
        /// ГЛАВНАЯ ПРИЧИНА «залипшего первого кадра» исправлена здесь же:
        /// Unity обновляет Animator только пока на объекте живёт хотя бы один
        /// активный MonoBehaviour. Если внешний код выключил/удалил скрипт
        /// мечника сразу после Died, Update больше не вызывается и тряска
        /// замирает навечно. Поэтому в Awake подписываемся на Health.Died ещё
        /// раз (первая подписка гибнет вместе со старым экземпляром скрипта) —
        /// новый экземпляр включает dying, зануляет счётчик зависания и
        /// возобновляет и луп анимации, и отсчёт до взрыва.
        /// </summary>
        private void ForceDeathAnimation()
        {
            EnsureAnimatorAwake();
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
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            if (!current.IsName(deathStateName))
            {
                animator.Play(deathStateName, 0, 0f);
            }

            // Анимация смерти играется РОВНО с той скоростью, что стоит в
            // контроллере (там m_Speed: 1). Никаких принудительных правок
            // animator.speed — именно они раньше могли либо замедлять клип
            // почти до остановки, либо обрывать луп.
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
