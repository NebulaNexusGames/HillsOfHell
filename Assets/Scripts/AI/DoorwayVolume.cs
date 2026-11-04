using System.Collections.Generic;
using CarGame.Buildings;
using CarGame.Player;
using CarGame.Vehicle;
using UnityEngine;

namespace CarGame.AI
{
    /// <summary>
    /// Зона дверного проёма для ИИ мобов. Вешается на дочерний ПУСТОЙ объект
    /// с BoxCollider, помеченным как Is Trigger (без Rigidbody), ровно на сам
    /// проём: коробка должна целиком перекрывать проход по ширине и высоте и
    /// немного выступать с обеих сторон стены (25-50 см), чтобы робот успел
    /// затаргетиться до входа.
    ///
    /// Что даёт мобам (через EnemyBase):
    /// 1. Проходимость: стена вокруг проёма считается препятствием только пока
    ///    мобиль НЕ смотрит в этот проём. Как только ближайший проём найден —
    ///    «стена» с ним перестаёт мешать свипам, сканированию направлений и
    ///    проверкам застревания, и любой наземный моб идёт через дверь прямо,
    ///    а не скользит вдоль косяка.
    /// 2. Целеуказание: ResolveDestination() у всех мобов целится в центр
    ///    проёма вместо точки цели за стеной — маршрут всегда через дверь.
    /// 3. Двери: при приближении моба BuildingDoor открывается автоматически
    ///    (если рядом нет игрока — приоритет у игрока). Мобы не ломают двери
    ///    телом и не застревают о закрытое полотно.
    /// 4. Зрение: линия взгляда сквозь ОТКРЫТЫЙ проём не блокируется его же
    ///    стеной/полотном — иначе моб «терял» игрока в момент входа в дверь.
    /// 5. Дрон: умеет снижаться к порогу прохода и вспарывать потолок проёма
    ///    (см. TryDronePassClearance в RobotDrone) — если высота потолка ниже
    ///    droneMinCeiling, дрон не пытается лететь через проём.
    ///
    /// Настройка в редакторе (для дизайнера):
    /// - Создать дочерний объект "Doorway" у двери/проёма, добавить Box Collider,
    ///   включить Is Trigger, повесить этот скрипт.
    /// - PassAxis — локальная ось тонкой стенки (обычно X или Z).
    /// - CeilingOverride — только если реальный потолок проёма ниже триггер-коробки
    ///   (например, посреди проёма нависает козырёк); обычно оставить 0 = авто.
    /// - Door Reference заполняется сам из родителей (BuildingDoor), можно
    ///   указать вручную.
    /// </summary>
    [DisallowMultipleComponent]
    public class DoorwayVolume : MonoBehaviour
    {
        public enum PassAxis
        {
            [InspectorName("Локальная X")] LocalX = 0,
            [InspectorName("Локальная Y")] LocalY = 1,
            [InspectorName("Локальная Z")] LocalZ = 2
        }

        [Header("Проход")]
        [Tooltip("Ось прохода — локальная ось тонкой стороны коробки (нормаль стены в проёме). Стену вокруг проёма мобы считают препятствием, пока не смотрят в эту зону")]
        [SerializeField] private PassAxis passAxis = PassAxis.LocalX;
        [Tooltip("Запас по горизонтали прохода, м. Прибавляется к полуширине зоны при проверке габаритов робота. Чуть больше 0 — терпимее к криво выставленной коробке")]
        [SerializeField] private float horizontalMargin = 0.1f;
        [Tooltip("Допуск по высоте порога, м. Робот считается способным переступить порог двери, если он выше его шага (stepUpHeight) не более чем на это значение")]
        [SerializeField] private float thresholdTolerance = 0.1f;

        [Header("Потолок проёма (для дрона)")]
        [Tooltip("Реальная высота свода проёма над полом зоны, м. 0 = авто: берётся верх триггер-коробки. Задавай вручную, если посреди проёма нависает козырёк/балка ниже коробки")]
        [SerializeField] private float ceilingOverride = 0f;
        [Tooltip("Минимальная высота свода, которую способен пройти дрон, м. Потолок ниже — дрон не пытается лететь через проём (разворачивается наружу)")]
        [SerializeField] private float droneMinCeiling = 0.9f;
        [Tooltip("Запас дрона под сводом проёма, м: целевая точка снижения = свод минус корпус дрона минус этот запас")]
        [SerializeField] private float droneCeilingClearance = 0.3f;

        [Header("Дверь")]
        [Tooltip("Полотно двери, принадлежащее этому проёму. Если пусто — ищется BuildingDoor среди родителей автоматически")]
        [SerializeField] private BuildingDoor doorReference;
        [Tooltip("Открывать дверь при приближении моба. Выключи для открытых арок без полотна")]
        [SerializeField] private bool openForEnemies = true;
        [Tooltip("Радиус, с которого моб открывает дверь, м")]
        [SerializeField] private float triggerRange = 6f;

