using System.Collections.Generic;
using UnityEngine;

namespace CarGame.UI
{
    /// <summary>
    /// Простой менеджер видимости экранных меню. Когда открывается одно меню
    /// (например, управление), остальные меню на том же Canvas выключаются —
    /// не просто затемняются, а исчезают совсем. При закрытии прежнее состояние
    /// возвращается. Это убирает «кашу» из наложенных друг на друга меню.
    /// </summary>
    public static class MenuManager
    {
        private static readonly List<GameObject> hidden = new List<GameObject>();

        /// <summary>
        /// Прячет всех соседей меню <paramref name="self"/> по Canvas, кроме
        /// перечисленных в <paramref name="keep"/>. Повторный вызов сначала
        /// возвращает то, что было спрятано ранее.
        /// </summary>
        public static void HideOthers(Transform self, params GameObject[] keep)
        {
            Restore();
            if (self == null) return;

            Canvas canvas = self.GetComponentInParent<Canvas>();
            Transform root = canvas != null ? canvas.transform : self.parent;
            if (root == null) return;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                GameObject candidate = child.gameObject;
                if (candidate == self.gameObject) continue;
                if (IsAncestorOfSelf(child, self)) continue;
                if (IsKept(candidate, keep)) continue;
                if (!candidate.activeSelf) continue;

                hidden.Add(candidate);
                candidate.SetActive(false);
            }
        }

        /// <summary>Возвращает видимость всему, что прятал <see cref="HideOthers"/>.</summary>
        public static void Restore()
        {
            for (int i = 0; i < hidden.Count; i++)
            {
                if (hidden[i] != null) hidden[i].SetActive(true);
            }

            hidden.Clear();
        }

        private static bool IsKept(GameObject candidate, GameObject[] keep)
        {
            if (keep == null) return false;

            for (int i = 0; i < keep.Length; i++)
                if (keep[i] == candidate) return true;

            return false;
        }

        private static bool IsAncestorOfSelf(Transform candidate, Transform self)
        {
            for (Transform t = self; t != null; t = t.parent)
                if (t == candidate) return true;

            return false;
        }
    }
}
