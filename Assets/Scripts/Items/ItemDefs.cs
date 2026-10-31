using CarGame.Controls;
using UnityEngine;

namespace CarGame.Items
{
    public enum ItemCategory
    {
        [InspectorName("Metal")] Metal = 0,
        [InspectorName("Firearm")] Firearm = 1,
        [InspectorName("Ammo")] Ammo = 2,
        [InspectorName("Tool")] Tool = 3,
        [InspectorName("Consumable")] Consumable = 4,
        [InspectorName("Currency")] Currency = 5,
        [InspectorName("Material")] Material = 6
    }

    public enum FirearmKind
    {
        [InspectorName("Pistol")] Pistol = 0,
        [InspectorName("Shotgun")] Shotgun = 1
    }

    /// <summary>
    /// Описание вида предмета. Общее для всех стеков, поэтому создаётся один раз
    /// и только читается — правила стакания и категории хранятся здесь же.
    /// </summary>
    public sealed class ItemDef
    {
        public ItemDef(string id, string displayName, ItemCategory category, int maxStack, Color tint, float weight = 1f, int price = 0)
        {
            Id = id;
            NumericId = string.Empty;
            englishName = displayName;
            Category = category;
            MaxStack = Mathf.Max(1, maxStack);
            Tint = tint;
            Weight = Mathf.Max(0.01f, weight);
            Price = Mathf.Max(0, price);
        }

        /// <summary>
        /// Буквенный ID предмета. Им помечен ассортимент в каталоге, инвентарь
        /// и всё, что связано с терминалами (например, "golden_bolt").
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// Второй, численный ID предмета. Именно он лежит в префабах
        /// Assets/Prefabs/Items и в разметке руки в сцене ("1" — ключ,
        /// "11" — Golden Bolt). Оба ID равноправны: TryGet и Canonical находят
        /// предмет по любому из них, поэтому и консоль в будущем сможет
        /// спавнить предметы по цифровому и по буквенному ID.
        /// </summary>
        public string NumericId { get; internal set; }

        private readonly string englishName;

        /// <summary>
        /// Русское название предмета. Заполняется в ItemDefs отдельно от
        /// английского — каталог и префабы держат английский, а интерфейс
        /// показывает язык по текущей настройке Localization.
        /// </summary>
        public string RussianName { get; internal set; }

        /// <summary>Название предмета на текущем языке (русский/английский).</summary>
        public string DisplayName =>
            Localization.IsEnglish || string.IsNullOrEmpty(RussianName) ? englishName : RussianName;

        public ItemCategory Category { get; private set; }
        public int MaxStack { get; private set; }
        public Color Tint { get; private set; }

        /// <summary>
        /// Вес предмета в килограммах. Один и тот же вес идёт в две вещи: в массу
        /// Rigidbody — от неё зависит, как далеко улетит предмет, — и в урон при
        /// ударе. Поэтому он и хранится здесь, рядом с остальными свойствами
        /// предмета, а не в физике и не в скриптах по отдельности.
        /// </summary>
        public float Weight { get; private set; }

        public FirearmKind Firearm { get; internal set; }
        public MetalType Metal { get; internal set; }

        /// <summary>Тип тюнинга (щит/турель/таран). null — предмет не тюнинг.</summary>
        public TuningKind? Tuning { get; internal set; }

        /// <summary>Прочность установленной части тюнинга в хп. 0 — не используется.</summary>
        public float TuningDurability { get; internal set; }

        /// <summary>Уровень для турелей/ядер. 0 — без уровня; выше — точнее бьёт по роботам.</summary>
        public int Tier { get; internal set; }

        public bool IsTuning => Tuning.HasValue;

        /// <summary>Цена в Golden Bolt в магазине терминала. 0 — предмет не продаётся.</summary>
        public int Price { get; private set; }

        public bool IsCurrency => Category == ItemCategory.Currency;

        public bool IsFirearm => Category == ItemCategory.Firearm;
        public bool IsAmmo => Category == ItemCategory.Ammo;
        public bool IsMetal => Category == ItemCategory.Metal;
        public bool IsTool => Category == ItemCategory.Tool;
        public bool IsStackable => MaxStack > 1;
    }

