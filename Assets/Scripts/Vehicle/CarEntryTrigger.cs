using CarGame.Player;
using UnityEngine;

namespace CarGame.Vehicle
{
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class CarEntryTrigger : MonoBehaviour, IInteractable, ICanInteract
    {
        [Tooltip("Машина, в которую садится игрок. Если пусто — ищется в родителях")]
        [SerializeField] private CarController car = null;

        private void Reset()
        {
            PrepareCollider();
            if (car == null) car = GetComponentInParent<CarController>();
        }

        private void Awake()
        {
            PrepareCollider();
            if (car == null) car = GetComponentInParent<CarController>();
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (car != null) car.Interact(interactor);
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return car != null && car.CanInteract(interactor);
        }

        private void PrepareCollider()
        {
            Collider triggerCollider = GetComponent<Collider>();
            if (triggerCollider != null) triggerCollider.isTrigger = true;
        }
    }
}