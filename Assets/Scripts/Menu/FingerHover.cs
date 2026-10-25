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

    static void Обновить()
    {
        if (_кадр == Time.frameCount) return;
        _кадр = Time.frameCount;
        _наведены[0] = _наведены[1] = null;
        if (GameSettings.РежимВвода != GameSettings.Ввод.Руки || !НайтиРуки()) return;
        _цели.RemoveAll(и => и == null);   // кнопки пересобранных окон уничтожены
        var т = _риг.Origin.transform;
        for (int с = 0; с < 2; с++)
        {
            var рука = с == 0 ? _руки.leftHand : _руки.rightHand;
            if (!рука.isTracked || !рука.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose кончик)) continue;
            Vector3 палец = т.TransformPoint(кончик.position);
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
