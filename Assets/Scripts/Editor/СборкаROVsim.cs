using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Название приложения и сборки: APK для автономного шлема (Quest без ПК) и EXE для ПК со шлемом.
//
// Название «ROVsim» ставится само после компиляции (подпись под значком на шлеме и
// в Windows). Идентификатор пакета не меняется: иначе шлем считал бы сборку новым
// приложением, и сохранённые настройки и миссии на нём пропали бы.
//
// APK собирается без открытого редактора, из командной строки (редактор должен быть
// закрыт — Unity не открывает один проект дважды):
//   Unity.exe -batchmode -quit -projectPath <проект> -buildTarget Android
//             -executeMethod СборкаROVsim.APK -logFile Builds/build.log
// Результат — Builds/ROVsim.apk (папка Builds в git не идёт).
// Эмулятор XR Interaction Simulator в APK не попадает: в его настройках включено
// «только в редакторе», а на шлеме ввод идёт от самого шлема (OpenXR, Meta Quest).
[InitializeOnLoad]
static class СборкаROVsim
{
    public const string Название = "ROVsim";

    static СборкаROVsim() => EditorApplication.delayCall += ПоставитьНазвание;

    static void ПоставитьНазвание()
    {
        if (PlayerSettings.productName == Название) return;
        PlayerSettings.productName = Название;
        AssetDatabase.SaveAssets();
        Debug.Log($"[Сборка] Название приложения: {Название}");
    }

    public static void APK()
    {
        Собрать($"Builds/{Название}.apk", BuildTarget.Android, BuildTargetGroup.Android);
        if (Application.isBatchMode) EditorApplication.Exit(_успех ? 0 : 1);
    }

    // EXE для ПК со шлемом (Quest через Link или Air Link, SteamVR): OpenXR под Windows.
    //   Unity.exe -batchmode -quit -projectPath <проект> -buildTarget Win64
    //             -executeMethod СборкаROVsim.EXE -logFile Builds/build_pc.log
    // Результат — папка Builds/ROVsim_PC: ROVsim.exe рядом с ROVsim_Data, переносить целиком.
    // Сборка на Mono: модуля IL2CPP под Windows в установленном Unity нет. Настройка
    // проекта (IL2CPP) на время сборки подменяется и возвращается обратно.
    public static void EXE()
    {
        var группа = UnityEditor.Build.NamedBuildTarget.Standalone;
        var было = PlayerSettings.GetScriptingBackend(группа);
        PlayerSettings.SetScriptingBackend(группа, ScriptingImplementation.Mono2x);
        try { Собрать($"Builds/{Название}_PC/{Название}.exe", BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone); }
        finally { PlayerSettings.SetScriptingBackend(группа, было); AssetDatabase.SaveAssets(); }
        if (Application.isBatchMode) EditorApplication.Exit(_успех ? 0 : 1);
    }

    static bool _успех;

    static void Собрать(string путь, BuildTarget цель, BuildTargetGroup группа)
    {
        ПоставитьНазвание();
        Directory.CreateDirectory(Path.GetDirectoryName(путь));
        var сцены = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var отчёт = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = сцены,
            locationPathName = путь,
            target = цель,
            targetGroup = группа,
            options = BuildOptions.None,
        });
        var итог = отчёт.summary;
        _успех = итог.result == BuildResult.Succeeded;
        Debug.Log($"[Сборка] {итог.result}: {итог.outputPath}, {итог.totalSize / (1024 * 1024)} МБ, " +
                  $"ошибок {итог.totalErrors}, {итог.totalTime.TotalMinutes:0.0} мин");
    }
}