        /// <summary>Все проёмы сцены (регистрация Awake/OnDestroy).</summary>
        public static IReadOnlyList<DoorwayVolume> All => volumes;

        private static readonly List<DoorwayVolume> volumes = new List<DoorwayVolume>();

        private BoxCollider zone;
        private Vector3 localHalf;
        private int axisIndex;
        private BuildingDoor door;
        private Transform doorVisual;
        private float nextDoorCheckTime;

        private void Awake()
        {
            zone = GetComponent<BoxCollider>();
            if (zone == null)
                zone = GetComponentInChildren<BoxCollider>();

            if (zone == null)
            {
                Debug.LogError($"[DoorwayVolume] '{name}': нет Box Collider — зона прохода не работает.", this);
                return;
            }

            // Триггер обязателен: физика роботов не должна отталкиваться от зоны.
            zone.isTrigger = true;

            axisIndex = (int)passAxis;
            Vector3 scale = transform.lossyScale;
            Vector3 size = zone.size;
            localHalf = new Vector3(
                Mathf.Abs(size.x * scale.x) * 0.5f,
                Mathf.Abs(size.y * scale.y) * 0.5f,
                Mathf.Abs(size.z * scale.z) * 0.5f);

            door = doorReference != null ? doorReference : GetComponentInParent<BuildingDoor>();
            doorVisual = door != null ? door.transform : null;

            volumes.Add(this);
        }

        private void OnDestroy()
        {
            volumes.Remove(this);
        }

        /// <summary>Центр проёма — точка, в которую мобы целятся при обходе стены.</summary>
        public Vector3 Center => zone != null ? zone.bounds.center : transform.position;

        /// <summary>Горизонтальная полуось зоны, перпендикулярная оси прохода
        /// (это и есть половина ширины самого проёма). Третья горизонтальная
        /// полуось — «толщина» коробки вдоль стены.</summary>
        private float HorizontalPerpHalf
        {
            get
            {
                // Ось Y всегда вертикальна и не участвует. Из X/Z выбираем ту,
                // которая НЕ является осью прохода.
                if (axisIndex == 0) return localHalf.z;
                if (axisIndex == 2) return localHalf.x;
                return Mathf.Max(localHalf.x, localHalf.z);
            }
        }

        /// <summary>Горизонтальная полуось зоны ВДОЛЬ стены (толщина коробки
        /// прохода с запасом по обе стороны от стены).</summary>
        private float HorizontalAlongWallHalf
        {
            get
            {
                if (axisIndex == 0) return localHalf.x;
                if (axisIndex == 2) return localHalf.z;
                return Mathf.Min(localHalf.x, localHalf.z);
            }
        }

        /// <summary>Ширина прохода, м (удвоенная полуось, перпендикулярная стене).</summary>
        public float PassageWidth => HorizontalPerpHalf * 2f;

        /// <summary>Высота свода проёма над полом зоны, м.</summary>
        public float CeilingHeight
        {
            get
            {
                if (ceilingOverride > 0.01f) return ceilingOverride;
                if (zone == null) return 0f;
                return zone.bounds.max.y - zone.bounds.min.y;
            }
        }

        public float FloorY => zone != null ? zone.bounds.min.y : transform.position.y;

        /// <summary>Мировое направление оси прохода (нормаль стены в проёме).</summary>
        public Vector3 PassDirection
        {
            get
            {
                Vector3 local = axisIndex switch
                {
                    0 => Vector3.right,
                    1 => Vector3.up,
                    _ => Vector3.forward
                };
                return transform.TransformDirection(local).normalized;
            }
        }

        /// <summary>Полуширина проёма вдоль стены (для проверки «цель внутри
        /// створа проёма», а не сбоку за косяком).</summary>
        public float HalfWidthAlongWall => HorizontalPerpHalf;

        /// <summary>Центр проёма на уровне пола (порог/точка входа наземных мобов).</summary>
        public Vector3 GroundCenter => new Vector3(Center.x, FloorY, Center.z);

        /// <summary>Коллайдер-коробка зоны проёма (для расчётов габаритов ИИ).</summary>
        public Collider ZoneCollider => zone;

        /// <summary>Минимальная высота свода, проходимая дроном, м.</summary>
        public float DroneMinCeiling => droneMinCeiling;

        /// <summary>Запас дрона под сводом, м.</summary>
        public float DroneCeilingClearance => droneCeilingClearance;

