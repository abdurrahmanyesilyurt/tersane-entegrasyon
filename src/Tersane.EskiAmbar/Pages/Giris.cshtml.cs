using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Tersane.EskiAmbar.Pages;

public sealed class GirisModel(LegacyUsers users) : PageModel
{
    [BindProperty(Name = "txtKullaniciAdi")] public string? KullaniciAdi { get; set; }
    [BindProperty(Name = "txtSifre")] public string? Sifre { get; set; }
    public string? Hata { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(KullaniciAdi) || string.IsNullOrEmpty(Sifre) || !users.IsValid(KullaniciAdi.Trim(), Sifre))
        {
            Hata = "Kullanıcı adı veya şifre hatalı!";
            return Page();
        }

        await HttpContext.SignInAsync(LegacyUsers.Principal(KullaniciAdi.Trim()));
        return LocalRedirect(returnUrl is not null && Url.IsLocalUrl(returnUrl) ? returnUrl : "/AmbarGiris.aspx");
    }
}
