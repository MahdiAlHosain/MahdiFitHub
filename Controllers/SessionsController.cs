using System.Security.Claims;
using MahdiFitHub.Data;
using MahdiFitHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MahdiFitHub.Controllers;

[Authorize]
public sealed class SessionsController(GymStore store) : Controller
{
    public async Task<IActionResult> Index()
    {
        var userId = CurrentUserId();
        ViewBag.BookedSessionIds = await store.GetBookedSessionIdsAsync(userId);
        return View(await store.GetSessionsAsync());
    }

    [Authorize(Roles = UserRoles.Staff)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateTrainersAsync();
        return View(new CreateSessionViewModel());
    }

    [Authorize(Roles = UserRoles.Staff)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateSessionViewModel model)
    {
        if (!await store.TrainerExistsAsync(model.TrainerId))
        {
            ModelState.AddModelError(nameof(model.TrainerId), "المدرب المحدد غير صالح.");
        }

        if (model.StartsAtLocal <= DateTime.Now)
        {
            ModelState.AddModelError(nameof(model.StartsAtLocal), "اختر موعدًا مستقبليًا.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateTrainersAsync(model.TrainerId);
            return View(model);
        }

        await store.AddSessionAsync(new GymSession
        {
            Title = model.Title.Trim(),
            Description = model.Description.Trim(),
            Room = model.Room.Trim(),
            StartsAtUtc = model.StartsAtLocal.ToUniversalTime(),
            DurationMinutes = model.DurationMinutes,
            Capacity = model.Capacity,
            TrainerId = model.TrainerId
        });
        TempData["Success"] = "تمت جدولة الحصة.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = UserRoles.Member)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Book(int id)
    {
        var memberId = CurrentUserId();
        var result = await store.BookAsync(id, memberId);
        if (result == "missing") return NotFound();
        if (result == "duplicate")
        {
            TempData["Error"] = "أنت مسجل في هذه الحصة مسبقًا.";
        }
        else if (result == "full")
        {
            TempData["Error"] = "اكتمل عدد المقاعد في هذه الحصة.";
        }
        else
        {
            TempData["Success"] = "تم حجز مقعدك.";
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = UserRoles.Member)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var memberId = CurrentUserId();
        await store.CancelBookingAsync(id, memberId);
        TempData["Success"] = "تم إلغاء الحجز.";

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await store.DeleteSessionAsync(id);
        return RedirectToAction(nameof(Index));
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task PopulateTrainersAsync(int? selected = null)
    {
        ViewBag.Trainers = new SelectList(
            await store.GetActiveTrainersAsync(),
            nameof(AppUser.Id), nameof(AppUser.FullName), selected);
    }
}