        /// <summary>
        /// Целевая высота полёта (мировая Y) для дрона с данным диаметром и
        /// высотой корпуса, проходящего под сводом этого проёма. Считается от
        /// нижней границы зоны (пол): центр корпуса ставится так, чтобы верх
        /// корпуса не доставал до свода на droneCeilingClearance. Если свод
        /// ниже droneMinCeiling — проход для дрона закрыт и метод вернёт false.
        /// </summary>
        public bool TryGetDronePassHeight(float robotDiameter, float robotHeight, out float passY)
        {
            passY = 0f;

            float ceiling = CeilingHeight;
            if (ceiling <= 0.01f) return false;
            if (ceiling < droneMinCeiling) return false;

            float halfBody = Mathf.Max(0.05f, robotHeight * 0.5f);
            float maxY = zone != null ? zone.bounds.max.y : Center.y + ceiling * 0.5f;

            // Верх корпуса под сводом с запасом; низ корпуса — не ниже пола зоны.
            passY = maxY - halfBody - Mathf.Max(0f, droneCeilingClearance);
            float floorY = FloorY + halfBody + 0.05f;
            if (passY < floorY) passY = floorY;

            // Проход физически невозможен: корпус не влезает между полом и сводом.
            if (maxY - floorY - halfBody < droneCeilingClearance * 0.5f) return false;

            return true;
        }

        /// <summary>
        /// Высота дрона над землёй, при которой корпус проходит сквозь СВОД
        /// этого проёма (центр корпуса = свод минус половина корпуса минус
        /// запас). Используется, когда цель ЛЕТИТ выше проёма (например, игрок
        /// на крыше или сам дрон-преследователь) — тогда вместо снижения под
        /// свод дрон вспарывает крышу здания на этой высоте. Возвращает false,
        /// если свода нет (зона не настроена).
        /// </summary>
        public bool TryGetDroneAboveCeilingHeight(float robotHeight, out float passY)
        {
            passY = 0f;
            if (zone == null) return false;

            float ceilingTop = zone.bounds.max.y;
            float halfBody = Mathf.Max(0.05f, robotHeight * 0.5f);
            passY = ceilingTop + halfBody + Mathf.Max(0f, droneCeilingClearance);
            return true;
        }

        /// <summary>
        /// Над какой высотой надо лететь, чтобы ПРОЙТИ СКВОЗЬ этот проём
        /// (между полом зоны и её сводом), для дрона с данной высотой корпуса.
        /// Отличие от TryGetDronePassHeight: здесь нет проверки droneMinCeiling
        /// — метод отвечает на вопрос «где дыра», а не «стоит ли туда лезть».
        /// Если дыры нет (корпус не влезает между полом и сводом) — false.
        /// </summary>
        public bool TryGetDroneThroughHeight(float robotHeight, out float passY)
        {
            passY = 0f;
            if (zone == null) return false;

            float floorY = FloorY;
            float ceilingTop = zone.bounds.max.y;
            float halfBody = Mathf.Max(0.05f, robotHeight * 0.5f);

            // Есть ли вообще щель между полом и сводом для корпуса?
            if (ceilingTop - floorY <= robotHeight + 0.1f) return false;

            // Центр: посередине щели, но не ниже пола+корпус и не выше свода-корпус.
            float middle = (floorY + ceilingTop) * 0.5f;
            float lowestAllowed = floorY + halfBody + 0.05f;
            float highestAllowed = ceilingTop - halfBody - Mathf.Max(0f, droneCeilingClearance);
            if (lowestAllowed > highestAllowed) return false;

            passY = Mathf.Clamp(middle, lowestAllowed, highestAllowed);
            return true;
        }

        /// <summary>Есть ли у проёма полотно двери.</summary>
        public bool HasDoor => door != null;

        /// <summary>Полотно двери этого проёма (null — открытая арка).</summary>
        public BuildingDoor Door => door;

        /// <summary>Открыто ли полотно (или полотна вовсе нет — открытая арка).</summary>
        public bool IsOpenOrAbsent => door == null || !door.IsClosed();

        /// <summary>
        /// Проём сейчас «для наземных мобов» закрыт чем-то, что нельзя открыть
        /// штатно: locked-дверь, заклинившее полотно или зона выставлена так,
        /// что прохода нет. EnemyBase использует это как сигнал включить
        /// игнорирование мешающих коллайдеров («продавливание») — иначе моб
        /// вечно скользил бы вдоль косяка.
        /// </summary>
        public bool IsEffectivelyBlockedForGround => door != null && door.IsClosed();

