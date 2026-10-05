using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace PulseTracker.Services
{
    public class AuthResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public static AuthResult Ok() => new() { Success = true };
        public static AuthResult Fail(string error) => new() { Success = false, Error = error };
    }

    public static class UserRole
    {
        public const string User = "User";
        public const string Trainer = "Trainer";
        public const string Admin = "Admin";
    }

    /// <summary>
    /// Регистрация и вход по ФИО/паролю. Почта хранится отдельно и обязана быть похожей
    /// на настоящую (содержать "@" и точку в домене). Пароль никогда не хранится в открытом виде —
    /// только PBKDF2-HMACSHA256 хеш со случайной солью (таблица Users). У каждого пользователя есть
    /// роль (User/Admin), которая определяет доступные разделы приложения.
    /// </summary>
    public static class AuthService
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100_000;

        // Учётная запись администратора по умолчанию — создаётся один раз при первом запуске.
        private const string DefaultAdminName = "Администратор";
        private const string DefaultAdminEmail = "admin@pulsetracker.local";
        private const string DefaultAdminPassword = "admin123";

        // Секретный код, без которого нельзя зарегистрироваться с ролью "Тренер".
        private const string TrainerCode = "9203";

        // Требует вид локальная-часть@домен.тld — точка в домене обязательна.
        private static readonly Regex EmailPattern = new(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        public static int? CurrentUserId { get; private set; }
        public static string CurrentUsername { get; private set; }
        public static string CurrentEmail { get; private set; }
        public static string CurrentRole { get; private set; }
        public static DateTime? CurrentCreatedAt { get; private set; }
        public static bool IsAdmin => CurrentRole == UserRole.Admin;
        public static bool IsTrainer => CurrentRole == UserRole.Trainer;
        // Кто видит раздел "Пользователи" и может назначать тренировки.
        public static bool CanManageUsers => IsAdmin || IsTrainer;

        public static AuthResult Register(string username, string email, string password, string role = UserRole.User, string trainerCode = null)
        {
            username = (username ?? "").Trim();
            email = (email ?? "").Trim();
            if (role != UserRole.User && role != UserRole.Trainer) role = UserRole.User;

            if (username.Length < 3) return AuthResult.Fail("ФИО должно быть не короче 3 символов.");
            if (!EmailPattern.IsMatch(email)) return AuthResult.Fail("Введите корректную почту вида имя@домен.точка-домен (например, name.surname@mail.ru).");
            if (string.IsNullOrEmpty(password) || password.Length < 4) return AuthResult.Fail("Пароль должен быть не короче 4 символов.");
            if (role == UserRole.Trainer && trainerCode?.Trim() != TrainerCode)
                return AuthResult.Fail("Неверный код тренера.");

            using var conn = Database.OpenConnection();

            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = @u COLLATE Cyrillic_General_CI_AS;";
                check.Parameters.AddWithValue("@u", username);
                var count = Convert.ToInt64(check.ExecuteScalar());
                if (count > 0) return AuthResult.Fail("Пользователь с таким ФИО уже зарегистрирован.");
            }
            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(*) FROM Users WHERE Email = @e COLLATE Cyrillic_General_CI_AS;";
                check.Parameters.AddWithValue("@e", email);
                var count = Convert.ToInt64(check.ExecuteScalar());
                if (count > 0) return AuthResult.Fail("Эта почта уже зарегистрирована.");
            }

            var (hash, salt) = HashPassword(password);
            var createdAt = DateTime.UtcNow;

            using (var insert = conn.CreateCommand())
            {
                // INSERT и чтение идентификатора — один запрос (один round-trip к серверу),
                // чтобы SCOPE_IDENTITY() гарантированно относился к этой же вставке.
                insert.CommandText = @"INSERT INTO Users (Username, Email, PasswordHash, PasswordSalt, Role, CreatedAt)
                                        VALUES (@u, @e, @h, @s, @r, @c);
                                        SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
                insert.Parameters.AddWithValue("@u", username);
                insert.Parameters.AddWithValue("@e", email);
                insert.Parameters.AddWithValue("@h", Convert.ToBase64String(hash));
                insert.Parameters.AddWithValue("@s", Convert.ToBase64String(salt));
                insert.Parameters.AddWithValue("@r", role);
                insert.Parameters.AddWithValue("@c", createdAt.ToString("o"));
                var id = Convert.ToInt64(insert.ExecuteScalar());
                CurrentUserId = (int)id;
                CurrentUsername = username;
                CurrentEmail = email;
                CurrentRole = role;
                CurrentCreatedAt = createdAt;
            }

            return AuthResult.Ok();
        }

        public static AuthResult Login(string username, string password)
        {
            username = (username ?? "").Trim();
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return AuthResult.Fail("Введите ФИО и пароль.");

            using var conn = Database.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT Id, Username, Email, PasswordHash, PasswordSalt, Role, CreatedAt FROM Users
                                 WHERE Username = @u COLLATE Cyrillic_General_CI_AS;";
            cmd.Parameters.AddWithValue("@u", username);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return AuthResult.Fail("Пользователь с таким ФИО не найден.");

            var id = reader.GetInt32(0);
            var realUsername = reader.GetString(1);
            var email = reader.GetString(2);
            var storedHash = Convert.FromBase64String(reader.GetString(3));
            var salt = Convert.FromBase64String(reader.GetString(4));
            var role = reader.IsDBNull(5) ? UserRole.User : reader.GetString(5);
            var createdAtRaw = reader.IsDBNull(6) ? null : reader.GetString(6);

            var computedHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
            if (!CryptographicOperations.FixedTimeEquals(storedHash, computedHash))
                return AuthResult.Fail("Неверный пароль.");

            CurrentUserId = id;
            CurrentUsername = realUsername;
            CurrentEmail = email;
            CurrentRole = role;
            CurrentCreatedAt = createdAtRaw != null && DateTime.TryParse(createdAtRaw, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
            return AuthResult.Ok();
        }

        public static void Logout()
        {
            CurrentUserId = null;
            CurrentUsername = null;
            CurrentEmail = null;
            CurrentRole = null;
            CurrentCreatedAt = null;
        }

        /// <summary>
        /// Создаёт учётную запись администратора по умолчанию, если в базе ещё нет ни одного админа.
        /// Не трогает текущую сессию (Current*) — вызывается один раз при старте приложения.
        /// </summary>
        public static void EnsureDefaultAdmin()
        {
            using var conn = Database.OpenConnection();

            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(*) FROM Users WHERE Role = @r;";
                check.Parameters.AddWithValue("@r", UserRole.Admin);
                var count = Convert.ToInt64(check.ExecuteScalar());
                if (count > 0) return;
            }

            var (hash, salt) = HashPassword(DefaultAdminPassword);
            using var insert = conn.CreateCommand();
            insert.CommandText = @"INSERT INTO Users (Username, Email, PasswordHash, PasswordSalt, Role, CreatedAt)
                                    VALUES (@u, @e, @h, @s, @r, @c);";
            insert.Parameters.AddWithValue("@u", DefaultAdminName);
            insert.Parameters.AddWithValue("@e", DefaultAdminEmail);
            insert.Parameters.AddWithValue("@h", Convert.ToBase64String(hash));
            insert.Parameters.AddWithValue("@s", Convert.ToBase64String(salt));
            insert.Parameters.AddWithValue("@r", UserRole.Admin);
            insert.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("o"));
            insert.ExecuteNonQuery();
        }

        private static (byte[] hash, byte[] salt) HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
            return (hash, salt);
        }
    }
}
