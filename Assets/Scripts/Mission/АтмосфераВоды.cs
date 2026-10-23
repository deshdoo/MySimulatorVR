using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Подводная атмосфера в миссии — по образцу: сине-зелёный оттенок, чуть темнее края кадра.
//
//   • мути своей не добавляет: в проекте уже есть подводная муть (полноэкранный эффект
//     DepthFog в UnderwaterRenderer, расчищается в конусе прожектора) — второй туман
//     поверх неё делал воду непроглядной;
//   • постобработка — свой глобальный объём на слое Underwater: его подхватывает только
//     камера аппарата (ей добавляется этот слой), кабина и меню не зеленеют. Только
//     дешёвое для Quest и мягко: лёгкая цветокоррекция и виньетка, без bloom.
// Вешает МагистральныйТрубопровод при запуске.
public class АтмосфераВоды : MonoBehaviour
{
    Camera _борт;
    float _проверка;

    void Start()
    {
        СоздатьОбъём();
    }

    void LateUpdate()
    {
        if (Time.unscaledTime < _проверка) return;
        _проверка = Time.unscaledTime + 0.5f;
        var борт = RovCameraRig.Бортовая();
        if (борт != null && борт != _борт)
        {
            _борт = борт;
            var данные = борт.GetUniversalAdditionalCameraData();
            данные.renderPostProcessing = true;
            данные.volumeLayerMask |= 1 << gameObject.layer;
        }
    }

    void СоздатьОбъём()
    {
        var go = new GameObject("Подводная цветокоррекция") { layer = gameObject.layer };
        go.transform.SetParent(transform, false);
        var объём = go.AddComponent<Volume>();
        объём.isGlobal = true;
        объём.priority = 5f;
        var профиль = ScriptableObject.CreateInstance<VolumeProfile>();
        var цвет = профиль.Add<ColorAdjustments>(true);
        цвет.colorFilter.Override(new Color(0.92f, 1f, 0.97f));
        цвет.saturation.Override(-8f);
        цвет.contrast.Override(8f);
        var виньетка = профиль.Add<Vignette>(true);
        виньетка.intensity.Override(0.28f);
        виньетка.smoothness.Override(0.5f);
        виньетка.color.Override(new Color(0f, 0.03f, 0.04f));
        объём.sharedProfile = профиль;
    }
}
