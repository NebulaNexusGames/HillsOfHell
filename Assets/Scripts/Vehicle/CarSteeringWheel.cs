using UnityEngine;

namespace CarGame.Vehicle
{
    /// <summary>
    /// Руль. Живёт на корне машины и вращает настоящий объект руля в сторону
    /// поворота. Угол берётся из реального угла поворота колёс, поэтому руль
    /// совпадает с тем, куда машина едет.
    /// </summary>
    [DisallowMultipleComponent]
    public class CarSteeringWheel : MonoBehaviour
    {
        [Header("Что вращать")]
        [Tooltip("Объект руля модели. Если пусто — вращается собственный трансформ компонента")]
        [SerializeField] private Transform wheel;
        [Tooltip("Локальная ось, вокруг которой вращается руль. У лежащего цилиндра это локальная Z")]
        [SerializeField] private Vector3 spinAxis = Vector3.forward;

        [Header("Угол")]
        [Tooltip("На сколько градусов руль поворачивается при полном вывороте колёс")]
        [SerializeField] private float maxAngle = 400f;
        [Tooltip("Максимальный угол поворота колёс, градусы. Должен совпадать с CarController.maxSteerAngle")]
        [SerializeField] private float maxSteerAngle = 30f;
        [Tooltip("Насколько быстро руль догоняет поворот, с")]
        [SerializeField] private float followSpeed = 9f;

        private CarController vehicle;
        private Quaternion baseRotation;
        private float angle;

        private void Awake()
        {
            if (wheel == null) wheel = transform;

            // Руль в модели уже повёрнут (на 135 градусов), поэтому запоминаем
            // исходную ориентацию и домножаем на неё, а не затираем её.
            baseRotation = wheel.localRotation;

            vehicle = GetComponentInParent<CarController>();
        }

        private void Update()
        {
            if (vehicle == null) return;

            float steerAngle = vehicle.SteerInput * maxSteerAngle;
            float target = maxAngle <= 0.0001f ? 0f : steerAngle / maxSteerAngle * maxAngle;

            angle = Mathf.Lerp(angle, target, 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));

            wheel.localRotation = baseRotation * Quaternion.AngleAxis(angle, spinAxis.normalized);
        }

        private void OnValidate()
        {
            spinAxis = spinAxis.sqrMagnitude < 0.0001f ? Vector3.forward : spinAxis.normalized;
            maxSteerAngle = Mathf.Max(0.01f, maxSteerAngle);
        }
    }
}
