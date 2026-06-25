using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using RevitBatchAdminWeb.Services;

namespace RevitBatchAdminWeb.Controllers
{
    public class DashboardController : Controller
    {
        private readonly ApiClient _apiClient;

        public DashboardController(ApiClient apiClient)
        {
            _apiClient = apiClient;
        }

        public async Task<IActionResult> Index()
        {
            string? token = HttpContext.Session.GetString("AdminToken");

            if (string.IsNullOrWhiteSpace(token))
                return RedirectToAction("Login", "Auth");

            _apiClient.SetBearerToken(token);

            var users = await _apiClient.GetUsersAsync();
            var licenses = await _apiClient.GetLicensesAsync();

            var now = DateTime.UtcNow;

            var trialLicenses = licenses
                .Where(l => string.Equals(l.licenseType, "Trial", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var annualLicenses = licenses
                .Where(l => string.Equals(l.licenseType, "Annual", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var lifetimeLicenses = licenses
                .Where(l => string.Equals(l.licenseType, "Lifetime", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var expiredLicenses = licenses
                .Where(l => DateTime.TryParse(l.expiryDate, out var exp) && exp < now)
                .ToList();

            var activeLicenses = licenses
                .Where(l => l.isActive && !(DateTime.TryParse(l.expiryDate, out var exp) && exp < now))
                .ToList();

            int paidCount = annualLicenses.Count + lifetimeLicenses.Count;
            int totalRelevant = paidCount + trialLicenses.Count;
            double conversionRate = totalRelevant > 0
                ? Math.Round((double)paidCount / totalRelevant * 100, 1)
                : 0;

            ViewBag.TotalUsers = users.Count;
            ViewBag.TrialUsers = trialLicenses.Select(l => l.userId).Distinct().Count();
            ViewBag.ActiveLicenses = activeLicenses.Count;
            ViewBag.ExpiredLicenses = expiredLicenses.Count;
            ViewBag.AnnualLicenses = annualLicenses.Count;
            ViewBag.LifetimeLicenses = lifetimeLicenses.Count;
            ViewBag.EmailsSent = 0;
            ViewBag.TrialConversionRate = conversionRate;

            // License distribution for doughnut chart
            var distribution = licenses
                .GroupBy(l => string.IsNullOrWhiteSpace(l.licenseType) ? "Unknown" : l.licenseType)
                .Select(g => new { type = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToList();

            // Monthly activity for last 6 months (trial vs paid)
            var monthly = new List<object>();
            for (int i = 5; i >= 0; i--)
            {
                var target = now.AddMonths(-i);
                var monthStart = new DateTime(target.Year, target.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var monthEnd = monthStart.AddMonths(1);

                int trial = licenses.Count(l =>
                    string.Equals(l.licenseType, "Trial", StringComparison.OrdinalIgnoreCase) &&
                    l.activatedAt != null &&
                    DateTime.TryParse(l.activatedAt.ToString(), out var d) &&
                    d >= monthStart && d < monthEnd);

                int paid = licenses.Count(l =>
                    (string.Equals(l.licenseType, "Annual", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(l.licenseType, "Lifetime", StringComparison.OrdinalIgnoreCase)) &&
                    l.activatedAt != null &&
                    DateTime.TryParse(l.activatedAt.ToString(), out var d) &&
                    d >= monthStart && d < monthEnd);

                monthly.Add(new { month = target.ToString("MMM yy"), trial, paid });
            }

            ViewBag.DistributionJson = JsonConvert.SerializeObject(distribution);
            ViewBag.MonthlyJson = JsonConvert.SerializeObject(monthly);

            return View();
        }
    }
}
