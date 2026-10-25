// =============================================================================
// DvlJanus.cs
// -----------------------------------------------------------------------------
// Общая геометрия и математика 4-лучевого доплеровского прибора (схема «Янус»).
// Используется и рабочим датчиком (RovSpeedSensor), и учебной анимацией
// (DopplerInstrumentsVisualizer) — чтобы они считали ОДИНАКОВО.
//
// Лучи X-типа: азимуты 45/135/225/315° относительно носа, наклон θ от вертикали.
// Один луч меряет только ПРОЕКЦИЮ скорости на себя: b_i = v · d_i.
// Неизвестных 3 (вперёд, вбок, вверх), лучей 4 — решаем методом наименьших
// квадратов v = (DᵀD)⁻¹Dᵀb. Лишний луч даёт невязку — «ошибку скорости»,
// по которой реальный DVL отбраковывает плохие измерения.
// =============================================================================

using UnityEngine;

public static class DvlJanus
{
    /// <summary>Направление луча i в осях аппарата (x — вправо, y — вверх, z — вперёд).</summary>
    public static Vector3 Луч(int i, float уголГрад, bool вниз)
    {
        float θ = уголГрад * Mathf.Deg2Rad;
        float φ = (45f + 90f * i) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(θ) * Mathf.Sin(φ), (вниз ? -1f : 1f) * Mathf.Cos(θ), Mathf.Sin(θ) * Mathf.Cos(φ));
    }

    /// <summary>
    /// Вектор скорости (в осях аппарата) по радиальным скоростям лучей b[i], где есть[i].
    /// Нужно ≥3 лучей. ошибка — СКО невязки (при 3 лучах всегда 0: проверить нечем).
    /// </summary>
    public static bool Решить(float[] b, bool[] есть, float уголГрад, bool вниз, out Vector3 v, out float ошибка)
    {
        v = Vector3.zero; ошибка = 0f;
        float a00 = 0, a01 = 0, a02 = 0, a11 = 0, a12 = 0, a22 = 0; Vector3 y = Vector3.zero; int n = 0;
        for (int i = 0; i < 4; i++)
        {
            if (!есть[i]) continue;
            Vector3 d = Луч(i, уголГрад, вниз);
            a00 += d.x * d.x; a01 += d.x * d.y; a02 += d.x * d.z;
            a11 += d.y * d.y; a12 += d.y * d.z; a22 += d.z * d.z;
            y += d * b[i]; n++;
        }
        if (n < 3) return false;
        float det = a00 * (a11 * a22 - a12 * a12) - a01 * (a01 * a22 - a12 * a02) + a02 * (a01 * a12 - a11 * a02);
        if (Mathf.Abs(det) < 1e-9f) return false;
        float i00 = (a11 * a22 - a12 * a12) / det, i01 = (a02 * a12 - a01 * a22) / det, i02 = (a01 * a12 - a02 * a11) / det;
        float i11 = (a00 * a22 - a02 * a02) / det, i12 = (a02 * a01 - a00 * a12) / det, i22 = (a00 * a11 - a01 * a01) / det;
        v = new Vector3(i00 * y.x + i01 * y.y + i02 * y.z, i01 * y.x + i11 * y.y + i12 * y.z, i02 * y.x + i12 * y.y + i22 * y.z);
        float s = 0f;
        for (int i = 0; i < 4; i++) if (есть[i]) { float r = b[i] - Vector3.Dot(v, Луч(i, уголГрад, вниз)); s += r * r; }
        ошибка = Mathf.Sqrt(s / n);
        return true;
    }

    /// <summary>
    /// Дальность вдоль луча до ближайшей неподвижной поверхности (дно, труба). Пропускает
    /// свой корпус, свой кабель и подвижные тела — иначе луч вниз упирается в кабель,
    /// висящий под аппаратом, и «дно» оказывается в полуметре.
    /// </summary>
    // Общий буфер попаданий: RaycastAll создаёт новый массив на каждый луч, а лучей у лага,
    // эхолота и гидролокатора — сотни в секунду; мусор от них давал рывки сборщика памяти.
    public static readonly RaycastHit[] Буфер = new RaycastHit[64];

    public static bool ЛучДоДна(Transform корпус, Rigidbody свой, Vector3 o, Vector3 d, float макс, out float R)
        => ЛучДоДна(корпус, свой, o, d, макс, out R, out _);

    // То же, плюс во что попал луч: грунт (TerrainCollider), труба, камень.
    public static bool ЛучДоДна(Transform корпус, Rigidbody свой, Vector3 o, Vector3 d, float макс, out float R, out Collider что)
    {
        R = макс;
        что = null;
        bool есть = false;
        int n = Physics.RaycastNonAlloc(o, d, Буфер, макс, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var h = Буфер[i];
            if (h.collider.transform.IsChildOf(корпус)) continue;
            if (h.rigidbody != null && h.rigidbody != свой && !h.rigidbody.isKinematic) continue;   // подвижные тела
            if (h.collider.GetComponentInParent<UmbilicalCable>() != null) continue;              // свой кабель (звенья и якоря)
            if (h.distance < R) { R = h.distance; что = h.collider; есть = true; }
        }
        return есть;
    }

    /// <summary>Гауссов шум, σ = 1 (Бокс—Мюллер).</summary>
    public static float Гаусс()
    {
        float u1 = Mathf.Max(1e-6f, Random.value), u2 = Random.value;
        return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
    }

    /// <summary>Курс по компасу (0 = север/+Z, 90 = восток/+X) горизонтального вектора в МИРЕ.</summary>
    public static float Курс(Vector3 мир) => (Mathf.Atan2(мир.x, мир.z) * Mathf.Rad2Deg + 360f) % 360f;
}
