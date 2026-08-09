using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace MahdiFitHub.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
    [EmailAddress(ErrorMessage = "صيغة البريد غير صحيحة")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public sealed class CreateUserViewModel
{
    [Required, StringLength(80, MinimumLength = 3)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(30, MinimumLength = 8)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = UserRoles.Member;

    [Phone]
    public string? Phone { get; set; }

    public int? MembershipPlanId { get; set; }
}

public sealed class CreatePlanViewModel
{
    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 730)]
    public int DurationDays { get; set; } = 30;

    [Range(0, 1_000_000)]
    public decimal Price { get; set; }

    [Range(1, 21)]
    public int WeeklyVisitLimit { get; set; } = 3;
}

public sealed class CreateSessionViewModel
{
    [Required, StringLength(100)]
    public string Title { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Room { get; set; } = string.Empty;

    [Required]
    public DateTime StartsAtLocal { get; set; } = DateTime.Now.AddDays(1);

    [Range(15, 240)]
    public int DurationMinutes { get; set; } = 60;

    [Range(1, 200)]
    public int Capacity { get; set; } = 15;

    [Range(1, int.MaxValue)]
    public int TrainerId { get; set; }
}

public sealed class DashboardViewModel
{
    public int ActiveMembers { get; init; }
    public int Trainers { get; init; }
    public int UpcomingSessions { get; init; }
    public int TotalBookings { get; init; }
    public IReadOnlyList<GymSession> NextSessions { get; init; } = [];
}

public sealed class CreateMembershipViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "اختر عضوًا.")]
    public int MemberId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "اختر خطة اشتراك.")]
    public int PlanId { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime StartDate { get; set; } = DateTime.Today;
}

public sealed class AddMealViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "اختر نوع الطعام بعد تحليل الصورة.")]
    public int FoodItemId { get; set; }

    [Range(typeof(decimal), "0.25", "10", ErrorMessage = "حدد كمية بين ربع حصة و10 حصص.")]
    public decimal ServingCount { get; set; } = 1;

    [Required, StringLength(30)]
    public string MealType { get; set; } = "غداء";

    public IFormFile? Image { get; set; }

    [StringLength(200)]
    public string? DetectedLabel { get; set; }
}

public sealed class NutritionDashboardViewModel
{
    public int DailyCalorieGoal { get; init; }
    public int DailyProteinGoal { get; init; }
    public IReadOnlyList<FoodItem> FoodCatalog { get; init; } = [];
    public IReadOnlyList<MealLog> TodayMeals { get; init; } = [];
    public int ConsumedCalories => TodayMeals.Sum(meal => meal.Calories);
    public int RemainingCalories => Math.Max(0, DailyCalorieGoal - ConsumedCalories);
    public decimal ProteinGrams => TodayMeals.Sum(meal => meal.ProteinGrams);
    public decimal CarbohydrateGrams => TodayMeals.Sum(meal => meal.CarbohydrateGrams);
    public decimal FatGrams => TodayMeals.Sum(meal => meal.FatGrams);
    public int ProgressPercentage => DailyCalorieGoal <= 0
        ? 0
        : Math.Min(100, (int)Math.Round(ConsumedCalories * 100d / DailyCalorieGoal));
}

public sealed class UpdateNutritionGoalViewModel
{
    [Range(1000, 6000, ErrorMessage = "هدف السعرات يجب أن يكون بين 1000 و6000.")]
    public int DailyCalories { get; set; } = 2200;

    [Range(30, 350, ErrorMessage = "هدف البروتين يجب أن يكون بين 30 و350 غراماً.")]
    public int DailyProteinGrams { get; set; } = 120;
}
