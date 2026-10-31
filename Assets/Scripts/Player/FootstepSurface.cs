using UnityEngine;

namespace CarGame.Player
{
    /// <summary>
    /// Явно помечает пол как поверхность для шагов. Нужен там, где под игроком
    /// лежит объект с общим именем (Cube, Floor) и определить поверхность
    /// по названию невозможно. Кладётся на пол или на его корневой объект.
    /// </summary>
    [DisallowMultipleComponent]
    public class FootstepSurface : MonoBehaviour
    {
        [Tooltip("Имя поверхности. Сравнивается без учёта регистра с полем SurfaceSteps.surface")]
        [SerializeField] private string surface = "Concrete";

        public string Surface => surface;
    }
}