    /// <summary>
    /// Правила стрельбы конкретного ствола. Патроны, ёмкость магазина, количество
    /// дробин и темп заданы здесь, чтобы пистолет и дробовик отличались только
    /// числами, а не отдельной логикой.
    /// </summary>
    public sealed class FirearmSpec
    {
        public FirearmKind Kind;
        public string AmmoId;
        public int MagazineCapacity;
        public int PelletsPerShot;
        public float DamagePerPellet;
        public float SpreadDegrees;
        public float Range;
        public float AssistRadius;
        public float FireInterval;
        public bool HoldToFire;
        public float ReloadTime;
        public float InspectTime;
        public float ShotTime;
        public float CarDamageMultiplier;

        public ItemDef AmmoDef
        {
            get
            {
                ItemDef found;
                return ItemDefs.TryGet(AmmoId, out found) ? found : null;
            }
        }
    }

    [System.Serializable]
    public sealed class ItemStack
    {
        public ItemDef Def;
        public int Amount;

        /// <summary>
        /// Состояние предмета, которое переживает выброс. У ствола это заряженные
        /// патроны: выброшенный пистолет хранит их у себя, и при повторном подборе
        /// магазин восстанавливается. Для остальных предметов поле не используется.
        /// </summary>
        public int Payload;

        public ItemStack()
        {
        }

        public ItemStack(ItemDef def, int amount)
        {
            Def = def;
            Amount = amount;
        }

        public bool IsEmpty => Def == null || Amount <= 0;

        public void Clear()
        {
            Def = null;
            Amount = 0;
            Payload = 0;
        }
    }

    public static class ItemDefs
    {
        public const string ScrapId = "scrap";
        public const string MetalId = "metal";
        public const string PlatinumId = "platinum";
        public const string PistolId = "pistol";
        public const string ShotgunId = "shotgun";
public const string PistolAmmoId = "pistol_ammo";
public const string ShotgunAmmoId = "shotgun_ammo";
public const string WrenchId = "wrench";
        public const string InjectorId = "injector";
        public const string EnergyDrinkId = "energy_drink";
        public const string BeerId = "beer";
        public const string GoldenBoltId = "golden_bolt";
        public const string LemonId = "lemon";
        public const string WoodenPlankId = "wooden_plank";
        public const string ShieldScrapId = "shield_scrap";
        public const string ShieldIronId = "shield_iron";
        public const string ShieldPlatinumId = "shield_platinum";
        public const string TurretScrapId = "turret_scrap";
        public const string TurretIronId = "turret_iron";
        public const string TurretPlatinumId = "turret_platinum";
        public const string RamScrapId = "scrap_ram";
        public const string RamIronId = "iron_ram";
        public const string RamPlatinumId = "platinum_ram";
        public const string RamId = "ram";
        public const string FlashlightId = "flashlight";
        public const string ShakeLightId = "shake_light";
        public const string CpuT1Id = "cpu_t1";
        public const string CpuT2Id = "cpu_t2";
        public const string CpuT3Id = "cpu_t3";
        public const string GazId = "gaz";

