using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Инвентарь игрока: 5 слотов быстрого доступа и 25 обычных. Слоты быстрого
    /// доступа идут первыми, поэтому огнестрел и патроны, которые нужны в бою,
    /// всегда попадают наверх. Хранилище живёт только в рантайме и не
    /// сериализуется — после перезапуска сцены набор выдаётся заново.
    /// </summary>
    [DisallowMultipleComponent]
    public class Inventory : MonoBehaviour
    {
        public const int QuickSlotCount = 5;
        public const int MainSlotCount = 25;
        public const int TotalSlotCount = QuickSlotCount + MainSlotCount;

        [Header("Стартовый набор")]
        [Tooltip("Выдать предметы при старте сцены")]
        [SerializeField] private bool grantStartingGear = true;
        [Tooltip("Единственный стартовый предмет — гаечный ключ в первом слоте. Остальные предметы выдаются только когда этот флаг выключен")]
        [SerializeField] private bool startOnlyWithWrench = true;
        [Tooltip("Выдать гаечный ключ в первый слот. Инструмент нужен всегда: без него машину не починить")]
        [SerializeField] private bool startWithWrench = true;
        [Tooltip("Выдать пистолет в первый быстрый слот")]
        [SerializeField] private bool startWithPistol;
        [SerializeField] private int startPistolAmmo;
        [Tooltip("Выдать дробовик. По умолчанию выключено: игрок начинает с одним стволом")]
        [SerializeField] private bool startWithShotgun;
        [SerializeField] private int startShotgunAmmo;
        [SerializeField] private int startScrap;

        private readonly ItemStack[] slots = new ItemStack[TotalSlotCount];

        public event System.Action Changed;

        public int SlotCount => TotalSlotCount;
        public int QuickSlots => QuickSlotCount;

        public static bool IsQuickIndex(int index)
        {
            return index >= 0 && index < QuickSlotCount;
        }

        private void Awake()
        {
            for (int i = 0; i < slots.Length; i++) slots[i] = new ItemStack();

            if (!grantStartingGear) return;

            // Ключ идёт первым, чтобы занять первый быстрый слот: игрок должен видеть
            // инструмент в хотбаре сразу, а не искать его по инвентарю.
            if (startWithWrench) TryAdd(ItemDefs.Wrench, 1);

            // Базовый старт — ровно один инструмент в руке. Оружие и металл выдаются
            // только когда этот флаг снят в инспекторе, иначе они переполняли бы
            // хотбар и занимали место, которое нужно ключу.
            if (startOnlyWithWrench) return;

            if (startWithPistol)
            {
                TryAdd(ItemDefs.Pistol, 1);
                TryAdd(ItemDefs.PistolAmmo, startPistolAmmo);
            }

            if (startWithShotgun)
            {
                TryAdd(ItemDefs.Shotgun, 1);
                TryAdd(ItemDefs.ShotgunAmmo, startShotgunAmmo);
            }

            TryAdd(ItemDefs.Scrap, startScrap);
        }

        public ItemStack Get(int index)
        {
            if (index < 0 || index >= slots.Length) return null;
            return slots[index];
        }

        public ItemDef DefAt(int index)
        {
            ItemStack stack = Get(index);
            return stack != null && !stack.IsEmpty ? stack.Def : null;
        }

        public bool IsEmpty(int index)
        {
            ItemStack stack = Get(index);
            return stack == null || stack.IsEmpty;
        }

        /// <summary>
        /// Кладёт предмет в первые подходящие слоты: сначала быстрый доступ, потом
        /// основные. Возвращает false, если предмет не поместился целиком — тогда
        /// в инвентарь не попадает вообще ничего, иначе подбор ломал бы стак.
        /// </summary>
        public bool TryAdd(ItemDef def, int amount)
        {
            return TryAdd(def, amount, 0, out _);
        }

        /// <summary>
        /// Кладёт предмет в инвентарь вместе с его состоянием. Payload нужно
        /// стволам: у выброшенного пистолета в альт-версии хранится, сколько в
        /// нём патронов, и при подборе эта цифра возвращается в инвентарь.
        /// </summary>
        public bool TryAdd(ItemDef def, int amount, int payload, out int slotIndex)
        {
            slotIndex = -1;
            if (def == null || amount <= 0) return false;

            int free = FreeSpace(def);
            if (free < amount) return false;

            int left = amount;
            for (int i = 0; i < slots.Length && left > 0; i++)
            {
                int before = slots[i].Amount;
                AddToSlot(slots[i], def, ref left);
                if (slotIndex < 0 && slots[i].Amount != before) slotIndex = i;
            }

            if (slotIndex >= 0) slots[slotIndex].Payload = payload;

            RaiseChanged();
            return true;
        }

        public int Count(ItemDef def)
        {
            if (def == null) return 0;

            int total = 0;
            for (int i = 0; i < slots.Length; i++)
                if (!slots[i].IsEmpty && ReferenceEquals(slots[i].Def, def)) total += slots[i].Amount;

            return total;
        }

        public bool Has(ItemDef def, int amount)
        {
            return Count(def) >= amount;
        }

        /// <summary>
        /// Снимает предмет из конкретной ячейки. Нужен для выдачи предмета в руку
        /// и для выброса: снять его нужно именно из той ячейки, по которой кликнули,
        /// а не из первого попавшегося с тем же предметом.
        /// </summary>
        public bool RemoveAt(int index, int amount)
        {
            if (index < 0 || index >= slots.Length || amount <= 0) return false;

            ItemStack stack = slots[index];
            if (stack.IsEmpty) return false;

            int take = Mathf.Min(stack.Amount, amount);
            stack.Amount -= take;
            if (stack.Amount <= 0) stack.Clear();

            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Снимает предмет из ячейки вместе с его состоянием. Выброс читает payload
        /// до RemoveAt, потому что тот чистит тот же стак.
        /// </summary>
        public bool TakeAt(int index, out ItemDef def, out int amount, out int payload)
        {
            def = null;
            amount = 0;
            payload = 0;

            if (index < 0 || index >= slots.Length) return false;

            ItemStack stack = slots[index];
            if (stack.IsEmpty) return false;

            def = stack.Def;
            amount = stack.Amount;
            payload = stack.Payload;

            return RemoveAt(index, amount);
        }

        /// <summary>Кладёт предмет в конкретную ячейку. Нужно для возврата выброшенного.</summary>
        public bool SetAt(int index, ItemDef def, int amount, int payload)
        {
            if (index < 0 || index >= slots.Length || def == null || amount <= 0) return false;

            ItemStack stack = slots[index];
            if (!stack.IsEmpty && !ReferenceEquals(stack.Def, def)) return false;

            stack.Def = def;
            stack.Amount = amount;
            stack.Payload = payload;

            RaiseChanged();
            return true;
        }

        /// <summary>Снимает нужное количество с любых слотов. Возвращает, сколько реально забрали.</summary>
        public int Remove(ItemDef def, int amount)
        {
            if (def == null || amount <= 0) return 0;

            int left = amount;
            for (int i = 0; i < slots.Length && left > 0; i++) TakeFromSlot(slots[i], def, ref left);

            int taken = amount - left;
            if (taken > 0) RaiseChanged();
            return taken;
        }

        /// <summary>
        /// Снимает одну единицу предмета вместе с её состоянием.
        ///
        /// Нужна для броска предмета, показанного в руке: рука показывает предмет
        /// из ячейки, но сам предмет при этом лежит в инвентаре, и выброс должен
        /// его оттуда убрать — иначе на земле появилась бы вторая копия.
        ///
        /// preferredIndex — ячейка, из которой предмет показывается. Предмет могли
        /// перенести, поэтому сначала проверяется она, и только потом ищется любой
        /// стак с тем же предметом.
        /// </summary>
        public bool TryTakeOne(ItemDef def, int preferredIndex, out int payload)
        {
            payload = 0;
            if (def == null) return false;

            ItemStack stack = Get(preferredIndex);
            if (stack == null || stack.IsEmpty || !ReferenceEquals(stack.Def, def))
            {
                int index = FindFirst(def);
                if (index < 0) return false;
                stack = slots[index];
            }

            payload = stack.Payload;
            stack.Amount -= 1;
            if (stack.Amount <= 0) stack.Clear();

            RaiseChanged();
            return true;
        }

        /// <summary>Номер первого слота с этим предметом, или -1.</summary>
        public int FindFirst(ItemDef def)
        {
            if (def == null) return -1;

            for (int i = 0; i < slots.Length; i++)
            {
                ItemStack stack = slots[i];
                if (!stack.IsEmpty && ReferenceEquals(stack.Def, def)) return i;
            }

            return -1;
        }

        /// <summary>
        /// Перенос предмета из одной ячейки в другую. Отличается от Swap тем, что
        /// работает и с пустыми ячейками: перенести можно в пустую и из пустой, а
        /// одинаковые стаки сливаются, пока не упрётся в MaxStack. Этим же
        /// пользуется перетаскивание мышью, поэтому предмет нельзя потерять на
        /// пустой ячейке и не нужно двигать по одной единице.
        /// </summary>
        public bool MoveOrSwap(int from, int to)
        {
            if (from < 0 || to < 0 || from >= slots.Length || to >= slots.Length || from == to) return false;

            ItemStack source = slots[from];
            ItemStack target = slots[to];

            if (source.IsEmpty) return false;

            if (target.IsEmpty)
            {
                target.Def = source.Def;
                target.Amount = source.Amount;
                target.Payload = source.Payload;
                source.Clear();
                RaiseChanged();
                return true;
            }

            if (ReferenceEquals(target.Def, source.Def) && target.Def.IsStackable)
            {
                int room = target.Def.MaxStack - target.Amount;
                if (room <= 0) return false;

                int moved = Mathf.Min(room, source.Amount);
                target.Amount += moved;
                source.Amount -= moved;
                if (source.Amount <= 0) source.Clear();

                RaiseChanged();
                return true;
            }

            Swap(from, to);
            return true;
        }

        public void Swap(int first, int second)
        {
            if (first < 0 || second < 0 || first >= slots.Length || second >= slots.Length || first == second) return;

            // Payload переезжает вместе с предметом: иначе ствол потерял бы
            // заряженные патроны при любом переносе между ячейками.
            ItemDef firstDef = slots[first].Def;
            int firstAmount = slots[first].Amount;
            int firstPayload = slots[first].Payload;

            slots[first].Def = slots[second].Def;
            slots[first].Amount = slots[second].Amount;
            slots[first].Payload = slots[second].Payload;
            slots[second].Def = firstDef;
            slots[second].Amount = firstAmount;
            slots[second].Payload = firstPayload;

            RaiseChanged();
        }

        /// <summary>
        /// Тратит на ремонт один случайный металл из инвентаря. Повторяет
        /// поведение багажника: за один удар ключом уходит ровно один кусок,
        /// поэтому тяжёлая платина не съедает разом всю полосу лома.
        /// </summary>
        public bool TryConsumeMetal(float missing, out MetalType usedType, out float gained)
        {
            usedType = MetalType.Scrap;
            gained = 0f;
            if (missing <= 0f) return false;

            MetalType[] types = { MetalType.Platinum, MetalType.Metal, MetalType.Scrap };

            int total = 0;
            for (int i = 0; i < types.Length; i++) total += Count(ItemDefs.Resolve(types[i]));
            if (total <= 0) return false;

            int roll = Random.Range(0, total);

            for (int i = 0; i < types.Length; i++)
            {
                MetalType type = types[i];
                ItemDef def = ItemDefs.Resolve(type);
                int have = Count(def);
                if (have <= 0) continue;

                if (roll >= have)
                {
                    roll -= have;
                    continue;
                }

                RemoveInternal(def, 1);
                usedType = type;
                gained = MetalInfo.Repair(type);

                if (gained > 0f) RaiseChanged();
                return gained > 0f;
            }

            return false;
        }

        /// <summary>
        /// Тратит на ремонт ровно один кусок УКАЗАННОГО металла (FixOne у
        /// постройки): другие металлы для него не подходят, даже если лежат в
        /// инвентаре.
        /// </summary>
        public bool TryConsumeMetal(float missing, MetalType type, out float gained)
        {
            gained = 0f;
            if (missing <= 0f) return false;

            ItemDef def = ItemDefs.Resolve(type);
            if (Count(def) <= 0) return false;

            RemoveInternal(def, 1);
            gained = MetalInfo.Repair(type);
            if (gained > 0f) RaiseChanged();
            return gained > 0f;
        }

        public void RaiseChanged()
        {
            if (Changed != null) Changed();
        }

        private int FreeSpace(ItemDef def)
        {
            int free = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                ItemStack stack = slots[i];
                if (stack.IsEmpty) { free += def.MaxStack; continue; }
                if (ReferenceEquals(stack.Def, def) && stack.Amount < def.MaxStack) free += def.MaxStack - stack.Amount;
            }

            return free;
        }

        private static void AddToSlot(ItemStack stack, ItemDef def, ref int left)
        {
            if (left <= 0) return;

            if (stack.IsEmpty)
            {
                int opened = Mathf.Min(left, def.MaxStack);
                stack.Def = def;
                stack.Amount = opened;
                left -= opened;
                return;
            }

            if (!ReferenceEquals(stack.Def, def) || stack.Amount >= def.MaxStack) return;

            int room = def.MaxStack - stack.Amount;
            int take = Mathf.Min(room, left);
            stack.Amount += take;
            left -= take;
        }

        private static void TakeFromSlot(ItemStack stack, ItemDef def, ref int left)
        {
            if (left <= 0 || stack.IsEmpty || !ReferenceEquals(stack.Def, def)) return;

            int take = Mathf.Min(stack.Amount, left);
            stack.Amount -= take;
            left -= take;

            if (stack.Amount <= 0) stack.Clear();
        }

        private void RemoveInternal(ItemDef def, int amount)
        {
            int left = amount;
            for (int i = 0; i < slots.Length && left > 0; i++) TakeFromSlot(slots[i], def, ref left);
        }
    }
}
