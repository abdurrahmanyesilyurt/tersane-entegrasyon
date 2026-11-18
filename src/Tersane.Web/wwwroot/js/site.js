/* Shared jQuery behaviour for the internal app. */
(function ($) {
    'use strict';

    // ── Anti-forgery token for AJAX POSTs (MVC validates it on every POST) ─────────────
    function token() {
        return $('input[name="__RequestVerificationToken"]').first().val();
    }

    window.tersane = {
        post: function (url, data) {
            return $.ajax({
                url: url,
                type: 'POST',
                data: data || {},
                headers: { 'RequestVerificationToken': token(), 'X-Requested-With': 'XMLHttpRequest' }
            });
        },
        toast: function (message, ok) {
            var $msg = $('<div class="flash-msg" role="status">').addClass(ok === false ? 'flash-err' : 'flash-ok').text(message);
            $('.topbar').after($msg);
            setTimeout(function () { $msg.fadeOut(400, function () { $msg.remove(); }); }, 5000);
        },
        // Turkish decimals: "1.250,5" → 1250.5
        parseDecimal: function (text) {
            if (text === null || text === undefined) return NaN;
            var s = String(text).trim().replace(/\s/g, '');
            if (s === '') return NaN;
            if (s.indexOf(',') >= 0) s = s.replace(/\./g, '').replace(',', '.');
            return /^-?\d+(\.\d+)?$/.test(s) ? parseFloat(s) : NaN;
        },
        formatQty: function (n) {
            return Number(n).toLocaleString('tr-TR', { maximumFractionDigits: 3 });
        }
    };

    // ── Filters that submit on change ───────────────────────────────────────────────
    $(document).on('change', '.js-autosubmit', function () { $(this).closest('form').trigger('submit'); });

    // ── Dependent dropdowns: project → blocks → work orders ─────────────────────────
    function fill($select, items, placeholder) {
        var selected = String($select.data('selected') || $select.val() || '');
        $select.empty().append($('<option>').val('').text(placeholder));
        $.each(items, function (_, item) {
            $select.append($('<option>').val(item.id).text(item.text).prop('selected', String(item.id) === selected));
        });
        $select.data('selected', '');
    }

    function loadWorkOrders() {
        var projectId = $('.js-project').val();
        var $wo = $('.js-workorders');
        if (!$wo.length) return;
        if (!projectId) { fill($wo, [], '(isteğe bağlı)'); return; }
        $.getJSON('/api/projeler/' + projectId + '/is-emirleri', { blockId: $('.js-blocks').val() || undefined })
            .done(function (items) { fill($wo, items, '(isteğe bağlı)'); });
    }

    $(document).on('change', '.js-project', function () {
        var projectId = $(this).val();
        var $blocks = $('.js-blocks');
        if (!projectId) { fill($blocks, [], '(projeye bağlı)'); loadWorkOrders(); return; }
        $blocks.prop('disabled', true);
        $.getJSON('/api/projeler/' + projectId + '/bloklar')
            .done(function (items) { fill($blocks, items, '(blok seçilmedi)'); })
            .always(function () { $blocks.prop('disabled', false); loadWorkOrders(); });
    });
    $(document).on('change', '.js-blocks', loadWorkOrders);

    // ── "ERP'ye gönder" (calls dbo.usp_MaterialRequest_Submit) ──────────────────────
    $(document).on('click', '.js-submit', function () {
        var $btn = $(this);
        if (!window.confirm($btn.data('no') + ' talebi onaylanıp ERP\'ye gönderilecek. Devam edilsin mi?')) return;
        $btn.prop('disabled', true).text('Gönderiliyor…'); // double-click guard (the procedure is idempotent too)
        window.tersane.post($btn.data('url'))
            .done(function (res) {
                window.tersane.toast(res.message, res.ok);
                if (!res.ok) { $btn.prop('disabled', false).text('ERP\'ye gönder'); return; }
                if ($btn.data('reload')) { setTimeout(function () { location.reload(); }, 600); return; }
                $btn.closest('tr').addClass('flash').find('.js-status').html('<span class="pill st-info">ERP kuyruğunda</span>');
                $btn.remove();
            })
            .fail(function () {
                window.tersane.toast('Sunucuya ulaşılamadı, tekrar deneyin.', false);
                $btn.prop('disabled', false).text('ERP\'ye gönder');
            });
    });

    // ── "Yeniden işle" (calls integration.usp_Outbox_Requeue) ───────────────────────
    $(document).on('click', '.js-requeue', function () {
        var $btn = $(this);
        $btn.prop('disabled', true);
        window.tersane.post($btn.data('url'))
            .done(function (res) {
                window.tersane.toast(res.message, res.ok);
                if ($btn.data('reload')) { setTimeout(function () { location.reload(); }, 800); return; }
                if (res.ok) {
                    $btn.closest('tr').removeClass('row-err').addClass('flash')
                        .find('.js-status').html('<span class="pill st-info">Bekliyor</span>');
                    $btn.remove();
                } else {
                    $btn.prop('disabled', false);
                }
            })
            .fail(function () { $btn.prop('disabled', false); window.tersane.toast('İşlem başarısız.', false); });
    });

    // ── Log detail modal (request/response bodies) ──────────────────────────────────
    function pretty(text) {
        if (!text) return '—';
        try { return JSON.stringify(JSON.parse(text), null, 2); } catch (e) { return text; }
    }

    $(document).on('click', '.js-log-detail', function () {
        $.getJSON('/entegrasyon/log/' + $(this).data('id')).done(function (log) {
            $('#log-url').text((log.httpStatus || '') + ' ' + (log.url || log.operation));
            $('#log-request').text(pretty(log.requestBody));
            $('#log-response').text(pretty(log.responseBody));
            bootstrap.Modal.getOrCreateInstance(document.getElementById('log-modal')).show();
        });
    });

    // ── Client-side table search + level filter (stock page) ────────────────────────
    function applyTableFilter() {
        var $input = $('.js-table-filter');
        if (!$input.length) return;
        var term = $input.val().toLocaleLowerCase('tr-TR');
        var level = $('.js-level.active').data('level') || '';
        $($input.data('target')).find('tbody tr[data-level]').each(function () {
            var $row = $(this);
            var matches = $row.text().toLocaleLowerCase('tr-TR').indexOf(term) >= 0;
            var levelOk = !level || $row.data('level') === level;
            $row.toggle(matches && levelOk);
        });
    }

    $(document).on('input', '.js-table-filter', applyTableFilter);
    $(document).on('click', '.js-level', function () {
        $('.js-level').removeClass('active');
        $(this).addClass('active');
        applyTableFilter();
    });

    // ── Work order status change from the list ──────────────────────────────────────
    $(document).on('change', '.js-wo-status', function () {
        var $sel = $(this);
        window.tersane.post('/is-emirleri/' + $sel.data('id') + '/durum', { status: $sel.val() })
            .done(function () { $sel.closest('tr').addClass('flash'); })
            .fail(function () { window.tersane.toast('Durum güncellenemedi.', false); });
    });

    // ── Pages that follow a running process reload themselves ───────────────────────
    $(function () {
        var seconds = parseInt($('[data-auto-refresh]').data('auto-refresh'), 10);
        if (seconds > 0) setTimeout(function () { location.reload(); }, seconds * 1000);

        // Load dependent dropdowns when a form opens with a project already chosen.
        if ($('.js-project').val()) $('.js-project').trigger('change');
    });
})(jQuery);
