using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// Голографический стенд контроллеров рядом с окном «Настройки → Управление → Контроллеры».
// Копии тех самых моделей, что у рига в руках (XRI UniversalController: Trigger, Bumper =
// Grip, ThumbStick, Button_A/B, Button_Home), висят и медленно покачиваются; от каждой
// кнопки — выноска: крупно, что кнопка делает в тренажёре, под этим мелко — её
// английское название, как оно напечатано в инструкциях к контроллерам.
//   • наведите луч на строку списка в окне — на модели загорится эта кнопка;
//   • нажмите кнопку на настоящем контроллере — загорится она же (ярче) и её выноска.
// Действия берутся из тех же привязок, что и список (ControllerBindings), — после
// переназначения выноски сами обновятся. Строит и убирает MainMenu.
public class СтендКонтроллеров : MonoBehaviour
{
    public const string Курок = "trigger", Хват = "grip", Первая = "primary", Вторая = "secondary", Меню = "menu", Стик = "stick", НажатьСтик = "stickclick";
    // Модели в 1,8 раза больше настоящих, выноски в две строки: что делает и английское имя.
    const float Масштаб = 1.8f, Шаг_м = 0.078f, МодельX = 0.13f, ПодписьX = 0.25f;

    static readonly Color Свет = new Color(0.3f, 0.85f, 1f), Ярко = new Color(1f, 0.85f, 0.3f);

    class Деталь
    {
        public int Рука; public string Кнопка;
        public Renderer[] Рендеры; public Color[] Цвета;
        public TextMeshPro Подпись; public LineRenderer Линия; public Vector3 Якорь;   // якорь — у подписи, в осях стенда
        public bool Свободна;
        public float Видно = 1f;   // 0 — кнопка с дальней стороны корпуса: линия гаснет
    }
    readonly List<Деталь> _детали = new List<Деталь>();
    readonly Transform[] _опоры = new Transform[2];
    readonly Renderer[][] _модели = new Renderer[2][];   // все детали модели — для центра корпуса
    readonly Vector3[] _центр = new Vector3[2];
    Material _матЛиний;
    TMP_FontAsset _шрифт;
    float _следОбн;

    // Подсветка от строки списка (MainMenu): рука 0/1 и кнопка; null — ничего.
    public int НаведённаяРука = -1;
    public string НаведённаяКнопка;

