using System.Collections;
using CarGame.Combat;
using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Огнестрельный ствол в руках игрока.
    ///
    /// Живёт на своём варианте руки — так же, как ключ живёт на своём, а
    /// инжектор на своём. Всё, что относится к самому стволу, лежит здесь:
    /// магазин, перезарядка, осмотр, отдача, выстрел и проигрывание клипов
    /// его Animator'а. На игроке не остаётся состояния оружия: PlayerFirearm
    /// только выбирает ствол и передаёт ему нажатия.
    ///
    /// Магазин намеренно не хранится в поле компонента. Он лежит в Payload
    /// ячейки инвентаря, в которой находится этот ствол. Вариант руки на все
    /// стволы одного вида один, а стволов в инвентаре может быть сколько
    /// угодно, поэтому и заряд хранится рядом с предметом, а не на варианте.
    /// Так каждый ствол получает свой магазин, и заряд переживает перенос
    /// между ячейками и выброс на землю.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponItem : MonoBehaviour
    {
        // В контроллерах стволов состояния называются по-разному, поэтому имена
        // берутся из массива нужного ствола, а не одним общим списком.
        private static readonly string[] PistolStates = { "Idle", "Shot", "Reloading", "View" };
        private static readonly string[] ShotgunStates = { "Idle", "Shot", "Reload", "Viev" };

        [Header("Что за ствол")]
        [Tooltip("ID предмета, которому принадлежит этот вариант: pistol или shotgun")]
        [SerializeField] private string itemId = ItemDefs.PistolId;

        [Header("Стрельба")]
        [Tooltip("Слои, по которым можно стрелять. Слои рук и интерфейса лучше исключить")]
        [SerializeField] private LayerMask shotMask = ~0;
        [Tooltip("Точка вылета пули (firepoint). Если задана - пуля вылетает отсюда, рейкаст идёт по направлению прицела")]
        [SerializeField] private Transform firePoint;
        [Tooltip("На каком расстоянии от камеры вспыхивает дуло, м")]
        [SerializeField] private float muzzleDistance = 0.55f;
        [Tooltip("Префаб эффекта выстрела. Если пусто — эффекта не будет")]
        [SerializeField] private GameObject shotEffectPrefab;
        [Tooltip("Показывать вспышку в дуле")]
        [SerializeField] private bool muzzleFlash = true;
        [Tooltip("Сколько попаданий дробовика показать эффектом, чтобы не наслаивать 8 одинаковых искр")]
        [SerializeField] private int maxHitEffects = 4;

        [Header("Трассер")]
        [Tooltip("Показывать летящий луч от дула к цели")]
        [SerializeField] private bool showTracer = true;
        [Tooltip("Толщина луча, м")]
        [SerializeField] private float tracerWidth = 0.03f;
        [Tooltip("Скорость полёта луча, м/с")]
        [SerializeField] private float tracerSpeed = 220f;
        [Tooltip("Цвет луча")]
        [SerializeField] private Color tracerColor = new Color(1f, 0.85f, 0.35f, 1f);

        [Header("Поза ствола")]
        [Tooltip("Сколько длится поза выстрела, с. Влияет только на анимацию")]
        [SerializeField] private float shootPoseTime = 0.12f;
        [Tooltip("Скорость возврата ствола после выстрела")]
        [SerializeField] private float recoverSpeed = 9f;

        private PlayerFirearm firearm;
        private PlayerInteractor interactor;
        private Inventory inventory;
        private PlayerController playerController;

        private Animator animator;
        private FirearmAction action = FirearmAction.Idle;
        private float actionEndTime;
        private float recoil;
        private float nextFireTime;

        private FirearmAction clipAction = FirearmAction.Idle;
        private float clipActionDuration;
        private float clipActionElapsed;
        private bool idlePosePending;

        public string ItemId => itemId;
        public ItemDef Def { get; private set; }
        public FirearmSpec Spec => Def != null && Def.IsFirearm ? FirearmSpecs.Resolve(Def.Firearm) : null;

        public FirearmAction Action => action;
        public bool IsReloading => IsAction(FirearmAction.Reload);
        public bool IsInspecting => IsAction(FirearmAction.Inspect);
        public bool IsShooting => recoil > 0.001f;

        /// <summary>
        /// Клип перезарядки или осмотра может быть длиннее игрового времени, и
        /// тогда ждать надо обоих: выстрел или смена слота посреди перезарядки
        /// оборвали бы анимацию на середине. Клип выстрела сюда не входит — иначе
        /// после дробовика нельзя было бы стрелять снова целых полторы секунды.
        /// </summary>
        private bool WaitingOnClip => clipActionDuration > 0f && clipAction != FirearmAction.Idle
            && (clipAction == FirearmAction.Reload || clipAction == FirearmAction.Inspect);

        public bool IsBusy => IsReloading || IsInspecting || WaitingOnClip;

        private bool IsAction(FirearmAction value) => action == value;
        private bool UsesAnimator => animator != null && animator.runtimeAnimatorController != null;

        private void Awake()
        {
            EnsureReady();
        }

        /// <summary>
        /// При каждом включении варианта Unity сбрасывает Animator в состояние,
        /// помеченное в контроллере как состояние по умолчанию. У пистолета им
        /// был выстрел, и клип выстрела у этих моделей зацикленный, поэтому
        /// ствол в руке непрерывно повторял выстрел вместо простоя.
        ///
        /// Свое состояние по умолчанию держим тут: при включении варианта
        /// возвращаем ствол в Idle, если он не занят чем-то другим.
        /// </summary>
        private void OnEnable()
        {
            idlePosePending = true;
        }

        private void EnsureIdlePose()
        {
            if (!idlePosePending) return;

            // Модель с Animator'ом приезжает в вариант уже после включения, поэтому
            // ждём первого кадра, на котором Animator уже доступен.
            if (!UsesAnimator) return;

            idlePosePending = false;
            if (clipAction == FirearmAction.Idle && action == FirearmAction.Idle) PlayState(0);
        }

        /// <summary>
        /// Всё, что компонент ищет, он ищет сам, потому что инициализироваться на
        /// старте сцены нельзя. Вариант руки в сцене выключен, значит его Awake
        /// не вызывается, пока ствол не покажут. А модель и Animator приезжают
        /// в вариант ещё позже — их подставляет PlayerHand, когда вариант
        /// оказывается пустым. Поэтому ссылки берутся лениво, при первом же
        /// обращении, и каждый раз дешёвой проверкой.
        /// </summary>
        private void EnsureReady()
        {
            if (Def == null)
            {
                ItemDef def;
                if (ItemDefs.TryGet(itemId, out def) && def.IsFirearm) Def = def;
            }

            if (firearm == null || inventory == null || interactor == null || playerController == null)
                ResolveReferences();

            // Animator лежит на модели, которую PlayerHand подставляет в вариант,
            // поэтому он может появиться позже первого обращения.
            if (animator == null) ResolveAnimator();
        }

        /// <summary>
        /// Задать ствол, если вариант достался из каталога предметов, а не из
        /// сцены: в сцере ID проставлен руками в инспекторе.
        /// </summary>
        public void Configure(string id)
        {
            if (!string.IsNullOrEmpty(id)) itemId = id;

            ItemDef def;
            if (ItemDefs.TryGet(itemId, out def) && def.IsFirearm) Def = def;
        }

        /// <summary>
        /// Отменить всё, что ствол делает прямо сейчас. Вызывается, когда вариант
        /// выключают или ствол сменили: незаконченная перезарядка не должна
        /// дозарядиться в следующем стволе.
        /// </summary>
        public void CancelActions()
        {
            action = FirearmAction.Idle;
            actionEndTime = 0f;
            recoil = 0f;
            nextFireTime = 0f;
            clipAction = FirearmAction.Idle;
            clipActionDuration = 0f;
            clipActionElapsed = 0f;
        }

        public int MagazineCapacity => Spec != null ? Spec.MagazineCapacity : 0;

        /// <summary>
        /// Ячейка, в которой сейчас лежит этот ствол, или -1. Номер слота идёт
        /// от игрока: в руке лежит вариант, а предмет остаётся в инвентаре.
        /// </summary>
        private int MagazineSlot
        {
            get
            {
                if (firearm == null || inventory == null) return -1;
                return firearm.EquippedIndex;
            }
        }

        /// <summary>
        /// Стек именно этого ствола. Проверка ID обязательна: вариант один на вид
        /// ствола, и пока в руке пистолет, слот дробовика трогать нельзя.
        /// </summary>
        private ItemStack MagazineStack()
        {
            EnsureReady();

            int slot = MagazineSlot;
            if (slot < 0 || slot >= Inventory.TotalSlotCount) return null;

            ItemStack stack = inventory.Get(slot);
            if (stack == null || stack.IsEmpty || stack.Def == null) return null;
            if (Def == null || stack.Def.Id != Def.Id) return null;
            return stack;
        }

        public int Magazine
        {
            get
            {
                ItemStack stack = MagazineStack();
                if (stack == null) return 0;
                return Mathf.Clamp(stack.Payload, 0, MagazineCapacity);
            }
            set
            {
                ItemStack stack = MagazineStack();
                if (stack == null) return;
                stack.Payload = Mathf.Clamp(value, 0, MagazineCapacity);
            }
        }

        public int ReserveAmmo
        {
            get
            {
                EnsureReady();

                FirearmSpec spec = Spec;
                ItemDef ammo = spec != null ? spec.AmmoDef : null;
                return ammo != null && inventory != null ? inventory.Count(ammo) : 0;
            }
        }

        public bool HasReserveForReload => ReserveAmmo > 0;

        /// <summary>
        /// Хочет ли ствол стрелять прямо сейчас. Читает кнопку сам, потому что
        /// пистолет стреляет на зажатой ЛКМ, а дробовик — по нажатию, и это
        /// свойство ствола, а не игрока. Решение «можно ли» принимает
        /// PlayerFirearm и приходит сюда уже решённым: разрешено — Fire().
        /// </summary>
        public bool WantsFire
        {
            get
            {
                EnsureReady();

                FirearmSpec spec = Spec;
                if (spec == null || IsBusy) return false;

                return spec.HoldToFire
                    ? PlayerInteractor.AttackHeld
                    : PlayerInteractor.AttackPressed;
            }
        }

        public bool TryStartReload()
        {
            EnsureReady();

            if (Spec == null || IsBusy) return false;
            if (!HasReserveForReload) return false;
            if (Magazine >= MagazineCapacity) return false;

            action = FirearmAction.Reload;
            actionEndTime = Time.time + Mathf.Max(0.1f, Spec.ReloadTime);
            PlayReloadClip();
            return true;
        }

        public bool TryStartInspect()
        {
            EnsureReady();

            if (Spec == null || IsBusy) return false;

            action = FirearmAction.Inspect;
            actionEndTime = Time.time + Mathf.Max(0.2f, Spec.InspectTime);
            PlayInspectClip();
            return true;
        }

        /// <summary>
        /// Один выстрел. Перезарядку ствол не начинает сам: если магазин пуст,
        /// выстрел лишь переводит оружие в перезарядку — так ведут себя и
        /// настоящие стволы, и игрок не теряет нажатие.
        ///
        /// Темп стрельбы держит сам ствол, а не игрок: пауза между выстрелами
        /// берётся из Spec этого ствола, и смена оружия её не обнуляет.
        ///
        /// Возвращает true, если выстрел действительно был — с него игрок
        /// обновляет цифры патронов.
        /// </summary>
        public bool Fire()
        {
            EnsureReady();

            FirearmSpec spec = Spec;
            if (spec == null) return false;
            if (Time.time < nextFireTime) return false;

            if (Magazine <= 0)
            {
                // Пустой магазин: не жмём следующий выстрел, даём перезарядке
                // начаться один раз, иначе ЛКМ без патронов тикает вхолостую.
                nextFireTime = Time.time + 0.25f;
                TryStartReload();
                return false;
            }

            Magazine = Magazine - 1;
            nextFireTime = Time.time + Mathf.Max(0.05f, spec.FireInterval);
            PlayShoot();

            if (!TryGetAimRay(out Ray ray)) return true;

            Vector3 aimOrigin = ray.origin;
            Vector3 aimDirection = ray.direction;
            Vector3 origin = firePoint != null ? firePoint.position : aimOrigin + aimDirection * muzzleDistance;
            int pellets = Mathf.Max(1, spec.PelletsPerShot);
            int effects = 0;

            if (muzzleFlash) SpawnEffect(shotEffectPrefab, origin, aimDirection, true);

            for (int i = 0; i < pellets; i++)
            {
                Vector3 direction = Hitscan.ApplySpread(aimDirection, spec.SpreadDegrees);
                HitscanResult shot = Hitscan.Cast(origin, direction, spec.Range, spec.AssistRadius, shotMask, IgnoreTransform);

                Hitscan.ApplyDamage(shot, IgnoreTransform, spec.DamagePerPellet, false, spec.CarDamageMultiplier);

                if (showTracer) SpawnTracer(origin, shot.Point, tracerWidth, tracerColor, tracerSpeed);

                if (effects >= maxHitEffects) continue;
                if (shot.Distance >= spec.Range) continue;

                SpawnEffect(shotEffectPrefab, shot.Point, -shot.Direction, false);
                effects++;
            }

            return true;
        }

        /// <summary>
        /// Таймеры и переходы клипов живут здесь, а не на PlayerFirearm: они
        /// должны идти только пока ствол в руке. У выключенного варианта рук
        /// Update не вызывается, и брошенная перезарядка просто замирает.
        /// </summary>
        private void Update()
        {
            EnsureReady();

            EnsureIdlePose();

            if (recoil > 0f) recoil = Mathf.MoveTowards(recoil, 0f, recoverSpeed * Time.deltaTime);

            UpdateClipAction();

            // Поза выстрела живёт по своему короткому таймеру и ствол не
            // блокирует: после дробовика можно стрелять снова, не дожидаясь
            // конца клипа выстрела. Поэтому она сбрасывается здесь, а не
            // вместе с перезарядкой и осмотром.
            if (IsAction(FirearmAction.Shoot) && Time.time >= actionEndTime)
                action = FirearmAction.Idle;

            if (!IsBusy || Time.time < actionEndTime) return;

            // Клип ещё идёт — не даём закончить действие и выстрелить поверх
            // недоигранной перезарядки.
            if (WaitingOnClip) return;

            if (IsReloading) FinishReload();
            else action = FirearmAction.Idle;
        }

        /// <summary>
        /// Дозаряжает магазин из запаса. Дробовик заряжается по одному патрону,
        /// поэтому его ёмкость в 2 соблюдается автоматически: больше двух в ствол
        /// не влезет, сколько бы патронов ни было в запасе.
        /// </summary>
        private void FinishReload()
        {
            action = FirearmAction.Idle;

            FirearmSpec spec = Spec;
            if (spec == null) return;

            ItemDef ammo = spec.AmmoDef;
            if (ammo == null || inventory == null) return;

            int capacity = spec.MagazineCapacity;
            int loaded = Magazine;
            int room = capacity - loaded;
            if (room <= 0) return;

            int taken = inventory.Count(ammo);
            if (taken <= 0) return;

            int move = Mathf.Min(room, taken);
            if (inventory.Remove(ammo, move) <= 0) return;

            Magazine = loaded + move;
        }

        private void PlayShoot()
        {
            recoil = 1f;
            action = FirearmAction.Shoot;
            actionEndTime = Time.time + shootPoseTime;
            PlayState(1);
            StartClipAction(FirearmAction.Shoot, Spec != null ? Spec.ShotTime : shootPoseTime);
        }

        private static void SpawnEffect(GameObject prefab, Vector3 point, Vector3 forward, bool atMuzzle)
        {
            if (prefab == null) return;
            ShotEffect.Spawn(prefab, point, forward, atMuzzle);
        }

        private const float TracerMinTravel = 0.02f;
        private const float TracerMaxTravel = 0.15f;
        private const float TracerFadeTime = 0.06f;

        private static Material tracerMaterial;
        private static MaterialPropertyBlock tracerBlock;

        /// <summary>
        /// Луч выстрела: LineRenderer летит от дула к цели за доли секунды,
        /// задерживается на ней на кадр и гаснет. Живёт прямо на стволе, потому
        /// что толщину и цвет задаёт именно он.
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

        private bool TryGetAimRay(out Ray ray)
        {
            if (interactor != null) return interactor.TryGetAimRay(out ray);

            Camera camera = Camera.main;
            if (camera == null)
            {
                ray = default;
                return false;
            }

            ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return true;
        }

        /// <summary>
        /// Объект, который луч не должен зацепить. Раньше это был transform
        /// PlayerFirearm, то есть игрок; вариант руки для этого не годится —
        /// он висит на камере рук и тоже попал бы под луч.
        /// </summary>
        private Transform IgnoreTransform => playerController != null ? playerController.transform : transform;

        private void ResolveReferences()
        {
            firearm = GetComponentInParent<PlayerFirearm>();
            interactor = GetComponentInParent<PlayerInteractor>();
            inventory = GetComponentInParent<Inventory>();
            playerController = GetComponentInParent<PlayerController>();
        }

        private void ResolveAnimator()
        {
            if (animator != null) return;

            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        /// <summary>
        /// Проигрывает состояние контроллера ствола: 0 — Idle, 1 — Shot,
        /// 2 — перезарядка, 3 — осмотр. Возвращает false, если такого состояния
        /// в контроллере нет: иначе длину клипа пришлось бы мерить у того
        /// состояния, которое осталось на экране.
        /// </summary>
        private bool PlayState(int index)
        {
            if (!UsesAnimator) return false;

            string[] names = Def != null && Def.Firearm == FirearmKind.Shotgun ? ShotgunStates : PistolStates;

            string state = names[Mathf.Clamp(index, 0, names.Length - 1)];
            int hash = Animator.StringToHash(state);
            if (!animator.HasState(0, hash)) return false;

            animator.Play(hash, 0, 0f);
            animator.Update(0f);
            return true;
        }

        private void PlayReloadClip()
        {
            PlayState(2);
            StartClipAction(FirearmAction.Reload, Spec != null ? Spec.ReloadTime : 0.5f);
        }

        private void PlayInspectClip()
        {
            PlayState(3);
            StartClipAction(FirearmAction.Inspect, Spec != null ? Spec.InspectTime : 0.5f);
        }

        /// <summary>
        /// Длительность клипа берём из Spec, а не у Animator'а. Раньше длина
        /// мерилась сразу после Play через GetCurrentAnimatorStateInfo, и если
        /// Animator ещё не успевал переключиться, возвращалась длина Idle — около
        /// 6 с вместо 1.85 с у дробовика. Ствол застревал на кадре перезарядки,
        /// пока не истечёт это чужое время. Времена в Spec уже совпадают с
        /// длиной клипов, поэтому считаем их источником истины: так возврат в
        /// Idle всегда происходит ровно тогда, когда клип действительно кончился.
        /// </summary>
        private void StartClipAction(FirearmAction next, float duration)
        {
            clipAction = next;
            clipActionElapsed = 0f;
            clipActionDuration = Mathf.Max(0.05f, duration);
        }

        private void UpdateClipAction()
        {
            if (clipAction == FirearmAction.Idle || clipActionDuration <= 0f) return;

            clipActionElapsed += Time.deltaTime;
            if (clipActionElapsed < clipActionDuration) return;

            clipAction = FirearmAction.Idle;
            clipActionDuration = 0f;
            clipActionElapsed = 0f;
            PlayState(0);
        }
    }
}
