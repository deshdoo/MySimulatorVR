using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

// Чем управлять — руками или контроллерами (Настройки → Управление).
//
// Риг кабины — «XR Origin Hands» из XR Interaction Toolkit: в нём уже есть и руки
// (отслеживание рук Quest через OpenXR: касание указательным пальцем — poke, захват
// кулаком и щипок — профиль Hand Interaction), и контроллеры Touch. Какие из них
// показывать, решает штатный XRInputModalityManager: он включает то, что гарнитура
// сейчас отслеживает.
//
// Сам менеджер не трогаем (раньше у него убирались ссылки на вторую пару — тогда
// эмулятор XR Interaction Simulator не видел рук и не давал их нажимать). Вместо этого
// в конце каждого кадра, ПОСЛЕ всей обработки XRI, выключается пара, которую выбрали не
// использовать, если менеджер её включил. Смена режима тоже применяется в конце кадра:
// выключать контроллер прямо внутри его же нажатия (карточка «Руки» в меню) нельзя —
// XRI падал с NullReferenceException в XRInteractionGroup.
//
// На гарнитуре руки и контроллеры меняются физически: в режиме «Руки» положите
// контроллеры — гарнитура начнёт видеть руки; в режиме «Контроллеры» возьмите их.
// В редакторе это делает эмулятор XR Interaction Simulator — при смене режима он сам
// переключается на руки или на контроллеры. Если в его окне вручную вернуть контроллеры,
// они не прячутся: иначе в меню нечем было бы нажать.
//
// У рук нет лучей: в режиме «Руки» у их Near-Far интеракторов выключен дальний луч
// (enableFarCasting), остаётся ближний захват рычагов. Включать его обратно пытается
// PokeGestureDetector рига (после жеста «указать»), поэтому он гасится каждый кадр.
// Что под пальцем — подсвечивает НаведениеПальцем.
public class РежимВводаРига : MonoBehaviour
{
    readonly List<GameObject> _лишние = new List<GameObject>();
    readonly List<NearFarInteractor> _лучиРук = new List<NearFarInteractor>();
    float _следПоиск;
    float _перезапускВ = -1f;   // когда перезапустить менеджеры после смены режима
    float _сверитьДо = -1f;     // до какого времени догонять эмулятор до режима (редактор)
    GameSettings.Ввод? _был;
    static РежимВводаРига _я;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Запуск()
    {
        if (_я != null) return;
        var go = new GameObject("РежимВводаРига");
        DontDestroyOnLoad(go);
        _я = go.AddComponent<РежимВводаРига>();
        SceneManager.sceneLoaded += (_, __) => Применить();   // новый риг — сразу, а не через секунду
    }

    // Режим сменили в меню: пересобрать список в конце кадра.
    public static void Применить() { if (_я != null) _я._следПоиск = 0f; }

    void LateUpdate()
    {
        var режим = GameSettings.РежимВвода;
        if (_был != режим)
        {
            _был = режим;
            if (режим != GameSettings.Ввод.Руки)
                foreach (var л in _лучиРук) if (л != null) л.enableFarCasting = true;
            // Старый список прятал бы ровно то, что новый режим показывает (контроллеры
            // при возврате к ним): менеджер их включит, а мы тут же выключим, и обратно их
            // уже никто не включит. Поэтому список — заново, когда эмулятор переключится.
            _лишние.Clear();
            ЭмуляторНа(режим);
            _перезапускВ = _следПоиск = Time.unscaledTime + 0.35f;   // эмулятор переключается за пару кадров
            _сверитьДо = Time.unscaledTime + 3f;
            Debug.Log($"[Ввод] Режим «{режим}».");
        }
        if (_сверитьДо > 0f) Сверить(режим);
        if (_перезапускВ > 0f && Time.unscaledTime >= _перезапускВ)
        {
            // Менеджер перезапускается и сам включает то, что гарнитура (эмулятор) видит
            // сейчас: пара, выключенная в прошлом режиме, иначе так и осталась бы выключенной.
            _перезапускВ = -1f;
            Найти();
            foreach (var м in FindObjectsByType<XRInputModalityManager>(FindObjectsSortMode.None))
                if (м.isActiveAndEnabled) { м.enabled = false; м.enabled = true; }
        }
        else if (Time.unscaledTime >= _следПоиск) Найти();
        foreach (var go in _лишние)
            if (go != null && go.activeSelf) go.SetActive(false);
        if (режим == GameSettings.Ввод.Руки)
            foreach (var л in _лучиРук)
                if (л != null && л.enableFarCasting) л.enableFarCasting = false;
    }