        // Численные ID из префабов Assets/Prefabs/Items и из разметки руки в
        // сцене. Каждый предмет знает свой номер, чтобы и поднятый из мира
        // предмет ("11" — болт), и созданный терминалом ("golden_bolt")
        // разрешались в один и тот же ItemDef.
        public const string WrenchNumber = "1";
        public const string ScrapNumber = "3";
        public const string MetalNumber = "4";
        public const string PlatinumNumber = "5";
        public const string InjectorNumber = "6";
        public const string PistolNumber = "7";
        public const string ShotgunNumber = "8";
        public const string PistolAmmoNumber = "9";
        public const string ShotgunAmmoNumber = "10";
        public const string GoldenBoltNumber = "11";
        public const string EnergyDrinkNumber = "12";
        public const string BeerNumber = "13";
        public const string LemonNumber = "14";
        public const string WoodenPlankNumber = "15";
        // Номера 16/17/18 держат турели: именно ими помечены их префабы в мире
        // (Turret1Item/2Item/3Item) и их чертежи в сцене.
        public const string TurretScrapNumber = "16";
        public const string TurretIronNumber = "17";
        public const string TurretPlatinumNumber = "18";
        // Номера 21/22/23 заняты тремя таранами (их префабы ScrapMetallRam,
        // MetallRam и PlatinumRam уже помечены этими номерами). Щиты держат
        // 24/25/26 — ровно те номера, которыми помечены их префабы в мире
        // (ScrapMetallShield, MetallShield, PlatinumShield), варианты руки и
        // чертежи на машине: чертёж сверяется с предметом в руке по этому ID.
        public const string ShieldScrapNumber = "24";
        public const string ShieldIronNumber = "25";
        public const string ShieldPlatinumNumber = "26";
        // Номера фонариков взяты из их префабов в Assets/Prefabs/Items и из
        // разметки руки в сцене: Shakelight — 19, Flashlight — 20.
        public const string ShakeLightNumber = "19";
        public const string FlashlightNumber = "20";
        // Легаси-таран держит свободный 27: 20 занял фонарь, 24 — щит скрапа.
        public const string RamNumber = "27";
        // Процессоры для крафта турелей. Номера 28/29/30 ровно те, которыми
        // помечены их префабы в Assets/Prefabs/Items (CPUT1/2/3).
        public const string CpuT1Number = "28";
        public const string CpuT2Number = "29";
        public const string CpuT3Number = "30";
        // Канистра с бензином. Номер 31 свободен; её префаб в мире — Gaz.prefab.
        public const string GazNumber = "31";
        // Кубы для спавна мобов. Номера из префабов DronCube/DinaBotCube/SwordBotCube.
        public const string SwordBotCubeId = "sword_bot_cube";
        public const string DinaBotCubeId = "dina_bot_cube";
        public const string DronCubeId = "dron_cube";
        public const string SwordBotCubeNumber = "32";
        public const string DinaBotCubeNumber = "33";
        public const string DronCubeNumber = "34";

        // --- Объекты: терминалы, машина, станция -----------------------------
        // Числовые ID совпадают с номерами вариантов руки из разметки
        // handVariants в сцене: Shop=35, Exchanger=36, SlotMachine=37, Car=38,
        // CraftingStation=39. Строковые ID — человекочитаемые ключи каталога.
        public const string ShopTerminalId = "shop_terminal";
        public const string ExchangerTerminalId = "exchanger_terminal";
        public const string SlotMachineTerminalId = "slot_machine_terminal";
        public const string CarId = "car";
        public const string CraftingStationId = "crafting_station";

        public const string ShopTerminalNumber = "35";
        public const string ExchangerTerminalNumber = "36";
        public const string SlotMachineTerminalNumber = "37";
        public const string CarNumber = "38";
        public const string CraftingStationNumber = "39";

// Вес каждого предмета подобран так, чтобы рядом стояли патроны и
        // инжектор — лёгкие, урон только за скорость, — и платина с дробовиком —
        // тяжёлые, которые прибавляют урона сверх скорости.
        public static readonly ItemDef Scrap = MakeMetal(ScrapId, MetalType.Scrap, new Color(0.45f, 0.42f, 0.38f), 1.6f, 1);
        public static readonly ItemDef Metal = MakeMetal(MetalId, MetalType.Metal, new Color(0.62f, 0.64f, 0.68f), 2.8f, 3);
        public static readonly ItemDef Platinum = MakeMetal(PlatinumId, MetalType.Platinum, new Color(0.85f, 0.87f, 0.92f), 4.5f, 6);

        public static readonly ItemDef Pistol = new ItemDef(PistolId, "Pistol", ItemCategory.Firearm, 1, new Color(0.78f, 0.78f, 0.84f), 1.8f, 8)
        {
            Firearm = FirearmKind.Pistol,
            RussianName = "Пистолет"
        };

        public static readonly ItemDef Shotgun = new ItemDef(ShotgunId, "Shotgun", ItemCategory.Firearm, 1, new Color(0.72f, 0.55f, 0.36f), 3.2f, 14)
        {
            Firearm = FirearmKind.Shotgun,
            RussianName = "Дробовик"
        };

