using System;
using System.Collections.Generic;
using System.Windows;
using PulseTracker.Models;
using PulseTracker.Services;

namespace PulseTracker.Dialogs
{
    public partial class AssignTrainingDialog : Window
    {
        private readonly int _userId;
        private readonly List<WorkoutPlan> _plans;

        public AssignTrainingDialog(int userId, string userFullName)
        {
            InitializeComponent();
            _userId = userId;
            _plans = AdminService.GetPlansForUser(userId);

            TitleText.Text = "Назначить тренировку";
            SubtitleText.Text = "Пользователь: " + userFullName;
            DatePickerBox.SelectedDate = DateTime.Today;

            PlanCombo.ItemsSource = _plans;
            if (_plans.Count > 0)
            {
                PlanCombo.SelectedIndex = 0;
                NameBox.Text = _plans[0].Name;
            }
        }

        private void PlanCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Раньше название тренировки менялось только один раз (пока поле было пустым),
            // поэтому после выбора другого плана (Б/В/Г и т.д.) в поле оставалось «День А…»,
            // хотя план был выбран правильно — из-за этого казалось, что тренеру доступен
            // только один вид тренировки. Теперь название всегда подстраивается под
            // выбранный план, если только тренер не ввёл своё название вручную.
            if (PlanCombo.SelectedItem is not WorkoutPlan plan) return;

            bool nameMatchesPreviousPlan = string.IsNullOrWhiteSpace(NameBox.Text) ||
                _plans.Exists(p => p.Name == NameBox.Text);
            if (nameMatchesPreviousPlan)
                NameBox.Text = plan.Name;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_plans.Count == 0)
            {
                ShowError("У этого пользователя пока нет ни одного плана тренировок.");
                return;
            }
            if (PlanCombo.SelectedItem is not WorkoutPlan plan)
            {
                ShowError("Выберите план.");
                return;
            }

            var date = DatePickerBox.SelectedDate ?? DateTime.Today;
            int.TryParse(DurationBox.Text, out var duration);
            int.TryParse(RpeBox.Text, out var rpe);

            try
            {
                AdminService.AssignSessionFromPlan(_userId, date, NameBox.Text.Trim(), plan.Id, duration, rpe, NotesBox.Text.Trim());
            }
            catch (Exception ex)
            {
                ShowError("Не удалось назначить тренировку: " + ex.Message);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void ShowError(string text)
        {
            ErrorText.Text = text;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
