using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Один шрифт на всех надписях. Шрифт окон и мониторов — ofont (Courier New), но его атлас
// собран только с кириллицей: латиница (Trigger, Grip, A/B…), запятая и тире брались из
// запасного LiberationSans и выглядели чужими — другой гарнитурой, «прыгали».
// При запуске из того же Courier New (Resources/Fonts/CourierNew) собирается динамический
// шрифт — недостающие знаки он дорисовывает сам — и ставится первым запасным у ofont.
// При выходе из игры убирается: шрифт-ассет в редакторе иначе запомнил бы ссылку на
// временный объект.
public static class ОдинШрифт
{
    static TMP_FontAsset _запасной;
    static readonly List<TMP_FontAsset> _дополненные = new List<TMP_FontAsset>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Запуск()
    {
        SceneManager.sceneLoaded += (_, __) => Дополнить();
        Application.quitting += Убрать;
        Дополнить();
    }

    static void Дополнить()
    {
        if (_запасной == null)
        {
            var ttf = Resources.Load<Font>("Fonts/CourierNew");
            if (ttf == null) { Debug.LogWarning("[Шрифт] Нет Resources/Fonts/CourierNew — латиница остаётся запасным шрифтом."); return; }
            _запасной = TMP_FontAsset.CreateFontAsset(ttf);
            if (_запасной == null) return;
            _запасной.name = "Courier New (латиница, auto)";
        }
        foreach (var ф in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (ф == null || ф == _запасной || !ф.name.StartsWith("ofont")) continue;
            ф.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
            if (ф.fallbackFontAssetTable.Contains(_запасной)) continue;
            ф.fallbackFontAssetTable.Insert(0, _запасной);
            _дополненные.Add(ф);
        }
    }

    static void Убрать()
    {
        foreach (var ф in _дополненные) if (ф != null) ф.fallbackFontAssetTable?.Remove(_запасной);
        _дополненные.Clear();
    }
}
