using System;
using System.Collections.Generic;
using CarGame.Player;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CarGame.Items
{
    /// <summary>
    /// Каталог альт-версий предметов — тех самых объектов, которые лежат в мире
    /// и поднимаются игроком. Все они собраны в папке Assets/Prefabs/Items и
    /// отличаются от вариантов руки ровно тем, чего у варианта руки нет: у них
    /// есть коллайдер и физика, и их можно поднять. Функционала (анимации,
    /// использования, стрельбы) у них нет — это просто вещь на полу.
    ///
    /// Смысл разделения такой. Вариант руки — это картинка: его включают и
    /// выключают, он никогда не существует в мире. Альт-версия — это предмет:
    /// он лежит, падает и поднимается. Раньше при выбросе из руки и из инвентаря
    /// создавалась обрезанная копия варианта руки с выключенными компонентами, и
    /// такой «предмет» нельзя было ни увидеть, ни поднять. Теперь для выброса
    /// берётся готовый префаб отсюда.
    ///
    /// Ссылка на префаб задаётся в инспекторе: поле prefab и есть тот самый
    /// файл из Assets/Prefabs/Items. Никакого Resources.Load и поиска по пути —
    /// префабы лежат вне Resources, а жёстко зашитый путь сломался бы при
    /// переименовании.
    /// </summary>
    [DisallowMultipleComponent]
    public class ItemWorldCatalog : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("ID предмета из ItemDefs. Можно писать и старенький номер из префаба: '1' — ключ")]
            public string itemId = string.Empty;

            [Tooltip("Префаб из Assets/Prefabs/Items, который появится в мире при выбросе")]
            public GameObject prefab;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private readonly Dictionary<string, GameObject> byId = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        private static ItemWorldCatalog instance;

        public static ItemWorldCatalog Instance
        {
            get
            {
                if (instance == null) instance = FindFirstObjectByType<ItemWorldCatalog>();
                return instance;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            byId.Clear();
        }

        private void Rebuild()
        {
            byId.Clear();
            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    Entry entry = entries[i];
                    if (entry == null || entry.prefab == null) continue;

                    string key = ItemDefs.Canonical(entry.itemId);
                    if (string.IsNullOrEmpty(key)) continue;

                    byId[key] = entry.prefab;
                }
            }

            // Автодобавление процессоров
            TryAutoAdd(ItemDefs.CpuT1Id, "CPUT1");
            TryAutoAdd(ItemDefs.CpuT2Id, "CPUT2");
            TryAutoAdd(ItemDefs.CpuT3Id, "CPUT3");
            // Автодобавление кубов для спавна мобов
            TryAutoAdd(ItemDefs.SwordBotCubeId, "SwordBotCube");
            TryAutoAdd(ItemDefs.DinaBotCubeId, "DinaBotCube");
            TryAutoAdd(ItemDefs.DronCubeId, "DronCube");
            // Автодобавление новых объектов: терминалы, машина, крафтовая станция
            TryAutoAdd(ItemDefs.ShopTerminalId, "ShopTerminal");
            TryAutoAdd(ItemDefs.ExchangerTerminalId, "ExchangerTerminal");
            TryAutoAdd(ItemDefs.SlotMachineTerminalId, "SlotMachineTerminal");
            TryAutoAdd(ItemDefs.CarId, "Car");
            TryAutoAdd(ItemDefs.CraftingStationId, "CraftingStation");
        }

        private void TryAutoAdd(string itemId, string prefabName)
        {
            string key = ItemDefs.Canonical(itemId);
            if (string.IsNullOrEmpty(key)) return;
            if (byId.ContainsKey(key)) return;

            // Ищем префаб среди уже загруженных
            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    Entry entry = entries[i];
                    if (entry == null || entry.prefab == null) continue;
                    if (entry.prefab.name == prefabName || entry.prefab.name.StartsWith(prefabName))
                    {
                        byId[key] = entry.prefab;
                        return;
                    }
                }
            }

            // Попробуем найти среди всех префабов в проекте через AssetDatabase (только в редакторе)
