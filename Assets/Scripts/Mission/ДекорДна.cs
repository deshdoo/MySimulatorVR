using System.Collections.Generic;
using UnityEngine;

// Камни и водоросли на дне вокруг трассы — по образцу: каменистые холмы и бровка
// каньона в валунах, на равнине — редкие камни и кустики водорослей.
//
// Камни — наборы Poly Haven «rock_moss_set_01/02» (CC0), упрощённые в Blender до
// ~2,5 тыс. треугольников (Resources/ДноТрассы/Камни_мох.fbx) — по бюджету Quest их 90.
// Водоросли — упрощённая копия модели из сцены (seaweed…). Мелкое вдали не рисуется.
// Раскладка с постоянным зерном: в режиме сцены (предпросмотр) и в Play — одно и то же.
// У трубы (3,5 м) ничего не ставится — там её осматривают.
public static class ДекорДна
{
    public const string ИмяКорня = "Декор дна";
    const int Камней = 90;
    const int Кустов = 16;
    const float Радиус_м = 85f;          // дальше в мути не видно — не тратим треугольники

    public static GameObject Расставить(ТрассаДна.Трасса т, int слой, bool предпросмотр)
    {
        Vector3 центр = ПостроительДна.Центр(т);
        var terrain = ПостроительДна.РельефПод(центр);
        if (terrain == null) return null;
        var корень = new GameObject(ИмяКорня) { layer = слой };
        // В сцену рельефа (Main), а не в активную: при открытой рядом кабине декор
        // попадал в её иерархию — камни и водоросли висели в сцене Cabin.
        if (terrain.gameObject.scene.IsValid()) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(корень, terrain.gameObject.scene);
        var rng = new System.Random(2718);

        int камней = РасставитьКамни(т, terrain, центр, корень.transform, слой, rng, предпросмотр);
        int водорослей = РасставитьВодоросли(т, terrain, центр, корень.transform, rng, предпросмотр);
        if (предпросмотр)
            foreach (var tr in корень.GetComponentsInChildren<Transform>(true))
                tr.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        else
            Debug.Log($"[Дно] Декор: камней {камней}, водорослей {водорослей}.");
        return корень;
    }

    static int РасставитьКамни(ТрассаДна.Трасса т, Terrain terrain, Vector3 центр, Transform корень, int слой,
                               System.Random rng, bool предпросмотр)
    {
        var набор = Resources.Load<GameObject>("ДноТрассы/Камни_мох");
        var мат1 = Resources.Load<Material>("ДноТрассы/Камень 01");
        var мат2 = Resources.Load<Material>("ДноТрассы/Камень 02");
        if (набор == null) { Debug.LogWarning("[Дно] Нет Resources/ДноТрассы/Камни_мох — камней не будет."); return 0; }
        // У мешей из FBX свой поворот и масштаб в префабе (Blender → Unity: ось Z вверх
        // превращается в поворот −90° по X) — берём их, иначе камень ляжет на бок.
        var формы = new List<(Mesh меш, Material мат, Quaternion пов, Vector3 масштаб)>();
        foreach (var mf in набор.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null)
                формы.Add((mf.sharedMesh, mf.name.StartsWith("set2") ? мат2 : мат1, mf.transform.localRotation, mf.transform.localScale));
        if (формы.Count == 0) return 0;

        var td = terrain.terrainData;
        Vector3 o = terrain.GetPosition(), size = td.size;
        int поставлено = 0;
        for (int k = 0; k < 6000 && поставлено < Камней; k++)
        {
            float x = Mathf.Lerp(центр.x - Радиус_м, центр.x + Радиус_м, (float)rng.NextDouble());
            float z = Mathf.Lerp(центр.z - Радиус_м, центр.z + Радиус_м, (float)rng.NextDouble());
            float nx = (x - o.x) / size.x, nz = (z - o.z) / size.z;
            if (nx < 0.01f || nx > 0.99f || nz < 0.01f || nz > 0.99f) continue;
            var p = new Vector2(x, z);
            if (Mathf.Abs(т.Поперёк_м(p)) < 3.5f) continue;
            float дальность = (p - new Vector2(центр.x, центр.z)).magnitude;
            if (дальность > Радиус_м) continue;

            Vector3 нормаль = td.GetInterpolatedNormal(nx, nz);
            float уклон = Mathf.Tan(Mathf.Acos(Mathf.Clamp01(нормаль.y)));
            float камни = ТрассаДна.Каменистость(т, p, уклон);
            float шанс = (0.03f + 0.6f * камни) * ТрассаДна.Гладко((Радиус_м - дальность) / 25f);
            if ((float)rng.NextDouble() > шанс) continue;

            var (меш, мат, повФормы, масштабФормы) = формы[rng.Next(формы.Count)];
            float масштаб = камни > 0.3f ? Mathf.Lerp(0.5f, 1.6f, (float)rng.NextDouble()) : Mathf.Lerp(0.2f, 0.55f, (float)rng.NextDouble());
            // Чуть наклонён по склону, повёрнут как попало, на четверть утоплен в грунт.
            var пов = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, нормаль, 0.5f))
                    * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            float грунт = terrain.SampleHeight(new Vector3(x, 0f, z)) + o.y;

