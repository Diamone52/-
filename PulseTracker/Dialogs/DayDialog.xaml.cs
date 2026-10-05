using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PulseTracker.Models;
using PulseTracker.Services;

namespace PulseTracker.Dialogs
{
    public partial class DayDialog : Window
    {
        private static readonly CultureInfo Ru = new("ru-RU");
        private readonly DateTime _date;

        public event Action<string> SessionOpenRequested;
        public event Action<DateTime> AddRequested;

        public DayDialog(DateTime date, List<WorkoutSession> sessions)
        {
            InitializeComponent();
            _date = date;
            TitleText.Text = date.ToString("d MMMM yyyy", Ru);

            SessionsPanel.Children.Clear();
            if (sessions.Count == 0)
            {
                SessionsPanel.Children.Add(new TextBlock
                {
                    Text = "Тренировок в этот день не было.", Foreground = (Brush)FindResource("InkFaintBrush"),
                    FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 2, 8)
                });
            }
            else
            {
                foreach (var s in sessions)
                {
                    var exNames = string.Join(", ", s.Exercises
                        .Select(en => DataStore.ExerciseById(en.ExerciseId)?.Name)
                        .Where(n => !string.IsNullOrEmpty(n)));

                    var stack = new StackPanel();
                    stack.Children.Add(new TextBlock { Text = s.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
                    stack.Children.Add(new TextBlock
                    {
                        Text = string.IsNullOrEmpty(exNames) ? "—" : exNames, FontSize = 12,
                        Foreground = (Brush)FindResource("InkFaintBrush"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap
                    });

                    var border = new Border
                    {
                        BorderBrush = (Brush)FindResource("LineSoftBrush"), BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(2, 9, 2, 9), Cursor = Cursors.Hand, Child = stack
                    };
                    var id = s.Id;
                    border.MouseLeftButtonUp += (_, __) => SessionOpenRequested?.Invoke(id);
                    SessionsPanel.Children.Add(border);
                }
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e) => AddRequested?.Invoke(_date);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
