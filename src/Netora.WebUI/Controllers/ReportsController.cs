using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Operations;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

public sealed class ReportsController(INetoraApiClient api) : Controller
{
    [HttpGet("/reports")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(ReportType type = ReportType.CustomerStatus,
        ReportRange range = ReportRange.CurrentMonth, string? format = null, CancellationToken ct = default)
    {
        if (!ModelState.IsValid || !Enum.IsDefined(type) || !Enum.IsDefined(range))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            ViewData["Error"] = "Choose a valid report and date range.";
            return View(new OperationsReportDto(ReportType.CustomerStatus, ReportRange.CurrentMonth, []));
        }
        var result = await api.GetOperationsReportAsync(type, range, ct);
        if (result.Data is not { } report)
        {
            ViewData["Error"] = result.Error?.Message ?? "The report could not be loaded.";
            return View(new OperationsReportDto(type, range, []));
        }
        if (format == "csv")
            return File(Encoding.UTF8.GetBytes(ToCsv(report)), "text/csv; charset=utf-8", $"netora-{type}-{range}.csv");
        return View(report);
    }

    internal static string ToCsv(OperationsReportDto report)
    {
        var csv = new StringBuilder("Metric,Count\r\n");
        foreach (var metric in report.Metrics)
        {
            var name = metric.Name;
            if (name.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || name.FirstOrDefault() is '\t' or '\r' or '\n')
                name = "'" + name;
            csv.Append('"').Append(name.Replace("\"", "\"\"")).Append("\",")
                .Append(metric.Count.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }
        return csv.ToString();
    }
}
