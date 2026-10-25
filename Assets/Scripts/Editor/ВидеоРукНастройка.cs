using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.OpenXR;

// Видео рук (ВидеоРук) работает через пакет Unity OpenXR: Meta — ему нужны две функции
// OpenXR для Android: «Meta Quest: Session» и «Meta Quest: Camera (Passthrough)».
// Они включаются здесь сами после компиляции, без пунктов меню. Типы ищутся по имени —
// код компилируется и до того, как пакет скачан (тогда просто ничего не находится).
[InitializeOnLoad]
static class ВидеоРукНастройка
{
    const string ПространствоMeta = "UnityEngine.XR.OpenXR.Features.Meta";
    static readonly string[] Нужные = { "ARSessionFeature", "ARCameraFeature" };

    static ВидеоРукНастройка() => EditorApplication.delayCall += Включить;

    static void Включить()
    {
        var настройки = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (настройки == null) return;
        bool изменено = false;
        foreach (var ф in настройки.GetFeatures())
        {
            if (ф == null) continue;
            var тип = ф.GetType();
            if (тип.Namespace != ПространствоMeta || Array.IndexOf(Нужные, тип.Name) < 0 || ф.enabled) continue;
            ф.enabled = true;
            изменено = true;
            Debug.Log($"[Видео рук] Включена функция OpenXR для Android: «{тип.Name}».");
        }
        if (!изменено) return;
        EditorUtility.SetDirty(настройки);
        AssetDatabase.SaveAssets();
    }
}
