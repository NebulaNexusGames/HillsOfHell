using CarGame.Audio;
using CarGame.Combat;
using CarGame.Player;
using CarGame.Vehicle;
using UnityEngine;

namespace CarGame.Items
{
    public interface IHandUsable
    {
        void OnUsePressed(PlayerHand hand);
        void OnUseHeld(PlayerHand hand);
        void OnUseReleased(PlayerHand hand);
        void OnUseCanceled();

        /// <summary>
        /// Предмет только что положен в руку. Здесь сбрасывается анимация:
        /// после применения вариант руки остаётся в кадре лечения, и без сброса
        /// новый предмет в руке выглядел бы замороженным на этом кадре.
        /// </summary>
        void OnEquipped(PlayerHand hand);
    }

    [DisallowMultipleComponent]
    public class WrenchItem : MonoBehaviour, IHandUsable
    {
        public enum MetalPreference
        {
            [InspectorName("Сначала металлолом")] ScrapFirst = 0,
            [InspectorName("Сначала металл")] MetalFirst = 1,
            [InspectorName("Сначала платина")] PlatinumFirst = 2,
            [InspectorName("Лучший из доступных")] Best = 3
        }

        [Header("Урон")]
        [Tooltip("Урон быстрого удара без зажатия")]
        [SerializeField] private float hitDamage = 16f;
        [Tooltip("Добавка урона при полностью заряженном ударе")]
        [SerializeField] private float chargedDamage = 44f;
        [Tooltip("Сколько секунд держать ЛКМ, чтобы зарядить усиленный удар, с")]
        [SerializeField] private float chargeTime = 0.9f;
        [Tooltip("С какого заряда удар считается усиленным и перестаёт чинить машину, 0..1")]
        [SerializeField] private float chargedThreshold = 0.5f;
        [Tooltip("Пауза между ударами, с")]
        [SerializeField] private float swingCooldown = 0.45f;

        [Header("Дальность удара")]
        [Tooltip("С какого расстояния от прицела начинается удар, м")]
        [SerializeField] private float hitStartOffset = 0.4f;
        [Tooltip("Длина луча удара от прицела, м")]
        [SerializeField] private float hitRange = 2.2f;
        [Tooltip("Сколько целей за удар. 0 — без ограничения")]
        [SerializeField] private int maxTargets = 3;
        [Tooltip("Слои, по которым бьёт ключ. Слой игрока лучше исключить")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Отбрасывание")]
        [Tooltip("Отталкивать ли предметы с Rigidbody")]
        [SerializeField] private bool knockback = true;
        [Tooltip("Сила отталкивания обычного удара")]
        [SerializeField] private float knockbackForce = 3.5f;
        [Tooltip("Множитель силы для усиленного удара")]
        [SerializeField] private float chargedKnockbackMultiplier = 2.2f;
        [Tooltip("Добавочная вертикальная составляющая отталкивания")]
        [SerializeField] private float knockbackLift = 0.25f;
        [Tooltip("Отталкивать ли саму машину. По умолчанию машина не отталкивается")]
        [SerializeField] private bool knockbackCar = false;
        [Tooltip("Максимальная скорость, до которой ключ разгоняет предмет, м/с")]
        [SerializeField] private float maxKnockbackSpeed = 12f;

        [Header("Ремонт")]
        [Tooltip("Ремонтировать машину одиночным ударом, тратя металл из багажника. Без металла машина не чинится")]
        [SerializeField] private bool repairCarFromTrunk = true;
        [Tooltip("Оставлено для совместимости. Сейчас за удар тратится один случайный метал из багажника, поэтому выбор не влияет на ремонт")]
        [SerializeField] private MetalPreference metalPreference = MetalPreference.ScrapFirst;

        [Header("Анимация")]
        [Tooltip("Аниматор модели ключа. Если пусто — ищется в варианте руки")]
        [SerializeField] private Animator animator;
        [Tooltip("Имя триггера удара в аниматоре")]
        [SerializeField] private string hitTrigger = "Hit";
        [Tooltip("Имя булева параметра «ключ зажат, идёт замах»")]
        [SerializeField] private string holdParameter = "Hold";
        [Tooltip("Имя булева параметра «ключ в покое»")]
        [SerializeField] private string idleParameter = "Idle";

        [Header("Звук")]
        [Tooltip("Звук попадания по объекту. Если пусто — генерируется")]
        [SerializeField] private AudioClip hitClip;
        [Tooltip("Звук усиленного удара по объекту. Если пусто — генерируется")]
        [SerializeField] private AudioClip chargedHitClip;
        [Tooltip("Звук удара по воздуху. Если пусто — генерируется")]
        [SerializeField] private AudioClip whiffClip;
        [Tooltip("Звук ремонта. Если пусто — генерируется")]
        [SerializeField] private AudioClip repairClip;
        [Tooltip("Громкость")]
        [SerializeField] private float volume = 0.85f;
        [Tooltip("Случайный разброс высоты звука, 0..1")]
        [SerializeField] private float pitchJitter = 0.12f;
        [Tooltip("Писать в консоль, что именно произошло при ударе. Удобно, пока настраиваешь баланс")]
        [SerializeField] private bool logSwings = true;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        private readonly System.Collections.Generic.List<int> hitRoots = new System.Collections.Generic.List<int>(8);

        private AudioSource audioSource;
        private Animator activeAnimator;
        private PlayerHand currentHand;
        private AudioClip generatedNormalClip;
        private AudioClip generatedHeavyClip;
        private AudioClip generatedWhiffClip;
        private float charge;
        private float nextSwingTime;
        private bool charging;
        private bool warnedAboutAnimator;
        private bool repairWarned;

        public float Charge => charge;
        public bool IsCharged => charge >= chargedThreshold;
        public bool IsCoolingDown => Time.time < nextSwingTime;
        public bool IsSwinging => charging;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }

        public void OnUsePressed(PlayerHand hand)
        {
            currentHand = hand;
            if (!CanUse(hand) || Time.time < nextSwingTime) return;

            charging = true;
            charge = 0f;
            ResolveAnimator(hand);
        }

        public void OnUseHeld(PlayerHand hand)
        {
            currentHand = hand;
            if (!charging || !CanUse(hand))
            {
                OnUseCanceled();
                return;
            }

            charge = Mathf.Clamp01(charge + Time.deltaTime / Mathf.Max(0.05f, chargeTime));
            SetHoldState(charge > 0.02f);
        }

        public void OnUseReleased(PlayerHand hand)
        {
            currentHand = hand;
            if (!charging) return;

            float swingCharge = charge;
            charging = false;
            charge = 0f;
            nextSwingTime = Time.time + swingCooldown;
            ResolveAnimator(hand);
            PlaySwingAnimation();

            if (CanUse(hand)) Swing(hand, swingCharge);
        }

        public void OnUseCanceled()
        {
            charging = false;
            charge = 0f;
            SetHoldState(false);
        }

        public void OnEquipped(PlayerHand hand)
        {
            charging = false;
            charge = 0f;
            SetHoldState(false);
        }

        private bool CanUse(PlayerHand hand)
        {
            if (hand == null) return false;
            if (!hand.IsHolding) return false;

            // HeldItem у инструмента может быть пустым: ключ игрок берёт прямо из
            // инвентаря, и объекта в мире у него нет. Достаточно того, что рука
            // занята и показывает нужный вариант.
            if (hand.HeldItem == null && hand.HeldFromInventory == null) return false;

            return true;
        }