        public static readonly ItemDef PistolAmmo = new ItemDef(PistolAmmoId, "Pistol Ammo", ItemCategory.Ammo, 120, new Color(0.9f, 0.76f, 0.32f), 1.4f, 2)
        {
            RussianName = "Патроны к пистолету"
        };
        public static readonly ItemDef ShotgunAmmo = new ItemDef(ShotgunAmmoId, "Shotgun Shells", ItemCategory.Ammo, 60, new Color(0.85f, 0.36f, 0.26f), 1.4f, 3)
        {
            RussianName = "Дробь для дробовика"
        };

        /// <summary>
        /// Гаечный ключ. MaxStack 1 — это инструмент, а не расходник: в инвентаре
        /// он лежит одной штукой, а работает через руку (WrenchItem).
        /// </summary>
        public static readonly ItemDef Wrench = new ItemDef(WrenchId, "Wrench", ItemCategory.Tool, 1, new Color(0.55f, 0.6f, 0.66f), 2.6f, 5)
        {
            RussianName = "Гаечный ключ"
        };

        /// <summary>
        /// Инджектор — инструмент-расходник: в руке он применяется через
        /// <c>InjectorItem</c>, а из инвентаря уходит по одной штуке после
        /// использования, поэтому стек больше единицы.
        /// </summary>
        public static readonly ItemDef Injector = new ItemDef(InjectorId, "Injector", ItemCategory.Tool, 6, new Color(0.72f, 0.86f, 0.92f), 0.7f, 4)
        {
            RussianName = "Инъектор"
        };

        /// <summary>
        /// Энергетик — расходник, как инжектор: применяется из руки через
        /// <c>EnergyDrinkItem</c> и даёт временное ускорение игрока.
        /// </summary>
        public static readonly ItemDef EnergyDrink = new ItemDef(EnergyDrinkId, "Energy Drink", ItemCategory.Tool, 6, new Color(0.35f, 0.78f, 0.95f), 0.8f, 6)
        {
            RussianName = "Энергетик"
        };

        /// <summary>
        /// Пиво — расходник, как инжектор: применяется из руки через
        /// <c>BeerItem</c> и на пару секунд «раскачивает» камеру.
        /// </summary>
        public static readonly ItemDef Beer = new ItemDef(BeerId, "Beer", ItemCategory.Tool, 6, new Color(0.85f, 0.68f, 0.28f), 0.9f, 4)
        {
            RussianName = "Пиво"
        };

        /// <summary>
        /// Лимон — расходник-лечение, как инжектор: применяется из руки и
        /// восстанавливает здоровье, после чего исчезает из инвентаря на одну
        /// штуку. Стек больше единицы, потому что это расходник, а не инструмент.
        /// </summary>
        public static readonly ItemDef Lemon = new ItemDef(LemonId, "Lemon", ItemCategory.Tool, 6, new Color(0.93f, 0.87f, 0.28f), 0.4f, 3)
        {
            RussianName = "Лимон"
        };

        /// <summary>
        /// Деревянная доска — материал: лежит в инвентаре стопкой, в руке
        /// показывается моделью, но собственного действия не имеет. Из мира
        /// поднимается префабом Assets/Prefabs/Items/WoodenPlank.prefab.
        /// </summary>
        public static readonly ItemDef WoodenPlank = new ItemDef(WoodenPlankId, "Wooden Plank", ItemCategory.Material, 99, new Color(0.62f, 0.45f, 0.28f), 3f, 2)
        {
            RussianName = "Деревянная доска"
        };

        // --- Объекты: терминалы, машина, станция ---------------------------------
        // Строительные/взаимодействующие объекты, которые теперь доступны из
        // терминала сендбокса в разделе "Объекты".

        public static readonly ItemDef ShopTerminal = new ItemDef(ShopTerminalId, "Shop Terminal", ItemCategory.Tool, 1, new Color(0.62f, 0.72f, 0.85f), 8f, 0)
        {
            RussianName = "Торговый терминал"
        };

