/* jQuery Validation for Turkish input: "12,5" and "1.250,5" are valid numbers.
   The server binds decimals with the tr-TR culture, so the client must agree with it.
   Loaded after jquery.validate (see _ValidationScriptsPartial). */
(function ($) {
    'use strict';
    $.validator.methods.number = function (value, element) {
        return this.optional(element) || !isNaN(window.tersane.parseDecimal(value));
    };
    $.validator.methods.range = function (value, element, param) {
        var n = window.tersane.parseDecimal(value);
        return this.optional(element) || (n >= param[0] && n <= param[1]);
    };
})(jQuery);
