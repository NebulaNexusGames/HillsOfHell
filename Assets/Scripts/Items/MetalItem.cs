using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    public enum MetalType
    {
        [InspectorName("Scrap")] Scrap = 0,
        [InspectorName("Metal")] Metal = 1,
        [InspectorName("Platinum")] Platinum = 2
    }

    public static class MetalInfo
    {
        private static readonly float[] RepairPerUnit = { 10f, 25f, 60f };
        private static readonly string[] DisplayNames = { "Scrap", "Metal", "Platinum" };
        private static readonly string[] RussianNames = { "Лом", "Металл", "Платина" };

        public static float Repair(MetalType type)
        {
            return RepairPerUnit[Index(type)];
        }

        public static string Name(MetalType type)
        {
            return DisplayNames[Index(type)];
        }

        public static string RussianName(MetalType type)
        {
            return RussianNames[Index(type)];
        }

        public static MetalType Best(MetalType first, MetalType second)
        {
            return Repair(second) > Repair(first) ? second : first;
        }

        private static int Index(MetalType type)
        {
            return Mathf.Clamp((int)type, 0, RepairPerUnit.Length - 1);
        }
    }

    [DisallowMultipleComponent]
    public class MetalItem : MonoBehaviour
    {
        [Tooltip("Тип металла, который чинит машину")]
        [SerializeField] private MetalType type = MetalType.Scrap;
        [Tooltip("Переопределить, сколько хп чинит одна штука. 0 — берётся значение типа")]
        [SerializeField] private float repairOverride = 0f;

        public MetalType Type => type;
        public float RepairAmount => repairOverride > 0.01f ? repairOverride : MetalInfo.Repair(type);

        private void Reset()
        {
            PickupItem item = GetComponent<PickupItem>();
            if (item == null) return;
            item.SetItemId(type.ToString().ToLowerInvariant());
        }

        public void Configure(MetalType metalType, float repair = 0f)
        {
            type = metalType;
            repairOverride = repair;
        }

        public static MetalItem SpawnScrap(Vector3 position, Quaternion rotation, float scale = 0.22f)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Scrap_{MetalType.Scrap}";
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = Vector3.one * scale;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = new Color(0.45f, 0.42f, 0.38f);

            PickupItem pickup = go.AddComponent<PickupItem>();
            pickup.SetItemId(MetalType.Scrap.ToString().ToLowerInvariant());

            return go.AddComponent<MetalItem>();
        }
    }
}
