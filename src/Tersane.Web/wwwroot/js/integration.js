/* Integration monitor: live KPIs (polling) and the mock ERP chaos switches. */
(function ($) {
    'use strict';

    var $status = $('#chaos-status');

    function render(summary) {
        $.each(summary.health, function (key, value) {
            $('[data-k="' + key + '"]').text(value === null ? '—' : value);
        });
        $('#kpi-failed').toggleClass('is-err', summary.health.failedCount > 0).toggleClass('is-ok', summary.health.failedCount === 0);

        if (summary.chaos) {
            $('#chaos-outage').prop('checked', summary.chaos.outage);
            $('#chaos-rate').val(summary.chaos.failureRate);
            $('#chaos-rate-out').text('%' + Math.round(summary.chaos.failureRate * 100));
            $('#chaos-latency').val(String(summary.chaos.latencyMs));
            $status.text(summary.chaos.outage ? 'ERP şu anda KAPALI (simülasyon)' : '');
        } else {
            $status.text('Sahte ERP\'ye ulaşılamıyor.');
        }
    }

    function poll() {
        $.getJSON('/entegrasyon/ozet').done(render).always(function () { setTimeout(poll, 5000); });
    }

    function sendChaos() {
        var data = {
            outage: $('#chaos-outage').is(':checked'),
            failurePercent: Math.round($('#chaos-rate').val() * 100),
            latencyMs: $('#chaos-latency').val()
        };
        window.tersane.post('/entegrasyon/erp-simulasyon', data).done(function (res) {
            if (res.chaos) render({ health: {}, chaos: res.chaos });
            window.tersane.toast(res.ok ? 'ERP simülasyonu güncellendi.' : 'Sahte ERP\'ye ulaşılamadı.', res.ok);
        });
    }

    $('#chaos-outage, #chaos-latency').on('change', sendChaos);
    $('#chaos-rate').on('input', function () { $('#chaos-rate-out').text('%' + Math.round(this.value * 100)); })
                    .on('change', sendChaos);

    poll();
})(jQuery);
