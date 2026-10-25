using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

// 3D-повтор миссии на «голографическом столе» — макет из НАСТОЯЩИХ моделей.
//
// Возить оператора по всей трассе в VR нельзя (аппарат стартует в десятках метров
// от трубы, движение камеры без движения тела укачивает), поэтому повтор — это
// уменьшенная копия места работ перед оператором на высоте стола:
//   • рельеф дна — промер лучами сеткой 64×64 по району работ и построенная по
//     нему поверхность (работает и с Terrain, и с мешем дна);
//   • копии всех моделей сцены в районе: труба, вентиль, камни, водоросли — те же
//     меши и материалы, только уменьшенные (без скриптов и коллайдеров);
//   • модель аппарата летит по записанной траектории (увеличена ×N для
//     заметности — иначе в масштабе 1:40 она размером с ноготь);
//   • траектория — только пройденная к текущему моменту повтора (раньше рядом тусклой
//     линией шёл весь путь вперёд, и его принимали за след другой миссии); дефекты —
//     булавки с подписью (серая → зелёная в момент обнаружения, красная — не найден);
//     удары — вспышки.
//     Где траекторию заслоняет рельеф или труба, она видна сквозь них бледнее —
//     путь читается с любого ракурса;
//   • по краю рельефа — стенки до основания стола, как срез торта: макет не полый.
// Масштаб подбирается так, чтобы район уместился в ~1 м.
//
// VR-камера кабины не рисует слой Underwater (и в меню это оставлено — фон меню
// чистая вода). Поэтому весь макет перекладывается на слой Default и освещается
// своим направленным светом (только этот слой), а настоящая сцена вокруг стола
// не рисуется — нет лишних труб в кадре и лишней нагрузки.
public class MenuReplayTable : MonoBehaviour
{
    public float Время { get; private set; }
    public float Длительность { get; private set; }
    public bool Пауза { get; set; }
    public float Скорость { get; set; } = 4f;
    public MissionReplay.Кадр Текущий { get; private set; }

    const float РазмерСтола = 1.0f;     // сторона макета, м
    const int Сетка = 64;               // разрешение рельефа
    const int МаксМоделей = 400;

    List<MissionReplay.Кадр> _кадры;
    Transform _макет, _аппарат;
    LineRenderer _пройдено, _пройденоСквозь;
    float _верхИнтереса = float.MaxValue;
    Vector3 _центр; float _масштаб, _сторона;
    Bounds _район;
    readonly List<(MissionArchive.Дефект д, Renderer голова)> _дефекты = new List<(MissionArchive.Дефект, Renderer)>();
    readonly List<(Transform t, float время)> _удары = new List<(Transform, float)>();
    readonly List<Object> _созданное = new List<Object>();
    Material _серый, _зелёный, _красный, _удар;
    int _индекс = -1;
    Vector3[] _точкиМакета;   // вся траектория в координатах макета — считается один раз

