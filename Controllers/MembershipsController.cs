using MahdiFitHub.Data;
using MahdiFitHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MahdiFitHub.Controllers;

[Authorize(Roles = UserRoles.Admin)]
public sealed class MembershipsController(GymStore store) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(await store.GetMembershipsAsync());

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        return View(new CreateMembershipViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateMembershipViewModel model)
    {
        if (model.StartDate.Date < DateTime.Today)
            ModelState.AddModelError(nameof(model.StartDate), "لا يمكن أن يبدأ الاشتراك بتاريخ سابق.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model.MemberId, model.PlanId);
            return View(model);
        }

        var result = await store.AddMembershipAsync(model.MemberId, model.PlanId, model.StartDate);
        if (result != "ok")
        {
            ModelState.AddModelError(string.Empty, result switch
            {
                "already-active" => "لدى هذا العضو اشتراك فعال بالفعل.",
                "member-missing" => "العضو المحدد غير متاح.",
                _ => "خطة الاشتراك المحددة غير متاحة."
            });
            await PopulateDropdownsAsync(model.MemberId, model.PlanId);
            return View(model);
        }

        TempData["Success"] = "تم إنشاء العضوية بنجاح.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var cancelled = await store.CancelMembershipAsync(id);
        TempData[cancelled ? "Success" : "Error"] = cancelled ? "تم إلغاء العضوية." : "تعذر إلغاء العضوية.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Renew(int id)
    {
        var result = await store.RenewMembershipAsync(id);
        if (result == "ok") TempData["Success"] = "تم تجديد العضوية بنجاح.";
        else TempData["Error"] = result == "plan-missing" ? "الخطة المرتبطة غير فعالة." : "تعذر تجديد العضوية.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdownsAsync(int? memberId = null, int? planId = null)
    {
        ViewBag.Members = new SelectList(await store.GetActiveMembersAsync(), nameof(AppUser.Id), nameof(AppUser.FullName), memberId);
        ViewBag.Plans = new SelectList(await store.GetPlansAsync(true), nameof(MembershipPlan.Id), nameof(MembershipPlan.Name), planId);
    }
}
