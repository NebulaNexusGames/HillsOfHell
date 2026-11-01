using CarGame.Player;
using UnityEngine;

namespace CarGame.Buildings
{
    /// <summary>
    /// Ручка двери здания. Точка взаимодействия (как точка заправки/входа).
    /// Нажатие открывает или закрывает дверь.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BuildingDoorHandle : MonoBehaviour, IInteractable, ICanInteract
    {
        [SerializeField] private BuildingDoor door;

        private void Awake()
        {
            if (door == null) door = GetComponentInParent<BuildingDoor>();
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return door != null;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (door == null) return;
            door.Toggle();
        }
    }
}
