// The move dialog (Shared/_MoveDialog.cshtml, .docs/pages/media.md#move-dialog): a tree of places with Root first, for
// Media and Pages. The screen's script opens it with what is moving. Everything inside the moved items and the place they
// already are can't be chosen; the button waits for a choice. The radios and the button belong to the screen's form.
(function () {
    "use strict";

    window.dnfMoveDialog = function (dialog) {
        var options = Array.prototype.slice.call(dialog.querySelectorAll("[data-move-option]"));
        var submit = dialog.querySelector("[data-move-submit]");
        var search = dialog.querySelector("[data-move-search]");
        var subject = dialog.querySelector("[data-move-subject]");
        var empty = dialog.querySelector("[data-move-empty]");
        var ownForm = submit.getAttribute("form");

        options.forEach(function (option) {
            // Indented here: the CSP allows no style attribute in the markup.
            option.style.paddingInlineStart = "calc(var(--dnf-space-2) + " + (parseInt(option.getAttribute("data-depth") || "0", 10) * 1.25) + "rem)";
            option.querySelector("input").addEventListener("change", function () { submit.disabled = false; });
        });

        function filter() {
            var query = search ? search.value.trim().toLowerCase() : "";
            var found = 0;
            options.forEach(function (option) {
                option.hidden = query !== "" && (option.getAttribute("data-search") || "").toLowerCase().indexOf(query) === -1;
                if (!option.hidden) { found++; }
            });
            empty.hidden = found > 0;
        }
        if (search) { search.addEventListener("input", filter); }

        return {
            // moving: the ids that move; current: where they are ("" = Root); text: says what moves; form: the form
            // the choice posts with, when it isn't the dialog's own.
            open: function (moving, current, text, form) {
                options.forEach(function (option) {
                    var id = option.getAttribute("data-move-option");
                    var inside = (option.getAttribute("data-ancestors") || "").split(" ").concat(id).some(function (place) { return moving.indexOf(place) !== -1; });
                    var input = option.querySelector("input");
                    input.checked = false;
                    input.setAttribute("form", form || ownForm);
                    input.disabled = inside || id === current;
                    option.querySelector("[data-move-note]").textContent = inside ? "Can't go inside itself" : id === current ? "Already here" : "";
                });
                submit.disabled = true;
                submit.setAttribute("form", form || ownForm);
                subject.textContent = text;
                if (search) { search.value = ""; }
                filter();
                dialog.showModal();
                // The dialog itself, not the search box: a phone's keyboard stays closed until it is wanted.
                dialog.focus();
            }
        };
    };
})();
