using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Упор видимой руки — для управления руками. Настоящую руку остановить нечем (у шлема
// нет обратной связи по силе), поэтому останавливается рука-призрак: её скелет сдвигается
// штатным XRHandSkeletonDriver.ApplyRootPoseOffset. Палец-poke, которым жмутся кнопки,
// остаётся на настоящей руке — нажатие засчитывается в момент касания, как и было.
//
//   • Окна (VrPanel, окно итогов, значки кабины): кончик указательного дошёл до плоскости
//     окна — рука встаёт на неё. Настоящая рука ушла глубже 6 см — призрак плавно её
//     догоняет (иначе руки разъедутся) и снова упрётся в это окно, только когда палец
//     выйдет из него назад или в сторону.
//   • Кнопки пульта: так же, плоскость — грань кнопки, к которой подошёл палец.
//   • Рычаг: пока рука держит рычаг, ладонь-призрак прилипает к точке хвата на рукоятке
//     и ходит вместе с ней. Сдвигается вся кисть, поза пальцев остаётся своя.
//
// Штатный XRHandSkeletonPokeDisplacer рига (тот же упор, но только в тонкой зоне касания
// кнопки) выключается, чтобы не спорить за сдвиг. При живом видео рук (ВидеоРук) упора
// нет: видео не сдвинуть, а маска не на месте показала бы фон вместо руки.
public class УпорРук : MonoBehaviour
{
    const float Глубина_м = 0.06f;     // глубже настоящая рука — призрак её догоняет
    const float Палец_м = 0.006f;      // полтолщины пальца: призрак встаёт подушечкой на поверхность
    const float Край_м = 0.01f;        // палец может чуть выйти за край цели, упор держится
    const float Подход_м = 0.03f;      // ближе этого запоминается грань кнопки пульта
    const float ХватМакс_м = 0.03f;    // ладонь-призрак у рукоятки — не дальше 3 см от неё
    const float Догнать_τ = 0.08f, Прилипнуть_τ = 0.03f;   // сглаживание, с (≈3τ до места)

    static readonly List<RectTransform> _окна = new List<RectTransform>();
    static readonly List<XRBaseInteractable> _кнопки = new List<XRBaseInteractable>();

    public static void Окно(RectTransform rt) { if (rt != null && !_окна.Contains(rt)) _окна.Add(rt); }
    public static void Кнопка(XRBaseInteractable и) { if (и != null && !_кнопки.Contains(и)) _кнопки.Add(и); }

    // Плоскость упора: окно (RectTransform) или коллайдер кнопки; нормаль смотрит к пальцу.
    struct Плоскость { public Object Цель; public Vector3 Точка, Нормаль; }

    class Рука
    {
        public readonly List<XRHandSkeletonDriver> Скелеты = new List<XRHandSkeletonDriver>();
        public Vector3 Сдвиг, ПредПалец;
        public bool БылПалец;
        public Плоскость Упор, Отпущена, Подход;
        public LeverInteractable Рычаг;
        public Vector3 ХватЛок, ОтступЛок;   // точка хвата и ладонь от неё — в осях рычага
    }

    readonly Рука[] _руки = { new Рука(), new Рука() };
    static readonly List<XRHandSubsystem> _системы = new List<XRHandSubsystem>();
    XRHandSubsystem _подсистема;
    XROrigin _риг;
    float _следПоиск;
    static УпорРук _я;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Запуск()
    {
        if (_я != null) return;
        var go = new GameObject("УпорРук");
        DontDestroyOnLoad(go);
        _я = go.AddComponent<УпорРук>();
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= _следПоиск) НайтиСкелеты();
        _окна.RemoveAll(rt => rt == null);
        _кнопки.RemoveAll(и => и == null);
        bool можно = GameSettings.РежимВвода == GameSettings.Ввод.Руки && !ВидеоРук.Активно && НайтиРуки();
        for (int с = 0; с < 2; с++)
        {
            var р = _руки[с];
            Vector3 цель = Vector3.zero;
            float τ = 0f;
            var рука = можно ? (с == 0 ? _подсистема.leftHand : _подсистема.rightHand) : default;
            if (можно && рука.isTracked && рука.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose кончик))
            {
                var т = НаведениеПальцем.ПространствоРук(_риг);   // суставы — в пространстве трекинга (Camera Offset)
                Vector3 палец = т.TransformPoint(кончик.position);
                if (НаРычаге(р, с, рука, т, out Vector3 кРучке)) { цель = кРучке; τ = Прилипнуть_τ; р.Упор = default; }
                else { цель = Упор(р, палец); τ = цель == Vector3.zero ? Догнать_τ : 0f; }   // упор — сразу, иначе палец успеет уйти в окно
                р.ПредПалец = палец; р.БылПалец = true;
            }
            else { р.БылПалец = false; р.Упор = р.Отпущена = р.Подход = default; р.Рычаг = null; }

            р.Сдвиг = τ <= 0f ? цель : Vector3.Lerp(р.Сдвиг, цель, 1f - Mathf.Exp(-Time.unscaledDeltaTime / τ));
            if (р.Сдвиг.sqrMagnitude < 1e-8f) р.Сдвиг = Vector3.zero;
            foreach (var s in р.Скелеты)
            {
                if (s == null || s.rootTransform == null || s.rootTransform.parent == null) continue;
                if (р.Сдвиг == Vector3.zero) s.ResetRootPoseOffset(); else s.ApplyRootPoseOffset(р.Сдвиг);
            }
        }
    }

    // ---- Окна и кнопки ----

    // > 0 — подушечка пальца за поверхностью, на столько метров.
    static float Глубина(in Плоскость п, Vector3 палец) => Vector3.Dot(п.Точка - палец, п.Нормаль) + Палец_м;

    static bool Жива(Object ц) =>
        ц is Component к && к != null && к.gameObject.activeInHierarchy && (!(к is Collider c) || c.enabled);

    // Палец над целью: его проекция на плоскость лежит в окне / на грани кнопки (с запасом).
    static bool НадЦелью(in Плоскость п, Vector3 палец)
    {
        Vector3 наПлоскости = палец + п.Нормаль * Vector3.Dot(п.Точка - палец, п.Нормаль);
        if (п.Цель is RectTransform rt)
        {
            Vector3 л = rt.InverseTransformPoint(наПлоскости);
            float край = Край_м / Mathf.Max(1e-6f, rt.lossyScale.x);
            Rect r = rt.rect;
            return л.x >= r.xMin - край && л.x <= r.xMax + край && л.y >= r.yMin - край && л.y <= r.yMax + край;
        }
        return п.Цель is Collider c && Vector3.Distance(наПлоскости, Ближайшая(c, наПлоскости)) < Край_м;
    }

    // Сдвиг призрака (к пальцу по нормали), чтобы он стоял на поверхности; ноль — упора нет.
    Vector3 Упор(Рука р, Vector3 палец)
    {
        // Уже упёрлись: держим, пока подушечка за плоскостью, не глубже 6 см и над целью.
        if (р.Упор.Цель != null)
        {
            float г = Глубина(р.Упор, палец);
            if (!Жива(р.Упор.Цель) || г <= 0f || !НадЦелью(р.Упор, палец)) р.Упор = default;
            else if (г > Глубина_м) { р.Отпущена = р.Упор; р.Упор = default; }
            else return р.Упор.Нормаль * г;
        }
        // Призрак догнал руку внутри цели: снова держать она начнёт, когда палец выйдет назад или в сторону.
        if (р.Отпущена.Цель != null && (!Жива(р.Отпущена.Цель) || Глубина(р.Отпущена, палец) <= 0f || !НадЦелью(р.Отпущена, палец)))
            р.Отпущена = default;

        // Новый упор: за этот кадр подушечка прошла поверхность спереди назад.
        Плоскость найдено = default;
        float лучше = float.MaxValue;
        if (р.БылПалец)
        {
            foreach (var rt in _окна)
                if (Жива(rt) && rt != р.Отпущена.Цель)
                    Проверить(new Плоскость { Цель = rt, Точка = rt.position, Нормаль = -rt.forward }, р, палец, ref найдено, ref лучше);
            if (р.Подход.Цель != null && Жива(р.Подход.Цель) && р.Подход.Цель != р.Отпущена.Цель)
                Проверить(р.Подход, р, палец, ref найдено, ref лучше);
        }

        // Грань кнопки пульта, к которой палец подходит снаружи, — на следующий кадр.
        р.Подход = default;
        float ближе = Подход_м;
        foreach (var и in _кнопки)
        {
            if (!и.isActiveAndEnabled) continue;
            foreach (var c in и.colliders)
            {
                if (!Жива(c)) continue;
                Vector3 cp = Ближайшая(c, палец);
                float d = Vector3.Distance(палец, cp);
                if (d < 1e-4f || d >= ближе) continue;   // палец внутри (грань не понять) или далеко
                ближе = d;
                р.Подход = new Плоскость { Цель = c, Точка = cp, Нормаль = (палец - cp) / d };
            }
        }

        if (найдено.Цель == null) return Vector3.zero;
        р.Упор = найдено;
        return найдено.Нормаль * лучше;
    }

    static void Проверить(in Плоскость п, Рука р, Vector3 палец, ref Плоскость найдено, ref float лучше)
    {
        float до = Глубина(п, р.ПредПалец), после = Глубина(п, палец);
        if (до <= 0f && после > 0f && после <= Глубина_м && после < лучше && НадЦелью(п, палец))
        {
            лучше = после;
            найдено = п;
        }
    }

    static Vector3 Ближайшая(Collider к, Vector3 p) =>
        к is MeshCollider м && !м.convex ? к.bounds.ClosestPoint(p) : к.ClosestPoint(p);

    // ---- Рычаг ----

    // Рука держит рычаг (захватом, не тычком): сдвиг, ставящий ладонь-призрак на точку хвата.
    bool НаРычаге(Рука р, int с, XRHand рука, Transform т, out Vector3 сдвиг)
    {
        сдвиг = Vector3.zero;
        LeverInteractable держит = null;
        foreach (var л in LeverInteractable.Все)
            if (л != null && л.ДержитРука(с)) { держит = л; break; }
        if (держит == null || !рука.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose п)) { р.Рычаг = null; return false; }

        Vector3 ладонь = т.TransformPoint(п.position);
        var ось = держит.transform;
        if (р.Рычаг != держит)
        {
            // Только что взяли: точка хвата — место рукоятки, ближайшее к ладони; ладонь от
            // неё не дальше 3 см (рука могла чуть промахнуться). Всё — в осях рычага,
            // чтобы точка ходила вместе с ним.
            р.Рычаг = держит;
            Vector3 хват = ось.position;
            float лучше = float.MaxValue;
            if (держит.Интерактабл != null)
                foreach (var c in держит.Интерактабл.colliders)
                {
                    if (c == null || !c.enabled) continue;
                    Vector3 cp = Ближайшая(c, ладонь);
                    float d = (cp - ладонь).sqrMagnitude;
                    if (d < лучше) { лучше = d; хват = cp; }
                }
            р.ХватЛок = ось.InverseTransformPoint(хват);
            р.ОтступЛок = ось.InverseTransformVector(Vector3.ClampMagnitude(ладонь - хват, ХватМакс_м));
        }
        сдвиг = ось.TransformPoint(р.ХватЛок) + ось.TransformVector(р.ОтступЛок) - ладонь;
        return true;
    }

    // ---- Поиск ----

    void НайтиСкелеты()
    {
        _следПоиск = Time.unscaledTime + 1f;   // риг меняется при загрузке сцены
        if (_риг == null) _риг = FindFirstObjectByType<XROrigin>();
        foreach (var р in _руки) р.Скелеты.Clear();
        if (_риг == null) return;
        foreach (var s in _риг.GetComponentsInChildren<XRHandSkeletonDriver>(true))
        {
            var события = s.handTrackingEvents;
            int с = события != null ? (события.handedness == Handedness.Left ? 0 : 1)
                                     : (s.name.ToLowerInvariant().Contains("left") ? 0 : 1);
            _руки[с].Скелеты.Add(s);
        }
        foreach (var d in _риг.GetComponentsInChildren<XRHandSkeletonPokeDisplacer>(true))
            if (d.enabled) d.enabled = false;
    }

    bool НайтиРуки()
    {
        if (_подсистема == null || !_подсистема.running)
        {
            _подсистема = null;
            SubsystemManager.GetSubsystems(_системы);
            foreach (var с in _системы) if (с.running) { _подсистема = с; break; }
            if (_подсистема == null) return false;
        }
        return _риг != null && _риг.Origin != null;
    }
}
