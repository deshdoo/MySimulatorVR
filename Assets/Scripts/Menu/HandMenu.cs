using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

// Меню на ладони — для управления руками, без контроллеров (у рук нет кнопки Menu).
// Разверните ПРАВУЮ ладонь к себе, пальцы прямые, и подержите полсекунды — рядом с рукой
// (левее, к середине тела) появится небольшое окно. Оно остаётся висеть на месте, поэтому
// кнопки можно нажимать пальцем той же правой руки (или левой):
//   • ИТОГИ И ПАУЗА — окно итогов (MissionDebriefPanel), пока оно открыто, сцена стоит;
//   • СПРАВКА — окно «?» (среда и работа приборов);
//   • НАСТРОЙКИ — звук и управление, как в главном меню, меняются сразу (НастройкиВИгре);
//     там же руки или контроллеры;
//   • ВЫХОД — окно выхода с сохранением;
//   • × — закрыть. Само закрывается через 12 с без нажатий.
// Положение руки — из XR Hands (суставы, которые строит гарнитура). Работает только в
// миссии и только когда гарнитура видит руку. Создаётся само при запуске.
//
// С контроллерами то же меню открывает кнопка «Меню» (Menu левого или B правого,
// ControllerBindings) — рядом с этим контроллером; с клавиатуры — Esc (перед глазами:
// в эмуляторе в режиме рук контроллеров нет, а жест ладонью мышью не сделать). Та же
// кнопка закрывает меню, а если открыто окно итогов или настроек — закрывает его.
public class МенюНаЛадони : MonoBehaviour
{
    const float Удержать_с = 0.5f, Живёт_с = 12f;
    const float W = 640f, H = 330f;

    static readonly List<XRHandSubsystem> _системы = new List<XRHandSubsystem>();
    static readonly XRHandJointID[] Кончики = { XRHandJointID.IndexTip, XRHandJointID.MiddleTip };
    XRHandSubsystem _руки;
    XROrigin _риг;
    float _держим, _закрытьВ;
    bool _отпущен = true;   // открыть снова — только после того, как ладонь развернули обратно
    VrPanel _панель;
    RectTransform _экран;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Создать()
    {
        if (FindFirstObjectByType<МенюНаЛадони>() != null) return;
        var go = new GameObject("МенюНаЛадони");
        DontDestroyOnLoad(go);
        go.AddComponent<МенюНаЛадони>();
    }

    void Update()
    {
        if (_панель != null)
        {
            _панель.Обновить();
            if (Time.unscaledTime > _закрытьВ) Закрыть();
        }
        bool кнопкой = ControllerBindings.МенюНажато(out int рукаКнопки);
        var kb = Keyboard.current;
        bool клавишей = kb != null && kb[KeyBindings.K(Key.Escape)].wasPressedThisFrame;
        // Пока ждём новую кнопку для переназначения, нажатие — это она, а не «Меню».
        if ((кнопкой || клавишей) && !MainMenu.Открыто && MissionManager.Instance != null && !НастройкиВИгре.ЖдёмКнопку)
        {
            var итоги = FindFirstObjectByType<MissionDebriefPanel>();
            if (НастройкиВИгре.Открыто) НастройкиВИгре.Закрыть();
            else if (итоги != null && итоги.Видно) { if (!итоги.Итоговое) итоги.Скрыть(); }
            else if (_панель != null) Закрыть();
            else ОткрытьУКонтроллера(кнопкой ? рукаКнопки : -1);
            return;
        }
        if (MainMenu.Открыто || НастройкиВИгре.Открыто || MissionManager.Instance == null || !ПраваяЛадонь(out Vector3 ладонь, out Vector3 кНаружи, out Vector3 голова))
        {
            if (MainMenu.Открыто && _панель != null) Закрыть();
            _держим = 0f; _отпущен = true;
            return;
        }
        bool жест = Vector3.Dot(кНаружи, (голова - ладонь).normalized) > 0.7f;
        if (!жест) { _держим = 0f; _отпущен = true; return; }
        if (!_отпущен || _панель != null) return;
        _держим += Time.unscaledDeltaTime;
        if (_держим < Удержать_с) return;
        _отпущен = false;
        Открыть(ладонь, кНаружи, голова);
    }