            var go = new GameObject($"Камень_{поставлено + 1}") { layer = слой };
            go.transform.SetParent(корень, false);
            go.transform.SetPositionAndRotation(new Vector3(x, грунт, z), пов * повФормы);
            go.transform.localScale = масштабФормы * масштаб;
            go.AddComponent<MeshFilter>().sharedMesh = меш;
            var рендер = go.AddComponent<MeshRenderer>();
            рендер.sharedMaterial = мат;
            Посадить(go.transform, рендер, x, z, грунт, 0.25f);
            Bounds b = меш.bounds;
            Отсекать(go, рендер, масштаб > 0.6f ? 0.012f : 0.025f);
            // Крупные — с коллайдером (аппарат о них бьётся), коробкой по габаритам: меш
            // из FBX не помечен читаемым, а MeshCollider в сборке его требует.
            if (!предпросмотр && масштаб > 0.6f)
            {
                var бокс = go.AddComponent<BoxCollider>();
                бокс.center = b.center;
                бокс.size = b.size * 0.8f;
            }
            поставлено++;
        }
        return поставлено;
    }

    // Водоросли кустиками. Модель в сцене (seaweed…) — 22,8 тыс. треугольников, сотня
    // её копий роняла FPS; поэтому меш — упрощённая в Blender копия (~2,5 тыс.,
    // Resources/ДноТрассы/Водоросль_лёгкая.fbx), материалы и высота — как у образца в сцене.
    static int РасставитьВодоросли(ТрассаДна.Трасса т, Terrain terrain, Vector3 центр, Transform корень, System.Random rng, bool предпросмотр)
    {
        // Исходные водоросли сцены — образец высоты и материала; сами они не раскрашены и
        // тяжёлые (22,8 тыс. треугольников), поэтому прячутся: в игре выключаются, в режиме
        // сцены — скрываются только в окне редактора (файл сцены не меняется).
        Renderer образец = null;
        var исходные = new List<GameObject>();
        foreach (var root in terrain.gameObject.scene.GetRootGameObjects())
            if (root.activeInHierarchy && root.name.ToLowerInvariant().StartsWith("seaweed"))
            {
                исходные.Add(root);
                if (образец == null) образец = root.GetComponentInChildren<MeshRenderer>();
            }
        var лёгкая = Resources.Load<GameObject>("ДноТрассы/Водоросль_лёгкая");
        var mf = лёгкая != null ? лёгкая.GetComponentInChildren<MeshFilter>() : null;
        if (образец == null || mf == null || mf.sharedMesh == null) return 0;
        float высотаОбразца = образец.bounds.size.y;
        var цвета = Окраски(образец.sharedMaterials, mf.sharedMesh.subMeshCount);
        foreach (var go in исходные)
        {
            if (Application.isPlaying) go.SetActive(false);
#if UNITY_EDITOR
            else UnityEditor.SceneVisibilityManager.instance.Hide(go, true);
#endif
        }

        Vector3 o = terrain.GetPosition(), size = terrain.terrainData.size;
        var колыхание = предпросмотр ? null : корень.gameObject.AddComponent<ВодорослиКолышутся>();
        int всего = 0;
        for (int куст = 0; куст < Кустов; куст++)
        {
            float x = Mathf.Lerp(центр.x - 60f, центр.x + 60f, (float)rng.NextDouble());
            float z = Mathf.Lerp(центр.z - 60f, центр.z + 60f, (float)rng.NextDouble());
            var мат = цвета[rng.Next(цвета.Length)];   // куст — одного вида и цвета
            int n = 3 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                float px = x + ((float)rng.NextDouble() - 0.5f) * 3f, pz = z + ((float)rng.NextDouble() - 0.5f) * 3f;
                if (px < o.x + 1f || px > o.x + size.x - 1f || pz < o.z + 1f || pz > o.z + size.z - 1f) continue;
                if (Mathf.Abs(т.Поперёк_м(new Vector2(px, pz))) < 2.5f) continue;
                float грунт = terrain.SampleHeight(new Vector3(px, 0f, pz)) + o.y;
                var go = new GameObject("Водоросль") { layer = корень.gameObject.layer };
                go.transform.SetParent(корень, false);
                go.transform.SetPositionAndRotation(new Vector3(px, грунт, pz),
                    Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * mf.transform.localRotation);
                go.transform.localScale = mf.transform.localScale;
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = мат;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // Высота — как у образца в сцене, ±; корень — в грунте на 5 см.
                float h = r.bounds.size.y;
                if (h > 0.001f) go.transform.localScale *= высотаОбразца * Mathf.Lerp(0.6f, 1.3f, (float)rng.NextDouble()) / h;
                Посадить(go.transform, r, px, pz, грунт - 0.05f, 0f);
                // Порог ниже, чем у камней: водоросль тонкая и при 0,02 «вырастала» уже с ~11 м,
                // где муть ещё прозрачная, — появление было заметно. С 0,013 — с ~16 м, в густой мути.
                Отсекать(go, r, 0.013f);
                // Корень-шарнир у грунта: вокруг него водоросль наклоняется целиком.
                var шарнир = new GameObject("Водоросль") { layer = корень.gameObject.layer };
                шарнир.transform.SetParent(корень, false);
                шарнир.transform.position = new Vector3(px, грунт - 0.05f, pz);
                go.name = "Меш";
                go.transform.SetParent(шарнир.transform, true);
                if (колыхание != null) колыхание.Добавить(шарнир.transform, rng);
                всего++;
            }
        }
        return всего;
    }

    // Ставит объект по его габаритам в мире: середина — над точкой (x, z), низ — на
    // грунте, утоплен на долю высоты.
    static void Посадить(Transform t, Renderer r, float x, float z, float грунт, float утопить)
    {
        Bounds wb = r.bounds;
        t.position += new Vector3(x - wb.center.x, грунт - утопить * wb.size.y - wb.min.y, z - wb.center.z);
    }

    // Окраски водорослей — как у настоящих на шельфе: бурые и оливковые (ламинария,
    // фукус), красные (багрянки — на глубине их больше всего), зелёные пятнами.
    // Материалы собираются заново на URP Lit (у образца берутся только текстуры): копия
    // материала образца из сцены у части кустов рисовалась ярко-фиолетовой — так Unity
    // показывает шейдер, который не собрался. Слотов — по числу частей меша: пустой слот
    // тоже рисуется фиолетовым.
    static Material[][] Окраски(Material[] образец, int частей)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var исх = образец.Length > 0 && образец[0] != null ? образец[0] : null;
        Debug.Log($"[Дно] Водоросли: образец — {образец.Length} материал(ов), шейдер «{(исх != null && исх.shader != null ? исх.shader.name : "нет")}», " +
                  $"частей у лёгкого меша {частей}; материалы собираются на {(lit != null ? lit.name : "копии образца")}.");
        var оттенки = new[]
        {
            // Тёмные и приглушённые: на глубине свет слабый, яркие цвета выглядели мультяшно.
            new Color(0.30f, 0.25f, 0.15f),   // бурая
            new Color(0.26f, 0.30f, 0.16f),   // оливковая
            new Color(0.28f, 0.17f, 0.10f),   // красно-бурая (багрянка): чистый красный в голубой мути лиловеет
            new Color(0.17f, 0.28f, 0.18f),   // зелёная
            new Color(0.24f, 0.24f, 0.15f),   // буро-зелёная
            // Розовой больше нет: под голубой мутью и холодным светом фар она выходила
            // лиловой и выглядела как «не загрузилась текстура».
        };
        int слотов = Mathf.Max(1, Mathf.Max(частей, образец.Length));
        var наборы = new Material[оттенки.Length][];
        for (int k = 0; k < оттенки.Length; k++)
        {
            наборы[k] = new Material[слотов];
            for (int m = 0; m < слотов; m++)
            {
                var о = m < образец.Length && образец[m] != null ? образец[m] : исх;
                Material мат;
                if (lit != null)
                {
                    мат = new Material(lit) { name = (о != null ? о.name : "Водоросль") + " (окраска)" };
                    var tex = о == null ? null : о.HasProperty("_BaseMap") ? о.GetTexture("_BaseMap") : о.HasProperty("_MainTex") ? о.GetTexture("_MainTex") : null;
                    if (tex != null) мат.SetTexture("_BaseMap", tex);
                    var норм = о != null && о.HasProperty("_BumpMap") ? о.GetTexture("_BumpMap") : null;
                    if (норм != null) { мат.SetTexture("_BumpMap", норм); мат.EnableKeyword("_NORMALMAP"); }
                    мат.SetFloat("_Cull", 0f);   // лента водоросли видна с обеих сторон
                }
                else if (о != null) мат = new Material(о) { name = о.name + " (окраска)" };
                else continue;
                if (мат.HasProperty("_BaseColor")) мат.SetColor("_BaseColor", оттенки[k]);
                else if (мат.HasProperty("_Color")) мат.SetColor("_Color", оттенки[k]);
                if (мат.HasProperty("_Smoothness")) мат.SetFloat("_Smoothness", 0.15f);   // без пластикового блеска
                мат.enableInstancing = true;
                наборы[k][m] = мат;
            }
        }
        return наборы;
    }

    // Совсем мелкое на экране (вдали, в мути) не рисуется: LODGroup с одним уровнем.
    static void Отсекать(GameObject go, Renderer r, float доляЭкрана)
    {
        var lod = go.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(доляЭкрана, new[] { r }) });
        lod.RecalculateBounds();
    }
}
