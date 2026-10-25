using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;

// Рычаги пульта — руками (режим «Руки»). Штатный захват XRI у рук срабатывает только от
// профиля Hand Interaction гарнитуры, а в эмуляторе XR Interaction Simulator не срабатывает
// вовсе: рычаг подсвечивался под пальцем, но не брался. Поэтому жест считается здесь, по
// суставам XR Hands (их строит и гарнитура, и эмулятор):
//   • хват или щипок — большой и указательный сомкнуты (в эмуляторе выражения Grab и
//     Pinch: у Grab пальцы обхватывают рукоять, кончики в ~11 см от ладони, но большой
//     и указательный сходятся до 2 см);
//   • кулак — пальцы прижаты к ладони (выражения Fist и Thumb). Поза Poke (указательный
//     вытянут) — не хват: ею жмут кнопки рядом с рычагами.
// Рука с таким жестом у рукоятки (ладонь или точка между пальцами не дальше 10 см от
// коллайдера рычага) берёт рычаг (LeverInteractable.ВзятьРукой) и водит его, как
// контроллер; разжали руку или гарнитура её потеряла — рычаг отпущен. Жест можно сделать
// и заранее, а потом поднести руку: в эмуляторе выражение включают до того, как ведут руку.
// Ладонь-призрак прилипает к рукоятке (УпорРук), как при любом захвате.
[DefaultExecutionOrder(-50)]   // точка руки сдвигается до Update рычагов
public class ХватРычагаРукой : MonoBehaviour
{
    const float Щипок_м = 0.03f, ЩипокОтпуск_м = 0.05f;    // большой — указательный
    const float Отпуск_м = 0.02f;                           // запас кулака на отпускание
    const float Дотянуться_м = 0.10f;

    // Кулак: кончик не дальше этого от ладони (указательный в кулаке лежит дальше остальных).
    static readonly (XRHandJointID палец, float м)[] ПальцыКулака =
        { (XRHandJointID.IndexTip, 0.095f), (XRHandJointID.MiddleTip, 0.07f), (XRHandJointID.RingTip, 0.07f) };
    static readonly List<XRHandSubsystem> _системы = new List<XRHandSubsystem>();

    class Рука
    {
        public Transform Точка;
        public bool Сжата, Щипок, ЗаЩипок;   // ЗаЩипок — взят точкой между пальцами, а не ладонью
        public LeverInteractable Рычаг;
    }

    readonly Рука[] _руки = { new Рука(), new Рука() };
    XRHandSubsystem _подсистема;
    XROrigin _риг;
    float _следПоиск;
    static ХватРычагаРукой _я;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Запуск()
    {
        if (_я != null) return;
        var go = new GameObject("ХватРычагаРукой");
        DontDestroyOnLoad(go);
        _я = go.AddComponent<ХватРычагаРукой>();
        for (int с = 0; с < 2; с++)
        {
            _я._руки[с].Точка = new GameObject(с == 0 ? "Левая ладонь" : "Правая ладонь").transform;
            _я._руки[с].Точка.SetParent(go.transform, false);
        }
    }

    void Update()
    {
        bool можно = GameSettings.РежимВвода == GameSettings.Ввод.Руки && !MainMenu.Открыто && НайтиРуки();
        for (int с = 0; с < 2; с++)
        {
            var р = _руки[с];
            var рука = можно ? (с == 0 ? _подсистема.leftHand : _подсистема.rightHand) : default;
            if (!можно || !рука.isTracked || !Ладонь(рука, out Vector3 ладонь, out Vector3 щипок, out bool кулак, out bool сомкнуты, р))
            {
                Отпустить(р);
                р.Сжата = р.Щипок = false;
                continue;
            }
            bool былЖест = р.Сжата || р.Щипок;
            р.Сжата = кулак;
            р.Щипок = сомкнуты;
            bool жест = кулак || сомкнуты;
            if (жест && !былЖест && р.Рычаг == null) Сообщить(с, кулак, ладонь, щипок);

            if (р.Рычаг != null)
            {
                if (р.Рычаг.ДержитРукой != с) { р.Рычаг = null; continue; }   // рычаг перехватил контроллер
                if (!жест || !р.Рычаг.isActiveAndEnabled) { Отпустить(р); continue; }
                р.Точка.position = р.ЗаЩипок ? щипок : ладонь;   // ведём той же точкой, что взяли: смена точки дёрнула бы рычаг
                continue;
            }
            if (!жест) continue;

            var рычаг = Ближайший(ладонь, out float кЛадони);
            var рычаг2 = Ближайший(щипок, out float кЩипку);
            р.ЗаЩипок = сомкнуты && рычаг2 != null && (рычаг == null || кЩипку < кЛадони);
            if (р.ЗаЩипок) рычаг = рычаг2;
            Vector3 точка = р.ЗаЩипок ? щипок : ладонь;
            if (рычаг == null) continue;
            р.Точка.position = точка;
            рычаг.ВзятьРукой(р.Точка, с);
            if (рычаг.ДержитРукой == с)
            {
                р.Рычаг = рычаг;
                UiSound.Клик();   // у рук нет вибро — щелчок
                Debug.Log($"[Рычаг] {(с == 0 ? "Левая" : "Правая")} рука взяла «{рычаг.axis}» {(р.ЗаЩипок ? "щипком" : "ладонью")}.");
            }
        }
    }