    // Правая ладонь в мире: центр, направление «из ладони» и голова. false — руку не видно
    // или пальцы сжаты (жест — открытая ладонь, а не кулак).
    bool ПраваяЛадонь(out Vector3 ладонь, out Vector3 кНаружи, out Vector3 голова)
    {
        ладонь = кНаружи = голова = Vector3.zero;
        if (_руки == null || !_руки.running)
        {
            _руки = null;
            SubsystemManager.GetSubsystems(_системы);
            foreach (var с in _системы) if (с.running) { _руки = с; break; }
            if (_руки == null) return false;
        }
        if (_риг == null) _риг = FindFirstObjectByType<XROrigin>();
        if (_риг == null || _риг.Camera == null) return false;
        var рука = _руки.rightHand;
        if (!рука.isTracked) return false;
        var т = _риг.Origin.transform;   // суставы — относительно начала XR Origin
        Pose корень = рука.rootPose;
        if (рука.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose п)) корень = п;
        ладонь = т.TransformPoint(корень.position);
        кНаружи = т.TransformDirection(корень.rotation * Vector3.down);   // как XRHandAxis.PalmDirection
        голова = _риг.Camera.transform.position;
        // Пальцы прямые: кончики указательного и среднего далеко от ладони (в кулаке — рядом).
        foreach (var id in Кончики)
            if (!рука.GetJoint(id).TryGetPose(out Pose кончик) || Vector3.Distance(т.TransformPoint(кончик.position), ладонь) < 0.07f)
                return false;
        return true;
    }

    // Окно встаёт РЯДОМ с рукой, а не над ладонью: левее правой кисти (к середине тела), на
    // той же дальности от глаз, чуть выше, лицом к глазам. Между краем окна и кистью —
    // ~4 см: развёрнутая к себе рука его не закрывает, и само оно не наезжает на руку.
    // Дальность не ближе 35 и не дальше 55 см — текст читается без напряжения.
    const float ШиринаОкна_м = 0.30f, ЗазорОтРуки_м = 0.09f;

    // Меню кнопкой контроллера: так же, как у ладони, — сбоку от контроллера к середине
    // тела. Клавишей (рука -1) или контроллер не виден — перед глазами, чуть ниже.
    void ОткрытьУКонтроллера(int рука)
    {
        if (_риг == null) _риг = FindFirstObjectByType<XROrigin>();
        if (_риг == null || _риг.Camera == null) return;
        var камера = _риг.Camera.transform;
        var м = _риг.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager>();
        var контроллер = м == null || рука < 0 ? null : рука == 0 ? м.leftController : м.rightController;
        bool уКонтроллера = контроллер != null && контроллер.activeInHierarchy;
        Vector3 где = уКонтроллера
            ? контроллер.transform.position
            : камера.position + Vector3.ProjectOnPlane(камера.forward, Vector3.up).normalized * 0.45f + Vector3.down * 0.2f;
        Открыть(где, Vector3.zero, камера.position, !уКонтроллера ? 0 : рука == 0 ? -1 : 1);
    }

    // сторона: 1 — окно левее руки (правая рука), -1 — правее (левая), 0 — по центру.
    void Открыть(Vector3 ладонь, Vector3 кНаружи, Vector3 голова, int сторона = 1)
    {
        var hud = FindFirstObjectByType<MissionHudDisplay>();
        _панель = new VrPanel("МенюНаЛадони", W, H, ШиринаОкна_м, _риг.gameObject.scene, hud != null ? hud.шрифт : null, true);
        Vector3 кРуке = ладонь - голова;
        Vector3 вперёд = Vector3.ProjectOnPlane(кРуке, Vector3.up);
        вперёд = вперёд.sqrMagnitude > 1e-4f ? вперёд.normalized : Vector3.ProjectOnPlane(_риг.Camera.transform.forward, Vector3.up).normalized;
        Vector3 вправо = Vector3.Cross(Vector3.up, вперёд);
        float дист = Mathf.Clamp(кРуке.magnitude, 0.35f, 0.55f);
        Vector3 поз = голова + кРуке.normalized * дист - вправо * сторона * (ШиринаОкна_м * 0.5f + ЗазорОтРуки_м) + Vector3.up * 0.04f;
        _панель.Корень.transform.SetPositionAndRotation(поз, Quaternion.LookRotation(поз - голова, Vector3.up));
        _панель.Окно();
        _экран = _панель.Контейнер(_панель.Rt, "Экран");
        var p = _панель;
        var з = p.Текст(_экран, 28, 18, 400, 36, 28, p.ЦветТекста, перенос: false);
        з.fontStyle = FontStyles.Bold; з.text = "Меню";
        p.Текст(_экран, 140, 26, W - 168, 28, 19, p.ЦветВтор, перенос: false).text =
            GameSettings.РежимВвода == GameSettings.Ввод.Руки ? "коснитесь кнопки пальцем" : "наведите луч и нажмите курок";

        void Кнопка(float x, float y, string надпись, Color цвет, System.Action действие) =>
            p.Кнопка(_экран, x, y, 284, 108, надпись, цвет, () => { Закрыть(); действие(); }, кегль: 24);
        Кнопка(28, 72, "<b>ИТОГИ И ПАУЗА</b>", p.ЦветАкцент, () => FindFirstObjectByType<MissionDebriefPanel>()?.Показать());
        Кнопка(328, 72, "<b>СПРАВКА</b>", p.ЦветАкцент, CabinPanels.ПереключитьСреду);
        Кнопка(28, 196, "<b>НАСТРОЙКИ</b>", p.ЦветВтор, НастройкиВИгре.Открыть);
        Кнопка(328, 196, "<b>ВЫХОД</b>", p.ЦветПлохо, CabinPanels.ПереключитьВыход);
        p.Кнопка(_экран, W - 78, 16, 50, 46, "×", p.ЦветВтор, Закрыть, кегль: 32);

        VrPanel.ЗаблокироватьНажатия(0.5f);   // палец мог оказаться у кнопки в миг появления
        UiSound.Клик();
        _закрытьВ = Time.unscaledTime + Живёт_с;
    }

    void Закрыть()
    {
        if (_панель != null) _панель.Уничтожить();
        _панель = null;
    }
}
