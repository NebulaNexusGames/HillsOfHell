using UnityEngine;

namespace CarGame.Effects
{
    /// <summary>
    /// Глобальное состояние кровавой луны. Цикл дня и ночи выставляет его при
    /// наступлении ночи полнолуния, а мобы и игрок читают множители, чтобы
    /// применить баффы/урон. Статика, а не ссылка на сцену, чтобы моб мог
    /// спросить состояние в своём Awake, не отыскивая DayNightCycle.
    ///
    /// Все множители равны 1 (нейтральное значение), когда событие неактивно.
    /// </summary>
    public static class BloodMoonState
    {
        /// <summary>Активна ли кровавая луна прямо сейчас.</summary>
        public static bool IsActive;

        /// <summary>Множитель скорости враждебных мобов.</summary>
        public static float EnemySpeedMultiplier = 1f;

        /// <summary>Множитель дальности зрения враждебных мобов.</summary>
        public static float EnemyVisionMultiplier = 1f;

        /// <summary>Множитель здоровья враждебных мобов.</summary>
        public static float EnemyHealthMultiplier = 1f;

        /// <summary>Множитель урона враждебных мобов.</summary>
        public static float EnemyDamageMultiplier = 1f;

        /// <summary>Урон игроку в секунду, пока он смотрит на кровавую луну.</summary>
        public static float PlayerLookDamagePerSecond;

        /// <summary>Нейтральные значения. Вызывается при старте сцены и когда событие гаснет.</summary>
        public static void Reset()
        {
            IsActive = false;
            EnemySpeedMultiplier = 1f;
            EnemyVisionMultiplier = 1f;
            EnemyHealthMultiplier = 1f;
            EnemyDamageMultiplier = 1f;
            PlayerLookDamagePerSecond = 0f;
        }

        /// <summary>
        /// Статика живёт между сценами, поэтому при загрузке любой сцены
        /// сбрасываем её: в меню кровавой луны быть не должно.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ResetOnSceneLoad()
        {
            Reset();
        }
    }
}