        /// <summary>
        /// Проверка геометрии прохода для конкретного моба: достаточно ли ширины
        /// и высоты, и может ли моб переступить порог.
        /// </summary>
        public bool FitsRobot(float robotDiameter, float robotHeight, float robotStepUp, bool isFlying)
        {
            if (zone == null) return false;

            if (PassageWidth <= 0.01f) return false;
            if (robotDiameter > PassageWidth + horizontalMargin * 2f) return false;

            // Летающему мобу нужен только свод; наземному — ещё и переступаемый порог.
            if (!isFlying && zone.bounds.min.y - robotStepUp > FloorY + thresholdTolerance + 0.001f)
                return false;

            float clearanceNeeded = isFlying
                ? Mathf.Max(droneMinCeiling, robotHeight + droneCeilingClearance)
                : robotHeight;
            if (CeilingHeight < clearanceNeeded) return false;

            return true;
        }

        /// <summary>
        /// Является ли коллайдер «стеной этого проёма»: принадлежит ли он тому же
        /// объекту/иерархии, что и зона (родитель/потомок), либо это полотно
        /// связанной двери. Такие коллайдеры EnemyBase перестаёт считать
        /// препятствиями, когда моб смотрит в данный проём, и не считает
        /// блокерами зрения сквозь него.
        /// </summary>
        public bool BelongsToStructure(Collider candidate)
        {
            if (candidate == null) return false;
            // Сама триггер-зона запросами с QueryTriggerInteraction.Ignore не находится.
            if (zone != null && candidate == zone) return false;

            Transform t = candidate.transform;
            Transform self = transform;

            if (t == self || t.IsChildOf(self)) return true;
            if (self.IsChildOf(t)) return true;

            // Полотно двери относится к проёму всегда (оно и есть преграда в закрытом виде).
            if (doorVisual != null && (t == doorVisual || t.IsChildOf(doorVisual))) return true;

            return false;
        }

        /// <summary>
        /// Попытка открыть дверь проёма ради моба. Вызывается из EnemyBase при
        /// таргете проёма. Условие: полотно есть, закрыто, никто из игроков не
        /// рядом (приоритет взаимодействия с дверью у игрока).
        /// </summary>
        public void TryOpenForEnemy(Vector3 robotPosition)
        {
            if (!openForEnemies || door == null) return;
            if ((Center - robotPosition).sqrMagnitude > triggerRange * triggerRange) return;
            if (!door.IsClosed()) return;

            // Не выдёргивать дверь из-под игрока: проверка раз в 0.5 с, не каждый кадр.
            if (Time.time < nextDoorCheckTime) return;
            nextDoorCheckTime = Time.time + 0.5f;

            // Игрок рядом — штатное открытие откладывается: приоритет у
            // взаимодействия игрока. Но если моб уже упёрся в полотно телом
            // (contact), дверь ВЫЛОМЫВАЕТСЯ в любую сторону от него — иначе
            // запертая изнутри дверь делала убежище недосягаемым.
            bool playerNear = IsAnyPlayerNearby();
            if (!playerNear || contact)
            {
                door.Open();
                if (contact) door.KickFrom(contact);
            }

            // Дверь упорно не открывается (locked-запрет, заклин): после
            // нескольких безрезультатных попыток моб выбивает её физически —
            // полотно становится полностью свободным телом.
            if (door.IsClosed())
            {
                blockedAttempts++;
                if (blockedAttempts >= BreakAfterFailedOpens && contact)
                {
                    door.BreakOpen(contact);
                    blockedAttempts = 0;
                }
            }
            else
            {
                blockedAttempts = 0;
            }
        }

        /// <summary>Число подряд безрезультатных попыток открыть дверь мобом
        /// (дверь осталась закрытой) — порог на выламывание физикой.</summary>
        private int blockedAttempts;
        private const int BreakAfterFailedOpens = 3;

        private bool IsAnyPlayerNearby()
        {
            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                PlayerController p = players[i];
                if (p == null || p.Equals(null)) continue;

                Vector3 pos = p.transform.position;
                if ((pos - Center).sqrMagnitude <= triggerRange * triggerRange) return true;

                // Игрок за рулём: считаем и позицию машины.
                CarController car = p.CurrentVehicle;
                if (car != null && !car.Equals(null)
                    && (car.transform.position - Center).sqrMagnitude <= triggerRange * triggerRange)
                    return true;
            }

            return false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            BoxCollider box = zone != null ? zone : GetComponent<BoxCollider>();
            if (box == null) return;

            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireCube(box.bounds.center, box.bounds.size);

            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);
            Gizmos.DrawCube(box.bounds.center, box.bounds.size);

            // Ось прохода: стрелка через проём.
            Vector3 dir = transform.TransformDirection(axisIndex switch
            {
                0 => Vector3.right,
                1 => Vector3.up,
                _ => Vector3.forward
            });
            Gizmos.DrawLine(box.bounds.center - dir, box.bounds.center + dir);
        }
#endif
    }
}
