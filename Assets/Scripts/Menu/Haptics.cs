using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Отдача контроллеров Touch — короткая вибрация: щелчок кнопки, захват рычага, упор
// рычага в крайнее положение, щелчок нейтрали. Вибромотор — у контроллера (компонент
// HapticImpulsePlayer рига XRI); у рук его нет — для рук вызов ничего не делает,
// отдачу там заменяют звук щелчка и подсветка.
public static class Вибро
{
    public const float Кнопка = 0.45f, Захват = 0.3f, Упор = 0.65f, Нейтраль = 0.2f;

    public static void Импульс(IXRInteractor кто, float сила, float длительность_с = 0.035f)
    {
        var к = кто as Component;
        if (к == null) return;
        var плеер = к.GetComponentInParent<HapticImpulsePlayer>();
        if (плеер != null) плеер.SendHapticImpulse(сила, длительность_с);
    }
}
