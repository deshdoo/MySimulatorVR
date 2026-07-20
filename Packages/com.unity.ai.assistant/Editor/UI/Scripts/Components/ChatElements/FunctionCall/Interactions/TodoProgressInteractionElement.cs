using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.UserInteraction;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class TodoProgressInteractionElement : InteractionContentView
    {
        const string k_ExpandedClass = "todo-progress-expand-icon--expanded";

        string m_PlanSubtitle;

        Label m_SubtitleLabel;
        Label m_CurrentStepLabel;
        VisualElement m_SpinnerSlot;
        VisualElement m_CurrentStepRow;
        ScrollView m_TaskListScroll;
        VisualElement m_TaskList;
        Button m_ExpandButton;
        VisualElement m_ExpandIcon;
        LoadingSpinner m_Spinner;

        bool m_Expanded = true;
        List<TodoItem> m_CurrentItems;

        public TodoProgressInteractionElement(string planPath)
        {
            m_PlanSubtitle = GetPlanDisplayName(planPath);
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_SubtitleLabel = view.Q<Label>("subtitleLabel");
            m_CurrentStepLabel = view.Q<Label>("currentStepLabel");
            m_SpinnerSlot = view.Q<VisualElement>("spinnerSlot");
            m_CurrentStepRow = view.Q<VisualElement>("currentStepRow");
            m_TaskListScroll = view.Q<ScrollView>("taskListScroll");
            m_TaskListScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            m_TaskListScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            m_TaskList = view.Q<VisualElement>("taskList");
            m_ExpandButton = view.SetupButton("expandButton", _ => ToggleExpanded());
            m_ExpandIcon = view.Q<VisualElement>("expandIcon");

            m_SubtitleLabel.text = m_PlanSubtitle;
            m_SubtitleLabel.SetDisplay(!string.IsNullOrEmpty(m_PlanSubtitle));

            m_Spinner = new LoadingSpinner();
            m_SpinnerSlot.Add(m_Spinner);

            UpdateExpandButton();

            m_CurrentStepRow.SetDisplay(!m_Expanded);
            m_TaskListScroll.SetDisplay(m_Expanded);

            if (m_CurrentItems != null)
                RefreshView();
        }

        public void UpdateTodos(List<TodoItem> items, string planPath)
        {
            m_CurrentItems = items;
            m_PlanSubtitle = GetPlanDisplayName(planPath);

            if (IsInitialized)
            {
                m_SubtitleLabel.text = m_PlanSubtitle;
                m_SubtitleLabel.SetDisplay(!string.IsNullOrEmpty(m_PlanSubtitle));
                RefreshView();
            }
        }

        void RefreshView()
        {
            if (m_CurrentItems == null || m_CurrentItems.Count == 0)
                return;

            var inProgress = m_CurrentItems.FirstOrDefault(t =>
                string.Equals(t.Status, "in_progress", StringComparison.OrdinalIgnoreCase));

            // Current step row — only visible when collapsed (expanded task list already shows it)
            if (inProgress != null)
            {
                m_CurrentStepLabel.text = inProgress.Description ?? string.Empty;
                m_CurrentStepRow.SetDisplay(!m_Expanded);
                m_Spinner.Show();
            }
            else
            {
                m_CurrentStepRow.SetDisplay(false);
                m_Spinner.Hide();
            }

            // Rebuild task list
            m_TaskList.Clear();
            foreach (var item in m_CurrentItems)
            {
                var row = new TodoTaskRow();
                row.Initialize(Context);
                row.SetData(item);
                m_TaskList.Add(row);
            }

            // All items terminal — plan is done
            if (m_CurrentItems.All(t =>
                string.Equals(t.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Status, "cancelled", StringComparison.OrdinalIgnoreCase)))
            {
                InvokeCompleted();
            }
        }

        void ToggleExpanded()
        {
            m_Expanded = !m_Expanded;
            m_TaskListScroll.SetDisplay(m_Expanded);
            // Current step row is a summary visible only when collapsed
            if (m_CurrentStepRow != null)
                m_CurrentStepRow.SetDisplay(!m_Expanded && m_CurrentItems?.Any(t =>
                    string.Equals(t.Status, "in_progress", System.StringComparison.OrdinalIgnoreCase)) == true);
            UpdateExpandButton();
        }

        void UpdateExpandButton()
        {
            if (m_ExpandIcon != null)
                m_ExpandIcon.EnableInClassList(k_ExpandedClass, m_Expanded);
        }

        // Strip directory components from LLM-provided path before display.
        static string GetPlanDisplayName(string planPath)
        {
            if (string.IsNullOrEmpty(planPath)) return string.Empty;
            return Path.GetFileNameWithoutExtension(planPath);
        }
    }
}
