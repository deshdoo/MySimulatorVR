using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

// Переназначение кнопок контроллеров (режим «Гарнитура VR, автономно»).
//
// Два вида действий:
//   1) штатные действия XR Interaction Toolkit — «взять» (Select: хват) и «нажать»
//      (Activate + UI Press: курок) для каждой руки. Именно на них построены рычаги
//      и кнопки пульта, поэтому переназначение действует на всё взаимодействие;
//   2) действия тренажёра, у которых на клавиатуре есть F-клавиши, а на автономной
//      гарнитуре клавиатуры нет: окно итогов, листание страниц.
// Переназначение — штатный механизм Input System (binding overrides): меняется
// только путь привязки контроллера (<XRController>), привязки рук/других
// устройств не трогаются. Overrides сохраняются в PlayerPrefs JSON и
// применяются при каждой загрузке рига.
public static class ControllerBindings
{
    public class Действие
    {
        public string Название, Группа;
        public InputAction[] Действия;   // переназначаются вместе (Activate + UI Press)
        public int Индекс;               // индекс привязки <XRController> в первом действии
    }

    // Свои действия тренажёра (с привязками по умолчанию).
    public static readonly InputAction ОкноИтогов = new InputAction("ОкноИтогов", InputActionType.Button, "<XRController>{LeftHand}/menuButton");
    public static readonly InputAction ДалееСтраница = new InputAction("ДалееСтраница", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
    public static readonly InputAction НазадСтраница = new InputAction("НазадСтраница", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");

    const string КлючXRI = "xri_binds_v1", КлючСвои = "trainer_binds_v1";
    static InputActionAsset _xri;
    static bool _инициализировано;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Запуск()
    {
        if (_инициализировано) return;
        _инициализировано = true;
        foreach (var a in Свои()) a.Enable();
        Загрузить(КлючСвои, Свои());
        SceneManager.sceneLoaded += (_, __) => НайтиXRI();
        НайтиXRI();
    }

    static InputAction[] Свои() => new[] { ОкноИтогов, ДалееСтраница, НазадСтраница };

    // Ассет действий XRI берём у InputActionManager рига (он их и включает).
    static void НайтиXRI()
    {
        foreach (var м in Object.FindObjectsByType<InputActionManager>(FindObjectsSortMode.None))
            foreach (var asset in м.actionAssets)
                if (asset != null && asset.FindAction("XRI Left Interaction/Select") != null)
                {
                    if (_xri == asset) return;
                    _xri = asset;
                    string json = PlayerPrefs.GetString(КлючXRI, "");
                    if (!string.IsNullOrEmpty(json)) _xri.LoadBindingOverridesFromJson(json);
                    return;
                }
    }

    // Список для меню «Управление» (гарнитура).
    public static List<Действие> Список()
    {
        НайтиXRI();
        var л = new List<Действие>();
        void XRI(string имя, string группа, params string[] пути)
        {
            if (_xri == null) return;
            var acts = new List<InputAction>();
            foreach (var p in пути) { var a = _xri.FindAction(p); if (a != null) acts.Add(a); }
            if (acts.Count == 0) return;
            int i = ИндексКонтроллера(acts[0]);
            if (i >= 0) л.Add(new Действие { Название = имя, Группа = группа, Действия = acts.ToArray(), Индекс = i });
        }
        XRI("Схватить рычаг пульта", "левая рука", "XRI Left Interaction/Select");
        XRI("Нажать кнопку пульта / меню", "левая рука", "XRI Left Interaction/Activate", "XRI Left Interaction/UI Press");
        XRI("Схватить рычаг пульта", "правая рука", "XRI Right Interaction/Select");
        XRI("Нажать кнопку пульта / меню", "правая рука", "XRI Right Interaction/Activate", "XRI Right Interaction/UI Press");
        л.Add(new Действие { Название = "Открыть окно итогов", Группа = "тренажёр", Действия = new[] { ОкноИтогов }, Индекс = 0 });
        л.Add(new Действие { Название = "Итоги: следующая страница", Группа = "тренажёр", Действия = new[] { ДалееСтраница }, Индекс = 0 });
        л.Add(new Действие { Название = "Итоги: предыдущая страница", Группа = "тренажёр", Действия = new[] { НазадСтраница }, Индекс = 0 });
        return л;
    }

    static int ИндексКонтроллера(InputAction a)
    {
        for (int i = 0; i < a.bindings.Count; i++)
            if (!a.bindings[i].isComposite && !a.bindings[i].isPartOfComposite && a.bindings[i].path.StartsWith("<XRController>"))
                return i;
        return -1;
    }

    // Человекочитаемое имя текущей кнопки: какой контроллер + какая кнопка на нём.
    // На Quest/Touch: левый — X, Y, «меню»; правый — A, B. Курок — под указательным
    // пальцем, хват — боковая кнопка под средним.
    public static string Имя(Действие д)
    {
        var b = д.Действия[0].bindings[д.Индекс];
        string p = (b.overridePath ?? b.path).ToLowerInvariant();
        bool левая = p.Contains("lefthand"), правая = p.Contains("righthand");
        string рука = левая ? "левый · " : правая ? "правый · " : "";
        string кнопка =
            p.Contains("grip") ? "хват" :
            p.Contains("trigger") ? "курок" :
            p.Contains("primarybutton") ? (левая ? "кнопка X" : правая ? "кнопка A" : "A / X") :
            p.Contains("secondarybutton") ? (левая ? "кнопка Y" : правая ? "кнопка B" : "B / Y") :
            p.Contains("menu") ? "кнопка меню" :
            p.Contains("thumbstickclick") || p.Contains("primary2daxisclick") ? "нажать стик" :
            д.Действия[0].GetBindingDisplayString(д.Индекс);
        return рука + кнопка;
    }

    public static bool Переназначено(Действие д) => !string.IsNullOrEmpty(д.Действия[0].bindings[д.Индекс].overridePath);

    // Интерактивное переназначение: ждём кнопку на КОНТРОЛЛЕРЕ (клавиатура и мышь
    // не принимаются, Esc — отмена). Действие на время ожидания выключается —
    // так требует Input System.
    public static InputActionRebindingExtensions.RebindingOperation Переназначить(Действие д, System.Action готово)
    {
        var главное = д.Действия[0];
        bool былоВкл = главное.enabled;
        главное.Disable();
        var оп = главное.PerformInteractiveRebinding(д.Индекс)
            .WithControlsHavingToMatchPath("<XRController>")
            .WithControlsExcluding("<Keyboard>")
            .WithControlsExcluding("<Mouse>")
            .WithControlsExcluding("<Pointer>")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(0.15f)
            .OnComplete(o => Завершить(o, д, былоВкл, готово))
            .OnCancel(o => Завершить(o, д, былоВкл, готово));
        return оп.Start();
    }

    static void Завершить(InputActionRebindingExtensions.RebindingOperation оп, Действие д, bool былоВкл, System.Action готово)
    {
        string путь = д.Действия[0].bindings[д.Индекс].overridePath;
        // Связанные действия (Activate + UI Press) получают тот же путь.
        for (int k = 1; k < д.Действия.Length; k++)
        {
            int i = ИндексКонтроллера(д.Действия[k]);
            if (i >= 0)
            {
                if (string.IsNullOrEmpty(путь)) д.Действия[k].RemoveBindingOverride(i);
                else д.Действия[k].ApplyBindingOverride(i, путь);
            }
        }
        if (былоВкл) д.Действия[0].Enable();
        оп.Dispose();
        Сохранить();
        готово?.Invoke();
    }

    public static void СброситьВсё()
    {
        if (_xri != null) _xri.RemoveAllBindingOverrides();
        foreach (var a in Свои()) a.RemoveAllBindingOverrides();
        Сохранить();
    }

    static void Сохранить()
    {
        if (_xri != null) PlayerPrefs.SetString(КлючXRI, _xri.SaveBindingOverridesAsJson());
        var sb = new System.Text.StringBuilder();
        foreach (var a in Свои()) sb.Append(a.SaveBindingOverridesAsJson()).Append('\n');
        PlayerPrefs.SetString(КлючСвои, sb.ToString());
        PlayerPrefs.Save();
    }

    static void Загрузить(string ключ, InputAction[] acts)
    {
        var строки = PlayerPrefs.GetString(ключ, "").Split('\n');
        for (int i = 0; i < acts.Length && i < строки.Length; i++)
            if (!string.IsNullOrEmpty(строки[i])) acts[i].LoadBindingOverridesFromJson(строки[i]);
    }
}
