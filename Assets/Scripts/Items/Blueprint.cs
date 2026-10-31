using System.Collections.Generic;
using CarGame.Audio;
using CarGame.Combat;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Чертёж постройки (щит из металла, турель, таран). Хранит две модели:
    /// призрачную (прозрачную) и настоящую. Пока чертёж не построен, видна
    /// только призрачная модель — и то не всегда:
    ///
    /// • если чертёж загодя лежит в сцене (например, закреплён на машине как
    ///   дочерний объект), он невидим, пока игрок со строй-предметом в руке не
    ///   наведётся на его коллайдер — тогда <see cref="BuildItem"/> показывает
    ///   призрак (<see cref="SetRevealed"/>);
    /// • если чертёж ставит игрок из руки, его создаёт <see cref="BuildItem"/>
    ///   сразу в режиме призрака (<see cref="SetPreviewMode"/>) и двигает по
    ///   прицелу, пока не будет нажата ЛКМ.
    ///
    /// Чертёж НЕ взаимодействует по E: его достраивает только строй-предмет в
    /// режиме стройки по ЛКМ. До постройки коллайдер чертежа — триггер: он не
    /// мешает физике, но по нему можно целиться. После постройки коллайдер
    /// становится обычным. Сооружение ставится так, чтобы точка низа
    /// (<see cref="magnet"/>) лежала на поверхности под ней.
    ///
    /// Постройка (<see cref="Commit"/>) гасит призрак, включает настоящую деталь
    /// и проигрывает звук стройки; разрушение — звук разрушения.
    /// </summary>
    [DisallowMultipleComponent]
    public class Blueprint : MonoBehaviour
    {
        [Header("Что это")]
        [Tooltip("Вид постройки. Используется, если не задан точный предмет (acceptedItemId)")]
        [SerializeField] private TuningKind kind = TuningKind.Shield;
        [Tooltip("Точный предмет, который принимает этот чертёж — ID варианта руки из разметки handVariants: 24, 25, 26 (щиты), 16, 17, 18 (турели), 21, 22, 23 (тараны); " +
                 "подойдёт и полное имя (shield_scrap и т.п.). Пусто — принимать по виду (kind)")]
        [SerializeField] private string acceptedItemId;

        [Header("Модели")]
        [Tooltip("Прозрачный призрак. Виден, пока чертёж не построен: в предпросмотре и когда на чертёж наводятся")]
        [SerializeField] private GameObject ghost;
        [Tooltip("Настоящая деталь. Включается после постройки. Здесь же живут рабочие компоненты (PlayerTurret, CarRam и т.п.)")]
        [SerializeField] private GameObject built;
        [Tooltip("Прочность настоящей детали. Пусто — ищется на ней. Когда кончается, чертёж снова становится пустым")]
        [SerializeField] private Health builtHealth;
        [Tooltip("Точка низа/магнит: к ней притягивается поверхность при установке. Пусто — берётся корень чертежа")]
        [SerializeField] private Transform magnet;
        [Tooltip("Чертёж уже построен на старте")]
        [SerializeField] private bool startBuilt;
        [Tooltip("После поломки постройки чертёж снова становится пустым и готовым к постройке — можно отстроить заново (важно для чертежей, уже стоящих в сцене). Выключено — чертёж используется один раз")]
        [SerializeField] private bool rebuildable = true;
        [Tooltip("Игнорировать пересечение с другими коллайдерами: постройку можно возвести, даже если она пересекает чужой коллайдер. Нужно для чертежей — частей машины, которые касаются её коллайдеров")]
        [SerializeField] private bool ignoreColliders;

        [Header("Движение")]
        [Tooltip("Запретить движение построенной детали (машина должна стоять на месте, пока игрок не разрешит ей ездить). По умолчанию движение разрешено.")]
        [SerializeField] private bool movementLocked = false;

        [Header("Звук")]
        [Tooltip("Звук стройки. Пусто — генерируется")]
        [SerializeField] private AudioClip buildClip;
        [Tooltip("Звук разрушения постройки. Пусто — генерируется")]
        [SerializeField] private AudioClip destroyClip;
        [Range(0f, 1f)]
        [Tooltip("Громкость звуков постройки")]
        [SerializeField] private float soundVolume = 0.85f;

        private Collider[] ownColliders;
        private AudioSource audioSource;
        private AudioClip generatedBuildClip;
        private AudioClip generatedDestroyClip;
        private BlueprintHolder holder;
        private bool previewMode;
        private bool isBuilt;
        private bool consumed;
        private bool initialized;

        public TuningKind Kind => kind;
        public bool IsBuilt => isBuilt;
        public bool IsPreview => previewMode;
        /// <summary>Чертёж израсходован: постройка разрушена и больше не строится.</summary>
        public bool IsConsumed => consumed;

        /// <summary>
        /// Запретить движение построенной структуры (машины должны стоять на
        /// месте, пока игрок не разрешит им ездить). По умолчанию движение
        /// разрешено — поэтому Car-item-built car после постройки едет сразу.
        /// </summary>
        public bool MovementLocked => movementLocked;

        /// <summary>
        /// Запретить/разрешить движение построенной структуры. Для машин
        /// (чертежи с <see cref="CarGame.Vehicle.CarController"/>) после постройки
        /// из предмета вызывается с <c>false</c>: иначе Car-item-built car
        /// оказывается в состоянии, когда физическое движение запрещено.
        /// </summary>
        public void SetMovementLocked(bool locked)
        {
            movementLocked = locked;
            if (built != null)
            {
                Rigidbody rb = built.GetComponentInChildren<Rigidbody>();
                if (rb != null)
                {
                    rb.constraints = locked ? RigidbodyConstraints.FreezePosition : RigidbodyConstraints.None;
                    return;
                }
            }

            Rigidbody fallbackRb = GetComponent<Rigidbody>();
            if (fallbackRb != null) fallbackRb.constraints = locked ? RigidbodyConstraints.FreezePosition : RigidbodyConstraints.None;
        }

        /// <summary>Разрешить постройку даже при пересечении с чужими коллайдерами.</summary>
        public bool IgnoreColliders => ignoreColliders;

        /// <summary>Прочность настоящей детали — для HUD и гаечного ключа.</summary>
        public Health BuiltHealth => builtHealth;

        /// <summary>
        /// Держатель этого чертежа (Empty с <see cref="BlueprintHolder"/>), если
        /// он есть. Чертежи одного держателя — варианты одного слота: пока один
        /// построен, остальные строить нельзя. Чертежи разных держателей (или
        /// без них) друг друга не блокируют, даже касаясь.
        /// </summary>
        public BlueprintHolder Holder
        {
            get
            {
                if (holder == null) holder = GetComponentInParent<BlueprintHolder>();
                return holder;
            }
        }

        /// <summary>
        /// Прочность, которой принадлежит попавший коллайдер. У постройки
        /// (щит/турель/таран) она лежит на дочерней «настоящей» детали, а луч и
        /// удар ключа попадают в коллайдер корня чертежа. Поэтому сначала через
        /// чертёж достаём прочность постройки, и лишь потом — прочность родителя
        /// (у турели на машине родитель — кузов, и без этого урон уходил бы в него).
        /// </summary>
        public static Health ResolveHealth(Collider collider)
        {
            if (collider == null) return null;

            Blueprint blueprint = collider.GetComponentInParent<Blueprint>();
            if (blueprint != null && blueprint.isBuilt && blueprint.builtHealth != null)
                return blueprint.builtHealth;

            return collider.GetComponentInParent<Health>();
        }

        /// <summary>Мировая точка низа/магнита — к ней притягивается поверхность при установке.</summary>
        public Vector3 MagnetPosition => magnet != null ? magnet.position : transform.position;

        /// <summary>
        /// Мировые габариты постройки по этому чертежу — по коллайдерам и
        /// рендерам, включая выключенную настоящую деталь. Нужны, чтобы на одно
        /// место нельзя было поставить вторую постройку.
        /// </summary>
        public bool TryGetFootprint(out Bounds bounds)
        {
            bounds = default;
            bool has = false;

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;
                if (!has) { bounds = colliders[i].bounds; has = true; }
                else bounds.Encapsulate(colliders[i].bounds);
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (!has) { bounds = renderers[i].bounds; has = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }

            return has;
        }

        /// <summary>
        /// Принимает ли чертёж предмет, который игрок держит в руке. Сравнение
        /// идёт по ID варианта руки (PlayerHand.ActiveVariantItemId — разметка
        /// handVariants в сцене), а не по ItemDef: чертёгу всё равно, откуда
        /// пришёл предмет, из мира или из терминала. Если задан точный предмет
        /// (<see cref="acceptedItemId"/>) — совпадение строго по нему, поэтому
        /// ржавый щит встаёт только на чертёж ржавого щита, а платиновый — на
        /// чертёж платинового; оба ID сводятся через ItemDefs.Canonical, так
        /// что и числовой "24", и "shield_scrap" — одно и то же. Иначе — по
        /// виду (kind), что оставлено для универсальных чертежей.
        /// </summary>
        public bool Accepts(string heldVariantId, TuningKind heldKind, bool anyItem)
        {
            if (anyItem) return true;

            if (!string.IsNullOrEmpty(acceptedItemId))
            {
                if (string.IsNullOrEmpty(heldVariantId)) return false;
                return string.Equals(
                    ItemDefs.Canonical(acceptedItemId),
                    ItemDefs.Canonical(heldVariantId),
                    System.StringComparison.OrdinalIgnoreCase);
            }

            return heldKind == kind;
        }

        private void Awake()
        {
            // Коллайдеры самого чертежа (корень и его дети), но не коллайдеры
            // настоящей детали: у неё могут быть свои триггеры, которые трогать
            // нельзя. Именно эти коллайдеры мы держим триггером, пока пусто.
            List<Collider> own = new List<Collider>(8);
            Collider[] all = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Collider collider = all[i];
                if (collider == null) continue;
                if (built != null && collider.transform.IsChildOf(built.transform)) continue;
                own.Add(collider);
            }

            ownColliders = own.ToArray();

            if (builtHealth == null && built != null) builtHealth = built.GetComponentInChildren<Health>(true);
            if (builtHealth != null)
            {
                // Постройка всегда «строение»: по этому признаку HUD показывает её
                // прочность при наведении, а ключ чинит её/ломает как постройку.
                builtHealth.SetOwner(Health.HpOwner.Structure);
                builtHealth.Died += OnBuiltDied;
            }

            if (startBuilt) Commit();
            else RestoreEmpty();

            initialized = true;
        }

        private void OnDestroy()
        {
            if (builtHealth != null) builtHealth.Died -= OnBuiltDied;
        }

        /// <summary>
        /// Показать/спрятать призрак пустого чертежа. Так <see cref="BuildItem"/>
        /// подсвечивает готовый чертёж, на который смотрит игрок.
        /// </summary>
        public void SetRevealed(bool value)
        {
            if (isBuilt || previewMode || consumed) return;
            ShowGhost(value);
        }

        /// <summary>
        /// Показать/спрятать именно призрак, не трогая корень чертежа. Так
        /// предпросмотр становится невидимым там, где строить нельзя, но
        /// объект остаётся активным — по нему считаются габариты клетки.
        /// </summary>
        public void SetGhostVisible(bool visible)
        {
            ShowGhost(visible);
        }

        /// <summary>
        /// Режим предпросмотра: призрак виден, коллайдеры и настоящая деталь
        /// выключены, чтобы призрак не мешал прицелу и не считался постройкой.
        /// </summary>
        public void SetPreviewMode()
        {
            if (consumed) return;

            previewMode = true;
            isBuilt = false;
            if (built != null) built.SetActive(false);
            ShowGhost(true);
            SetColliders(false, false);
        }

        /// <summary>Достраивает чертёж: призрак гаснет, настоящая деталь включается. false — уже построен.</summary>
        public bool Commit()
        {
            if (isBuilt || consumed) return false;

            previewMode = false;
            isBuilt = true;
            ShowGhost(false);
            if (built != null)
            {
                built.SetActive(true);
                // Постройка завершена — двигаться должно быть разрешено: машины,
                // собранные из предмета (CarItem), едут сразу после постройки, а не
                // остаются в состоянии с запрещенным движением.
                SetMovementLocked(false);
                // Объект, разрешённый к движению (машина), больше не управляется
                // скриптом чертежа: отключаем блокировку физики и отцепляем готовый
                // объект от инстанса префаба чертежа, чтобы физика и управление
                // шли через Rigidbody/CarController, а не через иерархию префаба.
                if (IsMovableBuilt()) DetachBuiltFromBlueprint();
            }
            SetColliders(true, true);
            if (builtHealth != null) builtHealth.ResetToFull();

            if (initialized) PlaySound(buildClip != null ? buildClip : GeneratedBuildClip());
            return true;
        }

        /// <summary>
        /// Готовый объект управляемой физической постройки (машины) несёт Rigidbody:
        /// такие объекты управляются физикой, а не скриптом чертежа.
        /// </summary>
        private bool IsMovableBuilt()
        {
            return built != null && built.GetComponentInChildren<Rigidbody>() != null;
        }

        /// <summary>
        /// Отцепляет готовый объект от инстанса префаба чертежа. Вызывается после
        /// возведения подвижной постройки (машины): объект сначала включается, а
        /// затем выкидывается из своего родительского объекта (префаба чертежа),
        /// чтобы скрипт чертежа больше не вёл с ним никакого управления.
        /// </summary>
        private void DetachBuiltFromBlueprint()
        {
            if (built == null) return;
            if (built.transform.parent == null) return;
            built.transform.SetParent(null, true);
        }

        private void OnBuiltDied(Health source)
        {
            // Разрушенная деталь исчезает в любом случае: иначе мёртвый корпус
            // остался бы стоять на месте. Дальше решает rebuildable: многоразовый
            // чертёж снова становится пустым и готовым к постройке, одноразовый —
            // израсходованным (корпус прячется, коллайдеры гаснут).
            PlaySound(destroyClip != null ? destroyClip : GeneratedDestroyClip());
            if (rebuildable) RestoreEmpty();
            else Consume();
        }

        /// <summary>
        /// Чертёж израсходован: постройка разрушена без права перестройки. Корпус
        /// прячется, коллайдеры гаснут — так одноразовая постройка (например,
        /// поставленная из руки турель) после смерти не остаётся стоять.
        /// </summary>
        private void Consume()
        {
            consumed = true;
            previewMode = false;
            isBuilt = false;
            ShowGhost(false);
            if (built != null) built.SetActive(false);
            SetColliders(false, false);
        }

        private void RestoreEmpty()
        {
            isBuilt = false;
            ShowGhost(false);
            if (built != null) built.SetActive(false);
            SetColliders(true, false);
        }

        private void ShowGhost(bool visible)
        {
            if (ghost != null && ghost.activeSelf != visible) ghost.SetActive(visible);
        }

        /// <summary>
        /// Включает/выключает коллайдеры чертежа. Пустой чертёж — триггер
        /// (solid: false), построенный — обычный коллайдер (solid: true).
        /// </summary>
        private void SetColliders(bool enabled, bool solid)
        {
            if (ownColliders == null) return;

            for (int i = 0; i < ownColliders.Length; i++)
            {
                Collider collider = ownColliders[i];
                if (collider == null) continue;

                collider.isTrigger = !solid;
                collider.enabled = enabled;
            }
        }

        private AudioClip GeneratedBuildClip()
        {
            if (generatedBuildClip == null) generatedBuildClip = ProceduralAudio.Impact(true);
            return generatedBuildClip;
        }

        private AudioClip GeneratedDestroyClip()
        {
            if (generatedDestroyClip == null) generatedDestroyClip = ProceduralAudio.Explosion();
            return generatedDestroyClip;
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip == null) return;

            AudioSource source = ResolveAudioSource();
            if (source == null) return;

            source.PlayOneShot(clip, soundVolume);
        }

        private AudioSource ResolveAudioSource()
        {
            if (audioSource != null) return audioSource;

            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            return audioSource;
        }
    }
}