    public bool Построить(MissionArchive.Запись запись, byte[] данные, Vector3 центрСтола, Quaternion поворот, TMP_FontAsset шрифт)
    {
        _кадры = MissionReplay.Распаковать(данные);
        if (_кадры.Count < 2) return false;
        Длительность = _кадры[_кадры.Count - 1].t;

        // Район работ: траектория + труба + дефекты, с полями 3 м.
        var b = new Bounds(_кадры[0].поз, Vector3.zero);
        foreach (var к in _кадры) b.Encapsulate(к.поз);
        foreach (var д in запись.дефекты) b.Encapsulate(д.позиция);
        var спавнер = FindFirstObjectByType<PipelineDefectSpawner>();
        if (спавнер != null && спавнер.ГеометрияТрубы(out Vector3 ta, out Vector3 tb, out _)) { b.Encapsulate(ta); b.Encapsulate(tb); }
        _сторона = Mathf.Max(6f, Mathf.Max(b.size.x, b.size.z) + 6f);
        _верхИнтереса = b.max.y + 8f;   // выше — судно на поверхности, буи: на макете не нужны
        _район = new Bounds(new Vector3(b.center.x, b.center.y, b.center.z), new Vector3(_сторона, b.size.y + 60f, _сторона));
        _масштаб = РазмерСтола / _сторона;

        _макет = new GameObject("МакетПовтора").transform;
        SceneManager.MoveGameObjectToScene(_макет.gameObject, gameObject.scene);
        _макет.SetParent(transform, false);
        _макет.SetPositionAndRotation(центрСтола, поворот);
        _созданное.Add(_макет.gameObject);

        _серый = Цвет(new Color(0.75f, 0.8f, 0.85f));
        _зелёный = Цвет(new Color(0.35f, 0.95f, 0.5f));
        _красный = Цвет(new Color(1f, 0.38f, 0.32f));
        _удар = Цвет(new Color(1f, 0.25f, 0.15f));

        ПостроитьРельеф();
        // Центр макета — середина района по горизонтали, низ дна — по вертикали
        // (рельеф уже построен относительно _центр, см. ПостроитьРельеф).
        КопироватьМодели();
        ПостроитьРамку();
        СоздатьСвет();

        _точкиМакета = new Vector3[_кадры.Count + 1];
        for (int i = 0; i < _кадры.Count; i++) _точкиМакета[i] = ВМакет(_кадры[i].поз);
        _пройдено = Линия("Пройдено", new Color(0.35f, 0.95f, 1f, 1f), 0.006f);
        _пройденоСквозь = Линия("Пройдено (сквозь)", new Color(0.35f, 0.95f, 1f, 0.4f), 0.006f, true);

        ПоставитьДефекты(запись, шрифт);

        foreach (var у in запись.удары)
        {
            var сф = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Подготовить(сф);
            сф.transform.localPosition = ВМакет(MissionReplay.В(_кадры, у.время_с).поз);
            сф.transform.localScale = Vector3.zero;
            сф.GetComponent<MeshRenderer>().sharedMaterial = _удар;
            _удары.Add((сф.transform, у.время_с));
        }

        // Модель аппарата.
        var rov = FindFirstObjectByType<RovController>();
        if (rov != null)
        {
            var п = Призрак.Создать(rov.transform, gameObject.scene, false);
            Призрак.НаСлой(п, СлойМакета);
            float габ = Призрак.Габарит(п);
            п.transform.SetParent(_макет, true);
            float реальный = габ * _масштаб;
            float целевой = Mathf.Max(реальный, 0.05f);   // не меньше 5 см
            п.transform.localScale = Vector3.one * (целевой / Mathf.Max(0.01f, габ)) / _макет.lossyScale.x;
            _аппарат = п.transform;
        }

        Время = 0f;
        Применить();
        return true;
    }

    const int СлойМакета = 0;   // Default — его VR-камера кабины видит

    void СоздатьСвет()
    {
        var go = new GameObject("СветМакета");
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(55f, 35f, 0f);
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.1f;
        l.color = new Color(0.9f, 0.97f, 1f);
        l.shadows = LightShadows.None;
        l.cullingMask = 1 << СлойМакета;
        _созданное.Add(go);
    }

    // Мир → макет: смещение от центра района, уменьшенное. Оси макета = оси мира,
    // повёрнутые вместе со столом.
    Vector3 ВМакет(Vector3 мир) => (мир - _центр) * _масштаб;

