using System;
using System.Collections.Generic;
using CarGame.UI;
using UnityEngine;

namespace CarGame.Items
{
    [DisallowMultipleComponent]
    public class SpawnTerminal : MonoBehaviour
    {
        public enum TerminalMode
        {
            Normal,
            Admin
        }

        public enum TerminalCategory
        {
            All,
            Consumables,
            Currency,
            WeaponsTools,
            Building,
            Craft,
            Mobs,
            Objects,
            AdminTools
        }

        [Serializable]
        public sealed class MobEntry
        {
            public string displayName = string.Empty;
            public GameObject prefab;
        }

        [Serializable]
        public sealed class CategoryEntry
        {
            public TerminalCategory category = TerminalCategory.All;
            public ItemDef[] items = Array.Empty<ItemDef>();
            public MobEntry[] mobs = Array.Empty<MobEntry>();
        }

        [Header("Точка спавна")]
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private Transform screenPoint;

        [Header("Активация")]
        [SerializeField] private TerminalActivationTrigger activeTrigger;
        [SerializeField] private float showDistance = 5f;

        [Header("Режим")]
        [SerializeField] private TerminalMode mode = TerminalMode.Normal;
        [SerializeField] private bool allowAdminToggle = true;

        [Header("Категории")]
        [SerializeField] private CategoryEntry[] categories = Array.Empty<CategoryEntry>();
        [SerializeField] private bool autoFillCategories = true;

        [Header("Админ: список мобов (спавнятся только спавн-кубом)")]
        [SerializeField] private MobEntry[] mobs = Array.Empty<MobEntry>();

        [Header("Вид")]
        [SerializeField] private GameObject model;

        [Header("Спавн")]
        [SerializeField] private int itemAmount = 1;
        [SerializeField] private float itemRadius = 0.8f;
        [SerializeField] private float spawnHeight = 0.6f;
        [SerializeField] private int maxSpawnCount = 10;

        private Light poweredLight;
        private bool playerInsideActive;
        private int currentCount = 1;
        private TerminalCategory currentCategory = TerminalCategory.All;

        public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;
        public Transform ScreenPoint => screenPoint != null ? screenPoint : SpawnPoint;
        public TerminalActivationTrigger ActiveTrigger => activeTrigger;
        public bool PlayerInsideActive => activeTrigger != null ? activeTrigger.IsPlayerInside : playerInsideActive;
        public MobEntry[] Mobs => mobs;
        public GameObject Model => model;
        public float ShowDistance => showDistance > 0.1f ? showDistance : 5f;
        public Vector3 PanelPosition => ScreenPoint.position;
        public int ItemAmount => Mathf.Clamp(currentCount, 1, maxSpawnCount);
        public TerminalMode Mode => mode;
        public TerminalCategory CurrentCategory => currentCategory;
        public int CurrentCount => currentCount;
        public bool AllowAdminToggle => allowAdminToggle;
        public CategoryEntry[] Categories => categories;

        public void SetMobs(MobEntry[] value) { mobs = value ?? Array.Empty<MobEntry>(); }
        public void SetSpawnPoint(Transform value) { spawnPoint = value; }
        public void SetScreenPoint(Transform value) { screenPoint = value; }
        public void SetActiveTrigger(TerminalActivationTrigger value) { activeTrigger = value; }
        public void SetPlayerInside(bool inside) { playerInsideActive = inside; }
        public void SetModel(GameObject value) { model = value; }
        public void SetMode(TerminalMode m) { mode = m; }
        public void SetCategory(TerminalCategory c) { currentCategory = c; }
        public void SetCount(int c) { currentCount = Mathf.Clamp(c, 1, maxSpawnCount); }
        public void AdjustCount(int delta) { SetCount(currentCount + delta); }

        private void Awake()
        {
            TerminalUI view = TerminalUI.Show(this);
            if (view != null && view.transform.parent != transform) view.transform.SetParent(transform, false);
            if (GetComponentInChildren<Collider>(true) == null)
            {
                BoxCollider box = gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(2f, 2f, 2f);
                box.center = new Vector3(0f, 1f, 0f);
            }
            if (autoFillCategories && (categories == null || categories.Length == 0))
                BuildDefaultCategories();
        }

