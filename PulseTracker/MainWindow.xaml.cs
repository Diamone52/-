using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PulseTracker.Models;
using PulseTracker.Services;
using PulseTracker.Dialogs;

namespace PulseTracker
{
    public partial class MainWindow : Window
    {
        private static readonly CultureInfo Ru = new("ru-RU");
        private DateTime _calCursor = DateTime.Today;

        public MainWindow()
        {
            InitializeComponent();
            NavAdmin.Visibility = AuthService.CanManageUsers ? Visibility.Visible : Visibility.Collapsed;
            BuildCalendarDowRow();
            NavDashboard.IsChecked = true;
            RenderDashboard();
            Loaded += (_, __) => RenderProgress();
            UpdateThemeButtonLabel();
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            AuthService.Logout();
            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            Close();
        }

        // ===================== Тема оформления =====================
        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.Toggle();
            UpdateThemeButtonLabel();

            // Разметка в XAML подхватывает новую палитру сама (DynamicResource), но элементы,
            // построенные в коде (календарь, карточки, графики), захватывают кисти через
            // FindResource один раз при создании — поэтому перерисовываем текущую вкладку.
            RefreshCurrentPageForTheme();
        }

        private void UpdateThemeButtonLabel()
        {
            ThemeToggleBtn.Content = ThemeService.Current == AppTheme.Dark ? "☀" : "🌙";
        }

        // Клик по самому календарю (за пределами конкретного дня) тоже переключает тему —
        // как дополнительный, более крупный триггер помимо кнопки в шапке. Клики по дням
        // и по кнопкам «←»/«→» гасятся раньше и сюда не доходят (см. RenderCalendar,
        // CalPrev_Click, CalNext_Click).
        private void CalendarPanel_Click(object sender, MouseButtonEventArgs e)
        {
            ThemeToggle_Click(sender, e);
        }

        private void RefreshCurrentPageForTheme()
        {
            string page = new[] { NavDashboard, NavLog, NavPlans, NavExercises, NavProgress, NavProfile, NavAdmin }
                .FirstOrDefault(rb => rb.IsChecked == true)?.Tag as string ?? "Dashboard";

            switch (page)
            {
                case "Dashboard": RenderDashboard(); break;
                case "Log": RenderCalendar(); RenderFullSessionList(); break;
                case "Plans": RenderPlans(); break;
                case "Exercises": RenderExercises(); break;
                case "Progress": RenderProgress(); break;
                case "Profile": RenderProfile(); break;
                case "Admin": RenderAdmin(); break;
            }
        }

        // ===================== Навигация =====================
        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (DashboardPage == null) return; // событие может прийти раньше, чем разметка окна полностью связана
            if (sender is not RadioButton rb) return;
            string page = rb.Tag as string;

            DashboardPage.Visibility = page == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
            LogPage.Visibility = page == "Log" ? Visibility.Visible : Visibility.Collapsed;
            PlansPage.Visibility = page == "Plans" ? Visibility.Visible : Visibility.Collapsed;
            ExercisesPage.Visibility = page == "Exercises" ? Visibility.Visible : Visibility.Collapsed;
            ProgressPage.Visibility = page == "Progress" ? Visibility.Visible : Visibility.Collapsed;
            ProfilePage.Visibility = page == "Profile" ? Visibility.Visible : Visibility.Collapsed;
            AdminPage.Visibility = page == "Admin" ? Visibility.Visible : Visibility.Collapsed;