    // Рельеф: самый нижний отклик луча в каждой точке сетки (дно — самое нижнее,
    // так труба и камни не «вспучивают» поверхность — они будут отдельными моделями).
    void ПостроитьРельеф()
    {
        var высоты = new float[Сетка + 1, Сетка + 1];
        float мин = float.MaxValue;
        float верх = _район.max.y;
        for (int z = 0; z <= Сетка; z++)
            for (int x = 0; x <= Сетка; x++)
            {
                Vector3 p = new Vector3(_район.min.x + _сторона * x / Сетка, верх, _район.min.z + _сторона * z / Сетка);
                float y = float.NaN;
                foreach (var h in Physics.RaycastAll(p, Vector3.down, _район.size.y + 40f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider.GetComponentInParent<RovController>() != null) continue;
                    if (float.IsNaN(y) || h.point.y < y) y = h.point.y;
                }
                высоты[x, z] = y;
                if (!float.IsNaN(y)) мин = Mathf.Min(мин, y);
            }
        if (мин == float.MaxValue) мин = _район.center.y - 2f;
        _центр = new Vector3(_район.center.x, мин, _район.center.z);

        var verts = new Vector3[(Сетка + 1) * (Сетка + 1)];
        var uv = new Vector2[verts.Length];
        for (int z = 0; z <= Сетка; z++)
            for (int x = 0; x <= Сетка; x++)
            {
                float y = float.IsNaN(высоты[x, z]) ? мин : высоты[x, z];
                Vector3 мир = new Vector3(_район.min.x + _сторона * x / Сетка, y, _район.min.z + _сторона * z / Сетка);
                verts[z * (Сетка + 1) + x] = ВМакет(мир);
                uv[z * (Сетка + 1) + x] = new Vector2((float)x / Сетка, (float)z / Сетка);
            }
        var tris = new int[Сетка * Сетка * 6];
        int t = 0;
        for (int z = 0; z < Сетка; z++)
            for (int x = 0; x < Сетка; x++)
            {
                int i = z * (Сетка + 1) + x;
                tris[t++] = i; tris[t++] = i + Сетка + 1; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + Сетка + 1; tris[t++] = i + Сетка + 2;
            }
        var mesh = new Mesh { name = "РельефМакета", vertices = verts, uv = uv, triangles = tris };
        mesh.RecalculateNormals();
        _созданное.Add(mesh);

        var go = new GameObject("Рельеф");
        go.transform.SetParent(_макет, false);
        go.layer = СлойМакета;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var m = Процедуры.Освещаемый(new Color(0.62f, 0.55f, 0.42f));
        _созданное.Add(m);
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ПостроитьСтенки(verts);
    }