        public static readonly ItemDef ExchangerTerminal = new ItemDef(ExchangerTerminalId, "Exchanger Terminal", ItemCategory.Tool, 1, new Color(0.62f, 0.78f, 0.82f), 8f, 0)
        {
            RussianName = "Обменный терминал"
        };

        public static readonly ItemDef SlotMachineTerminal = new ItemDef(SlotMachineTerminalId, "Slot Machine Terminal", ItemCategory.Tool, 1, new Color(0.82f, 0.68f, 0.62f), 8f, 0)
        {
            RussianName = "СлотОВАЯ МАШИНА"
        };

        public static readonly ItemDef Car = new ItemDef(CarId, "Car", ItemCategory.Tool, 1, new Color(0.55f, 0.62f, 0.72f), 1200f, 0)
        {
            RussianName = "Машина"
        };

        public static readonly ItemDef CraftingStation = new ItemDef(CraftingStationId, "Crafting Station", ItemCategory.Tool, 1, new Color(0.72f, 0.8f, 0.62f), 12f, 0)
        {
            RussianName = "Крафтовая станция"
        };

        // --- Тюнинг машины -----------------------------------------------------
        // Каждый тюнинг предмет ставится на чертёж (Blueprint), впитывает
        // удары своей прочностью и замедляет игрока, пока лежит в руке.
        // Вес подобран по «тяжести» защиты: платиновый щит тормозит сильнее.

        public static readonly ItemDef ShieldScrap = new ItemDef(ShieldScrapId, "Scrap Shield", ItemCategory.Tool, 1, new Color(0.5f, 0.47f, 0.43f), 5f, 10)
        {
            Tuning = TuningKind.Shield,
            TuningDurability = 120f,
            RussianName = "Щит из лома"
        };

        public static readonly ItemDef ShieldIron = new ItemDef(ShieldIronId, "Iron Shield", ItemCategory.Tool, 1, new Color(0.58f, 0.6f, 0.64f), 6.5f, 16)
        {
            Tuning = TuningKind.Shield,
            TuningDurability = 260f,
            RussianName = "Железный щит"
        };

        public static readonly ItemDef ShieldPlatinum = new ItemDef(ShieldPlatinumId, "Platinum Shield", ItemCategory.Tool, 1, new Color(0.85f, 0.87f, 0.92f), 8.5f, 26)
        {
            Tuning = TuningKind.Shield,
            TuningDurability = 480f,
            RussianName = "Платиновый щит"
        };

        public static readonly ItemDef TurretScrap = new ItemDef(TurretScrapId, "Scrap Turret", ItemCategory.Tool, 1, new Color(0.5f, 0.58f, 0.62f), 4.5f, 14)
        {
            Tuning = TuningKind.Turret,
            TuningDurability = 100f,
            Tier = 1,
            RussianName = "Турель из лома"
        };

        public static readonly ItemDef TurretIron = new ItemDef(TurretIronId, "Iron Turret", ItemCategory.Tool, 1, new Color(0.55f, 0.6f, 0.66f), 5.5f, 20)
        {
            Tuning = TuningKind.Turret,
            TuningDurability = 200f,
            Tier = 2,
            RussianName = "Железная турель"
        };

        public static readonly ItemDef TurretPlatinum = new ItemDef(TurretPlatinumId, "Platinum Turret", ItemCategory.Tool, 1, new Color(0.85f, 0.87f, 0.92f), 6.5f, 28)
        {
            Tuning = TuningKind.Turret,
            TuningDurability = 320f,
            Tier = 3,
            RussianName = "Платиновая турель"
        };

        public static readonly ItemDef RamScrap = new ItemDef(RamScrapId, "Scrap Ram", ItemCategory.Tool, 1, new Color(0.5f,0.47f,0.43f), 5f, 12) { Tuning = TuningKind.Ram, TuningDurability = 120f, RussianName = "Ржавый таран" };
        public static readonly ItemDef RamIron = new ItemDef(RamIronId, "Iron Ram", ItemCategory.Tool, 1, new Color(0.58f,0.6f,0.64f), 6.5f, 18) { Tuning = TuningKind.Ram, TuningDurability = 220f, RussianName = "Железный таран" };
        public static readonly ItemDef RamPlatinum = new ItemDef(RamPlatinumId, "Platinum Ram", ItemCategory.Tool, 1, new Color(0.85f,0.87f,0.92f), 8.5f, 30) { Tuning = TuningKind.Ram, TuningDurability = 400f, RussianName = "Платиновый таран" };