    void Найти()
    {
        _следПоиск = Time.unscaledTime + 1f;   // риг мог смениться при загрузке сцены
        _лишние.Clear();
        _лучиРук.Clear();
        bool руки = GameSettings.РежимВвода == GameSettings.Ввод.Руки;
        foreach (var м in FindObjectsByType<XRInputModalityManager>(FindObjectsSortMode.None))
        {
            foreach (var рука in new[] { м.leftHand, м.rightHand })
                if (рука != null) _лучиРук.AddRange(рука.GetComponentsInChildren<NearFarInteractor>(true));
            // В редакторе контроллеры в режиме «Руки» не прячем: эмулятор XR Interaction
            // Simulator по умолчанию изображает контроллеры, а руками нажимает плохо —
            // спрятав контроллеры, в меню нечем было бы даже вернуть режим обратно. На
            // гарнитуре — строго: положили контроллеры, появились руки.
            if (руки) { if (!ЭмуляторНаКонтроллерах()) { Добавить(м.leftController); Добавить(м.rightController); } }
            else { Добавить(м.leftHand); Добавить(м.rightHand); }
        }
    }

    void Добавить(GameObject go) { if (go != null) _лишние.Add(go); }

    // ---- Эмулятор в редакторе ----
    // Переключение у него внутреннее (SwitchDeviceMode — то же, что его клавиши), зовём
    // через отражение; сам метод проверяет, можно ли сейчас переключиться.
    static readonly MethodInfo _переключить = typeof(SimulatedDeviceLifecycleManager)
        .GetMethod("SwitchDeviceMode", BindingFlags.Instance | BindingFlags.NonPublic);

    static void ЭмуляторНа(GameSettings.Ввод режим)
    {
        if (!Application.isEditor || _переключить == null) return;
        var нужно = режим == GameSettings.Ввод.Руки ? SimulatedDeviceLifecycleManager.DeviceMode.Hand
                                                    : SimulatedDeviceLifecycleManager.DeviceMode.Controller;
        foreach (var э in FindObjectsByType<SimulatedDeviceLifecycleManager>(FindObjectsSortMode.None))
            if (э.isActiveAndEnabled && э.deviceMode != нужно && э.deviceMode != SimulatedDeviceLifecycleManager.DeviceMode.None)
                _переключить.Invoke(э, null);
    }

    // Эмулятор отказывает в переключении, пока не закончил прошлое (оно идёт пару кадров),
    // поэтому первая попытка может не пройти: 3 с после смены режима пробуем снова, пока он
    // не встанет в нужный режим. Переключение синхронно меняет его режим, так что лишний раз
    // туда-обратно он не щёлкнет. Не вышло — пишем в лог, почему.
    static readonly FieldInfo _занят = typeof(SimulatedDeviceLifecycleManager)
        .GetField("m_DeviceModeDirty", BindingFlags.Instance | BindingFlags.NonPublic);

    void Сверить(GameSettings.Ввод режим)
    {
        if (!Application.isEditor || _переключить == null) { _сверитьДо = -1f; return; }
        var нужно = режим == GameSettings.Ввод.Руки ? SimulatedDeviceLifecycleManager.DeviceMode.Hand
                                                    : SimulatedDeviceLifecycleManager.DeviceMode.Controller;
        bool готово = true;
        foreach (var э in FindObjectsByType<SimulatedDeviceLifecycleManager>(FindObjectsSortMode.None))
        {
            if (!э.isActiveAndEnabled || э.deviceMode == нужно || э.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.None) continue;
            готово = false;
            bool занят = _занят != null && (bool)_занят.GetValue(э);
            if (!занят) _переключить.Invoke(э, null);
            if (Time.unscaledTime > _сверитьДо)
            {
                var м = FindFirstObjectByType<XRInputModalityManager>();
                Debug.LogWarning($"[Ввод] Эмулятор не переключился на «{режим}»: он в режиме {э.deviceMode}, занят={занят}, " +
                                 $"у менеджера рига контроллеры {(м != null && (м.leftController != null || м.rightController != null) ? "есть" : "НЕ заданы")}, " +
                                 $"руки {(м != null && (м.leftHand != null || м.rightHand != null) ? "есть" : "НЕ заданы")}.");
            }
        }
        if (готово)
        {
            if (_перезапускВ < 0f && _сверитьДо - Time.unscaledTime < 2.6f) _перезапускВ = Time.unscaledTime + 0.1f;   // догнали не с первого раза — менеджеры заново
            _сверитьДо = -1f;
        }
        else if (Time.unscaledTime > _сверитьДо) _сверитьДо = -1f;
    }

    // Эмулятор сейчас изображает контроллеры (в его окне вернули их вручную) — тогда не прячем.
    static bool ЭмуляторНаКонтроллерах()
    {
        if (!Application.isEditor) return false;
        foreach (var э in FindObjectsByType<SimulatedDeviceLifecycleManager>(FindObjectsSortMode.None))
            if (э.isActiveAndEnabled && э.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Controller) return true;
        return false;
    }
}
