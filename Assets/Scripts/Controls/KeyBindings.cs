using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CarGame.Controls
{
    /// <summary>
    /// Логические действия игры. Переназначаются в настройках и хранятся между
    /// запусками в PlayerPrefs.
    /// </summary>
    public enum GameAction
    {
        MoveForward,
        MoveBack,
        MoveLeft,
        MoveRight,
        Jump,
        Sprint,
        Interact,
        Attack,
        Drop,
        Inventory,
        Reload,
        Inspect,
        CarForward,
        CarBack,
        CarLeft,
        CarRight,
        Handbrake,
        CarLights,
        TerminalExit,
        Pause,
        Slot1,
        Slot2,
        Slot3,
        Slot4,
        Slot5
    }

    /// <summary>
    /// Раскладка управления: что нажато и какие клавиши назначены. Всё держится
    /// на путях Input System ("&lt;Keyboard&gt;/w", "&lt;Mouse&gt;/leftButton"),
    /// поэтому одно и то же действие можно повесить и на клавишу, и на кнопку
    /// мыши. Значения живут в PlayerPrefs и переживают перезапуск.
    ///
    /// Раньше каждая система читала Keyboard.current напрямую — теперь все
    /// читают отсюда, и переназначение действует везде сразу.
    /// </summary>
    public static class KeyBindings
    {
        private const string PrefsPrefix = "keybind.";

        private static readonly Dictionary<GameAction, string> Defaults = new Dictionary<GameAction, string>
        {
            { GameAction.MoveForward, "<Keyboard>/w" },
            { GameAction.MoveBack, "<Keyboard>/s" },
            { GameAction.MoveLeft, "<Keyboard>/a" },
            { GameAction.MoveRight, "<Keyboard>/d" },
            { GameAction.Jump, "<Keyboard>/space" },
            { GameAction.Sprint, "<Keyboard>/leftShift" },
            { GameAction.Interact, "<Keyboard>/e" },
            { GameAction.Attack, "<Mouse>/leftButton" },
            { GameAction.Drop, "<Mouse>/rightButton" },
            { GameAction.Inventory, "<Keyboard>/tab" },
            { GameAction.Reload, "<Keyboard>/r" },
            { GameAction.Inspect, "<Keyboard>/f" },
            { GameAction.CarForward, "<Keyboard>/w" },
            { GameAction.CarBack, "<Keyboard>/s" },
            { GameAction.CarLeft, "<Keyboard>/a" },
            { GameAction.CarRight, "<Keyboard>/d" },
            { GameAction.Handbrake, "<Keyboard>/space" },
            { GameAction.CarLights, "<Keyboard>/f" },
            { GameAction.TerminalExit, "<Keyboard>/f1" },
            { GameAction.Pause, "<Keyboard>/escape" },
            { GameAction.Slot1, "<Keyboard>/1" },
            { GameAction.Slot2, "<Keyboard>/2" },
            { GameAction.Slot3, "<Keyboard>/3" },
            { GameAction.Slot4, "<Keyboard>/4" },
            { GameAction.Slot5, "<Keyboard>/5" }
        };

        /// <summary>
        /// Прежние значения по умолчанию, которые нужно молча перенести на новое
        /// при первом запуске. Сравнение идёт с сохранённым значением, поэтому
        /// пользовательское переназначение (любая другая клавиша) не трогается.
        ///
        /// Инвентарь изначально висел на B — теперь по умолчанию TAB, но у тех,
        /// кто не менял привязку, в PlayerPrefs лежит "b" и перекрывал бы новый
        /// дефолт. Такой сохранённый "b" подменяем на новый дефолт.
        /// </summary>
        private static readonly Dictionary<GameAction, string> LegacyDefaults = new Dictionary<GameAction, string>
        {
            { GameAction.Inventory, "<Keyboard>/b" }
        };

        private static readonly Dictionary<GameAction, string> bindings = new Dictionary<GameAction, string>();
        private static readonly Dictionary<GameAction, ButtonControl> resolved = new Dictionary<GameAction, ButtonControl>();
        private static bool loaded;

        /// <summary>
        /// Группа переназначаемых действий для настроек управления. Панель
        /// управления рисует заголовок группы и под ним её действия — так у
        /// игрока перед глазами главное: движение, взаимодействие, бросок и
        /// альт-взаимодействие.
        /// </summary>
        public sealed class Category
        {
            public readonly string Russian;
            public readonly string English;
            public readonly GameAction[] Actions;

            public Category(string russian, string english, GameAction[] actions)
            {
                Russian = russian;
                English = english;
                Actions = actions;
            }
        }

        /// <summary>Группы действий для настроек управления в порядке показа.</summary>
        public static readonly Category[] Categories =
        {
            new Category("ДВИЖЕНИЕ", "MOVEMENT", new[]
            {
                GameAction.MoveForward, GameAction.MoveBack, GameAction.MoveLeft, GameAction.MoveRight
            }),
            new Category("ВЗАИМОДЕЙСТВИЕ", "INTERACTION", new[] { GameAction.Interact }),
            new Category("БРОСОК", "THROW", new[] { GameAction.Drop }),
            new Category("АЛЬТ. ВЗАИМОДЕЙСТВИЕ", "ALT. INTERACT", new[] { GameAction.Attack }),
            new Category("МАШИНА", "CAR", new[] { GameAction.CarForward, GameAction.CarBack, GameAction.CarLeft, GameAction.CarRight, GameAction.Handbrake, GameAction.CarLights })
        };

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            Load();
        }

        private static void EnsureLoaded()
        {
            if (!loaded) Load();
        }

        private static void Load()
        {
            bindings.Clear();
            resolved.Clear();

            bool migrated = false;
            foreach (KeyValuePair<GameAction, string> pair in Defaults)
            {
                string path = PlayerPrefs.GetString(PrefsPrefix + pair.Key, pair.Value);

                string legacy;
                if (LegacyDefaults.TryGetValue(pair.Key, out legacy)
                    && !string.IsNullOrEmpty(path)
                    && string.Equals(NormalizePath(path), legacy, StringComparison.OrdinalIgnoreCase))
                {
                    path = pair.Value;
                    PlayerPrefs.SetString(PrefsPrefix + pair.Key, path);
                    migrated = true;
                }

                if (string.IsNullOrEmpty(path)) path = pair.Value;
                bindings[pair.Key] = NormalizePath(path);
            }

            if (migrated) PlayerPrefs.Save();

            loaded = true;
        }

        /// <summary>
        /// Чинит привязки, сохранённые со старым форматом имён клавиш.
        ///
        /// Слоты когда-то писались как "&lt;Keyboard&gt;/digit1", но у клавиши «1»
        /// в Input System имя контрола — "1", а не "digit1". Такой путь не
        /// находился, и цифры не переключали слоты вообще. Правим и дефолт, и
        /// уже сохранённое в PlayerPrefs значение.
        /// </summary>
        private static string NormalizePath(string path)
        {
            const string legacy = "<Keyboard>/digit";
            if (!string.IsNullOrEmpty(path) && path.StartsWith(legacy, StringComparison.OrdinalIgnoreCase))
                return "<Keyboard>/" + path.Substring(legacy.Length);

            return path;
        }

        public static string GetPath(GameAction action)
        {
            EnsureLoaded();
            string path;
            return bindings.TryGetValue(action, out path) ? path : string.Empty;
        }

        /// <summary>
        /// Ищет другое действие, занявшее тот же путь. Смотрим только действия,
        /// показанные в настройках управления (категории), — служебные привязки
        /// вроде машины не мешают. Нужно, чтобы две команды не оказались на
        /// одной клавише: панель управления обменивает привязки.
        /// </summary>
        public static bool TryFindConflict(string path, GameAction exclude, out GameAction conflict)
        {
            conflict = default(GameAction);
            if (string.IsNullOrEmpty(path)) return false;

            for (int c = 0; c < Categories.Length; c++)
            {
                GameAction[] actions = Categories[c].Actions;
                for (int i = 0; i < actions.Length; i++)
                {
                    GameAction action = actions[i];
                    if (action == exclude) continue;
                    if (string.Equals(GetPath(action), path, StringComparison.OrdinalIgnoreCase))
                    {
                        conflict = action;
                        return true;
                    }
                }
            }

            return false;
        }

        public static void SetPath(GameAction action, string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            EnsureLoaded();
            bindings[action] = path;
            resolved.Remove(action);

            PlayerPrefs.SetString(PrefsPrefix + action, path);
            PlayerPrefs.Save();

            Changed?.Invoke();
        }

        public static void ResetToDefaults()
        {
            EnsureLoaded();

            foreach (KeyValuePair<GameAction, string> pair in Defaults)
            {
                bindings[pair.Key] = pair.Value;
                PlayerPrefs.SetString(PrefsPrefix + pair.Key, pair.Value);
            }

            resolved.Clear();
            PlayerPrefs.Save();

            Changed?.Invoke();
        }

        public static bool IsPressed(GameAction action)
        {
            ButtonControl control = Resolve(action);
            return control != null && control.isPressed;
        }

        public static bool WasPressedThisFrame(GameAction action)
        {
            ButtonControl control = Resolve(action);
            return control != null && control.wasPressedThisFrame;
        }

        public static bool WasReleasedThisFrame(GameAction action)
        {
            ButtonControl control = Resolve(action);
            return control != null && control.wasReleasedThisFrame;
        }

        private static ButtonControl Resolve(GameAction action)
        {
            EnsureLoaded();

            ButtonControl cached;
            if (resolved.TryGetValue(action, out cached) && cached != null) return cached;

            string path = GetPath(action);
            ButtonControl control = string.IsNullOrEmpty(path) ? null : InputSystem.FindControl(path) as ButtonControl;
            resolved[action] = control;
            return control;
        }

        /// <summary>Человекочитаемое имя действия для настроек (текущий язык).</summary>
        public static string GetDisplayName(GameAction action)
        {
            return GetDisplayName(action, Localization.IsEnglish);
        }

        /// <summary>Человекочитаемое имя действия на заданном языке.</summary>
        public static string GetDisplayName(GameAction action, bool english)
        {
            switch (action)
            {
                case GameAction.MoveForward: return english ? "Move forward" : "Движение вперёд";
                case GameAction.MoveBack: return english ? "Move back" : "Движение назад";
                case GameAction.MoveLeft: return english ? "Move left" : "Влево";
                case GameAction.MoveRight: return english ? "Move right" : "Вправо";
                case GameAction.Jump: return english ? "Jump" : "Прыжок";
                case GameAction.Sprint: return english ? "Sprint" : "Бег";
                case GameAction.Interact: return english ? "Interact" : "Взаимодействие";
                case GameAction.Attack: return english ? "Alt interact" : "Альт. взаимодействие";
                case GameAction.Drop: return english ? "Throw / secondary" : "Бросок / второе";
                case GameAction.Inventory: return english ? "Inventory" : "Инвентарь";
                case GameAction.Reload: return english ? "Reload" : "Перезарядка";
                case GameAction.Inspect: return english ? "Inspect weapon" : "Осмотр оружия";
                case GameAction.CarForward: return english ? "Car: forward" : "Машина: вперёд";
                case GameAction.CarBack: return english ? "Car: back" : "Машина: назад";
                case GameAction.CarLeft: return english ? "Car: left" : "Машина: влево";
                case GameAction.CarRight: return english ? "Car: right" : "Машина: вправо";
                case GameAction.Handbrake: return english ? "Handbrake" : "Ручной тормоз";
                case GameAction.CarLights: return english ? "Car: lights" : "Машина: фары";
                case GameAction.TerminalExit: return english ? "Exit terminal" : "Выход из терминала";
                case GameAction.Pause: return english ? "Pause" : "Пауза";
                case GameAction.Slot1: return english ? "Slot 1" : "Слот 1";
                case GameAction.Slot2: return english ? "Slot 2" : "Слот 2";
                case GameAction.Slot3: return english ? "Slot 3" : "Слот 3";
                case GameAction.Slot4: return english ? "Slot 4" : "Слот 4";
                case GameAction.Slot5: return english ? "Slot 5" : "Слот 5";
                default: return action.ToString();
            }
        }

        /// <summary>Человекочитаемое имя текущей привязки для кнопки в настройках.</summary>
        public static string GetDisplayBinding(GameAction action)
        {
            return PrettyPrint(GetPath(action));
        }

        public static string PrettyPrint(string path)
        {
            if (string.IsNullOrEmpty(path)) return "—";

            int slash = path.LastIndexOf('/');
            string leaf = slash >= 0 ? path.Substring(slash + 1) : path;
            bool mouse = path.IndexOf("<Mouse>", StringComparison.OrdinalIgnoreCase) >= 0;

            switch (leaf)
            {
                case "leftButton": return Localization.Keys("ЛКМ", "LMB");
                case "rightButton": return Localization.Keys("ПКМ", "RMB");
                case "middleButton": return Localization.Keys("СКМ", "MMB");
                case "space": return Localization.Keys("Пробел", "Space");
                case "tab": return Localization.Keys("Tab", "Tab");
                case "leftShift": return Localization.Keys("Левый Shift", "Left Shift");
                case "rightShift": return Localization.Keys("Правый Shift", "Right Shift");
                case "leftCtrl": return Localization.Keys("Левый Ctrl", "Left Ctrl");
                case "rightCtrl": return Localization.Keys("Правый Ctrl", "Right Ctrl");
                case "leftAlt": return Localization.Keys("Левый Alt", "Left Alt");
                case "escape": return Localization.Keys("Esc", "Esc");
                case "upArrow": return Localization.Keys("Стрелка вверх", "Arrow Up");
                case "downArrow": return Localization.Keys("Стрелка вниз", "Arrow Down");
                case "leftArrow": return Localization.Keys("Стрелка влево", "Arrow Left");
                case "rightArrow": return Localization.Keys("Стрелка вправо", "Arrow Right");
            }

            if (mouse) return leaf;

            leaf = leaf.Replace("/", "");
            return leaf.Length == 1 ? leaf.ToUpperInvariant() : leaf;
        }
    }
}