        public static readonly ItemDef Ram = new ItemDef(RamId, "Ram Bumper", ItemCategory.Tool, 1, new Color(0.55f, 0.5f, 0.46f), 6.5f, 12)
        {
            Tuning = TuningKind.Ram,
            TuningDurability = 220f,
            RussianName = "Таран-бампер"
        };

        // Валюта магазина: Golden Bolt. Он в общем списке All, чтобы терминал спавна
        // мог создать болт в мире. В магазине он всё равно не продаётся —
        // RefillOfferPool отсекает валюту по def.IsCurrency, а принимает болт
        // только деталь депозита BoltDepositor. В мир попадает префабом
        // Assets/Prefabs/Items/GoldenBolt.prefab.
        public static readonly ItemDef GoldenBolt = new ItemDef(
            GoldenBoltId, "Golden Bolt", ItemCategory.Currency, 999, new Color(0.98f, 0.85f, 0.3f), 0.35f, 1)
        {
            RussianName = "Золотой болт"
        };

        // --- Фонарики ----------------------------------------------------------
        // Оба — инструменты: лежат в инвентаре по одному, а в руке работают
        // своими компонентами (FlashlightItem и ShakeLightItem).

        /// <summary>
        /// Обычный фонарик: светит сразу, ЛКМ включает и выключает луч.
        /// В руке работает через <c>FlashlightItem</c>.
        /// </summary>
        public static readonly ItemDef Flashlight = new ItemDef(FlashlightId, "Flashlight", ItemCategory.Tool, 1, new Color(0.82f, 0.84f, 0.7f), 1f, 9)
        {
            RussianName = "Фонарик"
        };

        /// <summary>
        /// Светотряс — фонарик без батарейки: свет копится тряской (ЛКМ), чем
        /// больше трясёшь, тем ярче луч. В руке работает через
        /// <c>ShakeLightItem</c>.
        /// </summary>
        public static readonly ItemDef ShakeLight = new ItemDef(ShakeLightId, "Hand-Crank Light", ItemCategory.Tool, 1, new Color(0.9f, 0.78f, 0.42f), 1.1f, 12)
        {
            RussianName = "Светотряс"
        };

        // --- Процессоры (материалы для крафта турелей) -------------------------
        // Три разных CPU: компонент турели соответствующего уровня. Лежат в
        // инвентаре стопкой, в руке показываются моделью, своего действия не
        // имеют. В мир попадают префабами CPUT1/2/3.prefab.
        public static readonly ItemDef CpuT1 = new ItemDef(CpuT1Id, "CPU T1", ItemCategory.Material, 99, new Color(0.45f, 0.62f, 0.4f), 0.8f, 6)
        {
            RussianName = "Процессор T1"
        };

        public static readonly ItemDef CpuT2 = new ItemDef(CpuT2Id, "CPU T2", ItemCategory.Material, 99, new Color(0.4f, 0.55f, 0.7f), 0.9f, 10)
        {
            RussianName = "Процессор T2"
        };

        public static readonly ItemDef CpuT3 = new ItemDef(CpuT3Id, "CPU T3", ItemCategory.Material, 99, new Color(0.62f, 0.45f, 0.72f), 1f, 16)
        {
            RussianName = "Процессор T3"
        };

        // --- Канистра с бензином -----------------------------------------------
        // Инструмент-расходник: из руки заправляет машину (зажать E у машины,
        // держа канистру), пока в ней есть бензин. Полная канистра взрывается
        // от попадания. Не уничтожается даже пустой — её надо выбросить.
        public static readonly ItemDef Gaz = new ItemDef(GazId, "Gas Can", ItemCategory.Tool, 1, new Color(0.85f, 0.3f, 0.2f), 5f, 18)
        {
            RussianName = "Канистра с бензином"
        };

