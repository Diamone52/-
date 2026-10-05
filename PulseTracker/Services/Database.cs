using System;
using System.IO;
using Microsoft.Data.SqlClient;

namespace PulseTracker.Services
{
    /// <summary>
    /// Отвечает за базу данных Microsoft SQL Server (LocalDB) и создание схемы
    /// (таблица пользователей + таблицы приложения). При первом запуске приложение
    /// само создаёт базу данных PulseTrackerDb на экземпляре (localdb)\MSSQLLocalDB —
    /// отдельно устанавливать SQL Server не требуется, LocalDB входит в состав
    /// компонента ".NET desktop development" / SQL Server Express LocalDB для Visual Studio.
    /// </summary>
    public static class Database
    {
        private const string DatabaseName = "PulseTrackerDb";
        private const string Instance = @"(localdb)\MSSQLLocalDB";

        // Файлы .mdf/.ldf хранятся в отдельной папке пользователя, чтобы не зависеть
        // от каталога по умолчанию экземпляра LocalDB.
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PulseTracker");

        private static readonly string MdfPath = Path.Combine(FolderPath, DatabaseName + ".mdf");
        private static readonly string LdfPath = Path.Combine(FolderPath, DatabaseName + "_log.ldf");

        // Подключение к серверу без указания базы данных — используется только для CREATE DATABASE.
        private static string MasterConnectionString =>
            $@"Server={Instance};Database=master;Trusted_Connection=True;TrustServerCertificate=True;";

        // Основная строка подключения, которой пользуется всё приложение.
        public static string ConnectionString =>
            $@"Server={Instance};Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True;";

        public static void Initialize()
        {
            Directory.CreateDirectory(FolderPath);
            EnsureDatabaseExists();

            using var conn = OpenConnection();
            foreach (var batch in SchemaBatches)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = batch;
                cmd.ExecuteNonQuery();
            }

            AuthService.EnsureDefaultAdmin();
        }

        // Создаёт файл базы данных PulseTrackerDb на экземпляре LocalDB, если его ещё нет.
        private static void EnsureDatabaseExists()
        {
            using var conn = new SqlConnection(MasterConnectionString);
            conn.Open();

            using var check = conn.CreateCommand();
            check.CommandText = "SELECT database_id FROM sys.databases WHERE name = @name;";
            check.Parameters.AddWithValue("@name", DatabaseName);
            var exists = check.ExecuteScalar() != null;
            if (exists) return;

            using var create = conn.CreateCommand();
            create.CommandText = $@"
CREATE DATABASE [{DatabaseName}]
ON PRIMARY (NAME = N'{DatabaseName}', FILENAME = N'{MdfPath}')
LOG ON (NAME = N'{DatabaseName}_log', FILENAME = N'{LdfPath}');";
            create.ExecuteNonQuery();
        }

