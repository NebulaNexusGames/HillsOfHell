using UnityEngine;

namespace CarGame.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public class RobotSwordsmanAnimator : MonoBehaviour
    {
        [Header("Animator")]
        [Tooltip("Аниматор робота-мечника. Если пусто — берётся с текущего объекта")]
        [SerializeField] private Animator animator;

        [Header("Параметры")]
        [Tooltip("Скорость движения, 0..1. Автоматически выставляется")]
        [SerializeField] private string moveSpeedParam = "MoveSpeed";
        [Tooltip("Атакует ли робот прямо сейчас")]
        [SerializeField] private string attackingParam = "Attacking";
        [Tooltip("Увидел цель (в боевом режиме)")]
        [SerializeField] private string inCombatParam = "InCombat";
        [Tooltip("Удар по игроку/цели")]
        [SerializeField] private string swingParam = "Swing";
        [Tooltip("Запуск потери цели")]
        [SerializeField] private string lostTargetParam = "LostTarget";
        [Tooltip("Смерть")]
        [SerializeField] private string deadParam = "Dead";
        [Tooltip("Начинает атаку")]
        [SerializeField] private string attackTriggerParam = "Attack";
        [Tooltip("Прерывание атаки")]
        [SerializeField] private string attackCancelParam = "CancelAttack";

        [Header("Скорости")]
        [Tooltip("Коэффициент сжатия скорости движения в параметр. Чем меньше — плавнее")]
        [SerializeField] private float moveSpeedLerp = 8f;
        [Tooltip("Значение MoveSpeed, когда стоит на месте")]
        [SerializeField] private float idleThreshold = 0.05f;

        private Vector3 lastPosition;
        private float currentSpeed;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            lastPosition = transform.position;
        }

        private void Update()
        {
            Vector3 velocity = (transform.position - lastPosition) / Time.deltaTime;
            lastPosition = transform.position;

            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            currentSpeed = Mathf.Lerp(currentSpeed, speed, Time.deltaTime * moveSpeedLerp);
            float param = currentSpeed < idleThreshold ? 0f : currentSpeed;

            SetFloat(moveSpeedParam, param);
        }

        public void SetAttacking(bool value)
        {
            SetBool(attackingParam, value);
        }

        public void SetInCombat(bool value)
        {
            SetBool(inCombatParam, value);
        }

        public void TriggerSwing()
        {
            SetTrigger(swingParam);
        }

        public void TriggerLostTarget()
        {
            SetTrigger(lostTargetParam);
        }

        public void TriggerDead()
        {
            SetTrigger(deadParam);
            SetBool(attackingParam, false);
            SetBool(inCombatParam, false);
        }

        public void TriggerAttack()
        {
            SetTrigger(attackTriggerParam);
        }

        public void TriggerAttackCancel()
        {
            SetTrigger(attackCancelParam);
        }

        private void SetBool(string name, bool value)
        {
            if (animator == null || string.IsNullOrEmpty(name)) return;
            animator.SetBool(name, value);
        }

        private void SetFloat(string name, float value)
        {
            if (animator == null || string.IsNullOrEmpty(name)) return;
            animator.SetFloat(name, value);
        }

        private void SetTrigger(string name)
        {
            if (animator == null || string.IsNullOrEmpty(name)) return;
            animator.ResetTrigger(name);
            animator.SetTrigger(name);
        }
    }
}