using System.Text.Json;
using MahdiFitHub.Models;
using Microsoft.AspNetCore.Identity;

namespace MahdiFitHub.Data;

public sealed class GymStore(IWebHostEnvironment environment, IPasswordHasher<AppUser> hasher)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath = Path.Combine(environment.ContentRootPath, "App_Data", "mahdifit.json");
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private StoreDocument _data = new();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await _gate.WaitAsync();
        try
        {
            if (File.Exists(_filePath))
            {
                await using (var input = File.OpenRead(_filePath))
                    _data = await JsonSerializer.DeserializeAsync<StoreDocument>(input, _jsonOptions) ?? new();
                var requiresSave = false;
                if (_data.SchemaVersion < 2)
                {
                    foreach (var member in _data.Users.Where(user => user.Role == UserRoles.Member && user.MembershipPlanId is not null))
                    {
                        var plan = _data.Plans.SingleOrDefault(item => item.Id == member.MembershipPlanId);
                        if (plan is null) continue;
                        _data.Memberships.Add(new Membership
                        {
                            Id = NextId(_data.Memberships.Select(item => item.Id)),
                            MemberId = member.Id,
                            PlanId = plan.Id,
                            StartDateUtc = member.JoinedAtUtc.Date,
                            EndDateUtc = DateTime.UtcNow.Date.AddDays(plan.DurationDays - 1),
                            PricePaid = plan.Price
                        });
                    }
                    _data.SchemaVersion = 2;
                    requiresSave = true;
                }
                if (_data.SchemaVersion < 3 || _data.FoodItems.Count == 0)
                {
                    EnsureNutritionCatalog();
                    _data.SchemaVersion = 3;
                    requiresSave = true;
                }
                if (requiresSave) await SaveUnsafeAsync();
            }
            else
            {
                Seed();
                await SaveUnsafeAsync();
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<AppUser?> FindUserByEmailAsync(string email) => await ReadAsync(() => _data.Users.SingleOrDefault(user => user.Email == email));
    public async Task<bool> EmailExistsAsync(string email) => await ReadAsync(() => _data.Users.Any(user => user.Email == email));
    public async Task<bool> TrainerExistsAsync(int id) => await ReadAsync(() => _data.Users.Any(user => user.Id == id && user.Role == UserRoles.Trainer && user.IsActive));
    public async Task<IReadOnlyCollection<int>> GetBookedSessionIdsAsync(int memberId) => await ReadAsync<IReadOnlyCollection<int>>(() => _data.Bookings.Where(booking => booking.MemberId == memberId).Select(booking => booking.SessionId).ToList());

    public async Task<IReadOnlyList<AppUser>> GetUsersAsync(string? role = null) => await ReadAsync<IReadOnlyList<AppUser>>(() =>
    {
        var users = _data.Users.Where(user => role is null || user.Role == role).OrderByDescending(user => user.JoinedAtUtc).ToList();
        foreach (var user in users) user.MembershipPlan = _data.Plans.SingleOrDefault(plan => plan.Id == user.MembershipPlanId);
        return users;
    });

    public async Task<IReadOnlyList<AppUser>> GetActiveTrainersAsync() => await ReadAsync<IReadOnlyList<AppUser>>(() =>
        _data.Users.Where(user => user.Role == UserRoles.Trainer && user.IsActive).OrderBy(user => user.FullName).ToList());

    public async Task AddUserAsync(AppUser user) => await WriteAsync(() =>
    {
        user.Id = NextId(_data.Users.Select(item => item.Id));
        _data.Users.Add(user);
    });

    public async Task<bool> ToggleUserAsync(int id) => await WriteAsync(() =>
    {
        var user = _data.Users.SingleOrDefault(item => item.Id == id);
        if (user is null) return false;
        user.IsActive = !user.IsActive;
        return true;
    });

    public async Task<IReadOnlyList<MembershipPlan>> GetPlansAsync(bool onlyActive = false) => await ReadAsync<IReadOnlyList<MembershipPlan>>(() =>
    {
        var plans = _data.Plans.Where(plan => !onlyActive || plan.IsActive).OrderBy(plan => plan.Price).ToList();
        foreach (var plan in plans) plan.Members = _data.Users.Where(user => user.MembershipPlanId == plan.Id).ToList();
        return plans;
    });

    public async Task<IReadOnlyList<Membership>> GetMembershipsAsync() => await ReadAsync<IReadOnlyList<Membership>>(() =>
        HydrateMemberships(_data.Memberships.OrderByDescending(item => item.StartDateUtc)).ToList());

    public async Task<IReadOnlyList<AppUser>> GetActiveMembersAsync() => await ReadAsync<IReadOnlyList<AppUser>>(() =>
        _data.Users.Where(user => user.Role == UserRoles.Member && user.IsActive).OrderBy(user => user.FullName).ToList());

    public async Task<string> AddMembershipAsync(int memberId, int planId, DateTime startDate) => await WriteAsync(() =>
    {
        var member = _data.Users.SingleOrDefault(user => user.Id == memberId && user.Role == UserRoles.Member && user.IsActive);
        if (member is null) return "member-missing";

        var plan = _data.Plans.SingleOrDefault(item => item.Id == planId && item.IsActive);
        if (plan is null) return "plan-missing";

        if (_data.Memberships.Any(item => item.MemberId == memberId && item.CancelledAtUtc is null && item.EndDateUtc.Date >= DateTime.UtcNow.Date))
            return "already-active";

        var startsAt = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        _data.Memberships.Add(new Membership
        {
            Id = NextId(_data.Memberships.Select(item => item.Id)),
            MemberId = memberId,
            PlanId = planId,
            StartDateUtc = startsAt,
            EndDateUtc = startsAt.AddDays(plan.DurationDays - 1),
            PricePaid = plan.Price
        });
        member.MembershipPlanId = planId;
        return "ok";
    });

    public async Task<bool> CancelMembershipAsync(int id) => await WriteAsync(() =>
    {
        var membership = _data.Memberships.SingleOrDefault(item => item.Id == id);
        if (membership is null || membership.CancelledAtUtc is not null) return false;
        membership.CancelledAtUtc = DateTime.UtcNow;
        if (_data.Users.SingleOrDefault(user => user.Id == membership.MemberId) is { } member)
            member.MembershipPlanId = null;
        return true;
    });

    public async Task<string> RenewMembershipAsync(int id) => await WriteAsync(() =>
    {
        var previous = _data.Memberships.SingleOrDefault(item => item.Id == id);
        if (previous is null) return "missing";
        var plan = _data.Plans.SingleOrDefault(item => item.Id == previous.PlanId && item.IsActive);
        if (plan is null) return "plan-missing";
        if (_data.Memberships.Any(item => item.MemberId == previous.MemberId && item.Id != id && item.CancelledAtUtc is null && item.EndDateUtc.Date >= DateTime.UtcNow.Date))
            return "already-active";

        var start = previous.IsActive ? previous.EndDateUtc.Date.AddDays(1) : DateTime.UtcNow.Date;
        _data.Memberships.Add(new Membership
        {
            Id = NextId(_data.Memberships.Select(item => item.Id)),
            MemberId = previous.MemberId,
            PlanId = previous.PlanId,
            StartDateUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc),
            EndDateUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc).AddDays(plan.DurationDays - 1),
            PricePaid = plan.Price
        });
        if (_data.Users.SingleOrDefault(user => user.Id == previous.MemberId) is { } member)
            member.MembershipPlanId = plan.Id;
        return "ok";
    });

    public async Task AddPlanAsync(MembershipPlan plan) => await WriteAsync(() =>
    {
        plan.Id = NextId(_data.Plans.Select(item => item.Id));
        _data.Plans.Add(plan);
    });

    public async Task<bool> TogglePlanAsync(int id) => await WriteAsync(() =>
    {
        var plan = _data.Plans.SingleOrDefault(item => item.Id == id);
        if (plan is null) return false;
        plan.IsActive = !plan.IsActive;
        return true;
    });

    public async Task<DashboardViewModel> GetDashboardAsync() => await ReadAsync(() =>
    {
        var now = DateTime.UtcNow;
        return new DashboardViewModel
        {
            ActiveMembers = _data.Users.Count(user => user.Role == UserRoles.Member && user.IsActive),
            Trainers = _data.Users.Count(user => user.Role == UserRoles.Trainer && user.IsActive),
            UpcomingSessions = _data.Sessions.Count(session => session.StartsAtUtc >= now),
            TotalBookings = _data.Bookings.Count,
            NextSessions = HydrateSessions(_data.Sessions.Where(session => session.StartsAtUtc >= now).OrderBy(session => session.StartsAtUtc).Take(5)).ToList()
        };
    });

    public async Task<IReadOnlyList<GymSession>> GetSessionsAsync() => await ReadAsync<IReadOnlyList<GymSession>>(() =>
        HydrateSessions(_data.Sessions.Where(session => session.StartsAtUtc >= DateTime.UtcNow.AddHours(-2)).OrderBy(session => session.StartsAtUtc)).ToList());

    public async Task AddSessionAsync(GymSession session) => await WriteAsync(() =>
    {
        session.Id = NextId(_data.Sessions.Select(item => item.Id));
        _data.Sessions.Add(session);
    });

    public async Task<string> BookAsync(int sessionId, int memberId) => await WriteAsync(() =>
    {
        var session = _data.Sessions.SingleOrDefault(item => item.Id == sessionId);
        if (session is null || session.StartsAtUtc <= DateTime.UtcNow) return "missing";
        if (!_data.Memberships.Any(item => item.MemberId == memberId && item.CancelledAtUtc is null && item.StartDateUtc.Date <= DateTime.UtcNow.Date && item.EndDateUtc.Date >= DateTime.UtcNow.Date)) return "no-membership";
        if (_data.Bookings.Any(item => item.SessionId == sessionId && item.MemberId == memberId)) return "duplicate";
        if (_data.Bookings.Count(item => item.SessionId == sessionId) >= session.Capacity) return "full";
        _data.Bookings.Add(new Booking { Id = NextId(_data.Bookings.Select(item => item.Id)), SessionId = sessionId, MemberId = memberId });
        return "ok";
    });

    public async Task CancelBookingAsync(int sessionId, int memberId) => await WriteAsync(() =>
        _data.Bookings.RemoveAll(item => item.SessionId == sessionId && item.MemberId == memberId));

    public async Task DeleteSessionAsync(int sessionId) => await WriteAsync(() =>
    {
        _data.Sessions.RemoveAll(item => item.Id == sessionId);
        _data.Bookings.RemoveAll(item => item.SessionId == sessionId);
    });

    public async Task<NutritionDashboardViewModel> GetNutritionDashboardAsync(int memberId) => await ReadAsync(() =>
    {
        var goal = _data.NutritionGoals.SingleOrDefault(item => item.MemberId == memberId) ?? new NutritionGoal { MemberId = memberId };
        var today = DateTime.UtcNow.Date;
        return new NutritionDashboardViewModel
        {
            DailyCalorieGoal = goal.DailyCalories,
            DailyProteinGoal = goal.DailyProteinGrams,
            FoodCatalog = _data.FoodItems.OrderBy(item => item.Name).ToList(),
            TodayMeals = _data.MealLogs
                .Where(item => item.MemberId == memberId && item.LoggedAtUtc.Date == today)
                .OrderByDescending(item => item.LoggedAtUtc)
                .ToList()
        };
    });

    public async Task<FoodItem?> GetFoodItemAsync(int id) => await ReadAsync(() =>
        _data.FoodItems.SingleOrDefault(item => item.Id == id));

    public async Task AddMealAsync(MealLog meal) => await WriteAsync(() =>
    {
        meal.Id = NextId(_data.MealLogs.Select(item => item.Id));
        _data.MealLogs.Add(meal);
    });

    public async Task UpdateNutritionGoalAsync(int memberId, int calories, int proteinGrams) => await WriteAsync(() =>
    {
        var goal = _data.NutritionGoals.SingleOrDefault(item => item.MemberId == memberId);
        if (goal is null)
        {
            _data.NutritionGoals.Add(new NutritionGoal
            {
                MemberId = memberId,
                DailyCalories = calories,
                DailyProteinGrams = proteinGrams
            });
            return;
        }

        goal.DailyCalories = calories;
        goal.DailyProteinGrams = proteinGrams;
    });

    public async Task<string?> DeleteMealAsync(int id, int memberId) => await WriteAsync(() =>
    {
        var meal = _data.MealLogs.SingleOrDefault(item => item.Id == id && item.MemberId == memberId);
        if (meal is null) return null;
        _data.MealLogs.Remove(meal);
        return meal.ImagePath;
    });

    private IEnumerable<GymSession> HydrateSessions(IEnumerable<GymSession> sessions)
    {
        foreach (var session in sessions)
        {
            session.Trainer = _data.Users.SingleOrDefault(user => user.Id == session.TrainerId);
            session.Bookings = _data.Bookings.Where(booking => booking.SessionId == session.Id).ToList();
            yield return session;
        }
    }

    private IEnumerable<Membership> HydrateMemberships(IEnumerable<Membership> memberships)
    {
        foreach (var membership in memberships)
        {
            membership.Member = _data.Users.SingleOrDefault(user => user.Id == membership.MemberId);
            membership.Plan = _data.Plans.SingleOrDefault(plan => plan.Id == membership.PlanId);
            yield return membership;
        }
    }

    private async Task<T> ReadAsync<T>(Func<T> action)
    {
        await _gate.WaitAsync();
        try { return action(); }
        finally { _gate.Release(); }
    }

    private async Task WriteAsync(Action action)
    {
        await _gate.WaitAsync();
        try { action(); await SaveUnsafeAsync(); }
        finally { _gate.Release(); }
    }

    private async Task<T> WriteAsync<T>(Func<T> action)
    {
        await _gate.WaitAsync();
        try { var result = action(); await SaveUnsafeAsync(); return result; }
        finally { _gate.Release(); }
    }

    private void Seed()
    {
        _data.SchemaVersion = 3;
        var monthly = new MembershipPlan { Id = 1, Name = "المرن الشهري", DurationDays = 30, Price = 35, WeeklyVisitLimit = 4 };
        var premium = new MembershipPlan { Id = 2, Name = "الأداء المفتوح", DurationDays = 90, Price = 85, WeeklyVisitLimit = 7 };
        _data.Plans.AddRange([monthly, premium]);

        var admin = NewUser(1, "مدير النظام", "admin@mahdifit.local", UserRoles.Admin);
        admin.PasswordHash = hasher.HashPassword(admin, "Admin123!");
        var trainer = NewUser(2, "ليان مصطفى", "trainer@mahdifit.local", UserRoles.Trainer);
        trainer.PasswordHash = hasher.HashPassword(trainer, "Trainer123!");
        var member = NewUser(3, "عضو تجريبي", "member@mahdifit.local", UserRoles.Member);
        member.MembershipPlanId = monthly.Id;
        member.PasswordHash = hasher.HashPassword(member, "Member123!");
        _data.Users.AddRange([admin, trainer, member]);

        _data.Memberships.Add(new Membership
        {
            Id = 1,
            MemberId = member.Id,
            PlanId = monthly.Id,
            StartDateUtc = DateTime.UtcNow.Date,
            EndDateUtc = DateTime.UtcNow.Date.AddDays(monthly.DurationDays - 1),
            PricePaid = monthly.Price
        });

        _data.Sessions.AddRange([
            new GymSession { Id = 1, Title = "قوة وظيفية", Description = "حصة متدرجة تجمع تمارين الحركة والقوة.", Room = "القاعة A", StartsAtUtc = DateTime.UtcNow.AddDays(1).Date.AddHours(15), DurationMinutes = 55, Capacity = 16, TrainerId = trainer.Id },
            new GymSession { Id = 2, Title = "استعادة وحركة", Description = "مرونة وتنفس لتحسين التعافي بعد التمرين.", Room = "استوديو الحركة", StartsAtUtc = DateTime.UtcNow.AddDays(2).Date.AddHours(16), DurationMinutes = 45, Capacity = 12, TrainerId = trainer.Id }
        ]);

        EnsureNutritionCatalog();
        _data.NutritionGoals.Add(new NutritionGoal { MemberId = member.Id, DailyCalories = 2200, DailyProteinGrams = 120 });
    }

    private void EnsureNutritionCatalog()
    {
        if (_data.FoodItems.Count > 0) return;

        _data.FoodItems.AddRange([
            Food(1, "صدر دجاج مشوي", "100 غرام", 165, 31, 0, 3.6m, "chicken", "hen", "poultry"),
            Food(2, "أرز أبيض مطبوخ", "كوب واحد", 205, 4.3m, 44.5m, 0.4m, "rice", "risotto"),
            Food(3, "بيض", "بيضة كبيرة", 72, 6.3m, 0.4m, 4.8m, "egg", "omelet", "omelette"),
            Food(4, "موز", "حبة متوسطة", 105, 1.3m, 27, 0.4m, "banana"),
            Food(5, "تفاح", "حبة متوسطة", 95, 0.5m, 25, 0.3m, "apple", "granny smith"),
            Food(6, "برتقال", "حبة متوسطة", 62, 1.2m, 15.4m, 0.2m, "orange", "citrus"),
            Food(7, "شوفان", "50 غرام", 190, 6.5m, 34, 3.5m, "oat", "oatmeal", "porridge"),
            Food(8, "لبن يوناني", "170 غرام", 100, 17, 6, 0.7m, "yogurt", "yoghurt"),
            Food(9, "تونة", "علبة مصفاة", 190, 42, 0, 2, "tuna", "fish"),
            Food(10, "سلطة خضار", "طبق متوسط", 120, 4, 18, 4, "salad", "lettuce", "greens"),
            Food(11, "خبز", "شريحة واحدة", 80, 3, 15, 1, "bread", "toast", "loaf"),
            Food(12, "بيتزا", "قطعة متوسطة", 285, 12, 36, 10, "pizza"),
            Food(13, "برغر بالجبنة", "ساندويتش", 520, 27, 40, 28, "burger", "cheeseburger", "hamburger"),
            Food(14, "معكرونة مطبوخة", "كوب واحد", 220, 8, 43, 1.3m, "pasta", "spaghetti", "carbonara"),
            Food(15, "سلمون مشوي", "100 غرام", 208, 20, 0, 13, "salmon"),
            Food(16, "ستيك لحم", "100 غرام", 250, 26, 0, 15, "steak", "beef", "meat loaf"),
            Food(17, "أفوكادو", "نصف حبة", 160, 2, 8.5m, 14.7m, "avocado"),
            Food(18, "حليب", "كوب واحد", 122, 8, 12, 4.8m, "milk"),
            Food(19, "تمر", "3 حبات", 66, 0.5m, 18, 0.1m, "date", "dates", "dried fruit"),
            Food(20, "شوربة عدس", "كوب واحد", 180, 12, 30, 3, "lentil", "soup"),
            Food(21, "حمص", "100 غرام", 166, 7.9m, 14.3m, 9.6m, "hummus", "chickpea"),
            Food(22, "بطاطا مقلية", "حصة متوسطة", 365, 4, 48, 17, "french fries", "fries", "chips"),
            Food(23, "دونات", "حبة واحدة", 260, 4, 31, 14, "donut", "doughnut"),
            Food(24, "كيك", "قطعة متوسطة", 350, 5, 50, 15, "cake", "cheesecake", "trifle"),
            Food(25, "آيس كريم", "كرة واحدة", 140, 2.5m, 17, 7, "ice cream", "gelato"),
            Food(26, "شاورما دجاج", "ساندويتش", 500, 30, 45, 22, "shawarma", "wrap", "gyro"),
            Food(27, "فلافل", "3 حبات", 170, 7, 16, 9, "falafel"),
            Food(28, "بطاطا مسلوقة", "حبة متوسطة", 160, 4, 37, 0.2m, "potato", "sweet potato"),
            Food(29, "بروكلي", "كوب واحد", 55, 3.7m, 11, 0.6m, "broccoli", "cauliflower"),
            Food(30, "فراولة", "كوب واحد", 49, 1, 12, 0.5m, "strawberry", "berries")
        ]);
    }

    private static FoodItem Food(int id, string name, string serving, int calories, decimal protein, decimal carbs, decimal fat, params string[] keywords) => new()
    {
        Id = id,
        Name = name,
        ServingName = serving,
        Calories = calories,
        ProteinGrams = protein,
        CarbohydrateGrams = carbs,
        FatGrams = fat,
        RecognitionKeywords = [.. keywords]
    };

    private static AppUser NewUser(int id, string name, string email, string role) => new() { Id = id, FullName = name, Email = email, Role = role, PasswordHash = string.Empty };
    private static int NextId(IEnumerable<int> ids) => ids.DefaultIfEmpty(0).Max() + 1;

    private async Task SaveUnsafeAsync()
    {
        var tempPath = _filePath + ".tmp";
        await using (var output = File.Create(tempPath)) await JsonSerializer.SerializeAsync(output, _data, _jsonOptions);
        File.Move(tempPath, _filePath, true);
    }

    private sealed class StoreDocument
    {
        public int SchemaVersion { get; set; }
        public List<AppUser> Users { get; set; } = [];
        public List<MembershipPlan> Plans { get; set; } = [];
        public List<GymSession> Sessions { get; set; } = [];
        public List<Booking> Bookings { get; set; } = [];
        public List<Membership> Memberships { get; set; } = [];
        public List<FoodItem> FoodItems { get; set; } = [];
        public List<MealLog> MealLogs { get; set; } = [];
        public List<NutritionGoal> NutritionGoals { get; set; } = [];
    }
}
