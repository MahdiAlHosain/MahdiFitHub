using System.Security.Claims;
using MahdiFitHub.Data;
using MahdiFitHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MahdiFitHub.Controllers;

[Authorize]
public sealed class NutritionController(GymStore store, IWebHostEnvironment environment) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View(await store.GetNutritionDashboardAsync(CurrentUserId()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMeal(AddMealViewModel model)
    {
        var food = await store.GetFoodItemAsync(model.FoodItemId);
        if (food is null)
        {
            TempData["Error"] = "اختر الطعام الذي ظهر في نتيجة التحليل أولاً.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "تحقق من نوع الوجبة والكمية ثم حاول مجدداً.";
            return RedirectToAction(nameof(Index));
        }

        string? imagePath = null;
        if (model.Image is { Length: > 0 })
        {
            if (model.Image.Length > 5 * 1024 * 1024)
            {
                TempData["Error"] = "حجم الصورة أكبر من 5 ميغابايت.";
                return RedirectToAction(nameof(Index));
            }

            var extension = model.Image.ContentType.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => null
            };
            if (extension is null)
            {
                TempData["Error"] = "صيغة الصورة غير مدعومة. استخدم JPG أو PNG أو WebP.";
                return RedirectToAction(nameof(Index));
            }

            var uploadDirectory = Path.Combine(environment.WebRootPath, "uploads", "meals");
            Directory.CreateDirectory(uploadDirectory);
            var fileName = $"meal-{CurrentUserId()}-{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(uploadDirectory, fileName);
            await using var output = System.IO.File.Create(fullPath);
            await model.Image.CopyToAsync(output);
            imagePath = $"/uploads/meals/{fileName}";
        }

        var quantity = model.ServingCount;
        await store.AddMealAsync(new MealLog
        {
            MemberId = CurrentUserId(),
            FoodItemId = food.Id,
            FoodName = food.Name,
            ServingName = food.ServingName,
            MealType = model.MealType.Trim(),
            ServingCount = quantity,
            Calories = (int)Math.Round(food.Calories * quantity),
            ProteinGrams = Math.Round(food.ProteinGrams * quantity, 1),
            CarbohydrateGrams = Math.Round(food.CarbohydrateGrams * quantity, 1),
            FatGrams = Math.Round(food.FatGrams * quantity, 1),
            ImagePath = imagePath,
            DetectedLabel = string.IsNullOrWhiteSpace(model.DetectedLabel) ? null : model.DetectedLabel.Trim(),
            LoggedAtUtc = DateTime.UtcNow
        });

        TempData["Success"] = $"تمت إضافة {food.Name} إلى سجل اليوم.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateGoal(UpdateNutritionGoalViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "تحقق من هدف السعرات والبروتين.";
            return RedirectToAction(nameof(Index));
        }

        await store.UpdateNutritionGoalAsync(CurrentUserId(), model.DailyCalories, model.DailyProteinGrams);
        TempData["Success"] = "تم تحديث هدفك الغذائي اليومي.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMeal(int id)
    {
        var imagePath = await store.DeleteMealAsync(id, CurrentUserId());
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            var fileName = Path.GetFileName(imagePath);
            var fullPath = Path.Combine(environment.WebRootPath, "uploads", "meals", fileName);
            if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
        }

        TempData["Success"] = "تم حذف الوجبة من سجل اليوم.";
        return RedirectToAction(nameof(Index));
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
