using CarGame.Combat;
using CarGame.Player;
using CarGame.UI;
using CarGame.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CarGame.Items
{
    /// <summary>
    /// Строй-предмет в руке: щит из металла, турель, таран. У него два режима:
    ///
    /// • Держание в руке (по умолчанию) — предмет обычный: виден в руке, его
    ///   можно выбросить по ПКМ. Призрака чертежа нет, но игрок замедляется.
    /// • Стройка — включается по ЛКМ. Предмет пропадает из руки, вместо него
    ///   виден призрак чертежа там, куда смотрит игрок. ЛКМ строит, ПКМ
    ///   возвращает в держание. В этом режиме игрок замедлен сильнее и не может
    ///   бежать.
    ///
    /// Галочка «Свободная постановка» (Allow Free Placement) отличает таран от
    /// щита/турели ровно одним: без неё предмет не ставит свой чертёж в мире, а
    /// достраивает только уже готовый чертёж под прицелом. Во всём остальном
    /// (режим стройки, замедление, поворот, возврат по ПКМ) он работает как
    /// обычная постройка, и префаб призрака ему не нужен.
    ///
    /// Чертёж ставится только на поверхность: точка низа (магнит) притягивается
    /// к коллайдеру прямо под ней. В воздухе и внутри «физических» объектов
    /// (предметы, машины, роботы) строить нельзя.
    ///
    /// Стройка по готовым чертежам в сцене приоритетна: если под прицелом есть
    /// чертёж этого вида, он достраивается, а не ставится новый. Готовый чертёж
    /// виден, только пока игрок со строй-предметом смотрит на него.
    ///
    /// Предмет одноразовый: после постройки он исчезает только у игрока
    /// (см. PlayerHand.ConsumeHeldItem), а вариант руки остаётся и просто
    /// выключается.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildItem : MonoBehaviour, IHandUsable, IBuildUsable
    {
        [Header("Чертёж")]
        [Tooltip("Вид постройки. Строит чертежи только этого вида")]
        [SerializeField] private TuningKind kind = TuningKind.Shield;
        [Tooltip("Строить чертежи ЛЮБОГО вида — для админских предметов (например, терминалов)")]
        [SerializeField] private bool buildAnyKind;
        [Tooltip("Префаб чертежа (с компонентом Blueprint), который ставится в мире")]
        [SerializeField] private GameObject blueprintPrefab;

        [Header("Куда ставить")]
        [Tooltip("Слои, по которым ищется поверхность под прицелом")]
        [SerializeField] private LayerMask placeMask = ~0;
        [Tooltip("Слои готовых чертежей под прицелом")]
        [SerializeField] private LayerMask blueprintMask = ~0;
        [Tooltip("Слои сплошных объектов, внутрь которых строить нельзя")]
        [SerializeField] private LayerMask solidMask = ~0;
        [Tooltip("Максимальная дистанция установки от камеры, м")]
        [SerializeField] private float maxDistance = 6f;
        [Tooltip("Насколько далеко вниз магнит ищет коллайдер под собой, м")]
        [SerializeField] private float magnetDrop = 4f;
        [Tooltip("Запрещать постановку, если объём постройки пересекает сплошную геометрию")]
        [SerializeField] private bool preventOverlap = true;

        [Header("Сетка")]
        [Tooltip("Шаг сетки по горизонтали, м. 0 — без выравнивания")]
        [SerializeField] private float gridSize = 1f;
        [Tooltip("Шаг поворота вокруг вертикали, градусы. 0 — без выравнивания")]
        [SerializeField] private float rotationStep = 90f;
        [Tooltip("Показывать призрак под прицелом. Выключено — только строить по готовым чертежам в сцене")]
        [SerializeField] private bool showGhost = true;
        [Tooltip("Разрешить свободную постановку чертежа на поверхность. Выключено — деталь ставится только на уже существующий чертёж под прицелом (таран)")]
        [SerializeField] private bool allowFreePlacement = true;

        [Header("Поворот")]
        [Tooltip("Клавиша, которой чертёж крутится вокруг вертикали")]
        [SerializeField] private Key rotateKey = Key.R;
        [Tooltip("На сколько градусов поворот за нажатие, если шаг поворота включён нулевым")]
        [SerializeField] private float rotateFallback = 45f;

        [Header("Замедление")]
        [Range(0.1f, 1f)]
        [Tooltip("Множитель скорости, пока строй-предмет лежит в руке")]
        [SerializeField] private float holdSpeedMultiplier = 0.75f;
        [Range(0.1f, 1f)]
        [Tooltip("Множитель скорости в режиме стройки. Бег при этом отключён")]
        [SerializeField] private float buildSpeedMultiplier = 0.5f;

        private readonly RaycastHit[] blueprintHits = new RaycastHit[16];
        private readonly Collider[] overlapBuffer = new Collider[16];

        private PlayerHand hand;
        private PlayerController player;
        private Transform playerRoot;
        private GameObject preview;
        private Blueprint previewBlueprint;
        private Blueprint revealedBlueprint;
        private Collider floorCollider;
        private Renderer[] heldRenderers;
        private bool heldVisualVisible = true;
        private bool buildMode;
        private bool placementValid;
        private float yawOffset;

        public TuningKind Kind => kind;
        public bool BuildModeActive => buildMode;

        private void Awake()
        {
            ResolveRefs();
        }

        private void OnDisable()
        {
            HideReveal();
            ClearPreview();
            if (player != null) player.ClearCarrySlow();
        }

        private void OnDestroy()
        {
            HideReveal();
            ClearPreview();
            if (player != null) player.ClearCarrySlow();
        }

        private void Update()
        {
            ResolveRefs();

            if (player != null)
                player.SetCarrySlow(buildMode ? buildSpeedMultiplier : holdSpeedMultiplier, buildMode);

            if (hand == null || !hand.IsHolding)
            {
                HideReveal();
                ClearPreview();
                return;
            }

            // Готовые чертежи в сцене видны ТОЛЬКО в режиме стройки: в простом
            // держании предмета прицеливание по ним ничего не подсвечивает.
            if (!buildMode)
            {
                HideReveal();
                return;
            }

            UpdateAimReveal();

            // Без свободной постановки призрак не нужен: деталь ставится только
            // на готовый чертёж под прицелом, а его подсветку делает UpdateAimReveal.
            if (!allowFreePlacement)
            {
                ClearPreview();
                return;
            }

            if (!showGhost || InventoryUI.AnyOpen) { ShowPreview(false); return; }
            if (revealedBlueprint != null) { ShowPreview(false); return; }

            EnsurePreview();
            if (preview == null) return;

            ShowPreview(true);
            if (RotatePressed()) yawOffset += RotateIncrement;
            UpdatePreviewPose();

            // Где строить нельзя — призрака не видно (предмет остаётся в руке).
            if (previewBlueprint != null) previewBlueprint.SetGhostVisible(placementValid);
            else ShowPreview(placementValid);
        }

        public void OnEquipped(PlayerHand source)
        {
            if (source != null) hand = source;
            ResolveRefs();

            buildMode = false;
            yawOffset = 0f;
            HideReveal();
            SetHeldVisible(true);
            ClearPreview();
        }

        public void OnUsePressed(PlayerHand source)
        {
            if (source != null) hand = source;
            ResolveRefs();

            // В обычном держании ЛКМ переводит предмет в режим стройки.
            if (!buildMode)
            {
                EnterBuildMode();
                return;
            }

            // В режиме стройки готовый чертёж под прицелом важнее нового.
            Blueprint target = FindAimedBlueprint();
            if (target != null)
            {
                BuildTarget(target);
                return;
            }

            // Свободная постановка запрещена — строим только на готовый чертёж.
            if (!allowFreePlacement) return;

            BuildAtPreview();
        }

        public void OnUseHeld(PlayerHand source)
        {
            if (source != null) hand = source;
        }

        public void OnUseReleased(PlayerHand source)
        {
            if (source != null) hand = source;
        }

        public void OnUseCanceled()
        {
            ExitBuildMode();
        }

        /// <summary>ПКМ в режиме стройки: не выбрасываем, а возвращаем предмет в руку.</summary>
        public void OnSecondary(PlayerHand source)
        {
            if (source != null) hand = source;
            ExitBuildMode();
        }

        private void EnterBuildMode()
        {
            buildMode = true;
            yawOffset = 0f;

            // Режим стройки одинаков для всех построек: предмет уходит из руки.
            SetHeldVisible(false);

            // Свой призрак (свободная постановка) — только если она разрешена.
            // Без неё предмет строит по готовому чертежу под прицелом, и префаб
            // призрака ему не нужен.
            if (allowFreePlacement && showGhost) EnsurePreview();
            else ClearPreview();
        }

        private void ExitBuildMode()
        {
            buildMode = false;
            SetHeldVisible(true);
            ClearPreview();
        }

        private void BuildTarget(Blueprint target)
        {
            if (target == null) return;
            // Чертежи одного держателя — варианты одного слота: пока один из них
            // построен, остальные не строятся (иначе стопка встанет друг на друга).
            BlueprintHolder holder = target.Holder;
            if (holder != null && holder.HasBuiltSibling(target)) return;
            if (!target.Commit()) return;

            HideReveal();
            ClearPreview();
            if (hand != null) hand.ConsumeHeldItem();
        }

        private void BuildAtPreview()
        {
            if (preview == null || previewBlueprint == null)
            {
                ClearPreview();
                return;
            }

            // В воздухе или в физическом объекте ставить нельзя — молча ничего не делаем.
            if (!placementValid) return;

            GameObject placed = preview;
            preview = null;
            previewBlueprint = null;

            placed.SetActive(true);
            Blueprint blueprint = placed.GetComponent<Blueprint>();
            if (blueprint != null) blueprint.Commit();

            if (hand != null) hand.ConsumeHeldItem();
        }

        private void EnsurePreview()
        {
            if (preview != null || blueprintPrefab == null) return;

            preview = Instantiate(blueprintPrefab);
            preview.name = blueprintPrefab.name + " (Blueprint)";
            previewBlueprint = preview.GetComponent<Blueprint>();

            if (previewBlueprint != null) previewBlueprint.SetPreviewMode();
            else DisableColliders(preview);

            ShowPreview(true);
            UpdatePreviewPose();
        }

        private void ShowPreview(bool visible)
        {
            if (preview == null) return;
            if (preview.activeSelf != visible) preview.SetActive(visible);
        }

        private void UpdatePreviewPose()
        {
            if (preview == null || hand == null) { placementValid = false; return; }
            if (!hand.TryGetAimRay(out Ray ray)) { placementValid = false; return; }

            Quaternion rotation = SnapRotation(ray.direction);

            // 1) Куда смотрит игрок. Не попали ни во что — в воздухе не строим.
            if (!Physics.Raycast(ray, out RaycastHit aimHit, maxDistance, placeMask, QueryTriggerInteraction.Ignore))
            {
                preview.transform.SetPositionAndRotation(ray.origin + ray.direction * maxDistance * 0.5f, rotation);
                placementValid = false;
                return;
            }

            preview.transform.SetPositionAndRotation(aimHit.point, rotation);

            // 2) Выравниваем по сетке по горизонтали ДО спуска магнита, чтобы он
            //    искал поверхность уже ровно на месте будущей постройки.
            preview.transform.position = SnapToGridHorizontal(preview.transform.position);

            // 3) Магнит (низ) должен лечь на коллайдер прямо под ним. Луч пускаем
            //    с запасом вверх, чтобы не начать внутри геометрии, если магнит
            //    оказался ниже поверхности прицела.
            Vector3 magnet = previewBlueprint != null ? previewBlueprint.MagnetPosition : preview.transform.position;
            Vector3 dropOrigin = new Vector3(magnet.x, aimHit.point.y + 0.5f, magnet.z);
            if (!Physics.Raycast(dropOrigin, Vector3.down, out RaycastHit groundHit, magnetDrop + 0.5f, placeMask, QueryTriggerInteraction.Ignore))
            {
                placementValid = false;
                return;
            }

            preview.transform.position += groundHit.point - magnet;
            floorCollider = groundHit.collider;

            // 4) Ни на физический объект, ни внутрь сплошной геометрии.
            //    Чертёж с IgnoreColliders ставится и поверх чужих коллайдеров
            //    (например, если он часть машины и касается её).
            bool ignoreColliders = previewBlueprint != null && previewBlueprint.IgnoreColliders;
            bool badSurface = groundHit.normal.y < 0.05f;
            if (!ignoreColliders) badSurface |= IsForbidden(aimHit) || IsForbidden(groundHit);

            placementValid = !badSurface && !(preventOverlap && !ignoreColliders && OverlapsSolid());
        }

        /// <summary>
        /// «Физический» объект — с любым Rigidbody (двигаемым или нет), предмет,
        /// машина или робот. На такие ставить нельзя: постройка не должна
        /// врастать в модель терминала.
        /// </summary>
        private bool IsForbidden(RaycastHit hit)
        {
            Collider collider = hit.collider;
            if (collider == null) return false;

            if (playerRoot != null && collider.transform.IsChildOf(playerRoot)) return true;
            return IsForbiddenCollider(collider);
        }

        /// <summary>
        /// «Физический» объект — с любым Rigidbody (двигаемым или нет), предмет,
        /// машина или робот. На такие ставить нельзя: постройка не должна
        /// врастать в модель терминала. Сам чертёж поверхность не запрещает:
        /// чертёж можно ставить на другой чертёж.
        /// </summary>
        private bool IsForbiddenCollider(Collider collider)
        {
            if (collider == null) return false;

            if (collider.GetComponentInParent<Blueprint>() != null) return false;
            if (collider.attachedRigidbody != null) return true;

            if (collider.GetComponentInParent<PlayerController>() != null) return true;
            if (collider.GetComponentInParent<Health>() != null) return true;
            if (collider.GetComponentInParent<PickupItem>() != null) return true;
            if (collider.GetComponentInParent<MetalItem>() != null) return true;
            if (collider.GetComponentInParent<CarController>() != null) return true;

            return false;
        }

        /// <summary>
        /// Занята ли клетка постройки: пересекает ли её объём ЛЮБОЙ другой
        /// коллайдер (не только физический объект). Так терминал с меш-коллайдером
        /// без Rigidbody тоже блокирует клетку. Поверхность под магнитом
        /// (<see cref="floorCollider"/>) и всё, что ниже низа постройки,
        /// исключаются — иначе нельзя было бы строить на земле. ПУСТЫЕ чертежи
        /// (и их призраки) исключаются: их можно ставить вплотную и друг на
        /// друга. А вот ГОТОВАЯ постройка клетку занимает — внутрь неё строить
        /// нельзя.
        /// </summary>
        private bool OverlapsSolid()
        {
            if (preview == null) return false;

            Vector3 center;
            Vector3 extents;
            Quaternion rotation = preview.transform.rotation;

            if (TryGetPreviewBounds(out Bounds bounds) && bounds.extents.sqrMagnitude > 0.01f)
            {
                center = bounds.center;
                extents = bounds.extents;
            }
            else
            {
                // Габаритов нет или они вырождены — берём клетку сетки вокруг
                // магнита, чтобы проверка всё равно работала.
                float size = gridSize > 0.01f ? gridSize : 1f;
                Vector3 magnet = previewBlueprint != null ? previewBlueprint.MagnetPosition : preview.transform.position;
                center = magnet + Vector3.up * size * 0.5f;
                extents = new Vector3(size * 0.5f, size * 0.5f, size * 0.5f);
                rotation = Quaternion.identity;
            }

            extents.x = Mathf.Max(extents.x, 0.05f);
            extents.y = Mathf.Max(extents.y, 0.05f);
            extents.z = Mathf.Max(extents.z, 0.05f);

            // Не меньше клетки сетки по горизонтали: объект, стоящий в клетке,
            // должен блокировать её целиком, даже если сама постройка уже.
            if (gridSize > 0.01f)
            {
                extents.x = Mathf.Max(extents.x, gridSize * 0.5f);
                extents.z = Mathf.Max(extents.z, gridSize * 0.5f);
            }

            float magnetY = (previewBlueprint != null ? previewBlueprint.MagnetPosition : preview.transform.position).y;
            int count = Physics.OverlapBoxNonAlloc(center, extents, overlapBuffer, rotation, solidMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = overlapBuffer[i];
                if (collider == null) continue;
                if (collider.transform.IsChildOf(preview.transform)) continue;
                if (playerRoot != null && collider.transform.IsChildOf(playerRoot)) continue;

                // Пустой чертёж/призрак клетку не занимает — на него ставить
                // можно. Готовую постройку не пропускаем: внутрь неё нельзя.
                Blueprint blue = collider.GetComponentInParent<Blueprint>();
                if (blue != null && !blue.IsBuilt) continue;

                // Поверхность, на которую ставим, и всё, что лежит не выше её,
                // клетку не занимают — иначе земля блокировала бы сама себя.
                if (collider == floorCollider) continue;
                if (collider.bounds.max.y <= magnetY + 0.1f) continue;

                return true;
            }

            return false;
        }

        private bool TryGetPreviewBounds(out Bounds bounds)
        {
            bounds = default;
            if (preview == null) return false;

            bool has = false;

            // Считаем и выключенные рендеры/коллайдеры: настоящая деталь лежит
            // в призраке выключенной, но её габариты нужны для проверки клетки.
            Renderer[] renderers = preview.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (!has) { bounds = renderer.bounds; has = true; }
                else { bounds.Encapsulate(renderer.bounds); }
            }

            Collider[] colliders = preview.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                if (!has) { bounds = collider.bounds; has = true; }
                else { bounds.Encapsulate(collider.bounds); }
            }

            return has;
        }

        private Vector3 SnapToGridHorizontal(Vector3 point)
        {
            if (gridSize <= 0.01f) return point;

            float size = gridSize;
            return new Vector3(
                Mathf.Round(point.x / size) * size,
                point.y,
                Mathf.Round(point.z / size) * size);
        }

        private Quaternion SnapRotation(Vector3 direction)
        {
            Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float step = Mathf.Max(0f, rotationStep);
            if (step > 0.01f) yaw = Mathf.Round(yaw / step) * step;

            return Quaternion.Euler(0f, yaw + yawOffset, 0f);
        }

        /// <summary>Нажата ли клавиша поворота чертежа (по умолчанию R).</summary>
        private bool RotatePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return false;

            KeyControl control = keyboard[rotateKey];
            return control != null && control.wasPressedThisFrame;
        }

        /// <summary>На сколько градусов крутится чертёж за нажатие.</summary>
        private float RotateIncrement => rotationStep > 0.01f ? rotationStep : Mathf.Max(0f, rotateFallback);

        /// <summary>
        /// Подсвечивает готовый чертёж под прицелом. Смена цели гасит прошлый,
        /// уход предмета из руки — тоже.
        /// </summary>
        private void UpdateAimReveal()
        {
            Blueprint aimed = FindAimedBlueprint();
            if (ReferenceEquals(aimed, revealedBlueprint)) return;

            if (revealedBlueprint != null) revealedBlueprint.SetRevealed(false);
            revealedBlueprint = aimed;
            if (revealedBlueprint != null) revealedBlueprint.SetRevealed(true);
        }

        private void HideReveal()
        {
            if (revealedBlueprint != null) revealedBlueprint.SetRevealed(false);
            revealedBlueprint = null;
        }

        /// <summary>Готовый чертёж под прицелом. Ищем сами, чтобы он был важнее прочих целей.</summary>
        private Blueprint FindAimedBlueprint()
        {
            if (hand == null || !hand.TryGetAimRay(out Ray ray)) return null;

            int count = Physics.RaycastNonAlloc(ray, blueprintHits, maxDistance, blueprintMask, QueryTriggerInteraction.Collide);
            string heldVariantId = hand.ActiveVariantItemId;

            Blueprint best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = blueprintHits[i];
                if (hit.collider == null) continue;
                if (playerRoot != null && hit.collider.transform.IsChildOf(playerRoot)) continue;

                Blueprint blueprint = hit.collider.GetComponentInParent<Blueprint>();
                if (blueprint == null || blueprint.IsBuilt || blueprint.IsPreview || blueprint.IsConsumed) continue;
                // За стеной чертёж строить нельзя, но кузов собственной машины
                // чертежу не помеха — сам чертёж на нём лежит.
                if (!blueprint.IgnoreColliders && IsBlockedByFrontColliders(hit, blueprint, count)) continue;
                if (!blueprint.Accepts(heldVariantId, kind, buildAnyKind)) continue;

                // Варианты одного слота: если у держателя уже есть построенный
                // собрат, этот чертёж строить нельзя.
                BlueprintHolder holder = blueprint.Holder;
                if (holder != null && holder.HasBuiltSibling(blueprint)) continue;

                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    best = blueprint;
                }
            }

            return best;
        }

        /// <summary>
        /// Стоит ли перед этим чертежем чужое препятствие — стена или другой
        /// автомобиль. Коллайдеры СВОЕЙ машины в расчёт не берутся: чертёж
        /// лежит на кузове, и кузов почти всегда ближе собственного чертежа,
        /// из-за чего прицел и «залипал» бы на машине.
        /// </summary>
        private bool IsBlockedByFrontColliders(RaycastHit hit, Blueprint blueprint, int count)
        {
            CarController blueprintCar = blueprint.GetComponentInParent<CarController>();
            float nearest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit other = blueprintHits[i];
                if (other.collider == null) continue;
                if (playerRoot != null && other.collider.transform.IsChildOf(playerRoot)) continue;
                if (blueprintCar != null && other.collider.GetComponentInParent<CarController>() == blueprintCar) continue;
                if (other.distance < nearest) nearest = other.distance;
            }

            return hit.distance > nearest + 0.05f;
        }

        private void SetHeldVisible(bool visible)
        {
            if (heldVisualVisible == visible) return;

            if (heldRenderers == null) heldRenderers = GetComponentsInChildren<Renderer>(true);
            heldVisualVisible = visible;

            for (int i = 0; i < heldRenderers.Length; i++)
            {
                if (heldRenderers[i] != null) heldRenderers[i].enabled = visible;
            }
        }

        private void ClearPreview()
        {
            if (preview != null) Destroy(preview);
            preview = null;
            previewBlueprint = null;
            floorCollider = null;
        }

        private void DisableColliders(GameObject source)
        {
            if (source == null) return;

            Collider[] colliders = source.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = false;
            }
        }

        private void ResolveRefs()
        {
            if (hand == null) hand = GetComponentInParent<PlayerHand>();
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (playerRoot == null && player != null) playerRoot = player.transform;
        }
    }

    /// <summary>
    /// Предмет в руке, у которого ПКМ может быть не броском, а отменой режима.
    /// Его читает <see cref="CarGame.Player.PlayerHand"/>. Пока <see cref="BuildModeActive"/>
    /// false, ПКМ работает как обычно — предмет можно выбросить.
    /// </summary>
    public interface IBuildUsable
    {
        bool BuildModeActive { get; }
        void OnSecondary(PlayerHand hand);
    }
}
