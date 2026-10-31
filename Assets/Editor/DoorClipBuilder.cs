using UnityEditor;
using UnityEngine;

/// <summary>
/// Пересобирает DoorOpen/DoorClose через AnimationUtility, чтобы Unity сама записала
/// корректные хеши в m_ClipBindingConstant.genericBindings.
///
/// Раньше биндинги в .anim были написаны вручную, и хеш пути дверь1 не был проверен
/// ни одним импортом Unity: строки path в кривых верные, а числовой хеш в
/// genericBindings - нет. Из-за этого клипы не анимировали дверь во время игры.
///
/// Запуск: меню Tools -> Car -> Rebuild Door Clips
/// </summary>
public static class DoorClipBuilder
{
    private const string DoorName = "\u0434\u0432\u0435\u0440\u044C1"; // дверь1
    private const float SwingDegrees = 66.695f;
    private const double Length = 0.33333334;

    private const string OpenPath = "Assets/Anims/Cars/DoorOpen.anim";
    private const string ClosePath = "Assets/Anims/Cars/DoorClose.anim";

    [MenuItem("Tools/Car/Rebuild Door Clips")]
    private static void Rebuild()
    {
        Transform car = FindCarRoot();
        if (car == null) return;

        Transform door = car.Find(DoorName);
        if (door == null)
        {
            Debug.LogError("[DoorClipBuilder] дверь1 not found under " + car.name);
            return;
        }

        // Ось Z поворота двери. Покой берём из сцены, чтобы закрытая поза
        // не отличалась от исходного положения и дверь не дёргалась при старте.
        Vector3 closed = door.localEulerAngles;
        Vector3 open = closed + new Vector3(0f, 0f, SwingDegrees);

        RebuildClip(OpenPath, closed, open);
        RebuildClip(ClosePath, open, closed);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[DoorClipBuilder] closed=" + closed + " open=" + open +
                  " path='" + DoorName + "' - binding hashes written by Unity.");
    }

    private static void RebuildClip(string path, Vector3 from, Vector3 to)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            Debug.LogError("[DoorClipBuilder] missing clip: " + path);
            return;
        }

        // Старые кривые включали позицию двери - перенос не нужен, петля только на Z.
        clip.ClearCurves();

        SetAngle(clip, "x", from.x, to.x);
        SetAngle(clip, "y", from.y, to.y);
        SetAngle(clip, "z", from.z, to.z);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorUtility.SetDirty(clip);

        EditorCurveBinding[] written = AnimationUtility.GetCurveBindings(clip);
        for (int i = 0; i < written.Length; i++)
        {
            Debug.Log("[DoorClipBuilder] " + path + " binding: path='" + written[i].path +
                      "' type=" + written[i].type + " prop=" + written[i].propertyName);
        }
    }

    private static void SetAngle(AnimationClip clip, string axis, float from, float to)
    {
        EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
            DoorName, typeof(Transform), "localEulerAnglesRaw." + axis);

        AnimationUtility.SetEditorCurve(clip, binding,
            AnimationCurve.Linear(0f, from, (float)Length, to));
    }

    /// <summary>
    /// Animator должен висеть на корне машины, а дверь быть его прямым ребёнком:
    /// тогда путь биндинга равен имени двери.
    /// </summary>
    private static Transform FindCarRoot()
    {
        Animator[] animators = Object.FindObjectsByType<Animator>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i].transform.Find(DoorName) != null) return animators[i].transform;
        }

        Debug.LogError("[DoorClipBuilder] no Animator with a child named " + DoorName + " found.");
        return null;
    }
}
