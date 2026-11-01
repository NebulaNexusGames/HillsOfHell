using System.Collections;
using System.Collections.Generic;
using CarGame.Combat;
using CarGame.Player;
using CarGame.Vehicle;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Крафт-станция — огромный пресс. У станции есть отдельный датчик (sensorVolume)
    /// — постоянно включённый триггер, который непрерывно считывает всё, что лежит
    /// внутри: и предметы, и их количество. В крафт попадают только предметы,
    /// которые физически находятся в этом объёме. Скрипт на каждом кадре сверяет
    /// набор и количество предметов с рецептами, и если комбинация не подходит ни
    /// под один крафт — нажатие E ничего не делает.
    ///
    /// Когда рецепт найден, поршень бьёт по предметам: ингредиенты исчезают, а на
    /// их месте появляется результат крафта из префаба.
    ///
    /// Предметы не уничтожаются: нечьи ингредиенты помечаются израсходованными
    /// через PickupItem.MarkConsumed и просто выключаются, как и везде в проекте.
    /// </summary>
    [DisallowMultipleComponent]
    public class CraftingStation : MonoBehaviour, IInteractable, ICanInteract
    {
        /// <summary>
        /// Одна ячейка рецепта: набор ингредиентов (массив внутри массива) и
        /// префаб результата, который появляется вместо них. Порядок ингредиентов
        /// не важен, лишние предметы под прессом не мешают. Рецептов у станции
        /// может быть несколько — сработает тот, чей набор целиком лежит под прессом.
        /// </summary>
        [System.Serializable]
        public sealed class CraftRecipe
        {
            [Tooltip("Ингредиенты рецепта: предмет и сколько штук нужно. Один и тот же предмет можно указать один раз с нужным количеством.")]
            public CraftIngredient[] ingredients = new CraftIngredient[0];

            [Tooltip("Префаб предмета — результат крафта этого рецепта")]
            public GameObject resultPrefab;

            [Tooltip("Сколько единиц результата появляется за один удар")]
            [Min(1)] public int resultAmount = 1;
        }

        [System.Serializable]
        public class CraftIngredient
        {
            [Tooltip("ID предмета-ингредиента, который должен лежать под триггером пресса")]
            public string itemId = "";

            [Tooltip("Сколько единиц этого ингредиента нужно за один удар")]
            [Min(1)] public int amount = 1;
        }

        /// <summary>
        /// Прицел стоит на триггере пресса — тогда E запускает пресс, а не стреляет.
        /// Синхронизируется в LateUpdate по PlayerInteractor.Target, читается в
        /// PlayerFirearm.Update.
        /// </summary>
        public static bool AimingPress { get; private set; }

        [Header("Анимация пресса")]
        [Tooltip("Аниматор пресса. Пусто — найдётся на этом объекте или в детях")]
        [SerializeField] private Animator animator;

        [Tooltip("Имя параметра-триггера, запускающего удар пресса")]
        [SerializeField] private string pressTrigger = "Press";

        [Tooltip("Имя параметра-триггера возврата пресса в покой")]
        [SerializeField] private string idleTrigger = "Idle";

        [Tooltip("Имя состояния покоя, в которое пресс возвращается после удара")]
        [SerializeField] private string idleState = "Idle";

        [Header("Триггеры")]
        [Tooltip("Триггер, наведясь на который и нажав E игрок активирует пресс")]
        [SerializeField] private Collider interactTrigger;

        [Tooltip("Датчик предметов: постоянно включённый триггер. На каждом кадре считывает всё, что лежит внутри — и вид предмета, и количество. В крафт попадут только предметы из этого объёма")]
        [SerializeField] private Collider sensorVolume;

        [Tooltip("Особый триггер пресса: включается на время удара и опускается к предметам")]
        [SerializeField] private Collider pressTriggerVolume;

        [Tooltip("Слои, среди которых ищутся предметы в датчике")]
        [SerializeField] private LayerMask itemMask = ~0;

        [Header("Рецепты")]
        [Tooltip("Ячейки рецептов: в каждой — набор ингредиентов и свой префаб результата. Рецептов может быть несколько")]
        [SerializeField] private CraftRecipe[] recipes = new CraftRecipe[0];

        [Tooltip("Смещение результата от центра датчика, если не задана точка спавна")]
        [SerializeField] private Vector3 resultOffset = new Vector3(0f, 0.2f, 0f);

        [Tooltip("Фиксированная точка, где появляется результат крафта. Пусто — центр датчика + resultOffset")]
        [SerializeField] private Transform spawnPoint;

        [Tooltip("Сколько секунд у результата крафта вообще нет физики после появления")]
        [SerializeField] private float spawnPhysicsFreeze = 0.5f;

        [Header("Урон поршня")]
        [Tooltip("Урон игроку, попавшему в объём удара (pressTriggerVolume) во время работы пресса")]
        [SerializeField] private float pressDamage = 500f;

        [Header("Тайминг удара")]
        [Tooltip("Сколько секунд ждать, пока поршень опустится и триггер коснётся предметов")]
        [SerializeField] private float pressDelay = 0.5f;

        [Tooltip("Сколько секунд ждать до возврата пресса в покой после крафта")]
        [SerializeField] private float pressRecovery = 1.0f;

        [Header("Дистанция")]
        [Tooltip("С какой дистанции можно нажать на пресс, м")]
        [SerializeField] private float range = 3f;

        private readonly List<PickupItem> contents = new List<PickupItem>();
        private readonly Collider[] overlapBuffer = new Collider[64];
        private PlayerInteractor interactor;
        private bool pressing;

        /// <summary>Есть ли сейчас в датчике набор предметов, подходящий под один из рецептов.</summary>
        public bool HasMatch { get; private set; }

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // Датчик предметов читает содержимое постоянно, поэтому всегда включён.
            if (sensorVolume != null) sensorVolume.enabled = true;

            // Особый триггер пресса выключен, пока пресс не запущен: он должен
            // срабатывать только во время удара, когда опустится на предметы.
            if (pressTriggerVolume != null) pressTriggerVolume.enabled = false;
        }

        private void OnEnable()
        {
            AimingPress = false;
            RequestIdleState();
        }

        private void OnDisable()
        {
            AimingPress = false;
        }

        private void Update()
        {
            if (interactor == null) interactor = FindFirstObjectByType<PlayerInteractor>();

            // Датчик читает содержимое каждый кадр: предметы можно класть и
            // убирать в любой момент, и HasMatch всегда отражает текущий набор.
            ReadSensor();

            // Пока пресс работает, его объём удара (pressTriggerVolume) опасен:
            // игрок, оказавшийся внутри, погибает. Триггер датчика предметов и
            // точка активации E тут ни при чём — бьёт именно объём удара.
            if (pressing) ApplyPressDamage();
        }

        /// <summary>
        /// Считывает всё, что лежит в датчике: собирает предметы, считает их по
        /// видам и сразу проверяет, подходит ли набор под какой-нибудь рецепт.
        /// Предмет вне датчика в крафте не участвует.
        /// </summary>
        public void ReadSensor()
        {
            contents.Clear();

            if (sensorVolume != null)
            {
                Bounds bounds = sensorVolume.bounds;
                int count = Physics.OverlapBoxNonAlloc(
                    bounds.center, bounds.extents, overlapBuffer, sensorVolume.transform.rotation,
                    itemMask, QueryTriggerInteraction.Collide);

                for (int i = 0; i < count; i++)
                {
                    PickupItem item = overlapBuffer[i] != null ? overlapBuffer[i].GetComponentInParent<PickupItem>() : null;
                    if (item == null) continue;
                    if (item.IsHeld || item.IsConsumed || item.IsStored) continue;
                    if (contents.Contains(item)) continue;

                    contents.Add(item);
                }
            }

            // Пустой датчик не может ничего скрафтить — не гоняем рецепты зря.
            if (contents.Count == 0 || recipes == null || recipes.Length == 0)
            {
                HasMatch = false;
                return;
            }

            List<PickupItem> unused;
            CraftRecipe match;
            HasMatch = TryMatchRecipe(out match, out unused);
        }

        /// <summary>
        /// Флаг для PlayerFirearm обновляется после всех Update: к этому моменту
        /// PlayerInteractor уже выбрал Target, поэтому флаг всегда свежий.
        /// </summary>
        private void LateUpdate()
        {
            AimingPress = IsAimedAndReady();
        }

        private bool IsAimedAndReady()
        {
            if (interactor == null) return false;
            if (!ReferenceEquals(interactor.Target, this)) return false;
            return CanInteract(interactor);
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null || pressing) return false;
            if (!HitsInteractTrigger(interactor)) return false;
            return InRange(interactor);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;

            // Пресс активируется по кнопке вне зависимости от наличия предметов под ним.
            ReadSensor();
            Press();
        }

        /// <summary>
        /// E срабатывает только по кнопке активации пресса (interactTrigger).
        /// Корпус, поршень и объём удара пресс НЕ активируют, а датчик подсчёта
        /// предметов (SensorVolume) — служебный: считать предметы и запускать
        /// пресс — это разные вещи.
        /// </summary>
        private bool HitsInteractTrigger(PlayerInteractor interactor)
        {
            if (interactTrigger == null) return false;

            Collider hit = interactor.TargetCollider;
            if (hit == null) return false;

            return hit == interactTrigger || hit.transform.IsChildOf(interactTrigger.transform);
        }

        private bool InRange(PlayerInteractor interactor)
        {
            Camera camera = interactor != null ? interactor.AimCamera : null;
            if (camera == null) return false;

            // Мерим до того, во что попал луч, а не до корня станции: у большого
            // пресса пивот бывает далеко от кнопки (здесь корень на ~10 м ниже
            // кнопки), и дистанция до корня не проходила бы никогда.
            Vector3 origin = camera.transform.position;
            Collider hit = interactor.TargetCollider;
            Vector3 point = hit != null ? hit.ClosestPoint(origin) : transform.position;

            return Vector3.Distance(origin, point) <= Mathf.Max(0.1f, range);
        }

        /// <summary>
        /// Запускает удар: проигрывает анимацию, включает триггер пресса и ждёт,
        /// пока тот опустится на предметы, после чего крафтит и уводит пресс в покой.
        /// Вызывается только когда датчик нашёл подходящий рецепт.
        /// </summary>
        private void Press()
        {
            pressing = true;

            if (pressTriggerVolume != null) pressTriggerVolume.enabled = true;

            PlayPressAnimation();
            StartCoroutine(PressRoutine());
        }

        private IEnumerator PressRoutine()
        {
            yield return new WaitForSeconds(Mathf.Max(0.01f, pressDelay));

            // Пока пресс опускался, набор мог измениться — перечитываем датчик
            // и крафтим только то, что лежит в нём сейчас.
            ReadSensor();
            CraftMatched();

            yield return new WaitForSeconds(Mathf.Max(0f, pressRecovery));

            if (pressTriggerVolume != null) pressTriggerVolume.enabled = false;
            RequestIdleState();
            pressing = false;
        }

        /// <summary>
        /// Бьёт игрока, оказавшегося в объёме удара, пока пресс работает.
        /// Урон гарантированно смертельный: пресс не оставляет полуживых.
        /// </summary>
        private void ApplyPressDamage()
        {
            if (pressTriggerVolume == null || !pressTriggerVolume.enabled) return;

            Bounds bounds = pressTriggerVolume.bounds;
            int count = Physics.OverlapBoxNonAlloc(
                bounds.center, bounds.extents, overlapBuffer, pressTriggerVolume.transform.rotation,
                ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider hit = overlapBuffer[i];
                if (hit == null) continue;

                // Канистра бензина под прессом взрывается
                PickupItem item = hit.GetComponentInParent<PickupItem>();
                if (item != null)
                {
                    string cid = ItemDefs.Canonical(item.ItemId);
                    if (cid == ItemDefs.GazId || cid == "gascan" || cid == "gas_can")
                    {
                        // Попытаемся взорвать через GasCanItem
                        GasCanItem can = hit.GetComponentInParent<GasCanItem>();
                        if (can != null)
                        {
                            can.SendMessage("Explode", SendMessageOptions.DontRequireReceiver);
                        }
                        else
                        {
                            // Резервный вариант - просто помечаем как уничтоженную и взорвём
                            item.MarkConsumed();
                        }
                    }
                }

                Health health = hit.GetComponentInParent<Health>();
                if (health == null || !health.IsPlayer || health.IsDead) continue;

                Vector3 origin = hit.ClosestPoint(bounds.center);
                Vector3 direction = (hit.bounds.center - bounds.center).normalized;
                health.TakeDamage(new DamageInfo(Mathf.Max(pressDamage, health.Max), gameObject, origin, direction));
            }
        }

        /// <summary>
        /// Ищет рецепт, ингредиенты которого целиком лежат в датчике, убирает
        /// ровно эти предметы и ставит на их место результат крафта.
        /// </summary>
        private void CraftMatched()
        {
            if (contents.Count == 0) return;

            List<PickupItem> used;
            CraftRecipe recipe;
            if (!TryMatchRecipe(out recipe, out used)) return;

            for (int i = 0; i < used.Count; i++)
            {
                if (used[i] != null) used[i].MarkConsumed();
            }

            SpawnResult(recipe);
        }

        private bool TryMatchRecipe(out CraftRecipe matched, out List<PickupItem> used)
        {
            matched = null;
            used = null;
            if (recipes == null) return false;

            int bestConsumed = -1;
            for (int r = 0; r < recipes.Length; r++)
            {
                // Рецепт без результата бессмыслен: ингредиенты потратились бы
                // впустую. Такую ячейку пропускаем.
                if (recipes[r] == null || recipes[r].resultPrefab == null) continue;

                List<PickupItem> taken;
                if (!TryMatch(recipes[r], out taken)) continue;

                // Выбираем самый «полный» подходящий рецепт — тот, что расходует
                // больше предметов. Иначе при 3 металлах первым срабатывал бы
                // рецепт на 2 (пистолет) и оставлял лишний металл, а не дробовик.
                if (taken.Count > bestConsumed)
                {
                    bestConsumed = taken.Count;
                    matched = recipes[r];
                    used = taken;
                }
            }

            return matched != null;
        }

        /// <summary>
        /// Проверяет один рецепт: каждому ингредиенту находится нужное количество
        /// свободных предметов в датчике. Лишние предметы не мешают и не расходуются.
        /// </summary>
        private bool TryMatch(CraftRecipe recipe, out List<PickupItem> used)
        {
            used = null;
            if (recipe == null || recipe.ingredients == null || recipe.ingredients.Length == 0) return false;

            List<PickupItem> pool = new List<PickupItem>(contents);
            List<PickupItem> taken = new List<PickupItem>();

            for (int i = 0; i < recipe.ingredients.Length; i++)
            {
                CraftIngredient ingredient = recipe.ingredients[i];
                if (ingredient == null || string.IsNullOrEmpty(ingredient.itemId)) return false;

                string want = ItemDefs.Canonical(ingredient.itemId);
                int need = Mathf.Max(1, ingredient.amount);

                for (int n = 0; n < need; n++)
                {
                    int found = -1;

                    for (int j = 0; j < pool.Count; j++)
                    {
                        if (pool[j] == null) continue;
                        if (ItemDefs.Canonical(pool[j].ItemId) != want) continue;
                        found = j;
                        break;
                    }

                    if (found < 0) return false;
                    taken.Add(pool[found]);
                    pool.RemoveAt(found);
                }
            }

            used = taken;
            return true;
        }

        /// <summary>Ставит результат рецепта в точку появления — предмет не уничтожается, а создаётся.</summary>
        private void SpawnResult(CraftRecipe recipe)
        {
            if (recipe == null || recipe.resultPrefab == null) return;

            Vector3 position;
            Quaternion rotation;

            if (spawnPoint != null)
            {
                // Результат появляется строго в заданной точке и с её поворотом.
                position = spawnPoint.position;
                rotation = spawnPoint.rotation;
            }
            else
            {
                Vector3 center = sensorVolume != null ? sensorVolume.bounds.center :
                    (pressTriggerVolume != null ? pressTriggerVolume.bounds.center : transform.position);
                position = center + resultOffset;
                rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            }

            GameObject spawned = Instantiate(recipe.resultPrefab, position, rotation);
            spawned.SetActive(true);

            PickupItem pickup = spawned.GetComponentInChildren<PickupItem>();
            if (pickup != null && recipe.resultAmount > 1) pickup.SetStackAmount(recipe.resultAmount);

            // Первые spawnPhysicsFreeze секунд у результата нет физики вообще: пока
            // предмет не «устоится», отключаем физику и коллайдеры, чтобы его не
            // отбрасывало при появлении. Потом всё включается обратно как было.
            StartCoroutine(FreezePhysicsRoutine(spawned, Mathf.Max(0f, spawnPhysicsFreeze)));
        }

        private IEnumerator FreezePhysicsRoutine(GameObject spawned, float duration)
        {
            if (spawned == null) yield break;

            Rigidbody[] bodies = spawned.GetComponentsInChildren<Rigidbody>(true);
            Collider[] colliders = spawned.GetComponentsInChildren<Collider>(true);

            bool[] bodyKinematic = new bool[bodies.Length];
            bool[] bodyGravity = new bool[bodies.Length];
            bool[] bodyCollisions = new bool[bodies.Length];
            bool[] colliderEnabled = new bool[colliders.Length];

            // Физика выключается сразу, ещё до следующего шага симуляции.
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == null) continue;
                bodyKinematic[i] = bodies[i].isKinematic;
                bodyGravity[i] = bodies[i].useGravity;
                bodyCollisions[i] = bodies[i].detectCollisions;
                bodies[i].isKinematic = true;
                bodies[i].useGravity = false;
                bodies[i].detectCollisions = false;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;
                colliderEnabled[i] = colliders[i].enabled;
                colliders[i].enabled = false;
            }

            if (duration > 0f) yield return new WaitForSeconds(duration);

            if (spawned == null) yield break;

            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == null) continue;
                bodies[i].isKinematic = bodyKinematic[i];
                bodies[i].useGravity = bodyGravity[i];
                bodies[i].detectCollisions = bodyCollisions[i];
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;
                colliders[i].enabled = colliderEnabled[i];
            }
        }

        private void PlayPressAnimation()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null) return;

            if (!string.IsNullOrEmpty(pressTrigger) && HasParameter(pressTrigger))
            {
                ClearAllTriggers();
                animator.SetTrigger(pressTrigger);
            }
        }

        private void RequestIdleState()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null) return;

            if (!string.IsNullOrEmpty(idleTrigger) && HasParameter(idleTrigger))
            {
                ClearAllTriggers();
                animator.SetTrigger(idleTrigger);
            }

            int hash = ResolveStateHash(idleState);
            if (hash != 0) animator.Play(hash, 0, 0f);
        }

        private void ClearAllTriggers()
        {
            if (animator == null) return;

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].type == AnimatorControllerParameterType.Trigger)
                    animator.ResetTrigger(parameters[i].name);
            }
        }

        private bool HasParameter(string name)
        {
            if (animator == null) return false;

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == name) return true;
            }

            return false;
        }

        private int ResolveStateHash(string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName)) return 0;

            string layerName = animator.GetLayerName(0);
            if (!string.IsNullOrEmpty(layerName))
            {
                int layered = Animator.StringToHash(layerName + "." + stateName);
                if (animator.HasState(0, layered)) return layered;
            }

            int hash = Animator.StringToHash(stateName);
            return animator.HasState(0, hash) ? hash : 0;
        }
    }
}