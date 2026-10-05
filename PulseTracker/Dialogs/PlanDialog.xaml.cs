using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PulseTracker.Models;
using PulseTracker.Services;

namespace PulseTracker.Dialogs
{
    public partial class PlanDialog : Window
    {
        private readonly WorkoutPlan _editing;
        private readonly List<PlanItem> _items = new();

        public PlanDialog(WorkoutPlan existing)
        {
            InitializeComponent();
            _editing = existing;

            AddExerciseCombo.ItemsSource = DataStore.Data.Exercises;
            if (DataStore.Data.Exercises.Count > 0) AddExerciseCombo.SelectedIndex = 0;

            if (_editing != null)
            {
                TitleText.Text = "Изменить план";
                NameBox.Text = _editing.Name;
                foreach (var it in _editing.Items)
                    _items.Add(new PlanItem { ExerciseId = it.ExerciseId, Sets = it.Sets, Reps = it.Reps, Weight = it.Weight });
            }
            RenderItems();
        }

        private void RenderItems()
        {
            ItemsPanel.Children.Clear();
            for (int idx = 0; idx < _items.Count; idx++)
            {
                int i = idx;
                var item = _items[i];
                var ex = DataStore.ExerciseById(item.ExerciseId);
                var name = ex?.Name ?? "—";

                var wrap = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                var card = new Border
                {
                    Background = (Brush)FindResource("BgSunkenBrush"),
                    BorderBrush = (Brush)FindResource("LineSoftBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14),
                    Child = wrap
                };

                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var rm = new Button { Content = "✕", Style = (Style)FindResource("IconButtonStyle") };
                rm.Click += (_, __) => { _items.RemoveAt(i); RenderItems(); };
                DockPanel.SetDock(rm, Dock.Right);
                head.Children.Add(rm);
                head.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                wrap.Children.Add(head);

                if (ex != null && ex.Type == ExerciseType.Strength)
                {
                    var grid = new Grid();
                    for (int c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    grid.Children.Add(LabeledField("Подходы", item.Sets.ToString(), t => { if (int.TryParse(t, out var v)) item.Sets = v; }, 0));
                    grid.Children.Add(LabeledField("Повторы", item.Reps.ToString(), t => { if (int.TryParse(t, out var v)) item.Reps = v; }, 1));
                    grid.Children.Add(LabeledField("Вес, кг", item.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture), t => { if (double.TryParse(t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v)) item.Weight = v; }, 2));
                    wrap.Children.Add(grid);
                }
                else
                {
                    var single = new Grid();
                    single.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    single.Children.Add(LabeledField("Число подходов", item.Sets.ToString(), t => { if (int.TryParse(t, out var v)) item.Sets = v; }, 0));
                    wrap.Children.Add(single);
                }

                ItemsPanel.Children.Add(card);
            }
        }

        private FrameworkElement LabeledField(string label, string value, System.Action<string> onChange, int column)
        {
            var stack = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 6, 0, 6, 0) };
            stack.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = (Brush)FindResource("InkFaintBrush"), Margin = new Thickness(0, 0, 0, 4) });
            var box = new TextBox { Text = value };
            box.TextChanged += (_, __) => onChange(box.Text);
            stack.Children.Add(box);
            Grid.SetColumn(stack, column);
            return stack;
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            if (AddExerciseCombo.SelectedItem is not Exercise ex) return;
            _items.Add(new PlanItem { ExerciseId = ex.Id, Sets = ex.Type == ExerciseType.Strength ? 3 : 3, Reps = 8, Weight = 0 });
            RenderItems();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Введите название плана", "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_editing != null)
            {
                _editing.Name = name;
                _editing.Items = _items;
            }
            else
            {
                DataStore.Data.Plans.Add(new WorkoutPlan { Name = name, Items = _items });
            }
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
