using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.AI.Assistant.Skills;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// Executes skill folder scans and owns all scan results and state.
    /// Triggered by <see cref="SkillsRegistryInitializer"/>.
    /// </summary>
    static class SkillsScanner
    {
        internal static DateTime LastRescanTime { get; private set; }

        static readonly string s_UserAppDataFolder = ComputeUserAppDataFolder();
        internal static string UserAppDataFolder => s_UserAppDataFolder;

        internal static readonly SkillsLoadResults LoadResults = new();

        internal static event Action OnSkillsRescanned;

        // Tracks project skill paths from the last scan — read by SkillsRegistryInitializer for change detection
        static readonly HashSet<string> s_LastProjectSkillPaths = new(StringComparer.OrdinalIgnoreCase);
        internal static HashSet<string> LastProjectSkillPaths => s_LastProjectSkillPaths;

        internal static bool InternalSkillsEnabled { get; private set; }

        static string ComputeUserAppDataFolder()
        {
            var appDataRoot = Application.platform == RuntimePlatform.OSXEditor
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support")
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataRoot, "Unity", "AIAssistantSkills");
        }

        internal static void RescanAll()
        {
            RescanProject();
            RescanUser();
        }

        internal static void RescanProject()
        {
            TimedScan("Project", ScanProjectFolders);
        }

        internal static void RescanUser()
        {
            TimedScan("User", ScanUserAppDataFolder);
        }

        static void ScanProjectFolders()
        {
            var tag = SkillRegistryTags.Project;
            SkillsRegistry.RemoveByTag(tag);

            var skillAssetPaths = AssetDatabase.FindAssets("t:TextAsset SKILL", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileName(path) == "SKILL.md")
                .ToList();

            s_LastProjectSkillPaths.Clear();
            foreach (var ap in skillAssetPaths)
            {
                var folder = Path.GetDirectoryName(ap)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(folder))
                    s_LastProjectSkillPaths.Add(folder);
            }

            var skillFiles = skillAssetPaths
                .Select(path => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)))
                .ToList();

            var allSkills = new List<SkillDefinition>();
            var allIssues = new List<SkillFileIssue>();

            SkillUtils.LoadSkillFiles(skillFiles, tag, allSkills, allIssues);
            SkillsRegistry.AddSkills(allSkills, allIssues);
            LoadResults.StoreIssues(tag, allIssues);
            OnSkillsRescanned?.Invoke();
        }

        internal static void ForceRescan()
        {
            RescanAll();
            SkillsRegistryInitializer.StartUserFolderWatcher(UserAppDataFolder);
        }

        internal static void CreateUserFolder()
        {
            try
            {
                Directory.CreateDirectory(UserAppDataFolder);
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"[SkillsScanner] CreateUserFolder: failed to create folder at {UserAppDataFolder}: {ex.Message}");
                return;
            }

            RescanUser();
            SkillsRegistryInitializer.StartUserFolderWatcher(UserAppDataFolder);
        }

        static void ScanUserAppDataFolder()
        {
            var tag = SkillRegistryTags.User;
            SkillsRegistry.RemoveByTag(tag);
            LoadResults.ClearIssues(tag);

            if (!Directory.Exists(UserAppDataFolder))
            {
                OnSkillsRescanned?.Invoke();
                return;
            }

            var allSkills = new List<SkillDefinition>();
            var allIssues = new List<SkillFileIssue>();
            SkillUtils.LoadSkillsFromFolder(UserAppDataFolder, tag, allSkills, allIssues);
            SkillsRegistry.AddSkills(allSkills, allIssues);
            LoadResults.StoreIssues(tag, allIssues);
            OnSkillsRescanned?.Invoke();
        }

        internal static void ShowInternalSkills(bool show, List<SkillFileIssue> issues = null)
        {
            InternalSkillsEnabled = show;
            if (show)
                LoadResults.StoreIssues(SkillRegistryTags.Internal, issues);
            else
                LoadResults.ClearIssues(SkillRegistryTags.Internal);
            OnSkillsRescanned?.Invoke();
        }

        static void TimedScan(string category, Action scan)
        {
            LastRescanTime = DateTime.Now;
            var sw = Stopwatch.StartNew();
            scan();
            sw.Stop();
            InternalLog.Log($"[SkillsScanner] '{category}' scan completed in {sw.ElapsedMilliseconds}ms");
        }
    }
}
