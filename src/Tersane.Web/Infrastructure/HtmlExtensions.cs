using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Tersane.Data;

namespace Tersane.Web.Infrastructure;

public static class HtmlExtensions
{
    /// <summary>Renders a coloured status pill: @Html.Pill(Ui.Status(x.Status)).</summary>
    public static IHtmlContent Pill(this IHtmlHelper _, (string Label, string Css) status)
        => new HtmlString($"<span class=\"pill {status.Css}\">{HtmlEncoder.Default.Encode(status.Label)}</span>");

    /// <summary>Adds "active" to a sidebar link when the current path starts with it.</summary>
    public static string ActiveIf(this IHtmlHelper html, string pathPrefix)
    {
        var path = html.ViewContext.HttpContext.Request.Path.Value ?? "/";
        var active = pathPrefix == "/" ? path == "/" : path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase);
        return active ? "active" : "";
    }
}

/// <summary>Sidebar badge with the number of failed integration messages (a view component runs its own query).</summary>
public sealed class IntegrationBadgeViewComponent(TersaneDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var failed = await db.OutboxMessages.CountAsync(m => m.Status == Data.Entities.OutboxStatus.Failed);
        return failed == 0
            ? Content("")
            : new Microsoft.AspNetCore.Mvc.ViewComponents.HtmlContentViewComponentResult(
                new HtmlString($"<span class=\"pill st-err nav-badge\" title=\"Hatalı entegrasyon mesajı\">{failed}</span>"));
    }
}
