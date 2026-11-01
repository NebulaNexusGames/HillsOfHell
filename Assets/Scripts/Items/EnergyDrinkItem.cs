using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Энергетик. Полностью повторяет инжектор по применению из руки
    /// (см. <c>ConsumableItem</c>), но вместо лечения даёт игроку
    /// временное ускорение.
    /// </summary>
    public class EnergyDrinkItem : ConsumableItem
    {
        [Header("Энергетик")]
        [Tooltip("Во сколько раз ускоряется игрок")]
        [SerializeField] private float speedMultiplier = 1.6f;
        [Tooltip("Сколько секунд держится ускорение")]
        [SerializeField] private float boostDuration = 8f;

        protected override void ApplyEffect(PlayerController player)
        {
            if (player == null)
            {
                Debug.LogWarning("[EnergyDrink] Не найден PlayerController. Энергетик израсходован.", this);
                return;
            }

            player.ApplySpeedBoost(speedMultiplier, boostDuration);
            Debug.Log($"[EnergyDrink] Ускорение x{speedMultiplier:0.##} на {boostDuration:0.#} с.", this);
        }
    }
}