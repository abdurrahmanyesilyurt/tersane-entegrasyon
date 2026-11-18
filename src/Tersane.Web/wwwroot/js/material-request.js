/* Material request form: dynamic lines with a material picker that shows live stock. */
(function ($) {
    'use strict';

    var materials = {};
    var $form = $('#request-form');
    var $lines = $('#lines');

    // Rename inputs to Lines[0].MaterialId, Lines[1].MaterialId… so MVC model binding sees a list,
    // then re-parse unobtrusive validation for the new elements.
    function reindex() {
        $lines.find('tr.line').each(function (i) {
            var $row = $(this);
            $row.find('.line-no').text(i + 1);
            $row.find('[name], [data-name]').each(function () {
                var $el = $(this);
                var field = $el.data('name') || $el.attr('name').replace(/^Lines\[\d+\]\./, '');
                $el.attr('name', 'Lines[' + i + '].' + field).data('name', field);
            });
            $row.find('[data-valmsg-for]').each(function () {
                var field = $(this).attr('data-valmsg-for').replace(/^Lines\[\d+\]\./, '');
                $(this).attr('data-valmsg-for', 'Lines[' + i + '].' + field);
            });
        });
        $form.removeData('validator').removeData('unobtrusiveValidation');
        $.validator.unobtrusive.parse($form);
    }

    function fillMaterialSelect($select) {
        var selected = String($select.data('selected') || $select.val() || '');
        var groups = {};
        $.each(materials, function (_, m) { (groups[m.category] = groups[m.category] || []).push(m); });
        $select.find('optgroup').remove();
        $.each(groups, function (category, items) {
            var $group = $('<optgroup>').attr('label', category);
            $.each(items, function (_, m) {
                var label = m.partNo + ' — ' + m.description + (m.blocked ? '  [ERP: kapalı]' : '');
                $group.append($('<option>').val(m.id).text(label).prop('selected', String(m.id) === selected));
            });
            $select.append($group);
        });
        showStock($select);
    }

    function showStock($select) {
        var $row = $select.closest('tr');
        var m = materials[$select.val()];
        var $hint = $row.find('.stock-hint').removeClass('is-blocked is-low');
        $row.find('.unit').text(m ? m.unit : '—');
        if (!m) { $hint.text(''); return; }

        if (m.blocked) {
            $hint.addClass('is-blocked').text('Bu malzeme ERP\'de satınalmaya kapalı; ERP talebi reddedecektir.');
            return;
        }
        var qty = window.tersane.parseDecimal($row.find('[name$=".Quantity"]').val());
        var text = 'Kullanılabilir: ' + window.tersane.formatQty(m.available) + ' ' + m.unit;
        if (m.onOrder > 0) text += ' · siparişte: ' + window.tersane.formatQty(m.onOrder);
        if (!isNaN(qty) && qty > m.available) { $hint.addClass('is-low'); text += ' · stok yetersiz, satınalma gerekecek'; }
        else if (m.level !== 'OK') { $hint.addClass('is-low'); }
        $hint.text(text);
    }

    $form.on('click', '.js-add-line', function () {
        var $row = $($('#line-template').html());
        $lines.append($row);
        reindex();
        fillMaterialSelect($row.find('.js-material'));
        $row.find('.js-material').trigger('focus');
    });

    $form.on('click', '.js-remove-line', function () {
        if ($lines.find('tr.line').length === 1) {
            window.tersane.toast('Talepte en az bir kalem olmalı.', false);
            return;
        }
        $(this).closest('tr').remove();
        reindex();
    });

    $form.on('change', '.js-material', function () { showStock($(this)); });
    $form.on('input', '[name$=".Quantity"]', function () { showStock($(this).closest('tr').find('.js-material')); });

    // Prevent double submit (the server is idempotent as well, but there is no need to try).
    // A disabled button does not post its value, so the chosen action goes into a hidden field.
    $form.on('click', 'button[name=submitNow]', function () { $form.data('submitNow', this.value); });
    $form.on('submit', function () {
        if ($form.valid && !$form.valid()) return;
        $form.find('input[type=hidden][name=submitNow]').remove();
        $('<input type="hidden" name="submitNow">').val($form.data('submitNow') || 'false').appendTo($form);
        $form.find('button[type=submit]').prop('disabled', true);
    });

    $.getJSON('/api/malzemeler').done(function (list) {
        $.each(list, function (_, m) { materials[m.id] = m; });
        $lines.find('.js-material').each(function () { fillMaterialSelect($(this)); });
        if (list.length === 0) {
            window.tersane.toast('Malzeme kataloğu boş: ERP senkronu henüz çalışmamış olabilir.', false);
        }
    });

    reindex();
})(jQuery);
