using System.ComponentModel.DataAnnotations;

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
