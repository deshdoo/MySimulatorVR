using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

// Переназначение кнопок контроллеров (Настройки → Управление → Контроллеры).
//
// Два вида действий:
//   1) штатные действия XR Interaction Toolkit — «взять» (Select: хват) и «нажать»
//      (Activate + UI Press: курок) для каждой руки. Именно на них построены рычаги
//      и кнопки пульта, поэтому переназначение действует на всё взаимодействие;
//   2) «Меню» тренажёра (итоги и пауза, справка, выход — то же, что меню на ладони):
//      на автономной гарнитуре клавиатуры нет. Две кнопки — Menu левого и B правого;
//   3) тело оператора (ХодьбаПоКабине): левый стик — шаг по кабине, правый — поворот
//      рывком, нажатие левого стика — назад в кресло пилота, Y — справка «?».
// Кнопки пульта (свет, автопилот, захват, камеры) на контроллер нарочно НЕ вынесены:
// как у настоящего поста «Марлина», ими управляют на пульте — этому тренажёр и учит.
// A и X свободны.
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
    // «Меню» с любой руки: у правого контроллера своей кнопки меню для игры нет (системная
    // занята гарнитурой) — там B. Заодно в эмуляторе: он жмёт кнопки только у того
    // контроллера, которым сейчас управляют, и с правого меню раньше было не открыть.
    public static readonly InputAction МенюЛевая = new InputAction("МенюЛевая", InputActionType.Button, "<XRController>{LeftHand}/menuButton");
    public static readonly InputAction МенюПравая = new InputAction("МенюПравая", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
    public static readonly InputAction Справка = new InputAction("Справка", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");
    public static readonly InputAction ВКресло = new InputAction("ВКресло", InputActionType.Button, "<XRController>{LeftHand}/{Primary2DAxisClick}");

    // Ассет действий XRI рига (для ХодьбаПоКабине).
    public static InputActionAsset XRI { get { НайтиXRI(); return _xri; } }

    // Нажата кнопка «Меню»: рука 0 — левая, 1 — правая.
    public static bool МенюНажато(out int рука)
    {
        рука = МенюЛевая.WasPressedThisFrame() ? 0 : МенюПравая.WasPressedThisFrame() ? 1 : -1;
        return рука >= 0;
    }

    const string КлючXRI = "xri_binds_v1", КлючСвои = "trainer_binds_v2";   // v2: две кнопки «Меню»
    static InputActionAsset _xri;
    static bool _инициализировано;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Запуск()
    {
        if (_инициализировано) return;
        _инициализировано = true;
        foreach (var a in Свои()) a.Enable();
        Загрузить(КлючСвои, Свои());
        SceneManager.sceneLoaded += (_, __) => { НайтиXRI(); ВыключитьПередвижение(); };
        НайтиXRI();
        ВыключитьПередвижение();
    }

    // Передвижение XRI по умолчанию выключено целиком: в риге на нём висят ещё «тащить мир»
    // за Grip и прыжок на A — за пультом это срабатывало случайно (сжал Grip у рычага —
    // кабина поехала, нажал A — прыжок). Шаг левым стиком и поворот правым включает
    // обратно ХодьбаПоКабине — только эти два действия.
    // Карты выключаются после того, как риг их включил (при каждой загрузке сцены).
    static void ВыключитьПередвижение()
    {
        if (_xri == null) return;
        foreach (var имя in new[] { "XRI Left Locomotion", "XRI Right Locomotion" })
            _xri.FindActionMap(имя)?.Disable();
    }

    static InputAction[] Свои() => new[] { МенюЛевая, МенюПравая, Справка, ВКресло };   // новые — только в конец (сохранение по порядку)

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
                    ЗахватОбхватом();
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
        XRI("Захват рычага", "левая рука", "XRI Left Interaction/Select");
        XRI("Нажатие кнопки / меню", "левая рука", "XRI Left Interaction/Activate", "XRI Left Interaction/UI Press");
        XRI("Захват рычага", "правая рука", "XRI Right Interaction/Select");
        XRI("Нажатие кнопки / меню", "правая рука", "XRI Right Interaction/Activate", "XRI Right Interaction/UI Press");
        л.Add(new Действие { Название = "Меню", Группа = "левая рука", Действия = new[] { МенюЛевая }, Индекс = 0 });
        л.Add(new Действие { Название = "Меню", Группа = "правая рука", Действия = new[] { МенюПравая }, Индекс = 0 });
        XRI("Ходьба по кабине", "левая рука", "XRI Left Locomotion/Move");
        л.Add(new Действие { Название = "В кресло пилота", Группа = "левая рука", Действия = new[] { ВКресло }, Индекс = 0 });
        л.Add(new Действие { Название = "Справка", Группа = "левая рука", Действия = new[] { Справка }, Индекс = 0 });
        XRI("Поворот", "правая рука", "XRI Right Locomotion/Snap Turn");
        return л;
    }

    static int ИндексКонтроллера(InputAction a)
    {
        for (int i = 0; i < a.bindings.Count; i++)
            if (!a.bindings[i].isComposite && !a.bindings[i].isPartOfComposite && a.bindings[i].path.StartsWith("<XRController>"))
                return i;
        return -1;
    }

    // Имя текущей кнопки по-русски, сторона буквой: «Захват Л» (Grip, боковая под
    // средним пальцем), «Курок П» (Trigger, под указательным), «Стик Л». A, B, X, Y
    // напечатаны на самих кнопках — остаются буквами; их и «Меню» нет на втором
    // контроллере — сторону не пишем. Английские имена подписаны на стенде.
    public static string Имя(Действие д)
    {
        var b = д.Действия[0].bindings[д.Индекс];
        string p = (b.overridePath ?? b.path).ToLowerInvariant();
        bool левая = p.Contains("lefthand"), правая = p.Contains("righthand");
        string сторона = левая ? " Л" : правая ? " П" : "";
        if (p.Contains("grip")) return "Захват" + сторона;
        if (p.Contains("trigger")) return "Курок" + сторона;
        if (p.Contains("primarybutton")) return левая ? "X" : правая ? "A" : "A / X";
        if (p.Contains("secondarybutton")) return левая ? "Y" : правая ? "B" : "B / Y";
        if (p.Contains("menu")) return "Меню";
        if (p.Contains("thumbstickclick") || p.Contains("primary2daxisclick")) return "Нажатие стика" + сторона;
        if (p.Contains("thumbstick") || p.Contains("primary2daxis")) return "Стик" + сторона;
        return д.Действия[0].GetBindingDisplayString(д.Индекс) + сторона;
    }

    // Какая это рука и кнопка (для стенда контроллеров): рука 0 — левая, 1 — правая;
    // кнопка — как в СтендКонтроллеров (trigger, grip, primary, secondary, menu, stick, stickclick).
    public static bool Разобрать(Действие д, out int рука, out string кнопка)
    {
        var b = д.Действия[0].bindings[д.Индекс];
        string p = (b.overridePath ?? b.path).ToLowerInvariant();
        рука = p.Contains("lefthand") ? 0 : p.Contains("righthand") ? 1 : -1;
        кнопка =
            p.Contains("grip") ? СтендКонтроллеров.Хват :
            p.Contains("trigger") ? СтендКонтроллеров.Курок :
            p.Contains("primarybutton") ? СтендКонтроллеров.Первая :
            p.Contains("secondarybutton") ? СтендКонтроллеров.Вторая :
            p.Contains("menu") ? СтендКонтроллеров.Меню :
            p.Contains("thumbstickclick") || p.Contains("primary2daxisclick") ? СтендКонтроллеров.НажатьСтик :
            p.Contains("thumbstick") || p.Contains("primary2daxis") ? СтендКонтроллеров.Стик : null;
        return рука >= 0 && кнопка != null;
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

    // Рычаг рукой берут обхватом, как настоящую рукоятку: пальцы согнуты вокруг ручки, но
    // кулак не сжат. Штатно XRI хватает рукой только по крепкому кулаку (graspFirm) или
    // щипку — обхват до «крепкого» не дотягивал. Привязка руки — сила хвата graspValue с
    // порогом 0,4; щипок остаётся. Привязки контроллеров не трогаются.
    static void ЗахватОбхватом()
    {
        if (_xri == null) return;
        foreach (var имя in new[] { "XRI Left Interaction/Select", "XRI Right Interaction/Select" })
        {
            var a = _xri.FindAction(имя);
            if (a == null) continue;
            for (int i = 0; i < a.bindings.Count; i++)
            {
                string путь = a.bindings[i].path;
                if (!путь.StartsWith("<HandInteraction>") || !путь.EndsWith("/graspFirm")) continue;
                a.ApplyBindingOverride(i, new InputBinding
                {
                    overridePath = путь.Substring(0, путь.Length - "graspFirm".Length) + "graspValue",
                    overrideInteractions = "press(pressPoint=0.4)",
                });
            }
        }
    }

    public static void СброситьВсё()
    {
        if (_xri != null) { _xri.RemoveAllBindingOverrides(); ЗахватОбхватом(); }
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