        private void Swing(PlayerHand hand, float swingCharge)
        {
            bool charged = swingCharge >= chargedThreshold;
            float damage = Mathf.Lerp(hitDamage, hitDamage + chargedDamage, charged ? 1f : swingCharge);
            bool hitSomething = false;
            bool repairedSomething = false;
            bool carBodyHit = false;

            if (!TryGetAimRay(hand, out Ray ray))
            {
                PlayWhiff(swingCharge);
                return;
            }

            Vector3 origin = ray.GetPoint(hitStartOffset);
            int count = Physics.RaycastNonAlloc(origin, ray.direction, hitBuffer, hitRange, hitMask, QueryTriggerInteraction.Ignore);
            SortByDistance(count);

            int damaged = 0;
            int pushed = 0;
            hitRoots.Clear();

            // Ключ бьёт по тому, на что наведён прицел. Если игрок целится в
            // турель/щит — работаем с постройкой, даже если под тем же прицелом
            // оказался кузов: иначе урон уходил бы в машину, а постройка стояла
            // бы целой. Прочность постройки живёт на дочерней детали, поэтому
            // берём её прямо из цели прицела, а не первым попаданием луча.
            Health aimedStructure = ResolveAimedStructure(hand, origin, out Vector3 aimedPoint);
            if (aimedStructure != null)
            {
                bool hasMaterial = HasRepairMaterial(aimedStructure);
                bool didRepair = false;
                bool didDamage = false;

                if (!charged && hasMaterial && !aimedStructure.IsFull)
                {
                    didRepair = Repair(aimedStructure, false);
                    repairedSomething = didRepair;
                }
                else if (!aimedStructure.IsDead && (!hasMaterial || charged))
                {
                    aimedStructure.TakeDamage(new DamageInfo(damage, gameObject, aimedPoint, ray.direction));
                    didDamage = true;
                    damaged++;
                }

                if (!didRepair && !didDamage)
                {
                    PlayWhiff(swingCharge);
                    return;
                }

                if (logSwings)
                {
                    string aimedResult = didRepair
                        ? "ремонт постройки"
                        : $"удар по постройке, урон {damage:0.#}";
                    Debug.Log($"[Wrench] Удар (заряд {swingCharge:0.00}, усиленный: {charged}) — {aimedResult}", this);
                }

                if (!charged && didRepair) PlaySound(repairClip, 0.6f, false, 1f);
                else PlaySound(charged ? chargedHitClip : hitClip, charged ? 1f : 0.7f, charged, 1f);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Collider target = hitBuffer[i].collider;
                if (target == null) continue;
                if (target.GetComponentInParent<PlayerController>() != null) continue;

                // Прочность постройки лежит на дочерней «настоящей» детали, а
                // ключ бьёт по коллайдеру корня — достаём её через чертёж раньше
                // прочности родителя (у турели на машине родитель — кузов).
                Health health = Blueprint.ResolveHealth(target);
                bool repairable = health != null && (health.IsCar || health.IsStructure)
                    && health.GetComponentInParent<IDamageReceiver>() == null;
                // Ограничение «только главный коллайдер кузова» относится лишь к
                // машине. Постройка (турель/щит) на машине — её дочерний объект,
                // но бьётся она своей прочностью, а не кузовом: иначе ключ её не
                // видел бы вовсе.
                bool mainBodyHit = repairable && (!health.IsCar || IsMainBody(target));

                Rigidbody body = target.attachedRigidbody;
                GameObject root = body != null ? body.gameObject : target.transform.root.gameObject;
                int rootId = root.GetInstanceID();
                bool alreadyCounted = hitRoots.Contains(rootId);

                if (!mainBodyHit && alreadyCounted) continue;
                if (!alreadyCounted) hitRoots.Add(rootId);

                Vector3 point = hitBuffer[i].point;
                hitSomething = true;

                if (repairable)
                {
                    if (mainBodyHit)
                    {
                        if (health.IsCar)
                        {
                            carBodyHit = true;
                            if (Repair(health, charged)) repairedSomething = true;
                        }
                        else
                        {
                            // Постройка: с металлом в инвентаре чинится, без металла —
                            // ломается. Одного пустого удара достаточно, чтобы пошёл урон.
                            bool hasMaterial = HasRepairMaterial(health);
                            if (!charged && hasMaterial && !health.IsFull)
                            {
                                if (Repair(health, false)) repairedSomething = true;
                            }
                            else if (!health.IsDead && (!hasMaterial || charged))
                            {
                                health.TakeDamage(new DamageInfo(damage, gameObject, point, ray.direction));
                                damaged++;
                            }
                        }
                    }

                    if (knockbackCar && ApplyKnockback(body, origin, ray.direction, charged)) pushed++;
                    continue;
                }

                if (health == null)
                {
                    if (ApplyKnockback(body, origin, ray.direction, charged)) pushed++;
                    continue;
                }

                if (health.IsDead) continue;
                if (maxTargets > 0 && damaged >= maxTargets) continue;

                health.TakeDamage(new DamageInfo(damage, gameObject, point, ray.direction));
                damaged++;

                if (ApplyKnockback(body, origin, ray.direction, charged)) pushed++;
            }

            if (logSwings)
            {
                string result = !hitSomething
                    ? "промах"
                    : repairedSomething
                        ? "ремонт"
                        : carBodyHit
                            ? "удар по кузову машины, ремонт не сработал"
                            : $"попадание, урон {damage:0.#}, целей {damaged}";

                string push = pushed > 0 ? $", отброшено {pushed}" : string.Empty;
                Debug.Log($"[Wrench] Удар (заряд {swingCharge:0.00}, усиленный: {charged}) — {result}{push}", this);
            }

            if (carBodyHit && !repairedSomething && !charged) WarnAboutRepair(charged);

            if (!hitSomething)
            {
                PlayWhiff(swingCharge);
                return;
            }

            if (!charged && repairedSomething) PlaySound(repairClip, 0.6f, false, 1f);
            else PlaySound(charged ? chargedHitClip : hitClip, charged ? 1f : 0.7f, charged, 1f);
        }