        // --- Кубы для спавна мобов ----------------------------------------------
        public static readonly ItemDef SwordBotCube = new ItemDef(SwordBotCubeId, "Sword Bot Cube", ItemCategory.Tool, 1, new Color(0.7f, 0.8f, 0.9f), 2f, 0)
        {
            RussianName = "Куб мечника"
        };

        public static readonly ItemDef DinaBotCube = new ItemDef(DinaBotCubeId, "Dina Bot Cube", ItemCategory.Tool, 1, new Color(0.7f, 0.9f, 0.7f), 2f, 0)
        {
            RussianName = "Куб Дины"
        };

        public static readonly ItemDef DronCube = new ItemDef(DronCubeId, "Dron Cube", ItemCategory.Tool, 1, new Color(0.9f, 0.7f, 0.7f), 2f, 0)
        {
            RussianName = "Куб дрона"
        };

        private static readonly ItemDef[] All =
        {
            Scrap, Metal, Platinum, Pistol, Shotgun, PistolAmmo, ShotgunAmmo, Wrench, Injector, EnergyDrink, Beer, GoldenBolt, Lemon, WoodenPlank,
            ShieldScrap, ShieldIron, ShieldPlatinum, TurretScrap, TurretIron, TurretPlatinum, RamScrap, RamIron, RamPlatinum, Ram, Flashlight, ShakeLight,
            CpuT1, CpuT2, CpuT3, Gaz, SwordBotCube, DinaBotCube, DronCube,
            ShopTerminal, ExchangerTerminal, SlotMachineTerminal, Car, CraftingStation
        };

        /// <summary>
        /// Все предметы игры по порядку. Терминал спавна показывает их списком,
        /// поэтому нужен доступ ко всем сразу, а не только поиск по ID.
        /// </summary>
        public static ItemDef[] Items => All;

        /// <summary>
        /// Поиск по обоим ID: буквенному ("golden_bolt") и численному ("11").
        /// Таблица собирается один раз из All, а не прописывается руками, чтобы
        /// номер и строка не разъехались, когда в список добавится предмет.
        /// Сравнение без учёта регистра: старые модели в сцене используют
        /// разный регистр.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, ItemDef> ById =
            new System.Collections.Generic.Dictionary<string, ItemDef>(System.StringComparer.OrdinalIgnoreCase);

        static ItemDefs()
        {
            Register(Wrench, WrenchNumber);
            Register(Scrap, ScrapNumber);
            Register(Metal, MetalNumber);
            Register(Platinum, PlatinumNumber);
            Register(Injector, InjectorNumber);
            Register(EnergyDrink, EnergyDrinkNumber);
            Register(Beer, BeerNumber);
            Register(Lemon, LemonNumber);
            Register(WoodenPlank, WoodenPlankNumber);
            Register(ShopTerminal, ShopTerminalNumber);
            Register(ExchangerTerminal, ExchangerTerminalNumber);
            Register(SlotMachineTerminal, SlotMachineTerminalNumber);
            Register(Car, CarNumber);
            Register(CraftingStation, CraftingStationNumber);
            Register(Pistol, PistolNumber);
            Register(Shotgun, ShotgunNumber);
            Register(PistolAmmo, PistolAmmoNumber);
            Register(ShotgunAmmo, ShotgunAmmoNumber);
            Register(GoldenBolt, GoldenBoltNumber);
            Register(ShieldScrap, ShieldScrapNumber);
            Register(ShieldIron, ShieldIronNumber);
            Register(ShieldPlatinum, ShieldPlatinumNumber);
            Register(TurretScrap, TurretScrapNumber);
            Register(TurretIron, TurretIronNumber);
            Register(TurretPlatinum, TurretPlatinumNumber);
            Register(RamScrap, "21"); Register(RamIron, "22"); Register(RamPlatinum, "23"); Register(Ram, RamNumber);
            Register(Flashlight, FlashlightNumber);
            Register(ShakeLight, ShakeLightNumber);
            Register(CpuT1, CpuT1Number);
            Register(CpuT2, CpuT2Number);
            Register(CpuT3, CpuT3Number);
            Register(Gaz, GazNumber);
            Register(SwordBotCube, SwordBotCubeNumber);
            Register(DinaBotCube, DinaBotCubeNumber);
            Register(DronCube, DronCubeNumber);
        }

