namespace CarGame.Player
{
    public interface IInteractable
    {
        void Interact(PlayerInteractor interactor);
    }

    public interface ICanInteract
    {
        bool CanInteract(PlayerInteractor interactor);
    }
}