        private void SortByDistance(int count)
        {
            for (int i = 1; i < count; i++)
            {
                RaycastHit current = hitBuffer[i];
                int j = i - 1;
                while (j >= 0 && hitBuffer[j].distance > current.distance)
                {
                    hitBuffer[j + 1] = hitBuffer[j];
                    j--;
                }

                hitBuffer[j + 1] = current;
            }
        }

        private bool ApplyKnockback(Rigidbody body, Vector3 origin, Vector3 rayDirection, bool charged)
        {
            if (!knockback) return false;
            if (body == null) return false;
            if (body.isKinematic) return false;

            CarController car = body.GetComponentInParent<CarController>();
            Health bodyHealth = body.GetComponentInParent<Health>();
            if ((car != null || (bodyHealth != null && bodyHealth.IsCar)) && !knockbackCar) return false;
            if (body.GetComponentInParent<PlayerController>() != null) return false;

            Vector3 push = rayDirection;
            if (push.sqrMagnitude < 0.001f) push = Vector3.forward;
            push = push.normalized + Vector3.up * knockbackLift;

            float force = knockbackForce * (charged ? chargedKnockbackMultiplier : 1f);
            body.AddForce(push.normalized * force, ForceMode.Impulse);

            Vector3 velocity = body.linearVelocity;
            if (velocity.sqrMagnitude > maxKnockbackSpeed * maxKnockbackSpeed)
                body.linearVelocity = velocity.normalized * maxKnockbackSpeed;

            return true;
        }

        /// <summary>
        /// Латает машину или постройку одиночным ударом. Машина берёт металл
        /// сначала из инвентаря, при нехватке — из багажника. Постройку чинят
        /// ТОЛЬКО металлом из инвентаря: металл багажника ей не подходит.
        /// </summary>
        private bool Repair(Health health, bool charged)
        {
            if (health == null) return false;
            if (charged) return false;
            if (health.IsFull) return false;

            bool inventoryOnly = health.IsStructure;
            if (!inventoryOnly && !repairCarFromTrunk) return false;

            float missing = health.Max - health.Current;

            Inventory inventory = ResolveInventory(health);
            float gained;

            if (health.FixOne)
            {
                // FixOne: годится только выбранный металл, остальные — как отсутствие.
                if (inventory == null || !inventory.TryConsumeMetal(missing, health.FixOneMetal, out gained) || gained <= 0f) return false;

                float applied = health.Heal(gained);
                Debug.Log($"[Wrench] Ремонт из инвентаря (FixOne): {MetalInfo.Name(health.FixOneMetal)} +{applied:0.#} хп, {health.Current:0.#}/{health.Max:0.#}", this);
                return true;
            }

            if (inventory != null && inventory.TryConsumeMetal(missing, out MetalType usedType, out gained) && gained > 0f)
            {
                float appliedFromBag = health.Heal(gained);
                Debug.Log($"[Wrench] Ремонт из инвентаря: {MetalInfo.Name(usedType)} +{appliedFromBag:0.#} хп, {health.Current:0.#}/{health.Max:0.#}", this);
                return true;
            }

            // Постройка из багажника не чинится — там металл не годится.
            if (inventoryOnly) return false;

            CargoZone cargo = ResolveCargo(health);
            if (cargo == null) return false;

            if (!cargo.TryConsumeMetal(missing, metalPreference, out usedType, out gained)) return false;
            if (gained <= 0f) return false;

            float appliedCargo = health.Heal(gained);

            Debug.Log($"[Wrench] Ремонт из багажника: {MetalInfo.Name(usedType)} +{appliedCargo:0.#} хп, {health.Current:0.#}/{health.Max:0.#}", this);
            return true;
        }

