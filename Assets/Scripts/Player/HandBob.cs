using UnityEngine;

namespace CarGame.Player
{
    /// <summary>
    /// Живое покачивание предмета в руке — как в шутерах от первого лица.
    ///
    /// При ходьбе рука ходит вверх-вниз, а на повороте или боковом шаге уходит
    /// в противоположную сторону и слегка заваливается вбок. Смещения считаются
    /// поверх базовой позы точки удержания, поэтому ни сцена, ни заготовленные
    /// варианты рук не двигаются — на место всё возвращается само.
    ///
    /// Компонент вешается на пустой объект рук (holdSocket) автоматически из
    /// PlayerHand, но его можно добавить и вручную, чтобы крутить ползунки.
    /// </summary>
    [DisallowMultipleComponent]
    public class HandBob : MonoBehaviour
    {
        [Header("Покачивание при ходьбе")]
        [Tooltip("На сколько метров рука поднимается и опускается")]
        [SerializeField] private float bobAmplitude = 0.03f;
        [Tooltip("Сколько шагов в секунду на полной скорости")]
        [SerializeField] private float bobFrequency = 1.9f;
        [Tooltip("Какая доля вертикального покачивания уходит вбок")]
        [Range(0f, 1f)]
        [SerializeField] private float bobLateral = 0.5f;
        [Tooltip("До этой скорости (м/с) покачивания нет — на месте рука не дрожит")]
        [SerializeField] private float bobDeadZone = 0.4f;
        [Tooltip("Скорость (м/с), на которой покачивание достигает полной амплитуды")]
        [SerializeField] private float bobFullSpeed = 6f;
        [Tooltip("Как быстро покачивание разгоняется и затухает")]
        [SerializeField] private float bobResponse = 8f;

        [Header("Инерция")]
        [Tooltip("На сколько метров рука уводится вбок на каждый метр/с боковой скорости или поворота")]
        [SerializeField] private float swayScale = 0.012f;
        [Tooltip("Максимальное боковое смещение, м")]
        [SerializeField] private float swayMax = 0.05f;
        [Tooltip("Максимальный завал рукой вбок при повороте, градусы")]
        [SerializeField] private float swayRoll = 4f;
        [Tooltip("Как быстро инерция догоняет движение")]
        [SerializeField] private float swayResponse = 8f;

        private PlayerController player;
        private Transform basis;
        private Vector3 baseLocalPosition;
        private Quaternion baseLocalRotation;
        private float phase;
        private float lastYaw;
        private float yawRate;
        private float sway;
        private float bob;

        private void Awake()
        {
            baseLocalPosition = transform.localPosition;
            baseLocalRotation = transform.localRotation;
            player = GetComponentInParent<PlayerController>();
            basis = transform.parent != null ? transform.parent : (player != null ? player.transform : transform);
            lastYaw = basis.eulerAngles.y;
        }

        private void OnEnable()
        {
            // Возврат из скрытого состояния (в машине, у терминала) не должен
            // дёргать руку — начинаем ровно с базовой позы.
            transform.localPosition = baseLocalPosition;
            transform.localRotation = baseLocalRotation;
            phase = 0f;
            yawRate = 0f;
            sway = 0f;
            bob = 0f;
            if (basis != null) lastYaw = basis.eulerAngles.y;
        }

        private void LateUpdate()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (player == null) return;

            // Тряска камеры выключена в настройках графики — рука стоит ровно.
            if (!PlayerController.CameraMotionEnabled)
            {
                transform.localPosition = baseLocalPosition;
                transform.localRotation = baseLocalRotation;
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (basis == null) basis = transform.parent != null ? transform.parent : player.transform;

            // Боковая скорость и скорость поворота — источник инерции: рука
            // уезжает туда, откуда игрок только что ушёл.
            float yaw = basis.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(lastYaw, yaw);
            lastYaw = yaw;
            yawRate = Mathf.Lerp(yawRate, yawDelta / dt, 1f - Mathf.Exp(-swayResponse * dt));

            Vector3 velocity = player.Body != null ? player.Body.velocity : Vector3.zero;
            float lateralVelocity = Vector3.Dot(new Vector3(velocity.x, 0f, velocity.z), basis.right);

            float targetSway = Mathf.Clamp(-(lateralVelocity + yawRate * 0.15f) * swayScale, -swayMax, swayMax);
            sway = Mathf.Lerp(sway, targetSway, 1f - Mathf.Exp(-swayResponse * dt));

            // Вертикальное покачивание при ходьбе: частота фиксированная, а
            // амплитуда выходит на полную только на скорости.
            float speed = player.Speed;
            float strength = speed > bobDeadZone
                ? Mathf.Clamp01((speed - bobDeadZone) / Mathf.Max(0.01f, bobFullSpeed))
                : 0f;
            bob = Mathf.Lerp(bob, bobAmplitude * strength, 1f - Mathf.Exp(-bobResponse * dt));

            phase += bobFrequency * dt * Mathf.PI * 2f;
            if (phase > Mathf.PI * 2f) phase -= Mathf.PI * 2f;
            float wave = Mathf.Sin(phase);

            transform.localPosition = baseLocalPosition + new Vector3(sway + wave * bob * bobLateral, wave * bob, 0f);

            float roll = Mathf.Clamp(-yawRate * 0.03f, -swayRoll, swayRoll);
            transform.localRotation = baseLocalRotation * Quaternion.Euler(wave * bob * 40f, 0f, roll);
        }
    }
}
