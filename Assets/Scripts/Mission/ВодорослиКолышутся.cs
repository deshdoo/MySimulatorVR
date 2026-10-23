using System.Collections.Generic;
using UnityEngine;

// Водоросли колышутся: медленно качаются от течения (сильнее, когда оно быстрее) и
// отклоняются от аппарата, когда он проходит рядом (струя движителей), а потом
// пружинисто возвращаются. Каждая водоросль наклоняется целиком вокруг корня — без
// своего шейдера, дёшево для Quest; считаются только те, что в 40 м от камеры.
// Течение — RovController.Течение (то же поле, что сносит аппарат). Вешает ДекорДна.
public class ВодорослиКолышутся : MonoBehaviour
{
    [Tooltip("Насколько течение 1 м/с наклоняет водоросль, рад.")]
    public float ОтТечения = 0.5f;
    [Tooltip("Наклон от струи аппарата вплотную, рад.")]
    public float ОтАппарата = 0.7f;
    [Tooltip("На каком расстоянии от аппарата ещё чувствуется струя, м.")]
    public float РадиусСтруи_м = 3f;

    class Стебель
    {
        public Transform Корень;
        public Quaternion Исходный;
        public float Фаза, Жёсткость;
        public Vector3 Наклон, СкоростьНаклона;   // горизонтальный вектор наклона, рад
    }

    readonly List<Стебель> _стебли = new List<Стебель>();
    WaterCurrent _течение;
    Rigidbody _аппарат;

    public void Добавить(Transform корень, System.Random rng)
    {
        _стебли.Add(new Стебель
        {
            Корень = корень, Исходный = корень.localRotation,
            Фаза = (float)rng.NextDouble() * 10f, Жёсткость = Mathf.Lerp(3f, 6f, (float)rng.NextDouble()),
        });
    }

    void Start()
    {
        // Течение живёт в аппарате (RovController.Течение) — то же поле, что его сносит.
        var rov = FindAnyObjectByType<RovController>();
        if (rov != null) { _аппарат = rov.GetComponent<Rigidbody>(); _течение = rov.Течение; }
    }

    void Update()
    {
        var cam = RovCameraRig.Бортовая();
        if (cam == null) cam = Camera.main;
        Vector3 глаз = cam != null ? cam.transform.position : Vector3.zero;
        float dt = Mathf.Min(Time.deltaTime, 0.05f), t = Time.time;
        Vector3 поз = _аппарат != null ? _аппарат.position : new Vector3(1e6f, 0f, 0f);
        float скоростьАппарата = _аппарат != null ? _аппарат.linearVelocity.magnitude : 0f;

        foreach (var с in _стебли)
        {
            if (с.Корень == null) continue;
            Vector3 p = с.Корень.position;
            if (cam != null && (p - глаз).sqrMagnitude > 1600f) continue;

            // Течение: средний наклон по потоку и покачивание вдоль него.
            Vector3 вода = _течение != null ? _течение.Скорость(p) : Vector3.zero;
            вода.y = 0f;
            Vector3 цель = вода * ОтТечения * (1f + 0.35f * Mathf.Sin(t * 0.9f + с.Фаза));
            // Слабое собственное покачивание даже в стоячей воде.
            цель += new Vector3(Mathf.Sin(t * 0.6f + с.Фаза), 0f, Mathf.Cos(t * 0.5f + с.Фаза * 1.3f)) * 0.04f;
            // Струя аппарата: отталкивает от себя, сильнее вблизи и на ходу.
            Vector3 отАппарата = p - поз; отАппарата.y = 0f;
            float d = отАппарата.magnitude;
            if (d < РадиусСтруи_м && d > 0.01f)
                цель += отАппарата / d * ОтАппарата * (1f - d / РадиусСтруи_м) * Mathf.Clamp01(0.3f + скоростьАппарата);

            // Пружина с затуханием — наклоняется и возвращается плавно, с лёгким перехлёстом.
            с.СкоростьНаклона += ((цель - с.Наклон) * с.Жёсткость - с.СкоростьНаклона * 2.2f) * dt;
            с.Наклон += с.СкоростьНаклона * dt;
            с.Наклон = Vector3.ClampMagnitude(с.Наклон, 0.9f);

            float угол = с.Наклон.magnitude;
            с.Корень.localRotation = угол < 1e-4f ? с.Исходный
                : Quaternion.AngleAxis(угол * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, с.Наклон / угол)) * с.Исходный;
        }
    }
}
