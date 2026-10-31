using UnityEngine;

namespace CarGame.UI
{
    /// <summary>
    /// Размер окна по умолчанию для интерфейсов терминалов. Своё значение
    /// размера теперь есть у каждой панели отдельно (windowWidth/windowHeight в
    /// TerminalUI, ShopTerminalUI, ExchangerTerminalUI, SlotMachineUI) — его и
    /// правят в инспекторе, чтобы окно село на модель конкретной стойки. Этот
    /// класс задаёт только стартовые значения для новых компонентов.
    ///
    /// Единицы — метры: терминалы висят в мире, и их панель имеет физический
    /// размер.
    /// </summary>
    public static class TerminalLayout
    {
        /// <summary>Ширина окна терминала по умолчанию, метры.</summary>
        public const float WindowWidth = 1.2f;

        /// <summary>Высота окна терминала по умолчанию, метры.</summary>
        public const float WindowHeight = 1.0f;
    }
}
