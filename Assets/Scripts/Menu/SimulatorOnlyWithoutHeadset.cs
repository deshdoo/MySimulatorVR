using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using Object = UnityEngine.Object;

// Эмулятор XR Interaction Simulator — только когда шлема нет. Штатно XRI создаёт его в
// редакторе при каждом запуске, даже с подключённым шлемом (Quest через Link), и эмулятор
// убирает настоящий шлем из ввода: голова, руки и контроллеры шлема переставали работать.
//
// XR запускается раньше загрузки сцены, поэтому здесь уже видно, поднялся ли настоящий
// шлем (работает подсистема вывода изображения). Если да — на этот запуск выключается
// автосоздание эмулятора (поле его настроек, через отражение: оно внутреннее), а при
// выходе из Play возвращается как было — в файл настроек ничего не попадает.
public static class ЭмуляторБезШлема
{
    // Класс настроек эмулятора внутренний — тип и поле берутся отражением из сборки XRI.
    static readonly System.Type _тип = typeof(XRInteractionSimulator).Assembly
        .GetType("UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRDeviceSimulatorSettings");
    static readonly PropertyInfo _авто = _тип?.GetProperty("automaticallyInstantiateSimulatorPrefab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    static readonly PropertyInfo _экземпляр = _тип?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
    static readonly List<XRDisplaySubsystem> _дисплеи = new List<XRDisplaySubsystem>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Проверить()
    {
        if (!Application.isEditor || _авто == null || _экземпляр == null || !ШлемРаботает()) return;
        var настройки = _экземпляр.GetValue(null) as Object;
        if (настройки == null || !(bool)_авто.GetValue(настройки)) return;
        _авто.SetValue(настройки, false);
        Application.quitting += () => { if (настройки != null) _авто.SetValue(настройки, true); };
        Debug.Log("[Ввод] Шлем подключён — эмулятор XR не создаётся.");
    }

    // Загрузчик OpenXR поднялся (есть рантайм и шлем — без них он падает и остаётся null)
    // или уже работает вывод изображения в шлем.
    static bool ШлемРаботает()
    {
        var м = UnityEngine.XR.Management.XRGeneralSettings.Instance != null ? UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager : null;
        if (м != null && м.activeLoader != null) return true;
        SubsystemManager.GetSubsystems(_дисплеи);
        foreach (var д in _дисплеи) if (д.running) return true;
        return false;
    }
}
