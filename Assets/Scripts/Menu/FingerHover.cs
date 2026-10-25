using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Подсветка «палец рядом» — для управления руками (Настройки → Управление → Руки).
// С контроллерами кнопку подсвечивает луч, у рук луча нет (дальний луч рук выключен,
// см. РежимВводаРига): кнопка окна, кнопка пульта или рычаг загорается, когда кончик
// указательного пальца подошёл к ней ближе 10 см, ещё до касания. Из нескольких
// близких целей горит одна — ближайшая к пальцу (у каждой руки своя).
//
// Цели регистрируются сами (VrPanel, окно итогов, значок кабины, кнопки пульта,
// рычаги) и спрашивают Наведён(...) там, где раньше смотрели только isHovered.
// Расчёт — один раз за кадр. Палец — сустав IndexTip из XR Hands (относительно начала
// XR Origin), как в меню на ладони.
public static class НаведениеПальцем
{
    public const float Радиус_м = 0.10f;

    static readonly List<XRBaseInteractable> _цели = new List<XRBaseInteractable>();
    static readonly XRBaseInteractable[] _наведены = new XRBaseInteractable[2];   // левая, правая
    static readonly Vector3[] _палец = new Vector3[2], _былПалец = new Vector3[2];
    static readonly bool[] _естьПалец = new bool[2], _былЛиПалец = new bool[2];
    static readonly List<XRHandSubsystem> _системы = new List<XRHandSubsystem>();
    static XRHandSubsystem _руки;
    static XROrigin _риг;
    static int _кадр = -1;

    public static void Добавить(XRBaseInteractable и)
    {
        if (и != null && !_цели.Contains(и)) _цели.Add(и);
    }

    public static bool Наведён(XRBaseInteractable и)
    {
        if (и == null) return false;
        Обновить();
        return _наведены[0] == и || _наведены[1] == и;
    }

    // Кончик указательного (0 — левая, 1 — правая) сейчас и в прошлый расчёт: для нажатия
    // «палец прошёл сквозь кнопку» (VrPanel). false — руку не видно оба раза.
    public static bool Палец(int рука, out Vector3 сейчас, out Vector3 было)
    {
        Обновить();
        сейчас = _палец[рука]; было = _былПалец[рука];
        return _естьПалец[рука] && _былЛиПалец[рука];
    }

    static void Обновить()
    {
        if (_кадр == Time.frameCount) return;
        _кадр = Time.frameCount;
        _наведены[0] = _наведены[1] = null;
        for (int с = 0; с < 2; с++) { _былПалец[с] = _палец[с]; _былЛиПалец[с] = _естьПалец[с]; _естьПалец[с] = false; }
        if (GameSettings.РежимВвода != GameSettings.Ввод.Руки || !НайтиРуки()) return;
        _цели.RemoveAll(и => и == null);   // кнопки пересобранных окон уничтожены
        var т = ПространствоРук(_риг);
        for (int с = 0; с < 2; с++)
        {
            var рука = с == 0 ? _руки.leftHand : _руки.rightHand;
            if (!рука.isTracked || !рука.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose кончик)) continue;
            Vector3 палец = т.TransformPoint(кончик.position);
            _палец[с] = палец; _естьПалец[с] = true;
            float лучше = Радиус_м;
            foreach (var и in _цели)
            {
                if (!и.isActiveAndEnabled) continue;   // скрытое окно, выключенный пульт
                foreach (var к in и.colliders)
                {
                    if (к == null || !к.enabled || !к.gameObject.activeInHierarchy) continue;
                    float d = Vector3.Distance(палец, Ближайшая(к, палец));
                    if (d < лучше) { лучше = d; _наведены[с] = и; }
                }
            }
        }
    }

    // Пространство, в котором XR Hands отдаёт суставы, — пространство трекинга: объект
    // Camera Offset рига, а не его корень. У рига режим «от гарнитуры» (Device), и Camera
    // Offset поднят на рост (~1,1 м): от корня все точки рук выходили на метр ниже видимой
    // руки — рычаг «в 103 см», палец не доставал до кнопок.
    public static Transform ПространствоРук(XROrigin риг) =>
        риг.CameraFloorOffsetObject != null ? риг.CameraFloorOffsetObject.transform : риг.Origin.transform;

    // ClosestPoint работает с коробкой, сферой, капсулой и выпуклым мешем; для
    // невыпуклого меша вернул бы саму точку (расстояние 0) — берём его габарит.
    static Vector3 Ближайшая(Collider к, Vector3 p) =>
        к is MeshCollider м && !м.convex ? к.bounds.ClosestPoint(p) : к.ClosestPoint(p);

    static bool НайтиРуки()
    {
        if (_руки == null || !_руки.running)
        {
            _руки = null;
            SubsystemManager.GetSubsystems(_системы);
            foreach (var с in _системы) if (с.running) { _руки = с; break; }
            if (_руки == null) return false;
        }
        if (_риг == null) _риг = Object.FindFirstObjectByType<XROrigin>();
        return _риг != null && _риг.Origin != null;
    }
}
