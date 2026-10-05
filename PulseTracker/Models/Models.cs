using System;
using System.Collections.Generic;

namespace PulseTracker.Models
{
    public enum ExerciseType
    {
        Strength,   // подходы x повторы x вес
        Cardio,     // время + дистанция
        Time        // только длительность (например планка)
    }

    public class Exercise
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string MuscleGroup { get; set; } = "";
        public ExerciseType Type { get; set; } = ExerciseType.Strength;

        public string TypeLabel => Type switch
        {
            ExerciseType.Strength => "Силовое",
            ExerciseType.Cardio => "Кардио",
            ExerciseType.Time => "На время",
            _ => ""
        };
    }

    public class PlanItem
    {
        public string ExerciseId { get; set; } = "";
        public int Sets { get; set; } = 3;
        public int Reps { get; set; } = 8;
        public double Weight { get; set; } = 0;
    }

    public class WorkoutPlan
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public List<PlanItem> Items { get; set; } = new();
    }

    public class SetEntry
    {
        public int Reps { get; set; }
        public double Weight { get; set; }
        public double DurationSec { get; set; }
        public double DistanceKm { get; set; }
    }

    public class ExerciseEntry
    {
        public string ExerciseId { get; set; } = "";
        public List<SetEntry> Sets { get; set; } = new();
    }

    public class WorkoutSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Date { get; set; } = DateTime.Today;
        public string Name { get; set; } = "";
        public string PlanId { get; set; } = null;
        public int DurationMin { get; set; }
        public int Rpe { get; set; }
        public string Notes { get; set; } = "";
        public List<ExerciseEntry> Exercises { get; set; } = new();
    }

    public class AppData
    {
        public List<Exercise> Exercises { get; set; } = new();
        public List<WorkoutPlan> Plans { get; set; } = new();
        public List<WorkoutSession> Sessions { get; set; } = new();
    }
}
