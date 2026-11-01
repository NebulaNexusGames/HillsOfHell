using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Пиво. Полностью повторяет инжектор по применению из руки
    /// (см. <c>ConsumableItem</c>), но вместо лечения «опьяняет» игрока:
    /// камера на несколько секунд начинает качаться из стороны в сторону.
    /// </summary>
    public class BeerItem : ConsumableItem
    {
        [Header("Пиво")]
        [Tooltip("Сколько секунд качает камеру")]
        [SerializeField] private float drunkDuration = 5f;
        [Tooltip("Сила опьянения, 0..1")]
        [SerializeField, Range(0f, 1f)] private float drunkStrength = 1f;

        protected override void ApplyEffect(PlayerController player)
        {
            if (player == null)
            {
                Debug.LogWarning("[Beer] Не найден PlayerController. Пиво израсходовано.", this);
                return;
            }

            player.ApplyDrunk(drunkDuration, drunkStrength);
            Debug.Log($"[Beer] Опьянение на {drunkDuration:0.#} с.", this);
        }
    }
}