using System;

namespace CarGame.Items
{
    /// <summary>
    /// Общий кошелёк магазинов терминала. Баланс один на все ShopTerminal:
    /// Golden Bolt, внесённые в любой Deposit-детали, тратятся в любом магазине.
    /// Живёт сессией игры, как и инвентарь — перезагрузка сцены его сбрасывает.
    /// </summary>
    public static class TerminalWallet
    {
        public static int Balance { get; private set; }

        /// <summary>Баланс изменился: панель магазина по нему перерисовывает счётчик.</summary>
        public static event Action Changed;

        /// <summary>Пополняет баланс. Обычно — депозит Golden Bolt в магазине.</summary>
        public static void Add(int amount)
        {
            if (amount <= 0) return;

            Balance += amount;
            Changed?.Invoke();
        }

        /// <summary>Списывает цену, если денег хватает. Покупка дешевле не идёт.</summary>
        public static bool TrySpend(int price)
        {
            if (price <= 0) return true;
            if (Balance < price) return false;

            Balance -= price;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Обнуляет баланс. Пригодится при редактировании и в редакторских сценах.</summary>
        public static void Reset()
        {
            if (Balance == 0) return;

            Balance = 0;
            Changed?.Invoke();
        }
    }
}