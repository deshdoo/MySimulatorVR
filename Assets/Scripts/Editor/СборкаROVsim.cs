using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Название приложения и сборка APK для автономного шлема (Quest без ПК).
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
        ПоставитьНазвание();
        Directory.CreateDirectory("Builds");
        var сцены = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var отчёт = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = сцены,
            locationPathName = $"Builds/{Название}.apk",
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        });
        var итог = отчёт.summary;
        Debug.Log($"[Сборка] {итог.result}: {итог.outputPath}, {итог.totalSize / (1024 * 1024)} МБ, " +
                  $"ошибок {итог.totalErrors}, {итог.totalTime.TotalMinutes:0.0} мин");
        if (Application.isBatchMode) EditorApplication.Exit(итог.result == BuildResult.Succeeded ? 0 : 1);
    }
}
