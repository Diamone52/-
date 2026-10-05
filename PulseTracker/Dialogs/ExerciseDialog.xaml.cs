using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PulseTracker.Models;
using PulseTracker.Services;

namespace PulseTracker.Dialogs
{
    public partial class ExerciseDialog : Window
    {
        private readonly Exercise _editing;

        public ExerciseDialog(Exercise existing)
        {
            InitializeComponent();
            _editing = existing;

            if (_editing != null)
            {
                TitleText.Text = "Изменить упражнение";
                NameBox.Text = _editing.Name;
                GroupBox.Text = _editing.MuscleGroup;
                SelectType(_editing.Type);
            }
            else
            {
                SelectType(ExerciseType.Strength);
            }
        }

        private void SelectType(ExerciseType type)
        {
            foreach (ComboBoxItem item in TypeCombo.Items)
                if ((string)item.Tag == type.ToString()) { TypeCombo.SelectedItem = item; break; }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Введите название упражнения", "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var selected = (ComboBoxItem)TypeCombo.SelectedItem;
            var type = System.Enum.Parse<ExerciseType>((string)selected.Tag);

            if (_editing != null)
            {
                _editing.Name = name;
                _editing.MuscleGroup = GroupBox.Text.Trim();
                _editing.Type = type;
            }
            else
            {
                DataStore.Data.Exercises.Add(new Exercise { Name = name, MuscleGroup = GroupBox.Text.Trim(), Type = type });
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
