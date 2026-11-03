// Webhook editor (.docs/pages/webhooks.md): "Add header" adds a row from #webhook-header-template, × removes one, and
// the inputs are renumbered (Headers[0].Key, Headers[0].Value, ...) so they bind in order. Without JavaScript the
// filled-in rows plus one empty row are shown and saved the same way.
(function () {
    "use strict";

    var list = document.querySelector("[data-webhook-headers]");
    var add = document.querySelector("[data-webhook-header-add]");
    var template = document.getElementById("webhook-header-template");
    if (!list || !add || !template) { return; }
    var max = parseInt(list.getAttribute("data-max"), 10) || 20;

    function rows() { return list.querySelectorAll("[data-webhook-header]"); }

    function renumber() {
        Array.prototype.forEach.call(rows(), function (row, index) {
            var inputs = row.querySelectorAll("input");
            var labels = row.querySelectorAll("label");
            ["Key", "Value"].forEach(function (part, i) {
                var id = "Headers_" + index + "__" + part;
                inputs[i].name = "Headers[" + index + "]." + part;
                inputs[i].id = id;
                labels[i].setAttribute("for", id);
            });
        });
        add.disabled = rows().length >= max;
    }

    add.addEventListener("click", function () {
        if (rows().length >= max) { return; }
        list.appendChild(template.content.cloneNode(true));
        renumber();
        var last = rows()[rows().length - 1];
        last.querySelector("input").focus();
    });

    list.addEventListener("click", function (event) {
        var remove = event.target.closest && event.target.closest("[data-webhook-header-remove]");
        if (!remove) { return; }
        var row = remove.closest("[data-webhook-header]");
        if (rows().length === 1) {
            // Keep one row to type into; just empty it.
            Array.prototype.forEach.call(row.querySelectorAll("input"), function (input) { input.value = ""; });
        } else {
            row.remove();
        }
        renumber();
    });

    renumber();
})();
