using CarGame.Vehicle;
using UnityEngine;

namespace CarGame.Combat
{
    /// <summary>
    /// Щит-стена. Урон, попавший в его коллайдер, гасится щитом (через
    /// <see cref="Health"/>, см. Hitscan/Blueprint.ResolveHealth) и не доходит
    /// до того, что за щитом.
    ///
    /// Щит ставится и на машину (как тюнинг), и на пол как отдельная стена.
    /// При этом колёса машин сквозь щит проезжают: физически он их не держит,
    /// хотя урон и пули ловит.
    /// </summary>
    [DisallowMultipleComponent]
    public class Shield : MonoBehaviour
    {
        [Tooltip("Коллайдеры щита. Пусто — берутся все коллайдеры этого объекта и детей")]
        [SerializeField] private Collider[] shieldColliders;
        [Tooltip("Колёса машин игнорируют щит")]
        [SerializeField] private bool ignoreCarWheels = true;
        [Tooltip("Кузов машины тоже игнорирует щит. Включи, если щит стоит вплотную к кузову")]
        [SerializeField] private bool ignoreCarBody;

        private void OnEnable() => Apply();
        private void Start() => Apply();

        /// <summary>Пересобрать игнор столкновений. Вызывается сам при включении объекта.</summary>
        public void Apply()
        {
            Collider[] walls = ResolveColliders();
            if (walls == null || walls.Length == 0) return;

            CarController[] cars = FindObjectsByType<CarController>(FindObjectsSortMode.None);
            for (int i = 0; i < cars.Length; i++)
            {
                CarController car = cars[i];
                if (car == null) continue;

                if (ignoreCarWheels)
                {
                    WheelCollider[] wheels = car.GetComponentsInChildren<WheelCollider>(true);
                    for (int w = 0; w < wheels.Length; w++)
                    {
                        if (wheels[w] == null) continue;
                        for (int c = 0; c < walls.Length; c++)
                        {
                            if (walls[c] == null) continue;
                            Physics.IgnoreCollision(wheels[w], walls[c], true);
                        }
                    }
                }

                if (!ignoreCarBody) continue;

                Collider[] bodies = car.BodyColliders;
                if (bodies == null) continue;

                for (int b = 0; b < bodies.Length; b++)
                {
                    Collider body = bodies[b];
                    if (body == null) continue;

                    for (int c = 0; c < walls.Length; c++)
                    {
                        if (walls[c] == null || walls[c] == body) continue;
                        Physics.IgnoreCollision(body, walls[c], true);
                    }
                }
            }
        }

        private Collider[] ResolveColliders()
        {
            if (shieldColliders != null && shieldColliders.Length > 0) return shieldColliders;
            return GetComponentsInChildren<Collider>(true);
        }
    }
}
