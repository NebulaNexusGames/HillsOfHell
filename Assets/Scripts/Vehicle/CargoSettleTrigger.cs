using CarGame.Items;
using UnityEngine;

namespace CarGame.Vehicle
{
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class CargoSettleTrigger : MonoBehaviour
    {
        [Tooltip("Зона багажника, которая замораживает предмет. Если пусто — ищется в родителях. Срабатывает ТОЛЬКО когда триггер висит на Анкоре (CargoZone.cargoAnchor)")]
        [SerializeField] private CargoZone zone;

        private void Reset()
        {
            PrepareCollider();
            if (zone == null) zone = GetComponentInParent<CargoZone>();
        }

        private void Awake()
        {
            PrepareCollider();
            if (zone == null) zone = GetComponentInParent<CargoZone>();
        }

        public void Configure(CargoZone targetZone)
        {
            zone = targetZone;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (zone == null) return;
            if (zone.Anchor == null || transform != zone.Anchor) return;

            PickupItem item = other.GetComponentInParent<PickupItem>();
            if (item == null || item.IsStored) return;

            zone.Settle(item);
        }

        private void PrepareCollider()
        {
            Collider trigger = GetComponent<Collider>();
            if (trigger != null) trigger.isTrigger = true;
        }
    }
}