        public static SqlConnection OpenConnection()
        {
            var conn = new SqlConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        // Схема из Database/schema.sql, встроенная в код батчами (по разделителю GO),
        // чтобы приложение создавало таблицы самостоятельно при первом запуске без
        // ручных шагов и без обращения к внешнему .sql файлу на диске.
        private static readonly string[] SchemaBatches =
        {
            @"IF OBJECT_ID('dbo.Users', 'U') IS NULL
CREATE TABLE dbo.Users (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    Username       NVARCHAR(200)  NOT NULL UNIQUE,
    Email          NVARCHAR(200)  NOT NULL UNIQUE,
    PasswordHash   NVARCHAR(200)  NOT NULL,
    PasswordSalt   NVARCHAR(200)  NOT NULL,
    Role           NVARCHAR(20)   NOT NULL DEFAULT 'User',
    CreatedAt      NVARCHAR(40)   NOT NULL
);",

            @"IF OBJECT_ID('dbo.Exercises', 'U') IS NULL
CREATE TABLE dbo.Exercises (
    Id             NVARCHAR(40) PRIMARY KEY,
    UserId         INT NOT NULL REFERENCES dbo.Users(Id) ON DELETE CASCADE,
    Name           NVARCHAR(200) NOT NULL,
    MuscleGroup    NVARCHAR(100),
    Type           NVARCHAR(20) NOT NULL CHECK (Type IN ('Strength','Cardio','Time'))
);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Exercises_UserId')
CREATE INDEX IX_Exercises_UserId ON dbo.Exercises(UserId);",

            @"IF OBJECT_ID('dbo.Plans', 'U') IS NULL
CREATE TABLE dbo.Plans (
    Id             NVARCHAR(40) PRIMARY KEY,
    UserId         INT NOT NULL REFERENCES dbo.Users(Id) ON DELETE CASCADE,
    Name           NVARCHAR(200) NOT NULL
);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Plans_UserId')
CREATE INDEX IX_Plans_UserId ON dbo.Plans(UserId);",

            @"IF OBJECT_ID('dbo.PlanItems', 'U') IS NULL
CREATE TABLE dbo.PlanItems (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    PlanId         NVARCHAR(40) NOT NULL REFERENCES dbo.Plans(Id) ON DELETE CASCADE,
    ExerciseId     NVARCHAR(40) NOT NULL REFERENCES dbo.Exercises(Id) ON DELETE NO ACTION,
    Sets           INT NOT NULL DEFAULT 3,
    Reps           INT NOT NULL DEFAULT 0,
    Weight         FLOAT NOT NULL DEFAULT 0,
    SortOrder      INT NOT NULL DEFAULT 0
);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PlanItems_PlanId')
CREATE INDEX IX_PlanItems_PlanId ON dbo.PlanItems(PlanId);",

            @"IF OBJECT_ID('dbo.Sessions', 'U') IS NULL
CREATE TABLE dbo.Sessions (
    Id             NVARCHAR(40) PRIMARY KEY,
    UserId         INT NOT NULL REFERENCES dbo.Users(Id) ON DELETE CASCADE,
    Date           NVARCHAR(10) NOT NULL,
    Name           NVARCHAR(200) NOT NULL,
    PlanId         NVARCHAR(40) NULL REFERENCES dbo.Plans(Id) ON DELETE NO ACTION,
    DurationMin    INT NOT NULL DEFAULT 0,
    Rpe            INT NOT NULL DEFAULT 0,
    Notes          NVARCHAR(MAX)
);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Sessions_UserId')
CREATE INDEX IX_Sessions_UserId ON dbo.Sessions(UserId);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Sessions_Date')
CREATE INDEX IX_Sessions_Date ON dbo.Sessions(Date);",

            @"IF OBJECT_ID('dbo.SessionExercises', 'U') IS NULL
CREATE TABLE dbo.SessionExercises (
    Id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    SessionId      NVARCHAR(40) NOT NULL REFERENCES dbo.Sessions(Id) ON DELETE CASCADE,
    ExerciseId     NVARCHAR(40) NOT NULL REFERENCES dbo.Exercises(Id) ON DELETE NO ACTION,
    SortOrder      INT NOT NULL DEFAULT 0
);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SessionExercises_SessionId')
CREATE INDEX IX_SessionExercises_SessionId ON dbo.SessionExercises(SessionId);",

            @"IF OBJECT_ID('dbo.SessionSets', 'U') IS NULL
CREATE TABLE dbo.SessionSets (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    SessionExerciseId   BIGINT NOT NULL REFERENCES dbo.SessionExercises(Id) ON DELETE CASCADE,
    SetIndex            INT NOT NULL DEFAULT 0,
    Reps                INT NOT NULL DEFAULT 0,
    Weight              FLOAT NOT NULL DEFAULT 0,
    DurationSec         FLOAT NOT NULL DEFAULT 0,
    DistanceKm          FLOAT NOT NULL DEFAULT 0
);",

            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SessionSets_SessionExerciseId')
CREATE INDEX IX_SessionSets_SessionExerciseId ON dbo.SessionSets(SessionExerciseId);",
        };
    }
}
