using MahdiFitHub.Data;
using MahdiFitHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MahdiFitHub.Controllers;

[Authorize]
public sealed class HomeController(GymStore store) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View(await store.GetDashboardAsync());
    }

    [AllowAnonymous]
    public IActionResult Error() => View();
}
