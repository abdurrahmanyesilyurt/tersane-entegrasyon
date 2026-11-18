using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Tersane.EskiAmbar.Pages;

public sealed class FisListesiModel(IFisRepository repository) : PageModel
{
    public IReadOnlyList<AmbarGirisFisi> Fisler { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => Fisler = await repository.ListAsync(200, ct);
}
