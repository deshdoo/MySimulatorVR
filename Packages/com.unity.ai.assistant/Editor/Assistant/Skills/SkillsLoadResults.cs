using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Skills;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// Holds parse errors produced by skill scans.
    /// </summary>
    class SkillsLoadResults
    {
        readonly Dictionary<string, List<SkillFileIssue>> m_ParsingIssuesByTag = new();
        readonly List<SkillFileIssue> m_SortedParsingIssues = new();
        bool m_ParsingIssuesDirty = true;

        /// <summary>
        /// All parse errors across all skill sources, sorted by skill folder name.
        /// </summary>
        internal IReadOnlyList<SkillFileIssue> SkillParsingIssues
        {
            get
            {
                if (m_ParsingIssuesDirty)
                {
                    SortIssues();
                    m_ParsingIssuesDirty = false;
                }
                return m_SortedParsingIssues;
            }
        }

        void SortIssues()
        {
            m_SortedParsingIssues.Clear();
            foreach (var list in m_ParsingIssuesByTag.Values)
                m_SortedParsingIssues.AddRange(list);
            m_SortedParsingIssues.Sort((a, b) =>
            {
                if (string.IsNullOrEmpty(a.DisplayName) && string.IsNullOrEmpty(b.DisplayName)) return 0;
                if (string.IsNullOrEmpty(a.DisplayName)) return -1;
                if (string.IsNullOrEmpty(b.DisplayName)) return 1;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Stores the issues from scanning one type (tag) of skills.
        /// </summary>
        internal void StoreIssues(string tag, List<SkillFileIssue> issues)
        {
            if (issues == null)
            {
                ClearIssues(tag);
                return;
            }
            
            m_ParsingIssuesByTag[tag] = issues;
            m_ParsingIssuesDirty = true;
        }

        /// <summary>
        /// Removes all stored issues for the given tag.
        /// </summary>
        internal void ClearIssues(string tag)
        {
            if (m_ParsingIssuesByTag.Remove(tag))
                m_ParsingIssuesDirty = true;
        }
    }
}
