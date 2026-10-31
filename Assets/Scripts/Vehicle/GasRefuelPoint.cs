using CarGame.Items;
using CarGame.Player;
using UnityEngine;

namespace CarGame.Vehicle
{
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class GasRefuelPoint : MonoBehaviour, IInteractable, ICanInteract
    {
        [SerializeField] private CarController car;

        public CarController Car => car;

        private void Awake()
        {
            if (car == null) car = GetComponentInParent<CarController>();
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null || car == null) return false;
            if (car.Fuel >= car.MaxFuel - 0.0001f) return false;
            PlayerHand hand = interactor.Hand;
            if (hand == null) return false;
            // Заправка только при наведении прицела именно на эту точку.
            if (interactor.Target != this) return false;
            GasCanItem can = GetGasCan(hand);
            if (can == null || can.IsEmpty) return false;
            return true;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            PlayerHand hand = interactor.Hand;
            if (hand == null) return;
            GasCanItem can = GetGasCan(hand);
            if (can == null) return;

            can.StartRefuel(car);
            if (hand != null) hand.SetDropBlocked(true);
        }

        private GasCanItem GetGasCan(PlayerHand hand)
        {
            if (hand == null) return null;
            GameObject active = hand.ActiveHandVariant;
            if (active != null)
            {
                GasCanItem c = active.GetComponent<GasCanItem>();
                if (c == null) c = active.GetComponentInChildren<GasCanItem>();
                if (c != null) return c;
            }
            return hand.GetComponentInChildren<GasCanItem>() ?? hand.GetComponent<GasCanItem>();
        }
    }
}

