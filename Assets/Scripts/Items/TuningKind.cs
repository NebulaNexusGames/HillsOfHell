namespace CarGame.Items
{
    /// <summary>
    /// Тип тюнинга. Чертёж (<see cref="Blueprint"/>) и предмет совпадают по типу:
    /// щит встаёт только на щитовый чертёж, турель — на турельный и так далее.
    /// </summary>
    public enum TuningKind
    {
        [UnityEngine.InspectorName("Щит")] Shield = 0,
        [UnityEngine.InspectorName("Турель")] Turret = 1,
        [UnityEngine.InspectorName("Таран")] Ram = 2
    }
}
