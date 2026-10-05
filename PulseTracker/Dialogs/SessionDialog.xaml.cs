using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PulseTracker.Models;
using PulseTracker.Services;

namespace PulseTracker.Dialogs
{
    public partial class SessionDialog : Window
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly string _sessionId;
        private readonly List<ExerciseEntry> _exercises = new();
        private bool _suppressPlanPrompt = false;

        public SessionDialog(string sessionId, string startPlanId, DateTime? dateOverride)
        {
            InitializeComponent();
            _sessionId = sessionId;

            AddExerciseCombo.ItemsSource = DataStore.Data.Exercises;
            if (DataStore.Data.Exercises.Count > 0) AddExerciseCombo.SelectedIndex = 0;

            BuildPlanCombo(null);

            if (_sessionId != null)
            {
                var s = DataStore.SessionById(_sessionId);
                TitleText.Text = "Изменить тренировку";
                DeleteBtn.Visibility = Visibility.Visible;
                DatePickerBox.SelectedDate = s.Date;
                NameBox.Text = s.Name;
                DurationBox.Text = s.DurationMin > 0 ? s.DurationMin.ToString() : "";
                RpeBox.Text = s.Rpe > 0 ? s.Rpe.ToString() : "";
                NotesBox.Text = s.Notes;
                foreach (var en in s.Exercises)
                    _exercises.Add(new ExerciseEntry
                    {
                        ExerciseId = en.ExerciseId,
                        Sets = en.Sets.Select(x => new SetEntry { Reps = x.Reps, Weight = x.Weight, DurationSec = x.DurationSec, DistanceKm = x.DistanceKm }).ToList()
                    });
                _suppressPlanPrompt = true;
                BuildPlanCombo(s.PlanId);
                _suppressPlanPrompt = false;
            }
            else
            {
                DatePickerBox.SelectedDate = dateOverride ?? DateTime.Today;
                if (startPlanId != null)
                {
                    var plan = DataStore.PlanById(startPlanId);
                    if (plan != null)
                    {
                        NameBox.Text = plan.Name;
                        _suppressPlanPrompt = true;
                        BuildPlanCombo(plan.Id);
                        _suppressPlanPrompt = false;
                        LoadExercisesFromPlan(plan);
                    }
                }
            }

            RenderExercises();
        }

