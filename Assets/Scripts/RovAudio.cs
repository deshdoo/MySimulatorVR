using UnityEngine;

// Звук работы аппарата — «динамики пульта»: оператор сидит в кабине, звук
// приходит с борта по кабелю (гидрофон / датчики), поэтому он не объёмный (2D).
//   • подводный фон — всегда, тихо;
//   • гул движителей — громкость и высота тона растут с тягой (DroneInput);
//   • глухой удар — при столкновении (RovHealthMonitor.ПриУдаре), громче при большей энергии;
//   • «пинг» — когда дефект зафиксирован осмотром.
// Громкость — по категориям из настроек (Окружение / Двигатели), общая — через
// AudioListener.volume. Все клипы синтезированы кодом (ПроцЗвук), файлов нет.
//
// Ставит на аппарат RovController сам. Пока сцена заморожена (меню, окно
// итогов), компонент выключен — звук молчит (плюс AudioListener.pause).
[DisallowMultipleComponent]
public class RovAudio : MonoBehaviour
{
    AudioSource _фон, _мотор, _события;
    AudioLowPassFilter _фильтр;
    float _тяга;

    void Awake()
    {
        _фон = Источник(ПроцЗвук.Окружение(), true);
        _мотор = Источник(ПроцЗвук.Мотор(), true);
        _мотор.volume = 0f;
        _события = Источник(null, false);
    }

    AudioSource Источник(AudioClip клип, bool цикл)
    {
        // Каждый источник — на своём дочернем объекте: фильтр НЧ на моторе не должен
        // глушить остальные звуки (фильтр действует на все источники объекта).
        var go = new GameObject("Звук_" + (клип != null ? клип.name : "События"));
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.clip = клип; s.loop = цикл; s.spatialBlend = 0f; s.playOnAwake = false;
        if (цикл) s.Play();
        return s;
    }

    void OnEnable()
    {
        RovHealthMonitor.ПриУдаре += НаУдар;
        foreach (var d in FindObjectsByType<PipelineDefect>(FindObjectsSortMode.None))
            d.ПриОбнаружении?.AddListener(НаДефект);
        if (_фон != null && !_фон.isPlaying) _фон.Play();
        if (_мотор != null && !_мотор.isPlaying) _мотор.Play();
    }

    void OnDisable()
    {
        RovHealthMonitor.ПриУдаре -= НаУдар;
        foreach (var d in FindObjectsByType<PipelineDefect>(FindObjectsSortMode.None))
            d.ПриОбнаружении?.RemoveListener(НаДефект);
        if (_фон != null) _фон.Pause();
        if (_мотор != null) _мотор.Pause();
    }

    void Update()
    {
        if (_фильтр == null) _фильтр = _мотор.gameObject.AddComponent<AudioLowPassFilter>();

        float цель = Mathf.Clamp01(Mathf.Max(Mathf.Abs(DroneInput.forward), Mathf.Abs(DroneInput.lateral),
                                             Mathf.Abs(DroneInput.vertical), Mathf.Abs(DroneInput.yaw)));
        // Автопилот держит аппарат САМ: движители всё время подрабатывают мелкими
        // поправками, даже когда команды крошечные, — слышен ровный рабочий гул
        // с медленной «подстройкой».
        var ап = RovAutopilot.Instance;
        if (ап != null && ап.Engaged)
            цель = Mathf.Max(цель, 0.35f + 0.08f * Mathf.PerlinNoise(Time.time * 0.4f, 0.3f));

        // Винт раскручивается/тормозит плавно.
        _тяга = Mathf.MoveTowards(_тяга, цель, Time.deltaTime * 0.8f);
        _мотор.volume = 0.45f * Mathf.SmoothStep(0f, 1f, _тяга) * GameSettings.ГромкостьДвигателей;
        _мотор.pitch = 0.85f + 0.3f * _тяга;
        _фильтр.cutoffFrequency = Mathf.Lerp(900f, 2200f, _тяга);   // на малой тяге — глуше, но слышно
        _фон.volume = 0.3f * GameSettings.ГромкостьОкружения;
    }

    void НаУдар(float энергия, string объект)
    {
        float громк = Mathf.Clamp01(0.3f + энергия / 400f);
        _события.PlayOneShot(ПроцЗвук.Удар(), громк * GameSettings.ГромкостьОкружения);
    }

    void НаДефект()
    {
        if (MissionResume.ИдётВосстановление) return;   // продолжение миссии «находит» старые дефекты — без пингов
        _события.PlayOneShot(ПроцЗвук.Пинг(), 0.5f * GameSettings.ГромкостьИнтерфейса);
    }
}
