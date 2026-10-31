using System;
using System.Collections.Generic;
using CarGame.UI;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Магазин терминала: четыре случайных предмета с ценами и запасом. Покупка
    /// идёт за Golden Bolt из общего кошелька TerminalWallet.
    ///
    /// Запас терминала случаен один раз за сцену: игрок покупает то, что есть,
    /// а когда всё раскуплено — магазин так и стоит пустым (автопополнения нет).
    ///
    /// Как и терминал спавна, магазин НЕ реализует IInteractable — клавиша E к
    /// нему отношения не имеет. Панель показывает вход в ActiveTrigger, а
    /// покупка — ЛКМ по кнопке. Депозит Golden Bolt — прицел на деталь Deposit.
    ///
    /// Раскладка объекта в сцене такая:
    /// ShopTerminal          — пустой корень, на нём висит этот компонент
    ///   Model               — внешний вид магазина
    ///   ShopPanel           — панель с коллайдером
    ///     SpawnPoint        — точка, у которой появляется купленное
    ///   ScreenPoint         — точка экрана, на которой висит панель магазина
    ///   ActiveTrigger       — объём, при входе в который панель появляется
    ///   DepositSlot         — деталь депозита с BoltDepositor и коллайдером
    /// </summary>
    [DisallowMultipleComponent]
    public class ShopTerminal : MonoBehaviour
    {
        [Serializable]
        public sealed class ShopOffer
        {
            public ItemDef Item;
            public int Stock;
        }

        [Header("Запас магазина")]
        [Tooltip("Сколько случайных предметов в ассортименте магазина")]
        [SerializeField, Range(2, 8)] private int poolSize = 4;

        [Tooltip("Сколько штук одного предмета минимум лежит на складе")]
        [SerializeField, Range(1, 10)] private int minStock = 1;

        [Tooltip("Сколько штук одного предмета максимум лежит на складе")]
        [SerializeField, Range(1, 10)] private int maxStock = 5;

        [Header("Точка спавна")]
        [Tooltip("SpawnPoint, у которого появляется купленное. Если пусто — от самого магазина")]
        [SerializeField] private Transform spawnPoint;

        [Header("Обновление стока")]
        [Tooltip("Сколько золотых болтов стоит обновление ассортимента магазина")]
        [SerializeField, Min(0)] private int refreshCost = 25;

        [Header("Показ панели")]
        [Tooltip("ScreenPoint: точка экрана магазина, на которой висит панель")]
        [SerializeField] private Transform screenPoint;

        [Tooltip("ActiveTrigger: объём, при входе в который панель появляется")]
        [SerializeField] private TerminalActivationTrigger activeTrigger;

        [Tooltip("Резервная дистанция, если ActiveTrigger не назначен")]
        [SerializeField] private float showDistance = 5f;

        [Header("Вид")]
        [Tooltip("Model магазина. Пока панель открыта, модель подсвечивается")]
        [SerializeField] private GameObject model;

        [Header("Падение купленного")]
        [Tooltip("На какой высоте над точкой спавна появляется купленный предмет, м")]
        [SerializeField] private float spawnHeight = 0.6f;

        [Header("Депозит")]
        [Tooltip("Деталь Deposit, в которую кладётся Golden Bolt. Нужна для подсветки готовности, если назначена")]
        [SerializeField] private GameObject depositSlot;

        private readonly List<ShopOffer> offers = new List<ShopOffer>();

        private Light poweredLight;

        /// <summary>Кэш состояния триггера, если он не назначен полем.</summary>
        private bool playerInsideActive;

        /// <summary>Ассортимент магазина. Не пополняется сам — что выпало, то выпало.</summary>
        public IReadOnlyList<ShopOffer> Offers => offers;

        /// <summary>Сколько стоит обновление ассортимента в золотых болтах.</summary>
        public int RefreshCost => Mathf.Max(0, refreshCost);

        /// <summary>Растёт при каждой пересборке ассортимента — по нему панель перестраивается.</summary>
        public int Revision { get; private set; }

        /// <summary>Куда ставить купленное: заданная точка или сам магазин.</summary>
        public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;

        /// <summary>Точка экрана магазина, на которой висит панель.</summary>
        public Transform ScreenPoint => screenPoint != null ? screenPoint : SpawnPoint;

        /// <summary>Триггер, при входе в который панель появляется.</summary>
        public TerminalActivationTrigger ActiveTrigger => activeTrigger;

        /// <summary>Игрок внутри триггера активации — значит панель на экране.</summary>
        public bool PlayerInsideActive => activeTrigger != null ? activeTrigger.IsPlayerInside : playerInsideActive;

        /// <summary>Внешний вид магазина.</summary>
        public GameObject Model => model;

        /// <summary>Деталь депозита Golden Bolt.</summary>
        public GameObject DepositSlot => depositSlot;

        public float ShowDistance => showDistance > 0.1f ? showDistance : DefaultShowDistance;

        private const float DefaultShowDistance = 5f;

        /// <summary>Куда панель встаёт, если магазин назначен.</summary>
        public Vector3 PanelPosition => ScreenPoint.position;

        /// <summary>Весь ассортимент раскуплен.</summary>
        public bool AllSoldOut
        {
            get
            {
                for (int i = 0; i < offers.Count; i++)
                {
                    if (offers[i] != null && offers[i].Stock > 0) return false;
                }
                return true;
            }
        }

        private void Awake()
        {
            // Панель создаём сразу, как и терминал спавна: иначе её Update не
            // вызывался бы и вход в ActiveTrigger ничего не делал.
            ShopTerminalUI.Show(this);

            if (GetComponentInChildren<Collider>(true) == null)
            {
                BoxCollider box = gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(2f, 2f, 2f);
                box.center = new Vector3(0f, 1f, 0f);
            }

            RefillOfferPool();
        }

        /// <summary>Задаёт точку, у которой появляется купленное.</summary>
        public void SetSpawnPoint(Transform value)
        {
            spawnPoint = value;
        }

        /// <summary>Задаёт точку экрана, на которой висит панель.</summary>
        public void SetScreenPoint(Transform value)
        {
            screenPoint = value;
        }

        /// <summary>Задаёт триггер активации панели.</summary>
        public void SetActiveTrigger(TerminalActivationTrigger value)
        {
            activeTrigger = value;
        }

        /// <summary>Состояние триггера. Вызывается из TerminalActivationTrigger.</summary>
        public void SetPlayerInside(bool inside)
        {
            playerInsideActive = inside;
        }

        /// <summary>Задаёт внешний вид магазина.</summary>
        public void SetModel(GameObject value)
        {
            model = value;
        }

        /// <summary>Задаёт деталь депозита Golden Bolt.</summary>
        public void SetDepositSlot(GameObject value)
        {
            depositSlot = value;
        }

        /// <summary>
        /// Набирает новый случайный ассортимент: poolSize разных предметов из
        /// всех продающихся (не валюта, цена больше нуля), у каждого свой запас.
        /// Вызывается при старте сцены и из редакторского меню по правой кнопке.
        /// </summary>
        [ContextMenu("Refill Offer Pool")]
        public void RefillOfferPool()
        {
            offers.Clear();

            List<ItemDef> candidates = new List<ItemDef>();
            ItemDef[] items = ItemDefs.Items;
            for (int i = 0; i < items.Length; i++)
            {
                ItemDef def = items[i];
                if (def == null || def.IsCurrency || def.Price <= 0) continue;
                if (!HasWorldPrefab(def)) continue;
                candidates.Add(def);
            }

            int take = Mathf.Min(Mathf.Max(1, poolSize), candidates.Count);
            int min = Mathf.Min(minStock, maxStock);
            int max = Mathf.Max(minStock, maxStock);

            // Перемешивание Фишера-Йейтса: половина берётся, остальные отбрасываются.
            for (int i = 0; i < take; i++)
            {
                int pick = UnityEngine.Random.Range(i, candidates.Count);
                ItemDef swap = candidates[i];
                candidates[i] = candidates[pick];
                candidates[pick] = swap;

                offers.Add(new ShopOffer
                {
                    Item = candidates[i],
                    Stock = UnityEngine.Random.Range(min, max + 1)
                });
            }

            Revision++;
        }

        /// <summary>
        /// Предмет попадает в продажу, только если у него есть префаб в каталоге
        /// мира. Иначе покупатель получил бы «пустышку»: предмет есть в списке, а
        /// положить в руки нечего. Каталога в сцене нет — не мешаем, показываем всё.
        /// </summary>
        private static bool HasWorldPrefab(ItemDef def)
        {
            ItemWorldCatalog catalog = ItemWorldCatalog.Instance;
            return catalog == null || catalog.Has(def);
        }

        /// <summary>
        /// Платное обновление ассортимента: списывает refreshCost из кошелька и
        /// набирает новые случайные предложения. Возвращает false, если денег
        /// не хватило — тогда ассортимент не трогается.
        /// </summary>
        public bool TryRefresh()
        {
            if (RefreshCost > 0 && !TerminalWallet.TrySpend(RefreshCost)) return false;

            RefillOfferPool();
            return true;
        }

        /// <summary>
        /// Покупка: списывает цену из кошелька и ставит предмет у точки спавна.
        /// Возвращает false, если раскуплено, цена нулевая, денег не хватило или
        /// ставить предмет нечем. При неудачном спавне деньги возвращаются.
        /// </summary>
        public bool Buy(ShopOffer offer)
        {
            if (offer == null || offer.Item == null || offer.Stock <= 0) return false;
            if (offer.Item.Price <= 0) return false;

            if (!TerminalWallet.TrySpend(offer.Item.Price)) return false;

            if (!SpawnItem(offer.Item))
            {
                TerminalWallet.Add(offer.Item.Price);
                return false;
            }

            offer.Stock--;
            return true;
        }

        /// <summary>
        /// Ставит предмет у точки спавна. Точка фиксированная, без разброса:
        /// купленное появляется ровно в одном месте, внутри магазина, а не
        /// разлетается по рандомному радиусу.
        /// </summary>
        public bool SpawnItem(ItemDef def)
        {
            if (def == null) return false;

            ItemWorldCatalog catalog = ItemWorldCatalog.Instance;
            if (catalog == null || !catalog.Has(def))
            {
                Debug.LogWarning($"[{nameof(ShopTerminal)}] Нет префаба для предмета {def.Id}", this);
                return false;
            }

            int payload = 0;
            if (def.IsFirearm)
            {
                // Купленный ствол сразу готов к стрельбе, как и спавн-терминальный.
                FirearmSpec spec = FirearmSpecs.Resolve(def.Firearm);
                if (spec != null) payload = spec.MagazineCapacity;
            }

            Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            Vector3 position = SpawnPoint.position + new Vector3(0f, spawnHeight, 0f);
            return catalog.Spawn(def, position, rotation, 1, payload, Vector3.zero) != null;
        }

        /// <summary>Подсвечивает магазин, пока панель открыта (как терминал спавна).</summary>
        public void SetPowered(bool on)
        {
            if (!on)
            {
                if (poweredLight != null) poweredLight.enabled = false;
                return;
            }

            if (poweredLight != null)
            {
                poweredLight.enabled = true;
                return;
            }

            if (model == null) return;

            GameObject go = new GameObject("PoweredLight");
            go.transform.SetParent(model.transform, false);

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(model.transform.position, Vector3.one * 0.5f);
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            go.transform.position = bounds.center + Vector3.up * 0.2f;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.98f, 0.82f, 0.3f);
            light.range = Mathf.Max(1.5f, bounds.size.magnitude * 1.5f);
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;

            poweredLight = light;
        }
    }
}