        private void BuildPlanCombo(string selectPlanId)
        {
            PlanCombo.Items.Clear();
            PlanCombo.Items.Add(new ComboBoxItem { Content = "— свободная тренировка —", Tag = null });
            foreach (var p in DataStore.Data.Plans)
                PlanCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });

            PlanCombo.SelectedIndex = 0;
            if (selectPlanId != null)
                foreach (ComboBoxItem item in PlanCombo.Items)
                    if ((string)item.Tag == selectPlanId) { PlanCombo.SelectedItem = item; break; }
        }

        private void LoadExercisesFromPlan(WorkoutPlan plan)
        {
            _exercises.Clear();
            foreach (var it in plan.Items)
            {
                var ex = DataStore.ExerciseById(it.ExerciseId);
                var entry = new ExerciseEntry { ExerciseId = it.ExerciseId };
                if (ex != null && ex.Type == ExerciseType.Strength)
                    for (int i = 0; i < Math.Max(1, it.Sets); i++) entry.Sets.Add(new SetEntry { Reps = it.Reps, Weight = it.Weight });
                else if (ex != null && ex.Type == ExerciseType.Time)
                    for (int i = 0; i < Math.Max(1, it.Sets); i++) entry.Sets.Add(new SetEntry { DurationSec = 0 });
                else
                    entry.Sets.Add(new SetEntry { DurationSec = 0, DistanceKm = 0 });
                _exercises.Add(entry);
            }
        }

        private void PlanCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressPlanPrompt) return;
            if (PlanCombo.SelectedItem is not ComboBoxItem item || item.Tag == null) return;
            var plan = DataStore.PlanById((string)item.Tag);
            if (plan == null) return;
            if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = plan.Name;
            if (MessageBox.Show($"Загрузить упражнения из плана «{plan.Name}»? Текущий список упражнений будет заменён.",
                    "Подтверждение", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                LoadExercisesFromPlan(plan);
                RenderExercises();
            }
        }

        private void AddExercise_Click(object sender, RoutedEventArgs e)
        {
            if (AddExerciseCombo.SelectedItem is not Exercise ex) return;
            var entry = new ExerciseEntry { ExerciseId = ex.Id };
            entry.Sets.Add(ex.Type == ExerciseType.Time ? new SetEntry { DurationSec = 0 }
                : ex.Type == ExerciseType.Cardio ? new SetEntry { DurationSec = 0, DistanceKm = 0 }
                : new SetEntry { Reps = 0, Weight = 0 });
            _exercises.Add(entry);
            RenderExercises();
        }

        private void RenderExercises()
        {
            ExercisesPanel.Children.Clear();
            if (_exercises.Count == 0)
            {
                ExercisesPanel.Children.Add(new TextBlock
                {
                    Text = "Добавьте упражнения ниже.", Foreground = (Brush)FindResource("InkFaintBrush"),
                    FontSize = 13, Margin = new Thickness(2, 6, 2, 6)
                });
                return;
            }

            for (int idx = 0; idx < _exercises.Count; idx++)
            {
                int eIdx = idx;
                var entry = _exercises[eIdx];
                var ex = DataStore.ExerciseById(entry.ExerciseId);
                var name = ex?.Name ?? "Упражнение удалено";

                var inner = new StackPanel();
                var card = new Border
                {
                    Background = (Brush)FindResource("BgSunkenBrush"),
                    BorderBrush = (Brush)FindResource("LineSoftBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14),
                    Margin = new Thickness(0, 0, 0, 10),
                    Child = inner
                };

                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var rmEntry = new Button { Content = "🗑", Style = (Style)FindResource("IconButtonStyle") };
                rmEntry.Click += (_, __) => { _exercises.RemoveAt(eIdx); RenderExercises(); };
                DockPanel.SetDock(rmEntry, Dock.Right);
                head.Children.Add(rmEntry);
                head.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                inner.Children.Add(head);

                var type = ex?.Type ?? ExerciseType.Strength;

                if (type == ExerciseType.Cardio)
                {
                    if (entry.Sets.Count == 0) entry.Sets.Add(new SetEntry());
                    var s0 = entry.Sets[0];
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var durStack = new StackPanel();
                    durStack.Children.Add(new TextBlock { Text = "Время, мин", FontSize = 11, Foreground = (Brush)FindResource("InkFaintBrush"), Margin = new Thickness(0, 0, 0, 4) });
                    var durBox = new TextBox { Text = s0.DurationSec > 0 ? Math.Round(s0.DurationSec / 60.0).ToString(Inv) : "" };
                    durBox.TextChanged += (_, __) => { if (double.TryParse(durBox.Text, NumberStyles.Any, Inv, out var v)) s0.DurationSec = v * 60; };
                    durStack.Children.Add(durBox);
                    Grid.SetColumn(durStack, 0);
                    grid.Children.Add(durStack);

                    var distStack = new StackPanel();
                    distStack.Children.Add(new TextBlock { Text = "Дистанция, км", FontSize = 11, Foreground = (Brush)FindResource("InkFaintBrush"), Margin = new Thickness(0, 0, 0, 4) });
                    var distBox = new TextBox { Text = s0.DistanceKm > 0 ? s0.DistanceKm.ToString(Inv) : "" };
                    distBox.TextChanged += (_, __) => { if (double.TryParse(distBox.Text, NumberStyles.Any, Inv, out var v)) s0.DistanceKm = v; };
                    distStack.Children.Add(distBox);
                    Grid.SetColumn(distStack, 2);
                    grid.Children.Add(distStack);

                    inner.Children.Add(grid);
                }
                else
                {
                    for (int j = 0; j < entry.Sets.Count; j++)
                    {
                        int sIdx = j;
                        var set = entry.Sets[sIdx];
                        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        if (type == ExerciseType.Strength) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

                        var idxText = new TextBlock { Text = (sIdx + 1).ToString(), FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("InkFaintBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                        Grid.SetColumn(idxText, 0);
                        row.Children.Add(idxText);

                        if (type == ExerciseType.Strength)
                        {
                            var repsBox = new TextBox { Text = set.Reps > 0 ? set.Reps.ToString() : "" };
                            repsBox.ToolTip = "Повторы";
                            repsBox.TextChanged += (_, __) => { if (int.TryParse(repsBox.Text, out var v)) set.Reps = v; };
                            Grid.SetColumn(repsBox, 1);
                            row.Children.Add(repsBox);

                            var weightBox = new TextBox { Text = set.Weight > 0 ? set.Weight.ToString(Inv) : "" };
                            weightBox.ToolTip = "Вес, кг";
                            weightBox.TextChanged += (_, __) => { if (double.TryParse(weightBox.Text, NumberStyles.Any, Inv, out var v)) set.Weight = v; };
                            Grid.SetColumn(weightBox, 2);
                            row.Children.Add(weightBox);

                            var rmSet = new Button { Content = "✕", Style = (Style)FindResource("IconButtonStyle") };
                            rmSet.Click += (_, __) => { entry.Sets.RemoveAt(sIdx); RenderExercises(); };
                            Grid.SetColumn(rmSet, 3);
                            row.Children.Add(rmSet);
                        }
                        else
                        {
                            var durBox = new TextBox { Text = set.DurationSec > 0 ? set.DurationSec.ToString(Inv) : "" };
                            durBox.ToolTip = "Секунд";
                            durBox.TextChanged += (_, __) => { if (double.TryParse(durBox.Text, NumberStyles.Any, Inv, out var v)) set.DurationSec = v; };
                            Grid.SetColumn(durBox, 1);
                            row.Children.Add(durBox);

                            var rmSet = new Button { Content = "✕", Style = (Style)FindResource("IconButtonStyle") };
                            rmSet.Click += (_, __) => { entry.Sets.RemoveAt(sIdx); RenderExercises(); };
                            Grid.SetColumn(rmSet, 2);
                            row.Children.Add(rmSet);
                        }

                        inner.Children.Add(row);
                    }

                    var addSet = new Button { Content = "+ подход", Style = (Style)FindResource("GhostButtonStyle"), HorizontalAlignment = HorizontalAlignment.Left };
                    addSet.Click += (_, __) =>
                    {
                        entry.Sets.Add(type == ExerciseType.Time ? new SetEntry { DurationSec = 0 } : new SetEntry { Reps = 0, Weight = 0 });
                        RenderExercises();
                    };
                    inner.Children.Add(addSet);
                }

                ExercisesPanel.Children.Add(card);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Тренировка" : NameBox.Text.Trim();
            var date = DatePickerBox.SelectedDate ?? DateTime.Today;
            string planId = (PlanCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            int.TryParse(DurationBox.Text, out var duration);
            int.TryParse(RpeBox.Text, out var rpe);

            if (_sessionId != null)
            {
                var s = DataStore.SessionById(_sessionId);
                s.Name = name; s.Date = date; s.PlanId = planId;
                s.DurationMin = duration; s.Rpe = rpe; s.Notes = NotesBox.Text.Trim();
                s.Exercises = _exercises;
            }
            else
            {
                DataStore.Data.Sessions.Add(new WorkoutSession
                {
                    Name = name, Date = date, PlanId = planId, DurationMin = duration,
                    Rpe = rpe, Notes = NotesBox.Text.Trim(), Exercises = _exercises
                });
            }
            DataStore.Save();
            DialogResult = true;
            Close();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_sessionId == null) return;
            if (MessageBox.Show("Удалить эту запись из дневника?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var s = DataStore.SessionById(_sessionId);
            DataStore.Data.Sessions.Remove(s);
            DataStore.Save();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