    public static СтендКонтроллеров Создать(Vector3 поз, Quaternion пов, UnityEngine.SceneManagement.Scene сцена, TMP_FontAsset шрифт)
    {
        var риг = FindFirstObjectByType<XROrigin>();
        if (риг == null) return null;
        var go = new GameObject("Стенд контроллеров");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, сцена);
        go.transform.SetPositionAndRotation(поз, пов);
        var с = go.AddComponent<СтендКонтроллеров>();
        с._шрифт = шрифт;
        if (!с.Построить(риг)) { Destroy(go); return null; }
        return с;
    }

    bool Построить(XROrigin риг)
    {
        var sh = Shader.Find("Sprites/Default");
        _матЛиний = sh != null ? new Material(sh) { name = "Выноски стенда (auto)" } : null;
        // Модели контроллеров рига: корень визуала — родитель «UniversalController».
        Transform[] визуал = new Transform[2];
        foreach (var t in риг.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "UniversalController" || t.parent == null) continue;
            int рука = ЛеваяЛи(t) ? 0 : 1;
            if (визуал[рука] == null) визуал[рука] = t.parent;
        }
        if (визуал[0] == null && визуал[1] == null) { Debug.LogWarning("[Стенд] У рига нет моделей контроллеров UniversalController."); return false; }

        for (int рука = 0; рука < 2; рука++)
        {
            if (визуал[рука] == null) continue;
            // Опора — «рамка контроллера»: вперёд — куда он указывает, вверх — его верх.
            var опора = new GameObject(рука == 0 ? "Левый" : "Правый").transform;
            опора.SetParent(transform, false);
            опора.localPosition = new Vector3(рука == 0 ? -МодельX : МодельX, 0f, 0f);
            опора.localScale = Vector3.one * Масштаб;
            var копия = Instantiate(визуал[рука].gameObject, опора, false);
            копия.transform.localPosition = визуал[рука].localPosition;
            копия.transform.localRotation = визуал[рука].localRotation;
            копия.transform.localScale = визуал[рука].localScale;
            копия.SetActive(true);
            foreach (var mb in копия.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
            foreach (var c in копия.GetComponentsInChildren<Component>(true))
                if (!(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer)) DestroyImmediate(c);
            foreach (var t in копия.GetComponentsInChildren<Transform>(true)) { t.gameObject.SetActive(true); t.gameObject.layer = gameObject.layer; }
            _опоры[рука] = опора;
            _модели[рука] = копия.GetComponentsInChildren<Renderer>(true);

            foreach (var t in копия.GetComponentsInChildren<Transform>(true))
            {
                string кнопка = КнопкаДетали(t.name);
                if (t.name == "TouchPad") { foreach (var r in t.GetComponentsInChildren<Renderer>()) r.enabled = false; continue; }   // у Quest тачпада нет
                if (кнопка == null) continue;
                var рендеры = t.GetComponentsInChildren<Renderer>();
                if (рендеры.Length == 0) continue;
                var д = new Деталь { Рука = рука, Кнопка = кнопка, Рендеры = рендеры, Цвета = new Color[рендеры.Length] };
                for (int i = 0; i < рендеры.Length; i++)
                {
                    var m = рендеры[i].material;   // своя копия — красим только эту деталь
                    д.Цвета[i] = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                }
                _детали.Add(д);
            }
        }
        РазложитьВыноски();
        ОбновитьТексты();
        СоздатьСвет();
        return true;
    }

    // Свой мягкий свет сверху-спереди: в меню на слое моделей нет освещения, и копии
    // контроллеров были чёрными. Светит только на стенд (малая дальность), без теней.
    void СоздатьСвет()
    {
        var go = new GameObject("Свет стенда");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.45f, -0.35f);
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 1.3f;
        l.intensity = 0.22f;   // до моделей ~0,55 м: точечный свет ~ сила / d², при 1,6 белый пластик пересвечивало
        l.color = new Color(0.92f, 0.97f, 1f);
        l.shadows = LightShadows.None;
    }

    static bool ЛеваяЛи(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
        {
            string n = p.name.ToLowerInvariant();
            if (n.Contains("left")) return true;
            if (n.Contains("right")) return false;
        }
        return true;
    }

    static string КнопкаДетали(string имя) => имя switch
    {
        "Trigger" => Курок,
        "Bumper" => Хват,
        "ThumbStick" => Стик,
        "Button_A" => Первая,
        "Button_B" => Вторая,
        "Button_Home" => Меню,
        _ => null,
    };

    // Подписи столбиком снаружи от каждой модели, по порядку высоты деталей — линии не перекрещиваются.
    void РазложитьВыноски()
    {
        Покачать(0f);
        for (int рука = 0; рука < 2; рука++)
        {
            var свои = _детали.FindAll(д => д.Рука == рука);
            свои.Sort((a, b) => ЛокальноВСтенде(b).y.CompareTo(ЛокальноВСтенде(a).y));
            float верх = (свои.Count - 1) * Шаг_м * 0.5f;
            for (int i = 0; i < свои.Count; i++)
            {
                var д = свои[i];
                float x = рука == 0 ? -ПодписьX : ПодписьX;
                д.Якорь = new Vector3(x, верх - i * Шаг_м, 0f);
                var go = new GameObject("Выноска " + д.Кнопка);
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                go.transform.localPosition = д.Якорь + new Vector3(рука == 0 ? -0.01f : 0.01f, 0f, 0f);
                var tm = go.AddComponent<TextMeshPro>();
                if (_шрифт != null) tm.font = _шрифт;
                tm.fontSize = 0.2f;
                tm.lineSpacing = -10f;
                tm.textWrappingMode = TextWrappingModes.NoWrap;
                tm.alignment = рука == 0 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
                tm.rectTransform.pivot = new Vector2(рука == 0 ? 1f : 0f, 0.5f);
                tm.rectTransform.sizeDelta = new Vector2(0.22f, 0.07f);
                д.Подпись = tm;
                var lr = new GameObject("Линия " + д.Кнопка).AddComponent<LineRenderer>();
                lr.gameObject.layer = gameObject.layer;
                lr.transform.SetParent(transform, false);
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.widthMultiplier = 0.0018f;
                lr.sharedMaterial = _матЛиний;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                д.Линия = lr;
            }
        }
    }

    Vector3 ЛокальноВСтенде(Деталь д) => transform.InverseTransformPoint(Центр(д));

    Vector3 ЦентрМодели(int рука)
    {
        Bounds b = default; bool есть = false;
        foreach (var r in _модели[рука])
        {
            if (r == null || !r.enabled) continue;
            if (!есть) { b = r.bounds; есть = true; } else b.Encapsulate(r.bounds);
        }
        return есть ? b.center : _опоры[рука].position;
    }

    static Vector3 Центр(Деталь д)
    {
        Bounds b = д.Рендеры[0].bounds;
        foreach (var r in д.Рендеры) b.Encapsulate(r.bounds);
        return b.center;
    }

    // Подписи: сверху что кнопка делает (по текущим привязкам), снизу её английское имя.
    void ОбновитьТексты()
    {
        var что = new Dictionary<(int, string), string>();
        foreach (var д in ControllerBindings.Список())
        {
            if (!ControllerBindings.Разобрать(д, out int рука, out string кнопка)) continue;
            var к = (рука, кнопка == НажатьСтик ? Стик : кнопка);
            string коротко = КороткоеДело(д.Название);
            что[к] = что.TryGetValue(к, out var было) ? было + ", " + коротко : коротко;
        }
        foreach (var д in _детали)
        {
            string имя = ИмяАнгл(д.Рука, д.Кнопка);
            string дело;
            if (что.TryGetValue((д.Рука, д.Кнопка), out var есть)) { дело = есть; д.Свободна = false; }
            else if (д.Кнопка == Стик) { дело = "Не используется"; д.Свободна = true; }
            else if (д.Кнопка == Меню && д.Рука == 1) { дело = "Меню гарнитуры"; д.Свободна = true; }
            else { дело = "Свободна"; д.Свободна = true; }
            д.Подпись.text = $"<b>{дело}</b>\n<size=70%>{имя}</size>";
        }
    }

    // Короткая подпись действия для выноски (полное название — в списке окна).
    static string КороткоеДело(string название)
    {
        string н = название.ToLowerInvariant();
        if (н.Contains("рычаг")) return "Захват рычага";
        if (н.Contains("нажатие")) return "Нажатие кнопки";
        return название;
    }

    // Английское имя кнопки, как в инструкциях к контроллерам (System — системная кнопка
    // правого контроллера, её действие задаёт гарнитура, а не тренажёр).
    static string ИмяАнгл(int рука, string кнопка) => кнопка switch
    {
        Курок => "Trigger",
        Хват => "Grip",
        Стик => "Thumbstick",
        Первая => рука == 0 ? "X" : "A",
        Вторая => рука == 0 ? "Y" : "B",
        Меню => рука == 0 ? "Menu" : "System",
        _ => кнопка,
    };

    // Покачивание: модели медленно поворачиваются почти на четверть оборота в каждую
    // сторону — видны и верх с кнопками, и бок с Grip, и курок сзади. Поворот вокруг
    // вертикали не меняет высоту кнопок, а подписи висят на месте (они не на моделях),
    // поэтому линии не перекрещиваются. Верх контроллера смотрит на оператора, нос — вверх.
    void Покачать(float t)
    {
        var базовый = Quaternion.LookRotation(new Vector3(0f, 0.8f, 0.6f), new Vector3(0f, 0.6f, -0.8f));
        for (int рука = 0; рука < 2; рука++)
        {
            if (_опоры[рука] == null) continue;
            float внутрь = рука == 0 ? 20f : -20f;   // слегка развёрнуты к середине — виден внутренний бок с Grip
            float качание = 80f * Mathf.Sin(t * 0.4f + рука * 0.8f);   // ~16 с туда-обратно
            _опоры[рука].localRotation = Quaternion.AngleAxis(внутрь + качание, Vector3.up) * базовый;
        }
    }

    void Update()
    {
        Покачать(Time.unscaledTime);
        if (Time.unscaledTime > _следОбн) { _следОбн = Time.unscaledTime + 0.5f; ОбновитьТексты(); }
        var кам = Camera.main;
        Vector3 глаз = кам != null ? кам.transform.position : transform.position - transform.forward;
        for (int р = 0; р < 2; р++) if (_опоры[р] != null) _центр[р] = ЦентрМодели(р);
        foreach (var д in _детали)
        {
            bool нажата = НажатаВЖизни(д.Рука, д.Кнопка);
            bool наведена = д.Рука == НаведённаяРука && д.Кнопка == НаведённаяКнопка;
            Color цвет = нажата ? Ярко : наведена ? Свет : Color.clear;
            for (int i = 0; i < д.Рендеры.Length; i++)
            {
                var m = д.Рендеры[i].material;
                Color c = цвет.a > 0f ? Color.Lerp(д.Цвета[i], цвет, 0.85f) : д.Цвета[i];
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            }
            Color текст = нажата ? Ярко : наведена ? Свет : д.Свободна ? new Color(0.6f, 0.66f, 0.72f) : new Color(0.9f, 0.94f, 0.97f);
            д.Подпись.color = текст;
            д.Подпись.transform.localScale = Vector3.one * (нажата || наведена ? 1.15f : 1f);
            // Кнопка повернулась на дальнюю сторону корпуса: линия к ней уходила бы в
            // модель и указывала не туда — гаснет почти до нуля, подпись остаётся.
            // Наведённая или нажатая — всегда яркая.
            Vector3 ц = Центр(д);
            bool сзади = Vector3.Dot((ц - _центр[д.Рука]).normalized, (глаз - ц).normalized) < -0.2f;
            д.Видно = Mathf.MoveTowards(д.Видно, сзади ? 0f : 1f, Time.unscaledDeltaTime * 3f);
            float альфа = нажата || наведена ? 0.95f : Mathf.Lerp(0.08f, 0.45f, д.Видно);
            д.Линия.startColor = д.Линия.endColor = new Color(текст.r, текст.g, текст.b, альфа);
            д.Линия.SetPosition(0, ц);
            д.Линия.SetPosition(1, transform.TransformPoint(д.Якорь));
        }
    }

    // Нажата ли эта кнопка на настоящем контроллере (стандартные «назначения» XR в Input
    // System). Найденные кнопки запоминаются — путь не разбирается каждый кадр.
    static readonly Dictionary<(int, string), (InputDevice устройство, InputControl кнопка, InputControl клик)> _кэш =
        new Dictionary<(int, string), (InputDevice, InputControl, InputControl)>();

    static bool НажатаВЖизни(int рука, string кнопка)
    {
        if (кнопка == Меню && рука == 1) return false;   // системная кнопка игре не передаётся
        var устройство = рука == 0 ? UnityEngine.InputSystem.XR.XRController.leftHand : UnityEngine.InputSystem.XR.XRController.rightHand;
        if (устройство == null) return false;
        if (!_кэш.TryGetValue((рука, кнопка), out var з) || з.устройство != устройство)
        {
            string путь = кнопка switch
            {
                Курок => "{TriggerButton}", Хват => "{GripButton}", Первая => "{PrimaryButton}",
                Вторая => "{SecondaryButton}", Меню => "{MenuButton}", Стик => "{Primary2DAxis}", _ => null,
            };
            з = (устройство, путь != null ? InputControlPath.TryFindChild(устройство, путь) : null,
                 кнопка == Стик ? InputControlPath.TryFindChild(устройство, "{Primary2DAxisClick}") : null);
            _кэш[(рука, кнопка)] = з;
        }
        if (з.кнопка is ButtonControl b) return b.isPressed;
        if (з.кнопка is Vector2Control v) return v.ReadValue().magnitude > 0.5f || (з.клик is ButtonControl клик && клик.isPressed);
        if (з.кнопка is AxisControl a) return a.ReadValue() > 0.5f;
        return false;
    }
}