        /// <summary>
        /// Есть ли у игрока металл для ремонта постройки. При FixOne считается
        /// только выбранный металл. Нет металла — постройка не ремонтируется, а
        /// ломается.
        /// </summary>
        private bool HasRepairMaterial(Health health)
        {
            Inventory inventory = ResolveInventory(health);
            if (inventory == null) return false;

            if (health.FixOne)
            {
                return inventory.Count(ItemDefs.Resolve(health.FixOneMetal)) > 0;
            }

            return inventory.Count(ItemDefs.Resolve(MetalType.Scrap)) > 0
                || inventory.Count(ItemDefs.Resolve(MetalType.Metal)) > 0
                || inventory.Count(ItemDefs.Resolve(MetalType.Platinum)) > 0;
        }

        /// <summary>
        /// Ищет инвентарь игрока. Металл берётся из него, поэтому ремонт не
        /// зависит от того, лежит ли что-то в багажнике. Сначала берётся
        /// инвентарь владельца ключа (так постройки чинит именно игрок),
        /// а если ключ без руки — первый инвентарь в сцене.
        /// </summary>
        private Inventory ResolveInventory(Health health)
        {
            if (currentHand != null)
            {
                Inventory fromHand = currentHand.GetComponentInParent<Inventory>();
                if (fromHand != null) return fromHand;
            }

            if (health != null)
            {
                PlayerController player = health.transform.root.GetComponentInChildren<PlayerController>(true);
                if (player != null)
                {
                    Inventory found = player.GetComponent<Inventory>();
                    if (found == null) found = player.GetComponentInChildren<Inventory>(true);
                    if (found != null) return found;
                }
            }

            return FindFirstObjectByType<Inventory>();
        }

        private void WarnAboutRepair(bool charged)
        {
            if (repairWarned) return;
            repairWarned = true;

            Debug.LogWarning(
                "[Wrench] Удар по кузову машины не вызвал ремонт. Проверь: у машины на корне должен быть CargoZone (поле Cargo Zone), "
                + "в багажнике должен лежать предмет с компонентом MetalItem, а урон идёт незажатой ЛКМ. "
                + $"repairCarFromTrunk={repairCarFromTrunk}, заряд={charged}", this);
        }

        private static CargoZone ResolveCargo(Health health)
        {
            if (health == null) return null;

            Transform root = health.transform.root;

            CarController car = health.GetComponentInParent<CarController>();
            if (car == null) car = root.GetComponentInChildren<CarController>(true);
            if (car != null && car.Cargo != null) return car.Cargo;

            CargoZone direct = health.GetComponentInParent<CargoZone>();
            if (direct != null) return direct;

            direct = health.GetComponent<CargoZone>();
            if (direct != null) return direct;

            return root.GetComponentInChildren<CargoZone>(true);
        }