        private static void Register(ItemDef def, string number)
        {
            def.NumericId = number;
            ById[def.Id] = def;
            ById[number] = def;
        }

        /// <summary>
        /// Приводит ID к буквенному виду: номер из старой разметки сцены
        /// превращается в строку ("11" — в "golden_bolt"), всё остальное
        /// остаётся как есть. Так и ключи каталога, и варианты руки, ищутся
        /// одинаково — что по номеру, что по строке.
        /// </summary>
        public static string Canonical(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;

            string canonical = id.Trim();
            ItemDef def;
            return ById.TryGetValue(canonical, out def) ? def.Id : canonical;
        }

        /// <summary>
        /// Ищет предмет по ID. Принимает и буквенный ("golden_bolt"), и
        /// численный ("11") ID — PickupItem пишет в себя второй, а инвентарь
        /// и терминалы пользуются первым.
        /// </summary>
        public static bool TryGet(string id, out ItemDef def)
        {
            if (string.IsNullOrEmpty(id))
            {
                def = null;
                return false;
            }

            return ById.TryGetValue(id.Trim(), out def);
        }

        public static ItemDef Resolve(FirearmKind kind)
        {
            return kind == FirearmKind.Shotgun ? Shotgun : Pistol;
        }

        public static ItemDef Resolve(MetalType type)
        {
            switch (type)
            {
                case MetalType.Metal: return Metal;
                case MetalType.Platinum: return Platinum;
                default: return Scrap;
            }
        }

        private static ItemDef MakeMetal(string id, MetalType type, Color tint, float weight, int price = 0)
        {
            return new ItemDef(id, MetalInfo.Name(type), ItemCategory.Metal, 99, tint, weight, price) { Metal = type, RussianName = MetalInfo.RussianName(type) };
        }
    }

    public static class FirearmSpecs
    {
        /// <summary>
        /// Времена перезарядки и осмотра совпадают с длиной клипов в
        /// контроллерах: Assets/Anims/Pistol/Model и Assets/Anims/ShotGun.
        /// WeaponItem всё равно меряет клип сам, но и игровое время должно
        /// совпадать с тем, что видит игрок.
        /// </summary>

        /// <summary>Пистолет: одна пуля за выстрел, при зажатой ЛКМ стреляет не чаще раза в 1.5 с.</summary>
        public static readonly FirearmSpec Pistol = new FirearmSpec
        {
            Kind = FirearmKind.Pistol,
            AmmoId = ItemDefs.PistolAmmoId,
            MagazineCapacity = 12,
            PelletsPerShot = 1,
            DamagePerPellet = 18f,
            SpreadDegrees = 1.2f,
            Range = 120f,
            AssistRadius = 0.1f,
            FireInterval = 1.5f,
            HoldToFire = true,
            ReloadTime = 1.58f,
            InspectTime = 1.33f,
            ShotTime = 0.25f,
            CarDamageMultiplier = 1.4f
        };

        /// <summary>Дробовик: один патрон на выстрел, за выстрел летит вся веерная пуля.</summary>
        public static readonly FirearmSpec Shotgun = new FirearmSpec
        {
            Kind = FirearmKind.Shotgun,
            AmmoId = ItemDefs.ShotgunAmmoId,
            MagazineCapacity = 2,
            PelletsPerShot = 8,
            DamagePerPellet = 9f,
            SpreadDegrees = 7f,
            Range = 40f,
            AssistRadius = 0.15f,
            FireInterval = 0.85f,
            HoldToFire = false,
            ReloadTime = 1.85f,
            InspectTime = 1.43f,
            ShotTime = 1.57f,
            CarDamageMultiplier = 1.4f
        };

        public static FirearmSpec Resolve(FirearmKind kind)
        {
            return kind == FirearmKind.Shotgun ? Shotgun : Pistol;
        }
    }
}
