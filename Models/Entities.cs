using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MahdiFitHub.Models;

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Trainer = "Trainer";
    public const string Member = "Member";
    public const string Staff = Admin + "," + Trainer;
}

public sealed class AppUser
{
    public int Id { get; set; }

    [MaxLength(80)]
    public required string FullName { get; set; }

    [MaxLength(160)]
    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    [MaxLength(20)]
    public required string Role { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
    public int? MembershipPlanId { get; set; }
    [JsonIgnore]
    public MembershipPlan? MembershipPlan { get; set; }
    [JsonIgnore]
    public ICollection<Booking> Bookings { get; set; } = [];
    [JsonIgnore]
    public ICollection<GymSession> LedSessions { get; set; } = [];
    [JsonIgnore]
    public ICollection<Membership> Memberships { get; set; } = [];
}

public sealed class MembershipPlan
{
    public int Id { get; set; }

    [MaxLength(80)]
    public required string Name { get; set; }

    public int DurationDays { get; set; }
    public decimal Price { get; set; }
    public int WeeklyVisitLimit { get; set; }
    public bool IsActive { get; set; } = true;
    [JsonIgnore]
    public ICollection<AppUser> Members { get; set; } = [];
}

public sealed class Membership
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    [JsonIgnore]
    public AppUser? Member { get; set; }
    public int PlanId { get; set; }
    [JsonIgnore]
    public MembershipPlan? Plan { get; set; }
    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public decimal PricePaid { get; set; }

    [JsonIgnore]
    public bool IsActive => CancelledAtUtc is null && StartDateUtc.Date <= DateTime.UtcNow.Date && EndDateUtc.Date >= DateTime.UtcNow.Date;

    [JsonIgnore]
    public string Status => CancelledAtUtc is not null
        ? "Cancelled"
        : EndDateUtc.Date < DateTime.UtcNow.Date ? "Expired" : StartDateUtc.Date > DateTime.UtcNow.Date ? "Scheduled" : "Active";
}

public sealed class GymSession
{
    public int Id { get; set; }

    [MaxLength(100)]
    public required string Title { get; set; }

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(80)]
    public required string Room { get; set; }

    public DateTime StartsAtUtc { get; set; }
    public int DurationMinutes { get; set; }
    public int Capacity { get; set; }
    public int TrainerId { get; set; }
    [JsonIgnore]
    public AppUser? Trainer { get; set; }
    [JsonIgnore]
    public ICollection<Booking> Bookings { get; set; } = [];
}

public sealed class Booking
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    [JsonIgnore]
    public GymSession? Session { get; set; }
    public int MemberId { get; set; }
    [JsonIgnore]
    public AppUser? Member { get; set; }
    public DateTime BookedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class FoodItem
{
    public int Id { get; set; }

    [MaxLength(100)]
    public required string Name { get; set; }

    [MaxLength(80)]
    public required string ServingName { get; set; }

    public int Calories { get; set; }
    public decimal ProteinGrams { get; set; }
    public decimal CarbohydrateGrams { get; set; }
    public decimal FatGrams { get; set; }
    public List<string> RecognitionKeywords { get; set; } = [];
}

public sealed class MealLog
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public int FoodItemId { get; set; }

    [MaxLength(100)]
    public required string FoodName { get; set; }

    [MaxLength(80)]
    public required string ServingName { get; set; }

    [MaxLength(30)]
    public required string MealType { get; set; }

    public decimal ServingCount { get; set; }
    public int Calories { get; set; }
    public decimal ProteinGrams { get; set; }
    public decimal CarbohydrateGrams { get; set; }
    public decimal FatGrams { get; set; }
    public DateTime LoggedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(240)]
    public string? ImagePath { get; set; }

    [MaxLength(200)]
    public string? DetectedLabel { get; set; }
}

public sealed class NutritionGoal
{
    public int MemberId { get; set; }
    public int DailyCalories { get; set; } = 2200;
    public int DailyProteinGrams { get; set; } = 120;
}
