// Ambar Giriş Fişi — eski usul jQuery. RPA botunun beklemesi gereken her şey burada:
// AJAX ile dolan raf listesi, blur'da malzeme sorgusu, alert() ile zorunlu alan uyarısı, confirm() ile kayıt onayı.
$(function () {
    var $depo = $('#ctl00_cph_ddlDepo');
    var $raf = $('#ctl00_cph_ddlRaf');
    var $kod = $('#ctl00_cph_txtMalzemeKodu');

    function raflariYukle() {
        var depo = $depo.val();
        var secili = $raf.attr('data-secili') || '';
        $raf.prop('disabled', true).html('<option value="">Yükleniyor...</option>');
        $('#lblRafDurum').text('');
        if (!depo) {
            $raf.html('<option value="">-- Önce depo seçiniz --</option>');
            return;
        }
        $('#imgRafYukleniyor').show();
        $.getJSON('/Servis/Raflar.ashx', { depo: depo }, function (raflar) {
            $raf.html('<option value="">-- Seçiniz --</option>');
            $.each(raflar, function (i, r) {
                $raf.append($('<option>').val(r).text(r).prop('selected', r === secili));
            });
            $raf.prop('disabled', false).attr('data-secili', '');
            $('#lblRafDurum').text(raflar.length + ' raf');
        }).always(function () { $('#imgRafYukleniyor').hide(); });
    }

    function malzemeSorgula() {
        var kod = $.trim($kod.val());
        $('#ctl00_cph_lblMalzemeHata').text('');
        $('#ctl00_cph_txtMalzemeAdi').val('');
        $('#ctl00_cph_lblBirim').text('');
        if (!kod) return;
        $('#ctl00_cph_txtMalzemeAdi').val('Sorgulanıyor...');
        $.getJSON('/Servis/MalzemeBilgi.ashx', { kod: kod }, function (m) {
            if (m.bulundu) {
                $('#ctl00_cph_txtMalzemeAdi').val(m.ad);
                $('#ctl00_cph_lblBirim').text(m.birim);
            } else {
                $('#ctl00_cph_txtMalzemeAdi').val('');
                $('#ctl00_cph_lblMalzemeHata').text('Malzeme bulunamadı!');
            }
        });
    }

    $depo.change(raflariYukle);
    $kod.blur(malzemeSorgula);

    $('#ctl00_cph_btnKaydet').click(function () {
        var eksik = [];
        if (!$.trim($('#ctl00_cph_txtFisTarihi').val())) eksik.push('Fiş Tarihi');
        if (!$depo.val()) eksik.push('Depo');
        if (!$raf.val()) eksik.push('Raf');
        if (!$.trim($('#ctl00_cph_txtTedarikci').val())) eksik.push('Tedarikçi');
        if (!$.trim($('#ctl00_cph_txtIrsaliyeNo').val())) eksik.push('İrsaliye No');
        if (!$.trim($kod.val())) eksik.push('Malzeme Kodu');
        if (!$.trim($('#ctl00_cph_txtMiktar').val())) eksik.push('Miktar');
        if (eksik.length > 0) {
            alert('Lütfen zorunlu alanları doldurunuz:\n- ' + eksik.join('\n- '));
            return;
        }
        if (confirm('Fiş kaydedilsin mi?')) {
            $(this).val('Kaydediliyor...').prop('disabled', true);
            $('#aspnetForm').submit();
        }
    });

    if ($depo.val()) raflariYukle();
    if ($kod.val()) malzemeSorgula();
});