        private void BuildDefaultCategories()
        {
            List<CategoryEntry> list = new List<CategoryEntry>();

            // Оружие/инструменты: гаечный ключ, пистолет, дробовик, фонарики
            list.Add(new CategoryEntry
            {
                category = TerminalCategory.WeaponsTools,
                items = GetByIds(ItemDefs.WrenchId, ItemDefs.PistolId, ItemDefs.ShotgunId, ItemDefs.FlashlightId, ItemDefs.ShakeLightId)
            });

            // Расходники: лимон, инжектор, энергетик, пиво, бензин, патроны и дробь
            list.Add(new CategoryEntry
            {
                category = TerminalCategory.Consumables,
                items = GetByIds(ItemDefs.LemonId, ItemDefs.InjectorId, ItemDefs.PistolAmmoId, ItemDefs.ShotgunAmmoId, ItemDefs.EnergyDrinkId, ItemDefs.BeerId, ItemDefs.GazId)
            });

            // Постройки: турели всех типов, щиты всех типов, тараны всех типов
            list.Add(new CategoryEntry
            {
                category = TerminalCategory.Building,
                items = GetByIds(
                    ItemDefs.TurretScrapId, ItemDefs.TurretIronId, ItemDefs.TurretPlatinumId,
                    ItemDefs.ShieldScrapId, ItemDefs.ShieldIronId, ItemDefs.ShieldPlatinumId,
                    ItemDefs.RamScrapId, ItemDefs.RamIronId, ItemDefs.RamPlatinumId
                )
            });

            // Валюта: пока только золотые болтики
            list.Add(new CategoryEntry
            {
                category = TerminalCategory.Currency,
                items = GetByIds(ItemDefs.GoldenBoltId)
            });

            // Предметы только для крафта: доска, все виды металла, процессоры всех мастей
            list.Add(new CategoryEntry
            {
                category = TerminalCategory.Craft,
                items = GetByIds(
                    ItemDefs.WoodenPlankId,
                    ItemDefs.ScrapId, ItemDefs.MetalId, ItemDefs.PlatinumId,
                    ItemDefs.CpuT1Id, ItemDefs.CpuT2Id, ItemDefs.CpuT3Id
                )
            });

            // Страница "Мобы": только спавн-кубы. Призвать моба напрямую из
            // терминала нельзя — моб появляется исключительно от броска куба.
            ItemDef[] cubes = GetByIds(ItemDefs.SwordBotCubeId, ItemDefs.DinaBotCubeId, ItemDefs.DronCubeId);
            list.Add(new CategoryEntry { category = TerminalCategory.Mobs, items = cubes });

            // Страница "Объекты": терминалы, машина, станция крафта.
            list.Add(new CategoryEntry
            {
                category = TerminalCategory.Objects,
                items = GetByIds(ItemDefs.ShopTerminalId, ItemDefs.SlotMachineTerminalId, ItemDefs.ExchangerTerminalId, ItemDefs.CarId, ItemDefs.CraftingStationId)
            });

            // Админ: инструменты
            list.Add(new CategoryEntry
            {
                category = TerminalCategory.AdminTools,
                items = GetByIds(ItemDefs.DelHammerId)
            });
            list.Add(new CategoryEntry { category = TerminalCategory.All, items = GetByIds(ItemDefs.WrenchId, ItemDefs.PistolId, ItemDefs.ShotgunId, ItemDefs.LemonId, ItemDefs.InjectorId, ItemDefs.PistolAmmoId, ItemDefs.ShotgunAmmoId, ItemDefs.TurretScrapId, ItemDefs.TurretIronId, ItemDefs.TurretPlatinumId, ItemDefs.ShieldScrapId, ItemDefs.ShieldIronId, ItemDefs.ShieldPlatinumId, ItemDefs.RamScrapId, ItemDefs.RamIronId, ItemDefs.RamPlatinumId, ItemDefs.GoldenBoltId, ItemDefs.WoodenPlankId, ItemDefs.ScrapId, ItemDefs.MetalId, ItemDefs.PlatinumId, ItemDefs.CpuT1Id, ItemDefs.CpuT2Id, ItemDefs.CpuT3Id, ItemDefs.SwordBotCubeId, ItemDefs.DinaBotCubeId, ItemDefs.DronCubeId, ItemDefs.ShopTerminalId, ItemDefs.ExchangerTerminalId, ItemDefs.SlotMachineTerminalId, ItemDefs.CarId, ItemDefs.CraftingStationId) });
            categories = list.ToArray();
        }

        private ItemDef[] GetByCats(params ItemCategory[] cats)
        {
            HashSet<ItemCategory> set = new HashSet<ItemCategory>(cats);
            List<ItemDef> res = new List<ItemDef>();
            ItemDef[] all = ItemDefs.Items;
            for (int i = 0; i < all.Length; i++)
            {
                ItemDef d = all[i];
                if (d == null) continue;
                if (set.Contains(d.Category)) res.Add(d);
            }
            return res.ToArray();
        }

        private ItemDef[] GetByIds(params string[] ids)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ids.Length; i++) if (!string.IsNullOrEmpty(ids[i])) set.Add(ids[i]);
            List<ItemDef> res = new List<ItemDef>();
            ItemDef[] all = ItemDefs.Items;
            for (int i = 0; i < all.Length; i++)
            {
                ItemDef d = all[i];
                if (d == null) continue;
                if (set.Contains(d.Id)) res.Add(d);
            }
            return res.ToArray();
        }

        private ItemDef[] FilterAll()
        {
            List<ItemDef> res = new List<ItemDef>();
            ItemDef[] all = ItemDefs.Items;
            for (int i = 0; i < all.Length; i++)
            {
                ItemDef d = all[i];
                if (d == null) continue;
                if (d.IsCurrency) continue;
                res.Add(d);
            }
            return res.ToArray();
        }

        public bool SpawnItem(ItemDef def)
        {
            if (def == null) return false;
            ItemWorldCatalog catalog = ItemWorldCatalog.Instance;
            if (catalog == null || !catalog.Has(def)) return false;

            int payload = 0;
            if (def.IsFirearm)
            {
                FirearmSpec spec = FirearmSpecs.Resolve(def.Firearm);
                if (spec != null) payload = spec.MagazineCapacity;
            }

            int count = ItemAmount;
            for (int i = 0; i < count; i++)
            {
                Quaternion rotation = RandomYaw();
                Vector3 position = NextPosition(itemRadius);
                catalog.Spawn(def, position, rotation, 1, payload, Vector3.zero);
            }
            return true;
        }

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
            light.color = new Color(0.45f, 0.85f, 1f);
            light.range = Mathf.Max(1.5f, bounds.size.magnitude * 1.5f);
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            poweredLight = light;
        }

        private Vector3 NextPosition(float radius)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * Mathf.Max(0f, radius);
            return SpawnPoint.position + new Vector3(offset.x, spawnHeight, offset.y);
        }

        private static Quaternion RandomYaw() => Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
    }
}
