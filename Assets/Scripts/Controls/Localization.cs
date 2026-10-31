using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarGame.Controls
{
    public enum GameLanguage
    {
        Russian = 0,
        English = 1
    }

    /// <summary>
    /// Язык интерфейса. Тексты задаются парами «русский/английский» прямо в
    /// месте использования: для постоянных надписей есть <see cref="LocalizedText"/>,
    /// для строк, которые перестраиваются каждый раз (терминалы, названия
    /// предметов), — <see cref="Keys"/>.
    ///
    /// Выбор хранится в PlayerPrefs, поэтому язык помнится между запусками.
    ///
    /// Терминалы и каталог хранят английский текст как базовый (Так их писали
    /// редакторские ассеты), поэтому в RU-режиме они переводятся через
    /// <see cref="ToCurrent"/> по таблице <see cref="RegisterRu"/>.
    /// </summary>
    public static class Localization
    {
        private const string PrefsKey = "settings.language";

        /// <summary>Таблица «английская строка → русская». Заполняется через <see cref="RegisterRu"/>.</summary>
        private static readonly Dictionary<string, string> EnglishToRussian =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static GameLanguage current = GameLanguage.Russian;
        private static bool loaded;

        public static event Action Changed;

        public static GameLanguage Language
        {
            get { EnsureLoaded(); return current; }
        }

        public static bool IsEnglish => Language == GameLanguage.English;
        public static bool IsRussian => Language == GameLanguage.Russian;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;

            string stored = PlayerPrefs.GetString(PrefsKey, GameLanguage.Russian.ToString());
            current = string.Equals(stored, GameLanguage.English.ToString(), StringComparison.OrdinalIgnoreCase)
                ? GameLanguage.English
                : GameLanguage.Russian;
            loaded = true;
        }

        /// <summary>
        /// Регистрирует перевод английского базового текста (терминалы, автомат)
        /// на русский. Английский всегда показывается как есть — таблица нужна
        /// только для русского режима.
        /// </summary>
        public static void RegisterRu(string english, string russian)
        {
            if (string.IsNullOrEmpty(english)) return;
            EnglishToRussian[english] = russian ?? english;
        }

        /// <summary>
        /// Возвращает текст на текущем языке: базовый английский —
        /// как есть, в русском режиме — по таблице <see cref="RegisterRu"/>.
        /// </summary>
        public static string ToCurrent(string text)
        {
            if (string.IsNullOrEmpty(text) || IsEnglish) return text;

            string russian;
            return EnglishToRussian.TryGetValue(text, out russian) ? russian : text;
        }

        public static void SetLanguage(GameLanguage language)
        {
            EnsureLoaded();
            if (current == language) return;

            current = language;
            PlayerPrefs.SetString(PrefsKey, current.ToString());
            PlayerPrefs.Save();

            Changed?.Invoke();
        }

        public static void Toggle()
        {
            SetLanguage(IsEnglish ? GameLanguage.Russian : GameLanguage.English);
        }

        /// <summary>Выбирает строку по текущему языку.</summary>
        public static string Pick(string russian, string english)
        {
            return IsEnglish ? english : russian;
        }

        /// <summary>Короткий алиас для <see cref="Pick"/>.</summary>
        public static string Keys(string russian, string english)
        {
            return IsEnglish ? english : russian;
        }
    }
}
