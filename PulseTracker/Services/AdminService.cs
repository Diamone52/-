using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PulseTracker.Models;

namespace PulseTracker.Services
{
    public class UserSummary
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public int SessionsCount { get; set; }
        public int PlansCount { get; set; }
    }

    /// <summary>
    /// Действия, доступные только администратору: обзор всех пользователей и назначение
    /// тренировки (на основе одного из планов пользователя) напрямую в его дневник —
    /// без переключения на сессию этого пользователя.
    /// </summary>
    public static class AdminService
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static List<UserSummary> GetAllUsers()
        {
            var result = new List<UserSummary>();
            using var conn = Database.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT u.Id, u.Username, u.Email, u.Role,
                       (SELECT COUNT(*) FROM Sessions s WHERE s.UserId = u.Id) AS SessionsCount,
                       (SELECT COUNT(*) FROM Plans p WHERE p.UserId = u.Id) AS PlansCount
                FROM Users u
                ORDER BY u.Role DESC, u.Username COLLATE Cyrillic_General_CI_AS;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result.Add(new UserSummary
                {
                    Id = r.GetInt32(0),
                    FullName = r.GetString(1),
                    Email = r.GetString(2),
                    Role = r.GetString(3),
                    SessionsCount = r.GetInt32(4),
                    PlansCount = r.GetInt32(5)
                });
            return result;
        }

        public static List<WorkoutPlan> GetPlansForUser(int userId)
        {
            var plans = new List<WorkoutPlan>();
            using var conn = Database.OpenConnection();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, Name FROM Plans WHERE UserId = @u;";
                cmd.Parameters.AddWithValue("@u", userId);
                using var r = cmd.ExecuteReader();
                while (r.Read()) plans.Add(new WorkoutPlan { Id = r.GetString(0), Name = r.GetString(1) });
            }
            foreach (var plan in plans)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT ExerciseId, Sets, Reps, Weight FROM PlanItems WHERE PlanId = @p ORDER BY SortOrder;";
                cmd.Parameters.AddWithValue("@p", plan.Id);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    plan.Items.Add(new PlanItem
                    {
                        ExerciseId = r.GetString(0),
                        Sets = r.GetInt32(1),
                        Reps = r.GetInt32(2),
                        Weight = r.GetDouble(3)
                    });
            }
            return plans;
        }

        public static Dictionary<string, Exercise> GetExercisesForUser(int userId)
        {
            var dict = new Dictionary<string, Exercise>();
            using var conn = Database.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, MuscleGroup, Type FROM Exercises WHERE UserId = @u;";
            cmd.Parameters.AddWithValue("@u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var ex = new Exercise
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    MuscleGroup = r.IsDBNull(2) ? "" : r.GetString(2),
                    Type = Enum.Parse<ExerciseType>(r.GetString(3))
                };
                dict[ex.Id] = ex;
            }
            return dict;
        }

        /// <summary>
        /// Создаёt новую запись в дневнике пользователя userId на основе плана planId
        /// (подходы/повторы/вес берутся из плана — как при обычном "Начать тренировку по плану").
        /// </summary>
        public static void AssignSessionFromPlan(int userId, DateTime date, string name, string planId,
            int durationMin, int rpe, string notes)
        {
            var exercises = GetExercisesForUser(userId);
            var plans = GetPlansForUser(userId);
            var plan = plans.Find(p => p.Id == planId);
            if (plan == null) throw new InvalidOperationException("План не найден.");

            var sessionId = Guid.NewGuid().ToString("N");

            using var conn = Database.OpenConnection();
            using var tx = conn.BeginTransaction();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO Sessions (Id, UserId, Date, Name, PlanId, DurationMin, Rpe, Notes)
                                     VALUES (@id,@u,@d,@n,@p,@dur,@rpe,@notes);";
                cmd.Parameters.AddWithValue("@id", sessionId);
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@d", date.ToString("yyyy-MM-dd", Inv));
                cmd.Parameters.AddWithValue("@n", string.IsNullOrWhiteSpace(name) ? plan.Name : name);
                cmd.Parameters.AddWithValue("@p", plan.Id);
                cmd.Parameters.AddWithValue("@dur", durationMin);
                cmd.Parameters.AddWithValue("@rpe", rpe);
                cmd.Parameters.AddWithValue("@notes", notes ?? "");
                cmd.ExecuteNonQuery();
            }

            int exOrder = 0;
            foreach (var item in plan.Items)
            {
                exercises.TryGetValue(item.ExerciseId, out var ex);
                var type = ex?.Type ?? ExerciseType.Strength;

                long entryRowId;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO SessionExercises (SessionId, ExerciseId, SortOrder) VALUES (@s,@e,@o); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
                    cmd.Parameters.AddWithValue("@s", sessionId);
                    cmd.Parameters.AddWithValue("@e", item.ExerciseId);
                    cmd.Parameters.AddWithValue("@o", exOrder++);
                    entryRowId = Convert.ToInt64(cmd.ExecuteScalar());
                }

                int setCount = Math.Max(1, item.Sets);
                for (int i = 0; i < setCount; i++)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO SessionSets (SessionExerciseId, SetIndex, Reps, Weight, DurationSec, DistanceKm)
                                         VALUES (@e,@i,@r,@w,0,0);";
                    cmd.Parameters.AddWithValue("@e", entryRowId);
                    cmd.Parameters.AddWithValue("@i", i);
                    cmd.Parameters.AddWithValue("@r", type == ExerciseType.Strength ? item.Reps : 0);
                    cmd.Parameters.AddWithValue("@w", type == ExerciseType.Strength ? item.Weight : 0);
                    cmd.ExecuteNonQuery();
                }
            }

            tx.Commit();
        }
    }
}
