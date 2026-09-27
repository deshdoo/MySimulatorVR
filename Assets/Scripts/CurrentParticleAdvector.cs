// =============================================================================
// CurrentParticleAdvector.cs
// -----------------------------------------------------------------------------
// Делает течение ВИДИМЫМ: взвесь в воде (частицы UnderwaterParticles в сцене)
// переносится тем же полем скоростей WaterCurrent, что сносит аппарат. Реальный
// пилот ROV оценивает течение именно так — по дрейфу частиц в луче прожектора.
// Проходящий вихрь виден как закрутка взвеси, средний поток — как общий дрейф.
//
// Мелкая взвесь почти нейтрально плавуча и имеет ничтожное число Стокса: она
// подстраивается под скорость воды за доли секунды. Поэтому скорость частицы
// не приравнивается к скорости воды скачком, а релаксирует к ней с постоянной
// времени τ (апериодическое звено), плюс медленное оседание («морской снег»):
//     v += (v_воды + v_оседания − v)·(1 − e^(−dt/τ))
//
// Добавляется автоматически из RovController (Start), если включено
// «Течение → ПереноситьВзвесь». Системы частиц ищутся по имени (ИмяВзвеси);
// должны симулироваться в мировых координатах (Simulation Space = World).
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CurrentParticleAdvector : MonoBehaviour
{
    [Tooltip("Время подстройки частицы под скорость воды, с. Меньше — точнее следует потоку.")]
    public float ВремяРелаксации_с = 0.4f;
    [Tooltip("Скорость оседания взвеси, м/с (морской снег: 0.001–0.01)")]
    public float СкоростьОседания = 0.004f;

    private RovController _rov;
    private readonly List<ParticleSystem> _системы = new List<ParticleSystem>();
    private ParticleSystem.Particle[] _буфер = new ParticleSystem.Particle[0];
    private float _следующийПоиск;

    void Awake() { _rov = GetComponent<RovController>(); }

    void LateUpdate()
    {
        var поле = _rov != null ? _rov.Течение : null;
        if (поле == null || !поле.включено) return;

        // Кабина (с камерами и их взвесью) может догрузиться позже — периодически ищем заново.
        if (Time.time >= _следующийПоиск) { НайтиСистемы(поле.ИмяВзвеси); _следующийПоиск = Time.time + 5f; }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float s = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, ВремяРелаксации_с));
        Vector3 оседание = Vector3.down * СкоростьОседания;

        foreach (var ps in _системы)
        {
            if (ps == null || !ps.gameObject.activeInHierarchy) continue;
            if (ps.main.simulationSpace != ParticleSystemSimulationSpace.World) continue;
            int n = ps.particleCount;
            if (n == 0) continue;
            if (_буфер.Length < n) _буфер = new ParticleSystem.Particle[ps.main.maxParticles];
            n = ps.GetParticles(_буфер);
            for (int i = 0; i < n; i++)
            {
                Vector3 vВоды = поле.Скорость(_буфер[i].position) + оседание;
                _буфер[i].velocity += (vВоды - _буфер[i].velocity) * s;
            }
            ps.SetParticles(_буфер, n);
        }
    }

    void НайтиСистемы(string имя)
    {
        _системы.Clear();
        if (string.IsNullOrEmpty(имя)) return;
        foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            if (ps.name.Contains(имя)) _системы.Add(ps);
    }
}
