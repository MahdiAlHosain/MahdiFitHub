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
                    await SaveUnsafeAsync();
                }
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
        _data.SchemaVersion = 2;
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
    }

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
    }
}