    // Ладонь и точка щипка в мире; сжата ли рука в кулак и сомкнут ли щипок (с запасом на
    // отпускание, чтобы дрожание пальцев не роняло рычаг).
    bool Ладонь(XRHand рука, out Vector3 ладонь, out Vector3 щипок, out bool кулак, out bool сомкнуты, Рука р)
    {
        ладонь = щипок = Vector3.zero;
        кулак = сомкнуты = false;
        var т = НаведениеПальцем.ПространствоРук(_риг);   // суставы — в пространстве трекинга (Camera Offset)
        if (!рука.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose п)) return false;
        ладонь = т.TransformPoint(п.position);

        float запас = р.Сжата ? Отпуск_м : 0f;
        кулак = true;
        foreach (var (id, м) in ПальцыКулака)
            if (!рука.GetJoint(id).TryGetPose(out Pose к) || Vector3.Distance(т.TransformPoint(к.position), ладонь) > м + запас) { кулак = false; break; }

        if (рука.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose б) && рука.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose у))
        {
            Vector3 a = т.TransformPoint(б.position), b = т.TransformPoint(у.position);
            щипок = (a + b) * 0.5f;
            сомкнуты = Vector3.Distance(a, b) < (р.Щипок ? ЩипокОтпуск_м : Щипок_м);
        }
        return true;
    }

    static LeverInteractable Ближайший(Vector3 точка, out float ближе)
    {
        LeverInteractable лучший = null;
        ближе = Дотянуться_м;
        foreach (var л in LeverInteractable.Все)
        {
            if (л == null || !л.isActiveAndEnabled || л.Интерактабл == null) continue;
            var колл = л.Интерактабл.colliders;
            if (колл == null || колл.Count == 0) continue;
            foreach (var c in колл)
            {
                if (c == null || !c.enabled) continue;
                Vector3 cp = c is MeshCollider м && !м.convex ? c.bounds.ClosestPoint(точка) : c.ClosestPoint(точка);
                float d = Vector3.Distance(точка, cp);
                if (d < ближе) { ближе = d; лучший = л; }
            }
        }
        return лучший;
    }

    // Для разбора в логе: рука сжалась — какой жест распознан и как далеко ближайший рычаг.
    static float _следСообщение;

    static void Сообщить(int с, bool кулак, Vector3 ладонь, Vector3 щипок)
    {
        if (Time.unscaledTime < _следСообщение) return;   // не чаще раза в секунду: лог в редакторе тяжёлый
        _следСообщение = Time.unscaledTime + 1f;
        float лучше = float.MaxValue; string имя = "нет";
        foreach (var л in LeverInteractable.Все)
        {
            if (л == null || л.Интерактабл == null) continue;
            foreach (var c in л.Интерактабл.colliders)
            {
                if (c == null) continue;
                foreach (var т in new[] { ладонь, щипок })
                {
                    Vector3 cp = c is MeshCollider м && !м.convex ? c.bounds.ClosestPoint(т) : c.ClosestPoint(т);
                    float d = Vector3.Distance(т, cp);
                    if (d < лучше) { лучше = d; имя = $"{л.axis}{(л.isActiveAndEnabled && c.enabled ? "" : " (выключен)")}"; }
                }
            }
        }
        Debug.Log($"[Рычаг] {(с == 0 ? "Левая" : "Правая")} рука: {(кулак ? "кулак" : "щипок")}; рычагов {LeverInteractable.Все.Count}, " +
                  $"ближайший {имя} в {(лучше < float.MaxValue ? (лучше * 100f).ToString("F0") + " см" : "—")} (берётся до {Дотянуться_м * 100f:F0} см).");
    }

    static void Отпустить(Рука р)
    {
        if (р.Рычаг != null) { р.Рычаг.ОтпуститьРукой(); Debug.Log($"[Рычаг] Отпущен «{р.Рычаг.axis}»."); }
        р.Рычаг = null;
    }

    bool НайтиРуки()
    {
        if (Time.unscaledTime >= _следПоиск || _риг == null)
        {
            _следПоиск = Time.unscaledTime + 1f;   // риг меняется при загрузке сцены
            _риг = FindFirstObjectByType<XROrigin>();
        }
        if (_риг == null || _риг.Origin == null) return false;
        if (_подсистема == null || !_подсистема.running)
        {
            _подсистема = null;
            SubsystemManager.GetSubsystems(_системы);
            foreach (var с in _системы) if (с.running) { _подсистема = с; break; }
        }
        return _подсистема != null;
    }
}
