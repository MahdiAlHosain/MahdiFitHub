using MahdiFitHub.Data;
using MahdiFitHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MahdiFitHub.Controllers;

[Authorize(Roles = UserRoles.Admin)]
public sealed class PlansController(GymStore store) : Controller
{
    public async Task<IActionResult> Index() => View(await store.GetPlansAsync());

    [HttpGet]
    public IActionResult Create() => View(new CreatePlanViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreatePlanViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        await store.AddPlanAsync(new MembershipPlan
        {
            Name = model.Name.Trim(),
            DurationDays = model.DurationDays,
            Price = model.Price,
            WeeklyVisitLimit = model.WeeklyVisitLimit
        });
        TempData["Success"] = "تمت إضافة الباقة.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        if (!await store.TogglePlanAsync(id))
        {
            return NotFound();
        }
        return RedirectToAction(nameof(Index));
    }
}