            switch (page)
            {
                case "Dashboard": RenderDashboard(); break;
                case "Log": RenderCalendar(); RenderFullSessionList(); break;
                case "Plans": RenderPlans(); break;
                case "Exercises": RenderExercises(); break;
                case "Progress": RenderProgress(); break;
                case "Profile": RenderProfile(); break;
                case "Admin": RenderAdmin(); break;
            }
        }

        // ===================== Общие помощники =====================
        private static string FmtDate(DateTime d) => d.ToString("d MMM", Ru);

        private Border SmallPill(string text, Brush fg, Brush bg)
        {
            return new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(9, 2, 9, 2),
                Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = fg }
            };
        }

        private Border BuildSessionRow(WorkoutSession s, bool showDate = true)
        {
            var exNames = string.Join(", ", s.Exercises
                .Select(en => DataStore.ExerciseById(en.ExerciseId)?.Name)
                .Where(n => !string.IsNullOrEmpty(n)));

            var grid = new Grid { Margin = new Thickness(2, 9, 2, 9) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = showDate ? new GridLength(56) : new GridLength(0) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (showDate)
            {
                var dateText = new TextBlock
                {
                    Text = FmtDate(s.Date), FontWeight = FontWeights.SemiBold, FontSize = 14,
                    Foreground = (Brush)FindResource("InkDimBrush"), VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(dateText, 0);
                grid.Children.Add(dateText);
            }

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(new TextBlock { Text = s.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            mid.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(exNames) ? "—" : exNames,
                FontSize = 12, Foreground = (Brush)FindResource("InkFaintBrush"), Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(mid, 1);
            grid.Children.Add(mid);

            if (s.Rpe > 0)
            {
                var pill = SmallPill("RPE " + s.Rpe, (Brush)FindResource("GoodBrush"), (Brush)FindResource("GoodSoftBrush"));
                pill.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(pill, 2);
                grid.Children.Add(pill);
            }

            var border = new Border
            {
                BorderBrush = (Brush)FindResource("LineSoftBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Cursor = Cursors.Hand,
                Child = grid
            };
            border.MouseLeftButtonUp += (_, __) => OpenSessionDialog(s.Id);
            return border;
        }

        // ===================== ОБЗОР =====================
        private void RenderDashboard()
        {
            TodayText.Text = DateTime.Today.ToString("dddd, d MMMM", Ru);
            TodayText.Text = char.ToUpper(TodayText.Text[0]) + TodayText.Text.Substring(1);

            var weekStart = DataStore.StartOfWeek(DateTime.Today);
            var weekSessions = DataStore.Data.Sessions.Where(s => s.Date >= weekStart).ToList();
            double weekVolume = weekSessions.Sum(s => DataStore.SessionVolume(s));

            var sessionDates = new HashSet<DateTime>(DataStore.Data.Sessions.Select(s => s.Date.Date));
            int streak = 0;
            var cursor = DateTime.Today;
            while (sessionDates.Contains(cursor)) { streak++; cursor = cursor.AddDays(-1); }

            StatsPanel.Children.Clear();
            StatsPanel.Children.Add(BuildStatCard("Тренировок на этой неделе", weekSessions.Count.ToString(), ""));
            StatsPanel.Children.Add(BuildStatCard("Объём за неделю", Math.Round(weekVolume).ToString("N0", Ru), "кг"));
            StatsPanel.Children.Add(BuildStatCard("Серия дней подряд", streak.ToString(), streak == 1 ? "день" : "дн."));
            StatsPanel.Children.Add(BuildStatCard("Всего записей в дневнике", DataStore.Data.Sessions.Count.ToString(), ""));

            RecentSessionsPanel.Children.Clear();
            var recent = DataStore.Data.Sessions.OrderByDescending(s => s.Date).Take(5).ToList();
            if (recent.Count == 0)
                RecentSessionsPanel.Children.Add(EmptyNote("Пока нет записей. Нажмите «Записать тренировку», чтобы начать дневник."));
            else
                foreach (var s in recent) RecentSessionsPanel.Children.Add(BuildSessionRow(s));

            RecordsPanel.Children.Clear();
            var records = DataStore.Data.Exercises.Where(e => e.Type == ExerciseType.Strength)
                .Select(e =>
                {
                    double best = 0; DateTime bestDate = default;
                    foreach (var s in DataStore.Data.Sessions)
                    {
                        var top = DataStore.SessionTopSet(s, e.Id);
                        if (top > best) { best = top; bestDate = s.Date; }
                    }
                    return (ex: e, best, bestDate);
                })
                .Where(r => r.best > 0)
                .OrderByDescending(r => r.best)
                .ToList();

            if (records.Count == 0)
                RecordsPanel.Children.Add(EmptyNote("Записывайте силовые тренировки, чтобы увидеть личные рекорды."));
            else
                foreach (var r in records)
                {
                    var grid = new Grid { Margin = new Thickness(2, 9, 2, 9) };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var mid = new StackPanel();
                    mid.Children.Add(new TextBlock { Text = r.ex.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
                    mid.Children.Add(new TextBlock { Text = FmtDate(r.bestDate), FontSize = 12, Foreground = (Brush)FindResource("InkFaintBrush") });
                    Grid.SetColumn(mid, 0);
                    grid.Children.Add(mid);
                    var pill = SmallPill(r.best.ToString(Ru) + " кг", (Brush)FindResource("GoodBrush"), (Brush)FindResource("GoodSoftBrush"));
                    pill.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(pill, 1);
                    grid.Children.Add(pill);
                    var border = new Border { BorderBrush = (Brush)FindResource("LineSoftBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
                    RecordsPanel.Children.Add(border);
                }
        }

        private Border BuildStatCard(string label, string num, string unit)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("InkFaintBrush"), Margin = new Thickness(0, 0, 0, 8) });
            var numPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
            numPanel.Children.Add(new TextBlock { Text = num, FontSize = 34, FontWeight = FontWeights.Bold, FontFamily = (FontFamily)FindResource("DisplayFontFamily") });
            if (!string.IsNullOrEmpty(unit))
                numPanel.Children.Add(new TextBlock { Text = " " + unit, FontSize = 13, Foreground = (Brush)FindResource("InkDimBrush"), Margin = new Thickness(4, 0, 0, 4) });
            stack.Children.Add(numPanel);

            var border = new Border
            {
                Style = (Style)FindResource("StatCardStyle"),
                Width = 250,
                BorderThickness = new Thickness(3, 1, 1, 1),
                BorderBrush = (Brush)FindResource("LineSoftBrush"),
                Child = stack
            };
            border.BorderBrush = (Brush)FindResource("AccentBrush");
            return border;
        }

        private TextBlock EmptyNote(string text) => new()
        {
            Text = text, Foreground = (Brush)FindResource("InkFaintBrush"), FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 10, 2, 10)
        };

        // ===================== ДНЕВНИК / КАЛЕНДАРЬ =====================
        private void BuildCalendarDowRow()
        {
            CalendarDowRow.Children.Clear();
            foreach (var d in new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" })
                CalendarDowRow.Children.Add(new TextBlock
                {
                    Text = d, FontSize = 10.5, FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("InkFaintBrush"), HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4)
                });
        }

        private void RenderCalendar()
        {
            CalTitleText.Text = _calCursor.ToString("MMMM yyyy", Ru);
            CalTitleText.Text = char.ToUpper(CalTitleText.Text[0]) + CalTitleText.Text.Substring(1);

            var first = new DateTime(_calCursor.Year, _calCursor.Month, 1);
            int startOffset = ((int)first.DayOfWeek + 6) % 7;
            int daysInMonth = DateTime.DaysInMonth(_calCursor.Year, _calCursor.Month);

            var byDate = DataStore.Data.Sessions.GroupBy(s => s.Date.Date).ToDictionary(g => g.Key, g => g.ToList());

            CalendarGrid.Children.Clear();
            for (int i = 0; i < startOffset; i++)
                CalendarGrid.Children.Add(new Border { Height = 46 });

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(_calCursor.Year, _calCursor.Month, day);
                bool has = byDate.ContainsKey(date);
                bool today = date == DateTime.Today;

                var cell = new Border
                {
                    Height = 46, Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Background = has ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("BgSunkenBrush"),
                    BorderBrush = today ? (Brush)FindResource("InkDimBrush") : (Brush)FindResource("LineSoftBrush")
                };
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new TextBlock
                {
                    Text = day.ToString(), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = today ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = has ? (Brush)FindResource("AccentSoftInkBrush") : (Brush)FindResource("InkDimBrush")
                });
                if (has)
                    stack.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = (Brush)FindResource("AccentSoftInkBrush"), Margin = new Thickness(0, 2, 0, 0) });
                cell.Child = stack;
                cell.MouseLeftButtonUp += (_, ev) =>
                {
                    // Не даём клику по дню "провалиться" на панель календаря и заодно
                    // переключить тему — открываем только диалог этого дня.
                    ev.Handled = true;
                    OpenDayDialog(date, byDate.TryGetValue(date, out var list) ? list : new List<WorkoutSession>());
                };
                CalendarGrid.Children.Add(cell);
            }
        }

        private void CalPrev_Click(object sender, RoutedEventArgs e) { _calCursor = _calCursor.AddMonths(-1); RenderCalendar(); }
        private void CalNext_Click(object sender, RoutedEventArgs e) { _calCursor = _calCursor.AddMonths(1); RenderCalendar(); }

        private void RenderFullSessionList()
        {
            FullSessionsPanel.Children.Clear();
            var sorted = DataStore.Data.Sessions.OrderByDescending(s => s.Date).ToList();
            if (sorted.Count == 0) { FullSessionsPanel.Children.Add(EmptyNote("Дневник пуст. Добавьте первую тренировку.")); return; }
            foreach (var s in sorted) FullSessionsPanel.Children.Add(BuildSessionRow(s));
        }

        private void OpenDayDialog(DateTime date, List<WorkoutSession> sessions)
        {
            var dlg = new DayDialog(date, sessions) { Owner = this };
            dlg.SessionOpenRequested += id => { dlg.Close(); OpenSessionDialog(id); };
            dlg.AddRequested += d => { dlg.Close(); OpenSessionDialog(null, null, d); };
            dlg.ShowDialog();
        }

        // ===================== ПЛАНЫ =====================
        private void RenderPlans() => RenderPlansInto(PlansPanel);

        private void RenderPlansInto(Panel target)
        {
            target.Children.Clear();
            if (DataStore.Data.Plans.Count == 0)
            {
                target.Children.Add(EmptyNote("Пока нет планов. Создайте шаблон тренировки, чтобы быстрее заполнять дневник."));
                return;
            }
            foreach (var p in DataStore.Data.Plans) target.Children.Add(BuildPlanCard(p));
        }

        private Border BuildPlanCard(WorkoutPlan p)
        {
            var stack = new StackPanel();

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var editBtn = new Button { Content = "✎", Style = (Style)FindResource("IconButtonStyle") };
            editBtn.Click += (_, __) => { new PlanDialog(p) { Owner = this }.ShowDialog(); RenderPlans(); };
            var delBtn = new Button { Content = "🗑", Style = (Style)FindResource("IconButtonStyle") };
            delBtn.Click += (_, __) =>
            {
                if (MessageBox.Show("Удалить этот план?", "Подтверждение", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    DataStore.Data.Plans.Remove(p);
                    DataStore.Save();
                    RenderPlans();
                }
            };
            actions.Children.Add(editBtn);
            actions.Children.Add(delBtn);
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
            head.Children.Add(new TextBlock { Text = p.Name, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, FontFamily = (FontFamily)FindResource("DisplayFontFamily") });
            stack.Children.Add(head);

            foreach (var it in p.Items)
            {
                var ex = DataStore.ExerciseById(it.ExerciseId);
                if (ex == null) continue;
                string detail = ex.Type == ExerciseType.Strength ? $"{it.Sets}×{it.Reps} · {it.Weight} кг"
                    : ex.Type == ExerciseType.Time ? $"{it.Sets} подх." : "кардио";
                var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = ex.Name, FontSize = 13.5, FontWeight = FontWeights.Medium });
                var detailText = new TextBlock { Text = detail, FontSize = 12, Foreground = (Brush)FindResource("InkFaintBrush") };
                Grid.SetColumn(detailText, 1);
                row.Children.Add(detailText);
                stack.Children.Add(row);
            }

            var startBtn = new Button { Content = "Начать тренировку по плану", Style = (Style)FindResource("SmallButtonStyle"), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            startBtn.Click += (_, __) => OpenSessionDialog(null, p.Id);
            stack.Children.Add(startBtn);

            return new Border { Style = (Style)FindResource("PanelStyle"), Width = 300, Margin = new Thickness(0, 0, 16, 16), VerticalAlignment = VerticalAlignment.Top, Child = stack };
        }

        private void AddPlan_Click(object sender, RoutedEventArgs e)
        {
            new PlanDialog(null) { Owner = this }.ShowDialog();
            RenderPlans();
        }

        // ===================== УПРАЖНЕНИЯ =====================
        private void RenderExercises()
        {
            ExercisesPanel.Children.Clear();
            if (DataStore.Data.Exercises.Count == 0)
            {
                ExercisesPanel.Children.Add(EmptyNote("Справочник пуст. Добавьте первое упражнение."));
                return;
            }
            foreach (var ex in DataStore.Data.Exercises) ExercisesPanel.Children.Add(BuildExerciseRow(ex));
        }

        private Border BuildExerciseRow(Exercise ex)
        {
            var grid = new Grid { Margin = new Thickness(2, 10, 2, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock { Text = ex.Name, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameText, 0);
            grid.Children.Add(nameText);

            var groupText = new TextBlock { Text = string.IsNullOrEmpty(ex.MuscleGroup) ? "—" : ex.MuscleGroup, Foreground = (Brush)FindResource("InkDimBrush"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(groupText, 1);
            grid.Children.Add(groupText);

            var tag = new Border
            {
                Background = (Brush)FindResource("BgSunkenBrush"), BorderBrush = (Brush)FindResource("LineBrush"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(9, 2, 9, 2),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = ex.TypeLabel, FontSize = 11, Foreground = (Brush)FindResource("InkDimBrush") }
            };
            Grid.SetColumn(tag, 2);
            grid.Children.Add(tag);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var editBtn = new Button { Content = "✎", Style = (Style)FindResource("IconButtonStyle") };
            editBtn.Click += (_, __) => { new ExerciseDialog(ex) { Owner = this }.ShowDialog(); RenderExercises(); };
            var delBtn = new Button { Content = "🗑", Style = (Style)FindResource("IconButtonStyle") };
            delBtn.Click += (_, __) =>
            {
                bool used = DataStore.Data.Sessions.Any(s => s.Exercises.Any(en => en.ExerciseId == ex.Id)) ||
                            DataStore.Data.Plans.Any(p => p.Items.Any(it => it.ExerciseId == ex.Id));
                if (used && MessageBox.Show("Это упражнение используется в планах или тренировках. Всё равно удалить?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    return;
                DataStore.Data.Exercises.Remove(ex);
                DataStore.Save();
                RenderExercises();
            };
            actions.Children.Add(editBtn);
            actions.Children.Add(delBtn);
            Grid.SetColumn(actions, 3);
            grid.Children.Add(actions);

            return new Border { BorderBrush = (Brush)FindResource("LineSoftBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        }

        private void AddExercise_Click(object sender, RoutedEventArgs e)
        {
            new ExerciseDialog(null) { Owner = this }.ShowDialog();
            RenderExercises();
        }

        // ===================== ПРОГРЕСС / ГРАФИКИ =====================
        private void RenderProgress()
        {
            var strengthEx = DataStore.Data.Exercises.Where(e => e.Type == ExerciseType.Strength).ToList();
            var currentId = (ProgressExerciseCombo.SelectedItem as Exercise)?.Id;
            ProgressExerciseCombo.ItemsSource = strengthEx;
            if (strengthEx.Count > 0)
                ProgressExerciseCombo.SelectedItem = strengthEx.FirstOrDefault(x => x.Id == currentId) ?? strengthEx[0];
            RenderCharts();
        }

        private void ProgressExercise_Changed(object sender, SelectionChangedEventArgs e) => RenderCharts();
        private void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => RenderCharts();

        private void RenderCharts()
        {
            RenderLineChart();
            RenderBarChart();
        }

        private void RenderLineChart()
        {
            LineChartCanvas.Children.Clear();
            var ex = ProgressExerciseCombo.SelectedItem as Exercise;
            ProgressExerciseTitle.Text = ex != null ? $"«{ex.Name}»" : "Добавьте силовое упражнение";
            if (ex == null) return;

            var points = DataStore.Data.Sessions
                .Where(s => s.Exercises.Any(en => en.ExerciseId == ex.Id))
                .OrderBy(s => s.Date)
                .Select(s => (date: s.Date, weight: DataStore.SessionTopSet(s, ex.Id)))
                .ToList();

            double w = LineChartCanvas.ActualWidth, h = LineChartCanvas.ActualHeight;
            if (w < 20 || h < 20) return;

            if (points.Count == 0)
            {
                var txt = new TextBlock { Text = "Нет данных по этому упражнению", Foreground = (Brush)FindResource("InkFaintBrush") };
                Canvas.SetLeft(txt, 10); Canvas.SetTop(txt, h / 2 - 10);
                LineChartCanvas.Children.Add(txt);
                return;
            }

            double padL = 42, padB = 28, padT = 10, padR = 12;
            double plotW = w - padL - padR, plotH = h - padT - padB;
            double maxW = points.Max(p => p.weight) * 1.15;
            if (maxW <= 0) maxW = 1;

            var lineBrush = (Brush)FindResource("LineBrush");
            var dimBrush = (Brush)FindResource("InkDimBrush");
            var accentBrush = (Brush)FindResource("AccentBrush");

            for (int i = 0; i <= 3; i++)
            {
                double y = padT + plotH - (plotH * i / 3.0);
                var gridLine = new Line { X1 = padL, X2 = w - padR, Y1 = y, Y2 = y, Stroke = lineBrush, StrokeThickness = 1 };
                LineChartCanvas.Children.Add(gridLine);
                var label = new TextBlock { Text = Math.Round(maxW * i / 3.0).ToString(Ru), FontSize = 10, Foreground = dimBrush };
                Canvas.SetLeft(label, 2); Canvas.SetTop(label, y - 7);
                LineChartCanvas.Children.Add(label);
            }

            var poly = new Polyline { Stroke = accentBrush, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i < points.Count; i++)
            {
                double x = points.Count == 1 ? padL + plotW / 2 : padL + plotW * i / (points.Count - 1);
                double y = padT + plotH - (points[i].weight / maxW * plotH);
                poly.Points.Add(new Point(x, y));

                var dot = new Ellipse { Width = 7, Height = 7, Fill = accentBrush };
                Canvas.SetLeft(dot, x - 3.5); Canvas.SetTop(dot, y - 3.5);
                LineChartCanvas.Children.Add(dot);

                if (i == 0 || i == points.Count - 1 || points.Count <= 6)
                {
                    var dl = new TextBlock { Text = FmtDate(points[i].date), FontSize = 9.5, Foreground = dimBrush };
                    Canvas.SetLeft(dl, Math.Max(padL, Math.Min(x - 14, w - 40))); Canvas.SetTop(dl, h - padB + 6);
                    LineChartCanvas.Children.Add(dl);
                }
            }
            LineChartCanvas.Children.Insert(0, poly);
        }

        private void RenderBarChart()
        {
            BarChartCanvas.Children.Clear();
            double w = BarChartCanvas.ActualWidth, h = BarChartCanvas.ActualHeight;
            if (w < 20 || h < 20) return;

            var weekStart = DataStore.StartOfWeek(DateTime.Today);
            var weeks = new List<(string label, double vol)>();
            for (int i = 7; i >= 0; i--)
            {
                var ws = weekStart.AddDays(-7 * i);
                var we = ws.AddDays(6);
                double vol = DataStore.Data.Sessions.Where(s => s.Date.Date >= ws && s.Date.Date <= we).Sum(s => DataStore.SessionVolume(s));
                weeks.Add((ws.ToString("dd.MM", Ru), vol));
            }

            double padL = 42, padB = 26, padT = 10, padR = 10;
            double plotW = w - padL - padR, plotH = h - padT - padB;
            double maxVol = Math.Max(1, weeks.Max(x => x.vol) * 1.15);

            var lineBrush = (Brush)FindResource("LineBrush");
            var dimBrush = (Brush)FindResource("InkDimBrush");
            var goodBrush = (Brush)FindResource("GoodBrush");

            for (int i = 0; i <= 3; i++)
            {
                double y = padT + plotH - (plotH * i / 3.0);
                BarChartCanvas.Children.Add(new Line { X1 = padL, X2 = w - padR, Y1 = y, Y2 = y, Stroke = lineBrush, StrokeThickness = 1 });
                var label = new TextBlock { Text = Math.Round(maxVol * i / 3.0).ToString(Ru), FontSize = 10, Foreground = dimBrush };
                Canvas.SetLeft(label, 2); Canvas.SetTop(label, y - 7);
                BarChartCanvas.Children.Add(label);
            }

            double slot = plotW / weeks.Count;
            double barWidth = Math.Min(30, slot * 0.55);
            for (int i = 0; i < weeks.Count; i++)
            {
                double barH = weeks[i].vol / maxVol * plotH;
                double x = padL + slot * i + (slot - barWidth) / 2;
                double y = padT + plotH - barH;
                var rect = new Rectangle { Width = barWidth, Height = Math.Max(0, barH), Fill = goodBrush, RadiusX = 3, RadiusY = 3 };
                Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
                BarChartCanvas.Children.Add(rect);

                var label = new TextBlock { Text = weeks[i].label, FontSize = 9, Foreground = dimBrush };
                Canvas.SetLeft(label, x + barWidth / 2 - 14); Canvas.SetTop(label, h - padB + 6);
                BarChartCanvas.Children.Add(label);
            }
        }

        // ===================== ПРОФИЛЬ =====================
        private void RenderProfile()
        {
            ProfileNameText.Text = AuthService.CurrentUsername;
            ProfileEmailText.Text = AuthService.CurrentEmail;
            ProfileRoleText.Text = AuthService.IsAdmin ? "Администратор" : AuthService.IsTrainer ? "Тренер" : "Пользователь";
            ProfileCreatedText.Text = AuthService.CurrentCreatedAt.HasValue
                ? AuthService.CurrentCreatedAt.Value.ToLocalTime().ToString("d MMMM yyyy", Ru)
                : "—";

            RenderPlansInto(ProfilePlansPanel);
        }

        // ===================== ПОЛЬЗОВАТЕЛИ (АДМИН) =====================
        private void RenderAdmin()
        {
            AdminUsersPanel.Children.Clear();
            var users = AdminService.GetAllUsers();
            if (users.Count == 0)
            {
                AdminUsersPanel.Children.Add(EmptyNote("Пользователей пока нет."));
                return;
            }
            foreach (var u in users) AdminUsersPanel.Children.Add(BuildUserRow(u));
        }

        private Border BuildUserRow(UserSummary u)
        {
            var grid = new Grid { Margin = new Thickness(2, 11, 2, 11) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nameStack.Children.Add(new TextBlock { Text = u.FullName, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            nameStack.Children.Add(new TextBlock { Text = u.Email, FontSize = 12, Foreground = (Brush)FindResource("InkFaintBrush") });
            Grid.SetColumn(nameStack, 0);
            grid.Children.Add(nameStack);

            var statsText = new TextBlock
            {
                Text = $"{u.PlansCount} \u00A0планов \u00B7 {u.SessionsCount} \u00A0тренировок",
                Foreground = (Brush)FindResource("InkDimBrush"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(statsText, 1);
            grid.Children.Add(statsText);

            var roleLabel = u.Role == UserRole.Admin ? "Админ" : u.Role == UserRole.Trainer ? "Тренер" : "Пользователь";
            var accentRole = u.Role == UserRole.Admin || u.Role == UserRole.Trainer;
            var rolePill = SmallPill(roleLabel,
                accentRole ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("GoodBrush"),
                accentRole ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("GoodSoftBrush"));
            rolePill.VerticalAlignment = VerticalAlignment.Center;
            rolePill.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(rolePill, 2);
            grid.Children.Add(rolePill);

            var assignBtn = new Button { Content = "Назначить тренировку", Style = (Style)FindResource("SmallButtonStyle") };
            assignBtn.Click += (_, __) =>
            {
                var dlg = new AssignTrainingDialog(u.Id, u.FullName) { Owner = this };
                if (dlg.ShowDialog() == true) RenderAdmin();
            };
            Grid.SetColumn(assignBtn, 3);
            grid.Children.Add(assignBtn);

            return new Border { BorderBrush = (Brush)FindResource("LineSoftBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        }

        // ===================== Открытие диалога тренировки (общий вход) =====================
        public void OpenSessionDialog(string sessionId, string startPlanId = null, DateTime? dateOverride = null)
        {
            var dlg = new SessionDialog(sessionId, startPlanId, dateOverride) { Owner = this };
            dlg.ShowDialog();
            RenderDashboard();
            if (LogPage.Visibility == Visibility.Visible) { RenderCalendar(); RenderFullSessionList(); }
            if (PlansPage.Visibility == Visibility.Visible) RenderPlans();
            if (ProgressPage.Visibility == Visibility.Visible) RenderProgress();
        }

        private void OpenSession_Click(object sender, RoutedEventArgs e) => OpenSessionDialog(null);
    }
}
