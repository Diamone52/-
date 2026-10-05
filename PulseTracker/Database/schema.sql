-- ============================================================
--  Пульс — база данных трекера тренировок (Microsoft SQL Server)
--  Таблица пользователей (вход/регистрация) + таблицы приложения,
--  привязанные к пользователю через UserId.
--
--  Выполняется на уровне базы данных PulseTrackerDb (создаётся
--  приложением автоматически при первом запуске, см. Services/Database.cs).
-- ============================================================

-- ---------- Пользователи (вход и регистрация) ----------
IF OBJECT_ID('dbo.Users', 'U') IS NULL
CREATE TABLE dbo.Users (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    Username       NVARCHAR(200)  NOT NULL UNIQUE,   -- ФИО, используется для входа
    Email          NVARCHAR(200)  NOT NULL UNIQUE,    -- почта, обязана содержать "@" и точку в домене
    PasswordHash   NVARCHAR(200)  NOT NULL,           -- PBKDF2-HMACSHA256, Base64
    PasswordSalt   NVARCHAR(200)  NOT NULL,           -- случайная соль, Base64
    Role           NVARCHAR(20)   NOT NULL DEFAULT 'User',  -- 'User', 'Trainer' или 'Admin'
    CreatedAt      NVARCHAR(40)   NOT NULL            -- ISO 8601
);
GO

-- ---------- Справочник упражнений ----------
IF OBJECT_ID('dbo.Exercises', 'U') IS NULL
CREATE TABLE dbo.Exercises (
    Id             NVARCHAR(40) PRIMARY KEY,          -- GUID
    UserId         INT NOT NULL REFERENCES dbo.Users(Id) ON DELETE CASCADE,
    Name           NVARCHAR(200) NOT NULL,
    MuscleGroup    NVARCHAR(100),
    Type           NVARCHAR(20) NOT NULL CHECK (Type IN ('Strength','Cardio','Time'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Exercises_UserId')
CREATE INDEX IX_Exercises_UserId ON dbo.Exercises(UserId);
GO

-- ---------- Планы (шаблоны) тренировок ----------
IF OBJECT_ID('dbo.Plans', 'U') IS NULL
CREATE TABLE dbo.Plans (
    Id             NVARCHAR(40) PRIMARY KEY,          -- GUID
    UserId         INT NOT NULL REFERENCES dbo.Users(Id) ON DELETE CASCADE,
    Name           NVARCHAR(200) NOT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Plans_UserId')
CREATE INDEX IX_Plans_UserId ON dbo.Plans(UserId);
GO

-- ---------- Упражнения внутри плана ----------
-- Примечание: ExerciseId -> NO ACTION (не CASCADE), иначе SQL Server отклонит схему
-- из-за нескольких каскадных путей удаления (Users -> Plans -> PlanItems и
-- Users -> Exercises -> PlanItems одновременно). Приложение удаляет записи
-- в правильном порядке самостоятельно (см. DataStore.Save()).
IF OBJECT_ID('dbo.PlanItems', 'U') IS NULL
CREATE TABLE dbo.PlanItems (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    PlanId         NVARCHAR(40) NOT NULL REFERENCES dbo.Plans(Id) ON DELETE CASCADE,
    ExerciseId     NVARCHAR(40) NOT NULL REFERENCES dbo.Exercises(Id) ON DELETE NO ACTION,
    Sets           INT NOT NULL DEFAULT 3,
    Reps           INT NOT NULL DEFAULT 0,
    Weight         FLOAT NOT NULL DEFAULT 0,
    SortOrder      INT NOT NULL DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PlanItems_PlanId')
CREATE INDEX IX_PlanItems_PlanId ON dbo.PlanItems(PlanId);
GO

-- ---------- Записи дневника (тренировки) ----------
-- Примечание: PlanId -> NO ACTION (не SET NULL), иначе SQL Server отклонит схему —
-- Users -> Sessions (CASCADE) и Users -> Plans -> Sessions (через PlanId) образуют
-- два каскадных пути удаления к одной таблице. Приложение и так удаляет Sessions
-- раньше Plans (см. DataStore.Save()), поэтому висячих ссылок не возникает.
IF OBJECT_ID('dbo.Sessions', 'U') IS NULL
CREATE TABLE dbo.Sessions (
    Id             NVARCHAR(40) PRIMARY KEY,          -- GUID
    UserId         INT NOT NULL REFERENCES dbo.Users(Id) ON DELETE CASCADE,
    Date           NVARCHAR(10) NOT NULL,             -- ISO 8601 (yyyy-MM-dd)
    Name           NVARCHAR(200) NOT NULL,
    PlanId         NVARCHAR(40) NULL REFERENCES dbo.Plans(Id) ON DELETE NO ACTION,
    DurationMin    INT NOT NULL DEFAULT 0,
    Rpe            INT NOT NULL DEFAULT 0,
    Notes          NVARCHAR(MAX)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Sessions_UserId')
CREATE INDEX IX_Sessions_UserId ON dbo.Sessions(UserId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Sessions_Date')
CREATE INDEX IX_Sessions_Date ON dbo.Sessions(Date);
GO

-- ---------- Упражнения внутри тренировки ----------
-- ExerciseId -> NO ACTION по той же причине, что и в PlanItems.
IF OBJECT_ID('dbo.SessionExercises', 'U') IS NULL
CREATE TABLE dbo.SessionExercises (
    Id             BIGINT IDENTITY(1,1) PRIMARY KEY,
    SessionId      NVARCHAR(40) NOT NULL REFERENCES dbo.Sessions(Id) ON DELETE CASCADE,
    ExerciseId     NVARCHAR(40) NOT NULL REFERENCES dbo.Exercises(Id) ON DELETE NO ACTION,
    SortOrder      INT NOT NULL DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SessionExercises_SessionId')
CREATE INDEX IX_SessionExercises_SessionId ON dbo.SessionExercises(SessionId);
GO

-- ---------- Подходы (сеты) внутри упражнения тренировки ----------
IF OBJECT_ID('dbo.SessionSets', 'U') IS NULL
CREATE TABLE dbo.SessionSets (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    SessionExerciseId   BIGINT NOT NULL REFERENCES dbo.SessionExercises(Id) ON DELETE CASCADE,
    SetIndex            INT NOT NULL DEFAULT 0,
    Reps                INT NOT NULL DEFAULT 0,
    Weight              FLOAT NOT NULL DEFAULT 0,
    DurationSec         FLOAT NOT NULL DEFAULT 0,
    DistanceKm          FLOAT NOT NULL DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SessionSets_SessionExerciseId')
CREATE INDEX IX_SessionSets_SessionExerciseId ON dbo.SessionSets(SessionExerciseId);
GO
