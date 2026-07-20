using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Визуальные эффекты повреждения бортовой камеры НПА.
// Реализует пост-обработку: шум плёнки (Film Grain), искажение объектива
// (Lens Distortion) и хроматическую аберрацию (Chromatic Aberration).
//
// Уровень эффектов зависит от RovSystems.cameraDamage:
//   0 — чисто (норма)
//   1 — лёгкий шум (Warning)
//   2 — сильный шум + искажение + аберрация (Critical)
//
// Использование:
//   1. Создай Volume на объекте (можно на ROV или на ROV.Main Camera)
//   2. Создай Volume Profile (New)
//   3. Добавь Overrides: Film Grain, Lens Distortion, Chromatic Aberration
//   4. Перетащи Volume в поле этого скрипта
public class CameraDamageEffect : MonoBehaviour
{
    [Header("Volume с эффектами пост-обработки")]
    public Volume volume;

    [Header("Интенсивность шума плёнки")]
    public float шумПриWarning  = 0.45f;
    public float шумПриCritical = 0.85f;

    [Header("Искажение объектива (отрицательное = пинч)")]
    public float искажениеПриWarning  = -0.15f;
    public float искажениеПриCritical = -0.45f;

    [Header("Хроматическая аберрация")]
    public float аберрацияПриWarning  = 0.25f;
    public float аберрацияПриCritical = 0.75f;

    [Header("Скорость перехода (сек)")]
    public float скоростьПерехода = 0.5f;

    private FilmGrain _grain;
    private LensDistortion _distortion;
    private ChromaticAberration _chroma;

    private float _currentNoise;
    private float _currentDistortion;
    private float _currentChroma;

    void Start()
    {
        if (volume == null || volume.profile == null)
        {
            Debug.LogWarning("[CameraDamageEffect] Не назначен Volume или Profile");
            return;
        }

        volume.profile.TryGet(out _grain);
        volume.profile.TryGet(out _distortion);
        volume.profile.TryGet(out _chroma);

        if (_grain == null)
            Debug.LogWarning("[CameraDamageEffect] В Volume Profile нет Film Grain");
        if (_distortion == null)
            Debug.LogWarning("[CameraDamageEffect] В Volume Profile нет Lens Distortion");
        if (_chroma == null)
            Debug.LogWarning("[CameraDamageEffect] В Volume Profile нет Chromatic Aberration");
    }

    void Update()
    {
        // === ОТЛАДКА: клавиша K — циклически меняет уровень повреждения камеры ===
        if (Keyboard.current != null && Keyboard.current[Key.K].wasPressedThisFrame)
        {
            RovSystems.cameraDamage = (RovSystems.cameraDamage + 1) % 3;
            Debug.Log($"[CameraDamage] Тест: уровень={RovSystems.cameraDamage}");
        }

        // Целевые значения по уровню повреждения
        float targetNoise = 0f, targetDistortion = 0f, targetChroma = 0f;
        switch (RovSystems.cameraDamage)
        {
            case 1:
                targetNoise = шумПриWarning;
                targetDistortion = искажениеПриWarning;
                targetChroma = аберрацияПриWarning;
                break;
            case 2:
                targetNoise = шумПриCritical;
                targetDistortion = искажениеПриCritical;
                targetChroma = аберрацияПриCritical;
                break;
        }

        // Плавный переход
        float k = Time.deltaTime / Mathf.Max(0.01f, скоростьПерехода);
        _currentNoise      = Mathf.Lerp(_currentNoise,      targetNoise,      k);
        _currentDistortion = Mathf.Lerp(_currentDistortion, targetDistortion, k);
        _currentChroma     = Mathf.Lerp(_currentChroma,     targetChroma,     k);

        // Применяем
        if (_grain != null)
        {
            _grain.active = true;
            _grain.intensity.value = _currentNoise;
        }
        if (_distortion != null)
        {
            _distortion.active = true;
            _distortion.intensity.value = _currentDistortion;
        }
        if (_chroma != null)
        {
            _chroma.active = true;
            _chroma.intensity.value = _currentChroma;
        }
    }
}
