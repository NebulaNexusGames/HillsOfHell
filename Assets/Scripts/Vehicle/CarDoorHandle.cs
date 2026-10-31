using CarGame.Player;
using UnityEngine;

namespace CarGame.Vehicle
{
    /// <summary>
    /// Ручка двери. Точка взаимодействия, как точка заправки/входа.
    /// При нажатии открывает дверь, отталкивая её от игрока.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CarDoorHandle : MonoBehaviour, IInteractable, ICanInteract
    {
        [SerializeField] private CarDoor door;
        [Tooltip("Если пусто — ищется в родителях")]
        [SerializeField] private PlayerController player;

        private void Awake()
        {
            if (door == null) door = GetComponentInParent<CarDoor>();
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (door == null) return false;
            if (player == null && interactor != null)
            {
                player = interactor.GetComponentInParent<PlayerController>();
            }
            if (player == null) return false;
            // Можно дернуть ручку только когда дверь закрыта/наполовину закрыта
            // или просто всегда — по требованию: "если игрок нажимает на ручку
            // двери дверь начинает подчиняться физике..."
            return true;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (door == null) return;
            if (player == null && interactor != null)
            {
                player = interactor.GetComponentInParent<PlayerController>();
            }
            Vector3 position = player != null ? player.transform.position : (interactor != null ? interactor.transform.position : transform.position);
            door.PushFrom(position);
        }
    }
}
