using System.Windows;
using System.Windows.Controls;
using PulseTracker.Services;

namespace PulseTracker
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        private void ModeChanged(object sender, RoutedEventArgs e)
        {
            if (ErrorText == null) return; // событие может прийти до полной инициализации
            bool isRegister = RegisterModeBtn.IsChecked == true;
            EmailPanel.Visibility = isRegister ? Visibility.Visible : Visibility.Collapsed;
            ConfirmPanel.Visibility = isRegister ? Visibility.Visible : Visibility.Collapsed;
            SubmitBtn.Content = isRegister ? "Зарегистрироваться" : "Войти";
            ErrorText.Visibility = Visibility.Collapsed;
        }

        private void RoleChanged(object sender, RoutedEventArgs e)
        {
            if (TrainerCodePanel == null) return; // событие может прийти до полной инициализации
            TrainerCodePanel.Visibility = RoleTrainerBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;
            bool isRegister = RegisterModeBtn.IsChecked == true;

            AuthResult result;
            if (isRegister)
            {
                if (password != ConfirmPasswordBox.Password)
                {
                    ShowError("Пароли не совпадают.");
                    return;
                }
                var role = RoleTrainerBtn.IsChecked == true ? UserRole.Trainer : UserRole.User;
                result = AuthService.Register(username, EmailBox.Text.Trim(), password, role, TrainerCodeBox.Text);
            }
            else
            {
                result = AuthService.Login(username, password);
            }

            if (!result.Success)
            {
                ShowError(result.Error);
                return;
            }

            DataStore.Load(AuthService.CurrentUserId!.Value);

            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        }

        private void ShowError(string text)
        {
            ErrorText.Text = text;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