    // Стенки по краю рельефа — как срез у торта: от каждой точки кромки дна отвесно
    // вниз до основания стола (уровень рамки), и дно макета. Выше рельефа стенки не
    // поднимаются. Без них макет был полым: сбоку под поверхность было видно насквозь.
    void ПостроитьСтенки(Vector3[] верх)
    {
        int n = Сетка + 1;
        var v = new List<Vector3>(); var tri = new List<int>();
        // Обход — так, чтобы лицевая сторона смотрела наружу (у Unity лицевая — по часовой).
        void Сторона(System.Func<int, int> индекс, bool наоборот)
        {
            int база = v.Count;
            for (int k = 0; k < n; k++)
            {
                Vector3 p = верх[индекс(k)];
                v.Add(p); v.Add(new Vector3(p.x, 0f, p.z));
            }
            for (int k = 0; k < n - 1; k++)
            {
                int a = база + k * 2;
                if (наоборот) { tri.Add(a); tri.Add(a + 2); tri.Add(a + 1); tri.Add(a + 2); tri.Add(a + 3); tri.Add(a + 1); }
                else          { tri.Add(a); tri.Add(a + 1); tri.Add(a + 2); tri.Add(a + 2); tri.Add(a + 1); tri.Add(a + 3); }
            }
        }
        Сторона(k => k, true);                  // ближний край (z = min)
        Сторона(k => (n - 1) * n + k, false);   // дальний (z = max)
        Сторона(k => k * n, false);             // левый (x = min)
        Сторона(k => k * n + n - 1, true);      // правый (x = max)
        // Дно макета — снизу тоже закрыт.
        Vector3 Низ(int i) { var p = верх[i]; return new Vector3(p.x, 0f, p.z); }
        int d = v.Count;
        v.Add(Низ(0)); v.Add(Низ(n - 1)); v.Add(Низ((n - 1) * n)); v.Add(Низ(n * n - 1));
        tri.Add(d); tri.Add(d + 1); tri.Add(d + 2); tri.Add(d + 1); tri.Add(d + 3); tri.Add(d + 2);

        var mesh = new Mesh { name = "СтенкиМакета" };
        mesh.SetVertices(v); mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        _созданное.Add(mesh);
        var go = new GameObject("Стенки");
        go.transform.SetParent(_макет, false);
        go.layer = СлойМакета;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var m = Процедуры.Освещаемый(new Color(0.44f, 0.37f, 0.28f));   // срез грунта темнее поверхности
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        _созданное.Add(m);
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Посторонняя труба: длинная и тонкая модель (или с «pipe» в названии материала).
    // Модель целиком — вместе с опорами и хомутами, которые сами по себе не длинные.
    static void ОтметитьЛишнее(Transform модель, Transform магистраль, HashSet<Transform> лишние)
    {
        if (модель == магистраль || магистраль.IsChildOf(модель)) return;
        if (модель.GetComponentInChildren<UmbilicalCable>() != null || модель.GetComponentInChildren<RovController>() != null) return;
        var все = модель.GetComponentsInChildren<MeshRenderer>(false);
        // Папка сцены (камни, водоросли, всё подряд) — не модель трубы: разбираем по детям.
        if (все.Length > 30)
        {
            foreach (Transform ребёнок in модель) ОтметитьЛишнее(ребёнок, магистраль, лишние);
            return;
        }
        foreach (var mr in все)
            if (Трубоподобная(mr, mr.bounds))
            {
                if (лишние.Add(модель))
                    Debug.Log($"[Повтор] На макет не идёт посторонняя труба «{модель.name}» (деталь «{mr.name}»).");
                return;
            }
    }

    static bool Трубоподобная(MeshRenderer mr, Bounds bb)
    {
        float[] e = { bb.size.x, bb.size.y, bb.size.z };
        System.Array.Sort(e);
        if (e[2] > 2.5f && e[1] < 1.6f) return true;
        foreach (var m in mr.sharedMaterials)
            if (m != null && m.name.IndexOf("pipe", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    static bool Судно(Transform t) =>
        t.root.name.IndexOf("boat", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
        t.root.name.IndexOf("ship", System.StringComparison.OrdinalIgnoreCase) >= 0;

    // Копии моделей сцены в районе работ (без аппарата, кабеля, эффектов меню и
    // гигантских плоскостей вроде поверхности воды).
    // Дубли не копируются: в сцене на одном месте бывает несколько моделей одной
    // трубы (уровни детализации LOD, подсветка-оверлей другим цветом) — на макете
    // они перекрывались (жёлтая поверх серой). Правила:
    //   • только LOD0;
    //   • если две модели почти в одном месте и почти одного размера — остаётся
    //     та, у которой материал «серее» (меньше насыщенность цвета);
    //   • материалов не больше, чем подмешей (лишние рисуются оверлеем поверх).
    //   • вдоль трубы миссии берутся ТОЛЬКО её модель (Труба у спавнера дефектов) и
    //     вентиль: в сцене рядом лежат ещё две трубные модели (pipeline,
    //     PipelineWithValve) со сдвигом ~0.7 м — в сцене они сливаются, а на макете
    //     1:40 выглядели тремя параллельными трубами.
    void КопироватьМодели()
    {
        var спавнер = FindFirstObjectByType<PipelineDefectSpawner>();
        Transform труба = спавнер != null ? спавнер.Труба : null;
        var вентильЦель = FindFirstObjectByType<InterventionTarget>();
        Transform вентиль = вентильЦель != null ? вентильЦель.transform : null;
        bool естьОсь = спавнер != null && спавнер.ГеометрияТрубы(out _осьА, out _осьБ, out _радиусТрубы);
        // Габариты деталей трубы миссии (в мире): всё длинное, что лежит ВНУТРИ
        // какой-либо её детали, — лишняя трубная деталь из другого объекта сцены
        // (так на макет попадал жёлтый сегмент поверх трубы).
        var деталиТрубы = new List<Bounds>();
        if (труба != null)
            foreach (var r in труба.GetComponentsInChildren<MeshRenderer>(false))
            {
                var bb = r.bounds; bb.Expand(0.6f);
                деталиТрубы.Add(bb);
            }
        var неLod0 = new HashSet<Renderer>();
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var lg in root.GetComponentsInChildren<LODGroup>(false))
            {
                var lods = lg.GetLODs();
                for (int k = 1; k < lods.Length; k++)
                    foreach (var r in lods[k].renderers) if (r != null) неLod0.Add(r);
            }

        // Труба миссии и вентиль — копией ЦЕЛИКОМ (вся иерархия, уменьшенная как одно
        // целое): поштучный перенос деталей ломал вложенные повороты/масштабы FBX,
        // и труба «расслаивалась» на параллельные копии.
        if (труба != null)
        {
            var копия = КопияЦеликом(труба);
            // У модели трубы несколько материалов (жёлтое покрытие MatPipeCoating,
            // металл фланцев, два серых). Под водой синий свет их сглаживает, а на
            // макете под белым светом труба выглядела «тремя разными» — красим её
            // одним серым. Вентиль (штурвал) оставляем родным.
            Transform вентильВКопии = null;
            if (вентиль != null && вентиль.IsChildOf(труба))
                вентильВКопии = копия.transform.Find(ПутьОт(труба, вентиль));
            var серый = МатериалСОбрезкой(new Color(0.63f, 0.66f, 0.69f));
            _созданное.Add(серый);
            foreach (var r in копия.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (вентильВКопии != null && r.transform.IsChildOf(вентильВКопии)) continue;
                var маты = r.sharedMaterials;
                for (int k = 0; k < маты.Length; k++) маты[k] = серый;
                r.sharedMaterials = маты;
            }
        }
        if (вентиль != null && (труба == null || !вентиль.IsChildOf(труба))) КопияЦеликом(вентиль);

        // Магистраль в бетоне заменила старую трубу на ножках, но в сцене остались другие
        // трубные модели: всё «трубное» не из магистрали — мимо, вместе со своим корнем
        // (опоры и хомуты такой модели не длинные, но тоже лишние).
        var магистраль = труба != null ? труба.GetComponent<МагистральныйТрубопровод>() : null;
        var лишниеКорни = new HashSet<Transform>();
        if (магистраль != null)
            foreach (var root in gameObject.scene.GetRootGameObjects())
                ОтметитьЛишнее(root.transform, магистраль.transform, лишниеКорни);

        var кандидаты = new List<(MeshRenderer mr, MeshFilter mf, Bounds bb, float насыщ)>();
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!mr.enabled || неLod0.Contains(mr)) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (mr.GetComponentInParent<RovController>() != null) continue;
                if (mr.GetComponentInParent<UmbilicalCable>() != null) continue;
                if (mr.GetComponentInParent<MenuDiorama>() != null || mr.GetComponentInParent<MenuReplayTable>() != null) continue;
                if (mr.GetComponentInParent<PipelineDefect>() != null) continue;   // дефекты этой сессии — не той миссии
                if (mr.transform.root.name.Contains("Призрак")) continue;
                if (Призрак.СломанныйМатериал(mr)) continue;
                var bb = mr.bounds;
                if (bb.min.y > _верхИнтереса || Судно(mr.transform)) continue;   // судно на поверхности — не к месту на дне
                bool лишняя = false;
                foreach (var к in лишниеКорни) if (mr.transform.IsChildOf(к)) { лишняя = true; break; }
                if (лишняя) continue;
                // Трубу и вентиль уже скопировали целиком — поштучно их не берём.
                if (труба != null && mr.transform.IsChildOf(труба)) continue;
                if (вентиль != null && mr.transform.IsChildOf(вентиль)) continue;
                bool своя = false;
                if (bb.size.magnitude > 0.8f && ВнутриТрубы(bb, деталиТрубы))
                {
                    Debug.Log($"[Повтор] Пропущен лишний трубный объект: «{mr.name}» (корень «{mr.transform.root.name}», материал «{(mr.sharedMaterial != null ? mr.sharedMaterial.name : "-")}»)");
                    continue;
                }
                // Чужая длинная модель вдоль трубы миссии = лишняя копия трубы.
                if (!своя && естьОсь && bb.size.magnitude > 2f && ДоОси(bb.center) < _радиусТрубы + 1.2f) continue;
                // Другие трубные объекты сцены (корень с «pipe» в имени) — целиком мимо.
                if (!своя && труба != null &&
                    mr.transform.root.name.IndexOf("pipe", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (bb.size.x > _сторона * 1.2f || bb.size.z > _сторона * 1.2f) continue;   // дно/вода целиком
                Vector3 c = bb.center;
                if (c.x < _район.min.x || c.x > _район.max.x || c.z < _район.min.z || c.z > _район.max.z) continue;
                кандидаты.Add((mr, mf, bb, Насыщенность(mr)));
            }
        кандидаты.Sort((a, b) => a.насыщ.CompareTo(b.насыщ));   // серые — первыми

        var взятые = new List<Bounds>();
        int n = 0;
        foreach (var (mr, mf, bb, _) in кандидаты)
        {
            if (n >= МаксМоделей) break;
            bool дубль = false;
            foreach (var в in взятые)
            {
                float разм = Mathf.Max(в.size.magnitude, bb.size.magnitude, 0.01f);
                if (Vector3.Distance(в.center, bb.center) < 0.05f * разм + 0.1f &&
                    Mathf.Abs(в.size.magnitude - bb.size.magnitude) < 0.15f * разм) { дубль = true; break; }
            }
            if (дубль) continue;
            взятые.Add(bb);

            var go = new GameObject(mr.name);
            go.layer = СлойМакета;
            go.transform.SetParent(_макет, false);
            go.transform.localPosition = ВМакет(mr.transform.position);
            go.transform.localRotation = mr.transform.rotation;
            go.transform.localScale = mr.transform.lossyScale * _масштаб;
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            var маты = mr.sharedMaterials;
            int подмешей = Mathf.Max(1, mf.sharedMesh.subMeshCount);
            if (маты.Length > подмешей) System.Array.Resize(ref маты, подмешей);
            r.sharedMaterials = маты;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            n++;
        }
    }

    Vector3 _осьА, _осьБ; float _радиусТрубы;

    // Копия объекта со всей иерархией, уменьшенная как одно целое. Клон создаётся
    // внутри ВЫКЛЮЧЕННОГО держателя — его скрипты не получают Awake; затем у клона
    // удаляется всё, кроме мешей (скрипты, коллайдеры, физика), и он включается.
    // Лежит ли объект вдоль трубы: его центр и оба конца длинной оси — внутри
    // (расширенных) габаритов деталей трубы.
    static bool ВнутриТрубы(Bounds bb, List<Bounds> детали)
    {
        Vector3 c = bb.center, e = bb.extents;
        Vector3 длинная = e.x >= e.y && e.x >= e.z ? new Vector3(e.x, 0, 0) : e.y >= e.z ? new Vector3(0, e.y, 0) : new Vector3(0, 0, e.z);
        return Внутри(c, детали) && Внутри(c + длинная * 0.8f, детали) && Внутри(c - длинная * 0.8f, детали);
    }

    static bool Внутри(Vector3 p, List<Bounds> детали)
    {
        foreach (var d in детали) if (d.Contains(p)) return true;
        return false;
    }

    // Серый с обрезкой по краю стола (Resources/Shaders/MacetClip): часть трубы,
    // уходящая за пределы рельефа, не рисуется. Нет шейдера — обычный серый.
    Material МатериалСОбрезкой(Color цвет)
    {
        var sh = Shader.Find("Custom/MacetClip");
        if (sh == null) return Процедуры.Освещаемый(цвет);
        var m = new Material(sh);
        m.SetColor("_BaseColor", цвет);
        m.SetFloat("_ClipHalf", РазмерСтола * 0.5f);
        m.SetMatrix("_ClipWorldToLocal", _макет.worldToLocalMatrix);
        return m;
    }

    static string ПутьОт(Transform корень, Transform t)
    {
        string путь = t.name;
        for (var p = t.parent; p != null && p != корень; p = p.parent) путь = p.name + "/" + путь;
        return путь;
    }

    GameObject КопияЦеликом(Transform src)
    {
        var держатель = new GameObject("Копия_" + src.name);
        держатель.SetActive(false);
        держатель.transform.SetParent(_макет, false);
        var копия = Instantiate(src.gameObject, держатель.transform);
        копия.transform.localPosition = ВМакет(src.position);
        копия.transform.localRotation = src.rotation;
        копия.transform.localScale = src.lossyScale * _масштаб;
        // Сначала скрипты (у них бывают RequireComponent на коллайдер/тело), потом остальное.
        foreach (var mb in копия.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
        foreach (var c in копия.GetComponentsInChildren<Component>(true))
            if (!(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer)) DestroyImmediate(c);
        foreach (var t in копия.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = СлойМакета;
        foreach (var r in копия.GetComponentsInChildren<MeshRenderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        держатель.SetActive(true);
        return копия;
    }

    float ДоОси(Vector3 p)
    {
        Vector3 ab = _осьБ - _осьА;
        float t = Mathf.Clamp01(Vector3.Dot(p - _осьА, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
        Vector3 q = _осьА + ab * t;
        return new Vector2(p.x - q.x, p.z - q.z).magnitude;   // по горизонтали: высота модели не важна
    }

    static float Насыщенность(Renderer r)
    {
        float макс = 0f;
        foreach (var m in r.sharedMaterials)
        {
            if (m == null) continue;
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : Color.gray;
            Color.RGBToHSV(c, out _, out float sat, out _);
            макс = Mathf.Max(макс, sat);
        }
        return макс;
    }

    // Тонкая светящаяся рамка по краю макета — «голографический стол».
    void ПостроитьРамку()
    {
        var lr = Линия("Рамка", new Color(0.3f, 0.85f, 1f, 0.8f), 0.004f);
        lr.loop = true;
        float h = РазмерСтола * 0.5f;
        lr.positionCount = 4;
        lr.SetPositions(new[] { new Vector3(-h, 0f, -h), new Vector3(h, 0f, -h), new Vector3(h, 0f, h), new Vector3(-h, 0f, h) });
    }

    void ПоставитьДефекты(MissionArchive.Запись запись, TMP_FontAsset шрифт)
    {
        for (int i = 0; i < запись.дефекты.Count; i++)
        {
            var д = запись.дефекты[i];
            Vector3 низ = ВМакет(д.позиция);
            const float высота = 0.045f;
            var игла = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Подготовить(игла);
            игла.transform.localPosition = низ + Vector3.up * высота * 0.5f;
            игла.transform.localScale = new Vector3(0.002f, высота * 0.5f, 0.002f);
            игла.GetComponent<MeshRenderer>().sharedMaterial = _серый;
            var голова = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Подготовить(голова);
            голова.transform.localPosition = низ + Vector3.up * высота;
            голова.transform.localScale = Vector3.one * 0.012f;
            var r = голова.GetComponent<MeshRenderer>();
            r.sharedMaterial = д.найден ? _серый : _красный;

            var метка = new GameObject("Номер");
            метка.transform.SetParent(_макет, false);
            метка.transform.localPosition = низ + Vector3.up * (высота + 0.018f);
            var t = метка.AddComponent<TextMeshPro>();
            if (шрифт != null) t.font = шрифт;
            t.text = д.метка;
            t.fontSize = 0.15f; t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false;
            t.color = д.найден ? new Color(0.35f, 0.95f, 0.5f) : new Color(1f, 0.38f, 0.32f);
            метка.AddComponent<КЗрителю>();
            _дефекты.Add((д, r));
        }
    }

    void Подготовить(GameObject go)
    {
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(_макет, false);
        var r = go.GetComponent<Renderer>();
        if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // сквозь — линия рисуется поверх всего (без проверки глубины): её видно и там, где
    // путь заслоняет рельеф или труба. Шейдер UI/Default берёт проверку глубины из
    // unity_GUIZTestMode — тот же приём, что у VR-окон (VrPanel).
    LineRenderer Линия(string имя, Color цвет, float ширина, bool сквозь = false)
    {
        var go = new GameObject(имя);
        go.transform.SetParent(_макет, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.widthMultiplier = ширина;
        Material m;
        var ui = сквозь ? Shader.Find("UI/Default") : null;
        if (ui != null)
        {
            m = new Material(ui) { name = имя };
            m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            m.color = Color.white;
            lr.startColor = lr.endColor = цвет;
        }
        else
        {
            m = Процедуры.Материал(true, Texture2D.whiteTexture);
            m.SetColor("_BaseColor", цвет);
        }
        _созданное.Add(m);
        lr.sharedMaterial = m;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return lr;
    }

    Material Цвет(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        _созданное.Add(m);
        return m;
    }

    public void Перемотать(float сек) => Время = Mathf.Clamp(Время + сек, 0f, Длительность);
    public void ВНачало() => Время = 0f;

    void Update()
    {
        if (_кадры == null) return;
        if (!Пауза)
        {
            Время += Time.unscaledDeltaTime * Скорость;
            if (Время >= Длительность) { Время = Длительность; Пауза = true; }
        }
        Применить();
    }

    void Применить()
    {
        var к = MissionReplay.В(_кадры, Время);
        Текущий = к;
        if (_аппарат != null)
        {
            _аппарат.localPosition = ВМакет(к.поз);
            _аппарат.localRotation = к.пов;
        }

        // Пройденная часть: без выделения памяти — берём готовые точки из общего
        // массива (SetPositions копирует первые positionCount), а последнюю точку
        // ставим в текущее положение аппарата. Индекс кадра — двоичным поиском.
        int lo = 0, hi = _кадры.Count - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (_кадры[mid].t <= Время) lo = mid; else hi = mid; }
        int индекс = _кадры[hi].t <= Время ? hi : lo;
        if (индекс != _индекс)
        {
            _индекс = индекс;
            _пройдено.positionCount = индекс + 2;
            _пройдено.SetPositions(_точкиМакета);
            _пройденоСквозь.positionCount = индекс + 2;
            _пройденоСквозь.SetPositions(_точкиМакета);
        }
        _пройдено.SetPosition(_пройдено.positionCount - 1, ВМакет(к.поз));
        _пройденоСквозь.SetPosition(_пройденоСквозь.positionCount - 1, ВМакет(к.поз));

        foreach (var (д, r) in _дефекты)
            if (д.найден) r.sharedMaterial = Время >= д.время_с ? _зелёный : _серый;

        // Вспышка удара: раздувается и гаснет за 1.5 с повтора.
        foreach (var (t, время) in _удары)
        {
            float dt = Время - время;
            float k = dt >= 0f && dt < 1.5f ? Mathf.Sin(dt / 1.5f * Mathf.PI) : 0f;
            t.localScale = Vector3.one * 0.04f * k;
        }
    }

    public void Убрать()
    {
        foreach (var o in _созданное) if (o != null) Destroy(o);
        _созданное.Clear();
        Destroy(gameObject);
    }
}

// Подпись всегда повёрнута к камере (номера дефектов на макете читаются с любой стороны).
public class КЗрителю : MonoBehaviour
{
    Transform _кам;
    void LateUpdate()
    {
        // Камера VR-рига: Camera.main ненадёжна — тегом MainCamera помечены и камеры аппарата.
        if (_кам == null) { var o = FindFirstObjectByType<XROrigin>(); if (o == null || o.Camera == null) return; _кам = o.Camera.transform; }
        transform.rotation = Quaternion.LookRotation(transform.position - _кам.position, Vector3.up);
    }
}
