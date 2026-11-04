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
            // Выломанная дверь — свободное физическое тело: ручка больше
            // не взаимодействует (нет смысла «открывать» то, что сорвали с петель).
            return door != null && !door.IsBroken;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (door == null || door.IsBroken) return;
            door.Toggle();
        }
    }
}
