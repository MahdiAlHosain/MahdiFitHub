using MahdiFitHub.Data;
using MahdiFitHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MahdiFitHub.Controllers;

[Authorize(Roles = UserRoles.Admin)]
public sealed class PeopleController(
    GymStore store,
    IPasswordHasher<AppUser> hasher) : Controller
{
    public async Task<IActionResult> Index(string? role = null)
    {
        if (role is not (UserRoles.Admin or UserRoles.Trainer or UserRoles.Member)) role = null;
        ViewBag.Role = role;
        return View(await store.GetUsersAsync(role));
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulatePlansAsync();
        return View(new CreateUserViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        if (model.Role is not (UserRoles.Admin or UserRoles.Trainer or UserRoles.Member))
        {
            ModelState.AddModelError(nameof(model.Role), "الدور المحدد غير صالح.");
        }

        var normalizedEmail = model.Email.Trim().ToLowerInvariant();
        if (await store.EmailExistsAsync(normalizedEmail))
        {
            ModelState.AddModelError(nameof(model.Email), "البريد مستخدم مسبقًا.");
        }

        if (!ModelState.IsValid)
        {
            await PopulatePlansAsync(model.MembershipPlanId);
            return View(model);
        }

        var user = new AppUser
        {
            FullName = model.FullName.Trim(),
            Email = normalizedEmail,
            Phone = model.Phone?.Trim(),
            Role = model.Role,
            MembershipPlanId = model.Role == UserRoles.Member ? model.MembershipPlanId : null,
            PasswordHash = string.Empty
        };
        user.PasswordHash = hasher.HashPassword(user, model.Password);
        await store.AddUserAsync(user);
        TempData["Success"] = "تم إنشاء الحساب بنجاح.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        if (!await store.ToggleUserAsync(id))
        {
            return NotFound();
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulatePlansAsync(int? selected = null)
    {
        ViewBag.Plans = new SelectList(
            await store.GetPlansAsync(onlyActive: true),
            nameof(MembershipPlan.Id), nameof(MembershipPlan.Name), selected);
    }
}
