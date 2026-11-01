using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Показ ствола в руках игрока.
    ///
    /// От компонента осталось ровно одно: включить нужный вариант руки по ID
    /// предмета. Ничего не создаётся в runtime — модель уже лежит в руках как
    /// заготовленный вариант PlayerHand с ID предмета (ItemDefs.PistolId или
    /// ItemDefs.ShotgunId).
    ///
    /// Раньше здесь же жил разбор состояний и клипов анимации. Он переехал в
    /// WeaponItem на сам вариант ствола: клипы и таймеры должны жить вместе со
    /// стволом и останавливаться вместе с ним, а не висеть на игроке.
    /// </summary>
    [DisallowMultipleComponent]
    public class FirearmViewmodel : MonoBehaviour
    {
        private Player.PlayerHand hand;
        private GameObject activeRoot;

        public bool ShowingFirearm => activeRoot != null;

        private void Awake()
        {
            hand = GetComponent<Player.PlayerHand>();
            if (hand == null) hand = GetComponentInParent<Player.PlayerHand>();
        }

        public void Show(FirearmKind kind)
        {
            if (hand == null) return;

            bool shotgun = kind == FirearmKind.Shotgun;
            GameObject target = hand.ShowHandVariantById(shotgun ? ItemDefs.ShotgunId : ItemDefs.PistolId);

            if (target == null)
            {
                Hide();
                return;
            }

            if (activeRoot == target)
            {
                if (!target.activeSelf) target.SetActive(true);
                return;
            }

            Hide();

            activeRoot = target;
            target.SetActive(true);
        }

        public void Hide()
        {
            if (activeRoot != null && activeRoot.activeSelf) activeRoot.SetActive(false);
            activeRoot = null;
        }
    }
}
