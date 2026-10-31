using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Пассивный вес тюнинг-предмета в руке: пока тяжёлая деталь лежит в руке,
    /// игрок идёт медленнее. Компонент живёт на варианте руки, который включён
    /// только пока предмет в руке, поэтому замедление действует ровно на это время.
    ///
    /// Намеренно не реализует <see cref="IHandUsable"/>: единственный
    /// «применяемый» компонент на варианте — <see cref="BuildItem"/>, иначе рука
    /// выбрала бы из двух IHandUsable произвольный и ЛКМ/ПКМ могли уйти не туда.
    /// </summary>
    [DisallowMultipleComponent]
    public class TuningItem : MonoBehaviour
    {
        [Header("Вес")]
        [Tooltip("Насколько каждый килограмм веса замедляет игрока (доля скорости на кг)")]
        [SerializeField] private float slowPerKg = 0.04f;
        [Range(0.1f, 1f)]
        [Tooltip("Нижняя граница множителя скорости")]
        [SerializeField] private float minMultiplier = 0.5f;
        [Tooltip("Длительность импульса замедления, обновляемого каждый кадр, с")]
        [SerializeField] private float refreshDuration = 0.25f;

        private PlayerHand hand;
        private PlayerController player;

        private void Awake()
        {
            ResolveRefs();
        }

        private void OnEnable()
        {
            ResolveRefs();
        }

        private void Update()
        {
            if (player == null || !player.isActiveAndEnabled) return;

            ItemDef def = ResolveDef();
            if (def == null) return;

            float multiplier = Mathf.Clamp(1f - def.Weight * slowPerKg, minMultiplier, 1f);
            if (multiplier >= 0.999f) return;

            // Короткий импульс, обновляемый каждый кадр: когда предмет уберут
            // из руки, эффект сам растает, а не залипнет на долгую длительность.
            player.ApplySpeedBoost(multiplier, Mathf.Max(0.05f, refreshDuration));
        }

        private ItemDef ResolveDef()
        {
            PlayerHand source = hand != null ? hand : GetComponentInParent<PlayerHand>();
            if (source == null) return null;
            return source.ResolveHeldDef();
        }

        private void ResolveRefs()
        {
            if (hand == null) hand = GetComponentInParent<PlayerHand>();
            if (player == null) player = GetComponentInParent<PlayerController>();
        }
    }
}