#if UNITY_EDITOR
            string[] guids = AssetDatabase.FindAssets(prefabName);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.EndsWith(".prefab")) continue;
                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (fileName != prefabName && !fileName.StartsWith(prefabName)) continue;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    byId[key] = prefab;
                    return;
                }
            }
#endif

            // Пытаемся найти по Resources если вдруг есть
            GameObject res = Resources.Load<GameObject>("Items/" + prefabName);
            if (res != null)
            {
                byId[key] = res;
                return;
            }
            res = Resources.Load<GameObject>(prefabName);
            if (res != null)
            {
                byId[key] = res;
                return;
            }
        }

        /// <summary>Префаб альт-версии для предмета. null — если для него префаба нет.</summary>
        public GameObject Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (byId.Count == 0) Rebuild();

            GameObject prefab;
            if (byId.TryGetValue(itemId, out prefab)) return prefab;

            string canonical = ItemDefs.Canonical(itemId);
            if (!string.IsNullOrEmpty(canonical) && byId.TryGetValue(canonical, out prefab)) return prefab;

            return null;
        }

        public bool Has(ItemDef def)
        {
            return def != null && Find(def.Id) != null;
        }

        /// <summary>
        /// Достраивает вариант руки, которому в сцене не хватает модели. У части
        /// предметов в сцене лежит пустой GameObject: Animator и скрипт на месте,
        /// а рисовать нечем — сама модель есть только внутри префаба альт-версии.
        /// Сцена не умеет ссылаться на меши префаба, поэтому модель переносится
        /// сюда один раз, при первом показе предмета.
        ///
        /// Переносятся именно дети с Renderer, вместе с именами: анимация
        /// инджектора бьёт по пути "Cube.002", и после переноса путь должен
        /// остаться прежним. Префаб-клон остаётся жить под вариантом и выключен —
        /// так же, как и сам вариант, его никогда не уничтожают.
        /// </summary>
        public bool BuildHandVisual(ItemDef def, GameObject handVariant, float targetSize)
        {
            if (def == null || handVariant == null) return false;

            GameObject prefab = Find(def.Id);
            if (prefab == null) return false;

            Transform root = handVariant.transform;
            GameObject source = Instantiate(prefab, root);
            source.name = "AltVersion_" + def.DisplayName;
            source.SetActive(false);
            SetLayerRecursive(source, handVariant.layer);
            MakeVisualOnly(source);

            Transform sourceTransform = source.transform;
            sourceTransform.localPosition = Vector3.zero;
            sourceTransform.localRotation = Quaternion.identity;
            sourceTransform.localScale = Vector3.one;

            List<Transform> moving = new List<Transform>();
            for (int i = 0; i < sourceTransform.childCount; i++)
            {
                Transform child = sourceTransform.GetChild(i);
                if (child.GetComponentInChildren<Renderer>(true) != null) moving.Add(child);
            }

            if (moving.Count == 0) return false;

            for (int i = 0; i < moving.Count; i++)
            {
                Transform child = moving[i];
                child.SetParent(root, true);
                child.gameObject.SetActive(true);
            }

            // Размер префаба задан в мировых единицах модели, а в руке предмет
            // должен быть размером с ладонь: гасим разницу масштабом корня и
            // сдвигаем картинку в начало координат, чтобы она стояла в кулаке.
            Bounds bounds = GetBounds(root);
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (targetSize > 0.001f && longest > 0.0001f)
            {
                root.localScale = root.localScale * (targetSize / longest);
                bounds = GetBounds(root);
            }

            Vector3 shift = root.position - bounds.center;
            for (int i = 0; i < moving.Count; i++) moving[i].position += shift;
            return true;
        }

        private static Bounds GetBounds(Transform target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds(target.position, Vector3.zero);
            if (renderers.Length == 0) return bounds;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>
        /// Модель в руке — это только картинка: ни физики, ни коллайдеров на ней
        /// быть не должно, иначе предмет в кулаке начинает ловить лучи.
        /// </summary>
        private static void MakeVisualOnly(GameObject go)
        {
            Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = false;
            }

            Rigidbody[] bodies = go.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == null) continue;
                bodies[i].isKinematic = true;
                bodies[i].useGravity = false;
                bodies[i].detectCollisions = false;
            }
        }

        /// <summary>
        /// Ставит в мир альт-версию предмета: с коллайдером, физикой и PickupItem,
        /// чтобы её можно было поднять. payload переживает выброс — так выброшенный
        /// ствол помнит, сколько в нём было патронов.
        ///
        /// Префабы для стволов лежат на слое PlayerArms, который рисует только
        /// камера рук, поэтому слой меняется на обычный: иначе предмет на полу
        /// не было бы видно.
        /// </summary>
        public GameObject Spawn(ItemDef def, Vector3 position, Quaternion rotation, int amount, int payload, Vector3 velocity, PlayerController thrower = null)
        {
            GameObject prefab = Find(def != null ? def.Id : null);
            if (prefab == null) return null;

            GameObject spawned = Instantiate(prefab, position, rotation);
            spawned.name = "Dropped " + (def != null ? def.DisplayName : prefab.name);

            // Префаб может лежать в проекте выключенным (у ShotGun.prefab корень —
            // объект ShotEffect с m_IsActive 0). Instantiate наследует это состояние,
            // и бросок давал невидимый предмет без физики: он не падал, не
            // попадал под луч и его нельзя было поднять.
            if (!spawned.activeSelf) spawned.SetActive(true);

            SetLayerRecursive(spawned, WorldLayer);
            StripFunctionality(spawned);

            Rigidbody body = spawned.GetComponent<Rigidbody>();
            if (body == null) body = spawned.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;

            if (spawned.GetComponentInChildren<Collider>(true) == null) AddColliderFromBounds(spawned);

            PickupItem pickup = spawned.GetComponent<PickupItem>();
            if (pickup == null) pickup = spawned.AddComponent<PickupItem>();

            pickup.SetItemId(def != null ? def.Id : string.Empty);
            pickup.SetStackAmount(Mathf.Max(1, amount));
            pickup.SetPayload(payload);

            // Кто бросил: предмет не должен ударить его же сразу после вылета.
            ThrownItemDamage impact = spawned.GetComponent<ThrownItemDamage>();
            if (impact != null) impact.SetThrower(thrower);

            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
            return spawned;
        }

        private const int WorldLayer = 0;

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
            }
        }

        /// <summary>
        /// У альт-версии не должно быть ничего, кроме collider и физики. Скрипты
        /// поведения и анимацию выключаем, а PickupItem оставляем: без него
        /// предмет не поднять. Rigidbody и коллайдеры — не Behaviour, поэтому
        /// этим циклом они не трогаются.
        /// </summary>
        private static void StripFunctionality(GameObject spawned)
        {
            Behaviour[] behaviours = spawned.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null) continue;
                if (behaviour is PickupItem) continue;
                if (behaviour is CarGame.Combat.Health) continue;
                if (behaviour is CarGame.Items.GasCanItem) continue;
                if (behaviour is SpawnCubeItem) continue;
                if (behaviour is SpawnCubeProjectile) continue;
                behaviour.enabled = false;
            }

            Collider[] colliders = spawned.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = true;
            }
        }

        private static void AddColliderFromBounds(GameObject target)
        {
            BoxCollider box = target.AddComponent<BoxCollider>();
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            Vector3 size = bounds.size;
            box.center = target.transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
                Mathf.Max(0.05f, Mathf.Abs(size.x)),
                Mathf.Max(0.05f, Mathf.Abs(size.y)),
                Mathf.Abs(size.z) < 0.05f ? 0.05f : Mathf.Abs(size.z));
        }
    }
}