        /// <summary>
        /// Постройка (турель/щит), на которую наведён прицел игрока, если она в
        /// пределах удара. Наведение решает, с чем ключ работает: постройка на
        /// машине важнее кузова под ней. point — точка удара из прицела.
        /// </summary>
        private Health ResolveAimedStructure(PlayerHand hand, Vector3 origin, out Vector3 point)
        {
            point = Vector3.zero;

            PlayerInteractor interactor = hand != null ? hand.Interactor : null;
            if (interactor == null) return null;

            Health aimed = interactor.AimedHealth;
            if (aimed == null || aimed.IsDead || !aimed.IsStructure) return null;

            point = interactor.AimedPoint != Vector3.zero ? interactor.AimedPoint : aimed.transform.position;
            return Vector3.Distance(origin, point) <= hitRange ? aimed : null;
        }

        private bool IsMainBody(Collider target)
        {
            CarController car = target.GetComponentInParent<CarController>();
            if (car == null) car = target.transform.root.GetComponentInChildren<CarController>(true);
            if (car == null) return true;

            Collider[] bodies = car.BodyColliders;
            if (bodies == null || bodies.Length == 0) return true;

            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == target) return true;
            }

            return false;
        }

        private bool TryGetAimRay(PlayerHand hand, out Ray ray)
        {
            if (hand != null && hand.TryGetAimRay(out ray)) return true;

            Camera camera = Camera.main;
            if (camera == null)
            {
                ray = default;
                return false;
            }

            ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return true;
        }

        private void ResolveAnimator(PlayerHand hand)
        {
            if (activeAnimator != null) return;

            if (hand != null && hand.ActiveHandVariant != null)
            {
                activeAnimator = hand.ActiveHandVariant.GetComponentInChildren<Animator>();
                if (activeAnimator != null) return;
            }

            if (animator != null)
            {
                activeAnimator = animator;
                return;
            }

            if (warnedAboutAnimator) return;

            warnedAboutAnimator = true;
            PickupItem pickup = GetComponent<PickupItem>();
            string id = pickup != null ? pickup.ItemId : string.Empty;
            Debug.LogWarning($"[Wrench] У ключа '{name}' не найден аниматор. Добавь Animator в вариант руки с ID '{id}'", this);
        }

        private void SetHoldState(bool holding)
        {
            if (activeAnimator == null) return;

            if (!string.IsNullOrEmpty(holdParameter)) activeAnimator.SetBool(holdParameter, holding);
            if (!string.IsNullOrEmpty(idleParameter)) activeAnimator.SetBool(idleParameter, !holding);
        }

        private void PlaySwingAnimation()
        {
            if (activeAnimator == null) return;

            if (!string.IsNullOrEmpty(hitTrigger)) activeAnimator.SetTrigger(hitTrigger);
            SetHoldState(false);
        }

        private void PlayWhiff(float swingCharge)
        {
            AudioClip clip = whiffClip != null ? whiffClip : GetGeneratedWhiffClip();
            PlaySound(clip, 0.5f, false, 1f + swingCharge * 0.2f);
        }

        private void PlaySound(AudioClip clip, float gain, bool heavy, float pitchScale)
        {
            if (clip == null) clip = GetGeneratedClip(heavy);
            if (clip == null) return;

            AudioSource source = ResolveAudioSource();
            if (source == null) return;

            source.pitch = pitchScale * (1f + Random.Range(-pitchJitter, pitchJitter));
            source.PlayOneShot(clip, volume * gain);
        }

        private AudioSource ResolveAudioSource()
        {
            if (currentHand != null && currentHand.SharedAudioSource != null) return currentHand.SharedAudioSource;
            if (audioSource != null && gameObject.activeInHierarchy) return audioSource;
            return audioSource;
        }

        private AudioClip GetGeneratedClip(bool heavy)
        {
            if (!heavy)
            {
                if (generatedNormalClip == null) generatedNormalClip = ProceduralAudio.Impact(false);
                return generatedNormalClip;
            }

            if (generatedHeavyClip == null) generatedHeavyClip = ProceduralAudio.Impact(true);
            return generatedHeavyClip;
        }

        private AudioClip GetGeneratedWhiffClip()
        {
            if (generatedWhiffClip == null) generatedWhiffClip = ProceduralAudio.Whiff();
            return generatedWhiffClip;
        }
    }
}
