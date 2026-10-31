using CarGame.Player;
using UnityEngine;

namespace CarGame.UI
{
    /// <summary>
    /// Единая точка «открыт ли сейчас какой-нибудь интерфейс».
    ///
    /// Пока на экране инвентарь, пауза или терминал, ЛКМ/ПКМ в руке должны
    /// молчать, а взгляд и курсор не перехватываться игрой. Собираем состояние
    /// всех окон в одном месте, чтобы рука, ствол и курсор читали один и тот
    /// же флаг, а не проверяли каждое окно по отдельности.
    ///
    /// Терминалы и пауза отделены от инвентаря: у терминала надо ещё и руку
    /// спрятать целиком, а с паузой — не запирать курсор, пока открыто меню.
    /// </summary>
    public static class UiState
    {
        /// <summary>Открыт любой терминал (спавн, магазин, обменник, автомат).</summary>
        public static bool TerminalOpen =>
            TerminalUI.IsOpen
            || ShopTerminalUI.IsOpen
            || ExchangerTerminalUI.IsOpen
            || SlotMachineUI.IsOpen;

        /// <summary>Открыт инвентарь, пауза, окно управления или графики.</summary>
        public static bool MenuOpen =>
            InventoryUI.AnyOpen
            || PauseMenuUI.AnyPaused
            || ControlsPanel.IsOpen
            || GraphicsPanel.IsOpen;

        /// <summary>На экране есть хоть один интерфейс, перехватывающий управление.</summary>
        public static bool AnyOpen => TerminalOpen || MenuOpen;

        /// <summary>
        /// Запирает курсор, если сейчас не открыто ни одного МЕНЮ. Терминал
        /// этого не запрещает: его панель нацелена на прицел и не отбирает у
        /// игрока камеру — курсор можно возвращать даже стоя в зоне терминала.
        /// </summary>
        public static void RelockCursorIfIdle()
        {
            if (!Application.isPlaying) return;
            if (MenuOpen) return;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static bool menuBlocked;
        private static bool restoreControl = true;
        private static bool restoreLook = true;

        /// <summary>
        /// Сбрасывает состояние блокировки меню к «первому запуску». Вызывается
        /// из SceneResetter при каждой новой загрузке сцены: статические поля
        /// переживают SceneManager.LoadScene, и если игрок умер/вышел в главное
        /// меню с открытым меню, menuBlocked остался бы true — управление и
        /// курсор были бы заблокированы на всей новой сцене.
        /// </summary>
        public static void ResetMenuState()
        {
            menuBlocked = false;
            restoreControl = true;
            restoreLook = true;
        }

        /// <summary>
        /// Любое меню (инвентарь, пауза, настройки) открылось. Состояние обзора
        /// и движения запоминается ОДИН раз — при первом открытии, когда игрок
        /// ещё управлялся. Так два меню, открытых одновременно (TAB и Esc), не
        /// перетирают «эталон» друг друга, и после закрытия обоих управление
        /// возвращается ровно тем, каким было до входа в меню.
        /// </summary>
        public static void EnterMenu()
        {
            if (!Application.isPlaying) return;

            PlayerController player = FindPlayer();
            if (!menuBlocked)
            {
                restoreControl = player != null && player.ControlEnabled;
                restoreLook = player != null && player.LookEnabled;
                menuBlocked = true;
            }

            if (player != null)
            {
                player.SetLookEnabled(false);
                player.SetControlEnabled(false);
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        /// <summary>
        /// Одно из меню закрылось. Пока открыто любое другое — держим блок и
        /// свободный курсор; когда закрылось последнее — возвращаем сохранённое
        /// управление и запираем курсор (если не открыт ещё и терминал).
        /// </summary>
        public static void LeaveMenu()
        {
            if (!Application.isPlaying) return;

            if (MenuOpen)
            {
                EnterMenu();
                return;
            }

            menuBlocked = false;

            PlayerController player = FindPlayer();
            if (player != null)
            {
                player.SetLookEnabled(restoreLook);
                player.SetControlEnabled(restoreControl);
            }

            RelockCursorIfIdle();
        }

        /// <summary>
        /// Зовётся, когда терминал перестал показываться. Панель терминала не
        /// запирает курсор, поэтому если игрок открывал меню, а потом ушёл из
        /// зоны, курсор вернём здесь.
        /// </summary>
        public static void OnTerminalClosed()
        {
            RelockCursorIfIdle();
        }

        private static PlayerController FindPlayer()
        {
            return Object.FindFirstObjectByType<PlayerController>();
        }
    }
}
