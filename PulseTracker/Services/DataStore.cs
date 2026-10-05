using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.SqlClient;
using PulseTracker.Models;

namespace PulseTracker.Services
{
    /// <summary>
    /// Хранит данные текущего пользователя в памяти (Data) и синхронизирует их с SQLite.
    /// Save() полностью перезаписывает строки пользователя — простая и надёжная стратегия
    /// для персонального трекера такого масштаба.
    /// </summary>
    public static class DataStore
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static int _userId;

        public static AppData Data { get; private set; } = new();

        public static void Load(int userId)
        {
            _userId = userId;
            using var conn = Database.OpenConnection();

            var exercises = new List<Exercise>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, Name, MuscleGroup, Type FROM Exercises WHERE UserId = @u;";
                cmd.Parameters.AddWithValue("@u", userId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    exercises.Add(new Exercise
                    {
                        Id = r.GetString(0),
                        Name = r.GetString(1),
                        MuscleGroup = r.IsDBNull(2) ? "" : r.GetString(2),
                        Type = Enum.Parse<ExerciseType>(r.GetString(3))
                    });
            }

            var plans = new List<WorkoutPlan>();
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

            var sessions = new List<WorkoutSession>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, Date, Name, PlanId, DurationMin, Rpe, Notes FROM Sessions WHERE UserId = @u;";
                cmd.Parameters.AddWithValue("@u", userId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    sessions.Add(new WorkoutSession
                    {
                        Id = r.GetString(0),
                        Date = DateTime.Parse(r.GetString(1), Inv),
                        Name = r.GetString(2),
                        PlanId = r.IsDBNull(3) ? null : r.GetString(3),
                        DurationMin = r.GetInt32(4),
                        Rpe = r.GetInt32(5),
                        Notes = r.IsDBNull(6) ? "" : r.GetString(6)
                    });
            }
            foreach (var session in sessions)
            {
                var entryIds = new List<(long RowId, string ExerciseId)>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id, ExerciseId FROM SessionExercises WHERE SessionId = @s ORDER BY SortOrder;";
                    cmd.Parameters.AddWithValue("@s", session.Id);
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) entryIds.Add((r.GetInt64(0), r.GetString(1)));
                }
                foreach (var (rowId, exId) in entryIds)
                {
                    var entry = new ExerciseEntry { ExerciseId = exId };
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT Reps, Weight, DurationSec, DistanceKm FROM SessionSets WHERE SessionExerciseId = @e ORDER BY SetIndex;";
                    cmd.Parameters.AddWithValue("@e", rowId);
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        entry.Sets.Add(new SetEntry
                        {
                            Reps = r.GetInt32(0),
                            Weight = r.GetDouble(1),
                            DurationSec = r.GetDouble(2),
                            DistanceKm = r.GetDouble(3)
                        });
                    session.Exercises.Add(entry);
                }
            }

            Data = new AppData { Exercises = exercises, Plans = plans, Sessions = sessions };

            // Первый вход этого пользователя — база пуста, заполняем демо-данными.
            if (Data.Exercises.Count == 0 && Data.Plans.Count == 0 && Data.Sessions.Count == 0)
            {
                Data = SeedData();
                Save();
            }
        }

        public static void Save()
        {
            using var conn = Database.OpenConnection();
            using var tx = conn.BeginTransaction();

            void Exec(string sql, Action<SqlCommand> bind = null)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                bind?.Invoke(cmd);
                cmd.ExecuteNonQuery();
            }

            // Удаляем все прежние строки пользователя (каскад подчистит дочерние таблицы).
            Exec("DELETE FROM Sessions WHERE UserId = @u;", c => c.Parameters.AddWithValue("@u", _userId));
            Exec("DELETE FROM Plans WHERE UserId = @u;", c => c.Parameters.AddWithValue("@u", _userId));
            Exec("DELETE FROM Exercises WHERE UserId = @u;", c => c.Parameters.AddWithValue("@u", _userId));

            foreach (var ex in Data.Exercises)
                Exec("INSERT INTO Exercises (Id, UserId, Name, MuscleGroup, Type) VALUES (@id,@u,@n,@g,@t);", c =>
                {
                    c.Parameters.AddWithValue("@id", ex.Id);
                    c.Parameters.AddWithValue("@u", _userId);
                    c.Parameters.AddWithValue("@n", ex.Name);
                    c.Parameters.AddWithValue("@g", (object)ex.MuscleGroup ?? "");
                    c.Parameters.AddWithValue("@t", ex.Type.ToString());
                });

            foreach (var plan in Data.Plans)
            {
                Exec("INSERT INTO Plans (Id, UserId, Name) VALUES (@id,@u,@n);", c =>
                {
                    c.Parameters.AddWithValue("@id", plan.Id);
                    c.Parameters.AddWithValue("@u", _userId);
                    c.Parameters.AddWithValue("@n", plan.Name);
                });
                int order = 0;
                foreach (var it in plan.Items)
                {
                    int sort = order++;
                    Exec("INSERT INTO PlanItems (PlanId, ExerciseId, Sets, Reps, Weight, SortOrder) VALUES (@p,@e,@s,@r,@w,@o);", c =>
                    {
                        c.Parameters.AddWithValue("@p", plan.Id);
                        c.Parameters.AddWithValue("@e", it.ExerciseId);
                        c.Parameters.AddWithValue("@s", it.Sets);
                        c.Parameters.AddWithValue("@r", it.Reps);
                        c.Parameters.AddWithValue("@w", it.Weight);
                        c.Parameters.AddWithValue("@o", sort);
                    });
                }
            }

            foreach (var s in Data.Sessions)
            {
                Exec("INSERT INTO Sessions (Id, UserId, Date, Name, PlanId, DurationMin, Rpe, Notes) VALUES (@id,@u,@d,@n,@p,@dur,@rpe,@notes);", c =>
                {
                    c.Parameters.AddWithValue("@id", s.Id);
                    c.Parameters.AddWithValue("@u", _userId);
                    c.Parameters.AddWithValue("@d", s.Date.ToString("yyyy-MM-dd", Inv));
                    c.Parameters.AddWithValue("@n", s.Name);
                    c.Parameters.AddWithValue("@p", (object)s.PlanId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@dur", s.DurationMin);
                    c.Parameters.AddWithValue("@rpe", s.Rpe);
                    c.Parameters.AddWithValue("@notes", (object)s.Notes ?? "");
                });

                int exOrder = 0;
                foreach (var entry in s.Exercises)
                {
                    int exSort = exOrder++;
                    long entryRowId;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT INTO SessionExercises (SessionId, ExerciseId, SortOrder) VALUES (@s,@e,@o); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
                        cmd.Parameters.AddWithValue("@s", s.Id);
                        cmd.Parameters.AddWithValue("@e", entry.ExerciseId);
                        cmd.Parameters.AddWithValue("@o", exSort);
                        entryRowId = Convert.ToInt64(cmd.ExecuteScalar());
                    }

                    int setIndex = 0;
                    foreach (var set in entry.Sets)
                    {
                        int idx = setIndex++;
                        Exec("INSERT INTO SessionSets (SessionExerciseId, SetIndex, Reps, Weight, DurationSec, DistanceKm) VALUES (@e,@i,@r,@w,@d,@dist);", c =>
                        {
                            c.Parameters.AddWithValue("@e", entryRowId);
                            c.Parameters.AddWithValue("@i", idx);
                            c.Parameters.AddWithValue("@r", set.Reps);
                            c.Parameters.AddWithValue("@w", set.Weight);
                            c.Parameters.AddWithValue("@d", set.DurationSec);
                            c.Parameters.AddWithValue("@dist", set.DistanceKm);
                        });
                    }
                }
            }

            tx.Commit();
        }

        public static Exercise ExerciseById(string id) => Data.Exercises.FirstOrDefault(e => e.Id == id);
        public static WorkoutPlan PlanById(string id) => Data.Plans.FirstOrDefault(p => p.Id == id);
        public static WorkoutSession SessionById(string id) => Data.Sessions.FirstOrDefault(s => s.Id == id);

        public static double SessionVolume(WorkoutSession session)
        {
            double total = 0;
            foreach (var entry in session.Exercises)
            {
                var ex = ExerciseById(entry.ExerciseId);
                if (ex == null || ex.Type != ExerciseType.Strength) continue;
                foreach (var s in entry.Sets)
                    total += s.Reps * s.Weight;
            }
            return total;
        }

        public static double SessionTopSet(WorkoutSession session, string exerciseId)
        {
            var entry = session.Exercises.FirstOrDefault(e => e.ExerciseId == exerciseId);
            if (entry == null) return 0;
            double best = 0;
            foreach (var s in entry.Sets)
                if (s.Weight > best) best = s.Weight;
            return best;
        }

        public static DateTime StartOfWeek(DateTime d)
        {
            int diff = ((int)d.DayOfWeek + 6) % 7; // понедельник = 0
            return d.Date.AddDays(-diff);
        }

        private static AppData SeedData()
        {
            var squat = new Exercise { Name = "Приседания со штангой", MuscleGroup = "Ноги", Type = ExerciseType.Strength };
            var bench = new Exercise { Name = "Жим лёжа", MuscleGroup = "Грудь", Type = ExerciseType.Strength };
            var deadlift = new Exercise { Name = "Становая тяга", MuscleGroup = "Спина", Type = ExerciseType.Strength };
            var run = new Exercise { Name = "Бег", MuscleGroup = "Кардио", Type = ExerciseType.Cardio };
            var plank = new Exercise { Name = "Планка", MuscleGroup = "Кор", Type = ExerciseType.Time };
            var row = new Exercise { Name = "Тяга штанги в наклоне", MuscleGroup = "Спина", Type = ExerciseType.Strength };
            var pullup = new Exercise { Name = "Подтягивания", MuscleGroup = "Спина", Type = ExerciseType.Strength };
            var lunge = new Exercise { Name = "Выпады с гантелями", MuscleGroup = "Ноги", Type = ExerciseType.Strength };
            var pressStand = new Exercise { Name = "Жим штанги стоя", MuscleGroup = "Плечи", Type = ExerciseType.Strength };
            var bike = new Exercise { Name = "Велотренажёр", MuscleGroup = "Кардио", Type = ExerciseType.Cardio };

            var exercises = new List<Exercise> { squat, bench, deadlift, run, plank, row, pullup, lunge, pressStand, bike };

            var planA = new WorkoutPlan
            {
                Name = "День А — база",
                Items = new List<PlanItem>
                {
                    new() { ExerciseId = squat.Id, Sets = 4, Reps = 6, Weight = 80 },
                    new() { ExerciseId = bench.Id, Sets = 4, Reps = 8, Weight = 60 },
                    new() { ExerciseId = plank.Id, Sets = 3, Reps = 0, Weight = 0 }
                }
            };
            var planB = new WorkoutPlan
            {
                Name = "День Б — спина и тяги",
                Items = new List<PlanItem>
                {
                    new() { ExerciseId = deadlift.Id, Sets = 4, Reps = 5, Weight = 100 },
                    new() { ExerciseId = row.Id, Sets = 4, Reps = 8, Weight = 50 },
                    new() { ExerciseId = pullup.Id, Sets = 3, Reps = 8, Weight = 0 }
                }
            };
            var planC = new WorkoutPlan
            {
                Name = "День В — ноги и плечи",
                Items = new List<PlanItem>
                {
                    new() { ExerciseId = squat.Id, Sets = 4, Reps = 8, Weight = 70 },
                    new() { ExerciseId = lunge.Id, Sets = 3, Reps = 10, Weight = 16 },
                    new() { ExerciseId = pressStand.Id, Sets = 4, Reps = 8, Weight = 35 }
                }
            };
            var planD = new WorkoutPlan
            {
                Name = "День Г — кардио и кор",
                Items = new List<PlanItem>
                {
                    new() { ExerciseId = run.Id, Sets = 1, Reps = 0, Weight = 0 },
                    new() { ExerciseId = bike.Id, Sets = 1, Reps = 0, Weight = 0 },
                    new() { ExerciseId = plank.Id, Sets = 3, Reps = 0, Weight = 0 }
                }
            };
            var plans = new List<WorkoutPlan> { planA, planB, planC, planD };

            DateTime D(int daysAgo) => DateTime.Today.AddDays(-daysAgo);

            var sessions = new List<WorkoutSession>
            {
                new()
                {
                    Date = D(11), Name = "День Б — спина и тяги", PlanId = planB.Id, DurationMin = 48, Rpe = 7,
                    Notes = "Становая пошла тяжело, но техника чистая.",
                    Exercises = new List<ExerciseEntry>
                    {
                        new() { ExerciseId = deadlift.Id, Sets = new(){ new(){Reps=5,Weight=95}, new(){Reps=5,Weight=100}, new(){Reps=4,Weight=102.5}, new(){Reps=4,Weight=102.5} } },
                        new() { ExerciseId = row.Id, Sets = new(){ new(){Reps=8,Weight=47.5}, new(){Reps=8,Weight=50}, new(){Reps=7,Weight=50}, new(){Reps=7,Weight=50} } },
                        new() { ExerciseId = pullup.Id, Sets = new(){ new(){Reps=9,Weight=0}, new(){Reps=8,Weight=0}, new(){Reps=7,Weight=0} } }
                    }
                },
                new()
                {
                    Date = D(9), Name = "День А — база", PlanId = planA.Id, DurationMin = 52, Rpe = 7,
                    Notes = "Ровный темп, приседания дались легко.",
                    Exercises = new List<ExerciseEntry>
                    {
                        new() { ExerciseId = squat.Id, Sets = new(){ new(){Reps=6,Weight=75}, new(){Reps=6,Weight=80}, new(){Reps=5,Weight=82}, new(){Reps=5,Weight=82} } },
                        new() { ExerciseId = bench.Id, Sets = new(){ new(){Reps=8,Weight=55}, new(){Reps=8,Weight=60}, new(){Reps=7,Weight=60}, new(){Reps=6,Weight=60} } },
                        new() { ExerciseId = plank.Id, Sets = new(){ new(){DurationSec=60}, new(){DurationSec=65}, new(){DurationSec=60} } }
                    }
                },
                new()
                {
                    Date = D(7), Name = "День В — ноги и плечи", PlanId = planC.Id, DurationMin = 50, Rpe = 6,
                    Notes = "Выпады даются лучше с каждой неделей.",
                    Exercises = new List<ExerciseEntry>
                    {
                        new() { ExerciseId = squat.Id, Sets = new(){ new(){Reps=8,Weight=65}, new(){Reps=8,Weight=70}, new(){Reps=7,Weight=70}, new(){Reps=7,Weight=70} } },
                        new() { ExerciseId = lunge.Id, Sets = new(){ new(){Reps=10,Weight=14}, new(){Reps=10,Weight=16}, new(){Reps=9,Weight=16} } },
                        new() { ExerciseId = pressStand.Id, Sets = new(){ new(){Reps=8,Weight=32.5}, new(){Reps=8,Weight=35}, new(){Reps=7,Weight=35}, new(){Reps=6,Weight=35} } }
                    }
                },
                new()
                {
                    Date = D(6), Name = "Кардио на восстановление", DurationMin = 30, Rpe = 4,
                    Notes = "Лёгкая пробежка в парке.",
                    Exercises = new List<ExerciseEntry>
                    {
                        new() { ExerciseId = run.Id, Sets = new(){ new(){DurationSec=1800, DistanceKm=5} } }
                    }
                },
                new()
                {
                    Date = D(4), Name = "День А — база", PlanId = planA.Id, DurationMin = 55, Rpe = 8,
                    Notes = "Личный рекорд в приседе.",
                    Exercises = new List<ExerciseEntry>
                    {
                        new() { ExerciseId = squat.Id, Sets = new(){ new(){Reps=6,Weight=80}, new(){Reps=5,Weight=85}, new(){Reps=5,Weight=87.5}, new(){Reps=4,Weight=87.5} } },
                        new() { ExerciseId = deadlift.Id, Sets = new(){ new(){Reps=5,Weight=100}, new(){Reps=5,Weight=105}, new(){Reps=3,Weight=110} } }
                    }
                },
                new()
                {
                    Date = D(1), Name = "Верх тела", DurationMin = 40, Rpe = 6, Notes = "",
                    Exercises = new List<ExerciseEntry>
                    {
                        new() { ExerciseId = bench.Id, Sets = new(){ new(){Reps=8,Weight=60}, new(){Reps=8,Weight=62.5}, new(){Reps=6,Weight=65} } }
                    }
                }
            };

            return new AppData { Exercises = exercises, Plans = plans, Sessions = sessions };
        }
    }
}
