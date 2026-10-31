using CarGame.Controls;

namespace CarGame.UI
{
    /// <summary>
    /// Каталог переводов терминалов: английский текст хранится в ассетах и
    /// сценах как базовый, а русский приходит отсюда через
    /// <see cref="Localization.ToCurrent"/> на момент показа.
    /// Инициализируется один раз первым обращением.
    /// </summary>
    public static class TerminalTexts
    {
        static TerminalTexts()
        {
            Localization.RegisterRu("SPAWN", "СПАВН");
            Localization.RegisterRu("MOBS", "МОБЫ");
            Localization.RegisterRu("Mob", "Моб");

            Localization.RegisterRu("SHOP", "МАГАЗИН");
            Localization.RegisterRu("BALANCE", "БАЛАНС");
            Localization.RegisterRu("SOLD OUT", "РАСПРОДАНО");
            Localization.RegisterRu("REFRESH", "ОБНОВИТЬ");

            Localization.RegisterRu("EXCHANGER", "ОБМЕННИК");
            Localization.RegisterRu("TOTAL", "ИТОГО");
            Localization.RegisterRu("CONFIRM", "ПОДТВЕРДИТЬ");
            Localization.RegisterRu("CANCEL", "ОТМЕНА");
            Localization.RegisterRu("EMPTY", "ПУСТО");

            Localization.RegisterRu("SLOTS", "АВТОМАТ");
            Localization.RegisterRu("BET", "СТАВКА");
            Localization.RegisterRu("DANGER", "ОПАСНО");
            Localization.RegisterRu("JACKPOT!", "ДЖЕКПОТ!");
            Localization.RegisterRu("DOUBLE!", "УДВОЕНИЕ!");
            Localization.RegisterRu("LUCK UP", "УДАЧА!");
            Localization.RegisterRu("NO LUCK", "НЕ ПОВЕЗЛО");
            Localization.RegisterRu("LUCK", "УДАЧА");
        }

        /// <summary>Переводит английскую строку терминала на текущий язык.</summary>
        public static string L(string english)
        {
            return Localization.ToCurrent(english);
        }
    }
}