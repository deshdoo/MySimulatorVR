using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// В режиме сцены видно то же, что в Play, — без нажатий:
//   • после перекомпиляции и открытия сцены дно трассы записывается в ассет
//     Assets/Terrain/<ИмяДанных>.asset и назначается рельефу под трассой (только если
//     версия формул изменилась — иначе ничего не делается);
//   • труба показывается предпросмотром с примером повреждений — скрытый объект,
//     в файл сцены не сохраняется и убирается перед Play.
// Изменения рельефа и сдвиг объектов на дне — через Undo, сцену остаётся сохранить.
[InitializeOnLoad]
static class ПредпросмотрДна
{
    const string Папка = "Assets/Terrain";
    const string ИмяПредпросмотра = "Предпросмотр трубы (не сохраняется)";

    static ПредпросмотрДна()
    {
        EditorApplication.delayCall += Обновить;
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.ExitingEditMode) Убрать();
            else if (s == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Обновить;
        };
        EditorSceneManager.sceneOpened += (_, __) => EditorApplication.delayCall += Обновить;
    }

    static void Убрать()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.parent == null && (t.name == ИмяПредпросмотра || t.name == ДекорДна.ИмяКорня) &&
                (t.gameObject.hideFlags & HideFlags.DontSave) != 0)
                Object.DestroyImmediate(t.gameObject);
    }

    static void Обновить()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            Убрать();
            if (!НайтиТрассу(out Vector3 центр, out Vector3 ось, out float длина, out var маг))
            { Debug.Log("[Предпросмотр дна] Нет ни магистрали, ни спавнера с трубой в открытых сценах — пропуск."); return; }
            var т = new ТрассаДна.Трасса(центр - ось * длина * 0.5f, центр + ось * длина * 0.5f);
            var terrain = ПостроительДна.РельефПод(центр);
            if (terrain == null) { Debug.Log($"[Предпросмотр дна] Под трассой ({центр}) нет рельефа — пропуск."); return; }
            if (terrain.terrainData.name != ПостроительДна.ИмяДанных) ЗаписатьДно(terrain, т, маг != null ? маг.gameObject : null);
            if (маг != null) маг.ПостроитьПредпросмотр();
            int слой = LayerMask.NameToLayer("Underwater");
            ДекорДна.Расставить(т, слой >= 0 ? слой : 0, предпросмотр: true);
            Debug.Log($"[Предпросмотр дна] Готово: рельеф «{terrain.name}» ({terrain.terrainData.name}), труба {(маг != null ? "показана" : "нет в сцене")}.");
        }
        catch (System.Exception e) { Debug.LogException(e); }
    }

    // Трасса — по магистрали в сцене, а если её нет — по старой трубе спавнера (как MissionBootstrap).
    static bool НайтиТрассу(out Vector3 центр, out Vector3 ось, out float длина, out МагистральныйТрубопровод маг)
    {
        центр = ось = Vector3.zero; длина = 120f;
        маг = Object.FindFirstObjectByType<МагистральныйТрубопровод>();
        if (маг != null)
        {
            центр = (маг.Начало + маг.Конец) * 0.5f;
            ось = маг.Конец - маг.Начало; ось.y = 0f;
            if (ось.sqrMagnitude < 1e-4f) ось = Vector3.right;
            ось.Normalize();
            длина = маг.ДлинаТрассы_м;
            return true;
        }
        var спавнер = Object.FindFirstObjectByType<PipelineDefectSpawner>(FindObjectsInactive.Include);
        if (спавнер == null || спавнер.Труба == null) return false;
        var рендеры = спавнер.Труба.GetComponentsInChildren<Renderer>(true);
        if (рендеры.Length == 0) return false;
        Bounds b = рендеры[0].bounds;
        foreach (var r in рендеры) b.Encapsulate(r.bounds);
        центр = b.center;
        ось = b.size.x >= b.size.z ? Vector3.right : Vector3.forward;
        return true;
    }

    static void ЗаписатьДно(Terrain terrain, ТрассаДна.Трасса т, GameObject труба)
    {
        ПостроительДна.Рассчитать(terrain, т, out float уровень, out Vector3 o, out Vector3 size);
        var наДне = ПостроительДна.ОбъектыНаДне(terrain, труба);
        var td = Object.Instantiate(terrain.terrainData);
        ПостроительДна.Сгенерировать(td, т, o, size, уровень, ПостроительДна.Слои(СлойАссет));

        if (!AssetDatabase.IsValidFolder(Папка)) AssetDatabase.CreateFolder("Assets", "Terrain");
        string путь = $"{Папка}/{ПостроительДна.ИмяДанных}.asset";
        AssetDatabase.DeleteAsset(путь);
        AssetDatabase.CreateAsset(td, путь);

        var col = terrain.GetComponent<TerrainCollider>();
        Undo.RecordObject(terrain, "Дно трассы");
        Undo.RecordObject(terrain.transform, "Дно трассы");
        if (col != null) Undo.RecordObject(col, "Дно трассы");
        foreach (var (tr, _, _) in наДне) Undo.RecordObject(tr, "Дно трассы");
        ПостроительДна.Назначить(terrain, td, o);
        ПостроительДна.Сдвинуть(terrain, наДне, rb: false);
        PrefabUtility.RecordPrefabInstancePropertyModifications(terrain);
        PrefabUtility.RecordPrefabInstancePropertyModifications(terrain.transform);
        if (col != null) PrefabUtility.RecordPrefabInstancePropertyModifications(col);
        foreach (var (tr, _, _) in наДне) PrefabUtility.RecordPrefabInstancePropertyModifications(tr);

        // Старые версии дна больше никто не использует.
        foreach (var guid in AssetDatabase.FindAssets("t:TerrainData", new[] { Папка }))
        {
            string п = AssetDatabase.GUIDToAssetPath(guid);
            string имя = Path.GetFileNameWithoutExtension(п);
            if (п != путь && (имя.StartsWith("Дно_трассы") || имя.StartsWith("Дно трассы"))) AssetDatabase.DeleteAsset(п);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        Debug.Log($"[Дно] Дно трассы записано в {путь} и назначено рельефу «{terrain.name}». Сохрани сцену.");
    }

    // Слои рельефа в редакторе — ассетами (иначе ссылки из ассета рельефа не сохранятся).
    static TerrainLayer СлойАссет(string имя)
    {
        string путь = $"{Папка}/{имя}.terrainlayer";
        var слой = AssetDatabase.LoadAssetAtPath<TerrainLayer>(путь);
        var образец = ПостроительДна.Слой(имя);
        if (образец == null) return слой;
        if (слой == null)
        {
            if (!AssetDatabase.IsValidFolder(Папка)) AssetDatabase.CreateFolder("Assets", "Terrain");
            AssetDatabase.CreateAsset(образец, путь);
            return образец;
        }
        EditorUtility.CopySerialized(образец, слой);
        слой.name = имя;
        EditorUtility.SetDirty(слой);
        return слой;
    }
}
