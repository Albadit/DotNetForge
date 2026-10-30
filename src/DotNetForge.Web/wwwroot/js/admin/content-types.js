// The visual field builder of the Content types screen (.docs/pages/content-types.md). The field list lives in the
// form's FieldsJson textarea - the only thing that is posted - so the screen still works without JavaScript (edit the
// JSON). This script renders that list as editable cards: add fields from a palette, configure them, drag to reorder,
// nest fields in groups and repeaters. The server (ContentSchema) is the authority; the checks here are hints.
(function () {
    "use strict";

    var root = document.querySelector("[data-schema-builder]");
    var source = document.querySelector("[data-schema-source]");
    if (!root || !source) { return; }

    var readOnly = root.getAttribute("data-readonly") === "true";
    var relations = [];
    try { relations = JSON.parse(root.getAttribute("data-relations") || "[]"); } catch (e) { relations = []; }
    var titleSelect = document.querySelector("[data-title-field]");

    var TYPES = [
        { type: "text", label: "Text", icon: "type", hint: "A short line: a name or a title." },
        { type: "textarea", label: "Long text", icon: "align-left", hint: "Several lines of plain text." },
        { type: "richText", label: "Rich text", icon: "pilcrow", hint: "Formatted text: headings, lists, links." },
        { type: "number", label: "Number", icon: "hash", hint: "Prices, quantities, ratings." },
        { type: "boolean", label: "Yes / no", icon: "toggle", hint: "A switch: in stock, featured." },
        { type: "date", label: "Date", icon: "calendar", hint: "A day, optionally with a time." },
        { type: "select", label: "Select", icon: "list-checks", hint: "One or more options from a list." },
        { type: "email", label: "Email", icon: "at-sign", hint: "An email address." },
        { type: "url", label: "Link", icon: "link", hint: "A web address or a site path." },
        { type: "slug", label: "Slug", icon: "route", hint: "A URL-safe name, made from a text field." },
        { type: "image", label: "Image", icon: "image", hint: "One or more pictures from Media." },
        { type: "file", label: "File", icon: "file", hint: "Documents and downloads from Media." },
        { type: "relationship", label: "Relationship", icon: "network", hint: "Links to entries of another type." },
        { type: "array", label: "Array", icon: "list-ordered", hint: "Several values of one kind: tags, features." },
        { type: "group", label: "Group", icon: "braces", hint: "Fields kept together: an address, SEO." },
        { type: "repeater", label: "Repeater", icon: "repeat", hint: "Rows of fields: variants, team members." }
    ];
    var KEY = /^[a-z][A-Za-z0-9_]{0,49}$/;
    var MAX_DEPTH = 3;

    var fields = [];
    var open = {};
    var dragged = null;

    function typeInfo(type) { return TYPES.filter(function (t) { return t.type === type; })[0] || TYPES[0]; }
    function icon(name, size) { return window.dnfUI.icon(name, size); }
    function esc(value) {
        return String(value == null ? "" : value).replace(/[&<>"']/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[c]; });
    }
    function newId() {
        var bytes = new Uint8Array(6);
        (window.crypto || window.msCrypto).getRandomValues(bytes);
        return Array.prototype.map.call(bytes, function (b) { return ("0" + b.toString(16)).slice(-2); }).join("");
    }
    function el(tag, attrs, html) {
        var node = document.createElement(tag);
        Object.keys(attrs || {}).forEach(function (k) {
            if (attrs[k] === false || attrs[k] == null) { return; }
            node.setAttribute(k, attrs[k] === true ? "" : attrs[k]);
        });
        if (html != null) { node.innerHTML = html; }
        return node;
    }

    /** "Featured image" → "featuredImage", unique among the siblings. */
    function keyFrom(label, siblings, self) {
        var words = String(label).normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^A-Za-z0-9]+/g, " ").trim().split(" ").filter(Boolean);
        var key = words.map(function (w, i) { return i === 0 ? w.toLowerCase() : w.charAt(0).toUpperCase() + w.slice(1).toLowerCase(); }).join("");
        if (!/^[a-z]/.test(key)) { key = "field" + key.charAt(0).toUpperCase() + key.slice(1); }
        key = key.slice(0, 44);
        var candidate = key;
        for (var n = 2; siblings.some(function (f) { return f !== self && f.key === candidate; }); n++) {
            candidate = key + n;
        }
        return candidate;
    }

    function keyProblem(field, siblings) {
        if (!KEY.test(field.key || "")) { return "Start with a lower-case letter; use letters, digits and _."; }
        if (siblings.some(function (f) { return f !== field && (f.key || "").toLowerCase() === field.key.toLowerCase(); })) { return "Another field already uses this API name."; }
        return null;
    }

    // ---- Persist: the textarea is what the form posts ---------------------------------------------------------
    function sync() {
        source.value = JSON.stringify(fields, null, 2);
        if (titleSelect) {
            var current = titleSelect.value;
            var candidates = fields.filter(function (f) { return f.type === "text" || f.type === "slug" || f.type === "email"; });
            titleSelect.innerHTML = candidates.map(function (f) {
                return "<option value=\"" + esc(f.key) + "\"" + (f.key === current ? " selected" : "") + ">" + esc(f.label || f.key) + " (" + esc(f.key) + ")</option>";
            }).join("") || "<option value=\"\">Add a text field first</option>";
        }
    }

    function load() {
        try {
            var parsed = JSON.parse(source.value || "[]");
            if (!Array.isArray(parsed)) { throw new Error("not a list"); }
            fields = parsed.filter(function (f) { return f && typeof f === "object"; });
            fields.forEach(ensureIds);
            return true;
        } catch (e) {
            return false;
        }
    }
    function ensureIds(field) {
        if (typeof field.id !== "string" || !/^[a-z0-9]{4,32}$/.test(field.id)) { field.id = newId(); }
        if (Array.isArray(field.fields)) { field.fields.forEach(ensureIds); }
    }

    // ---- Rendering -------------------------------------------------------------------------------------------
    function render() {
        root.innerHTML = "";
        root.appendChild(renderList(fields, 1));
        if (!readOnly) { root.appendChild(addControl(fields, 1)); }
    }

    function renderList(list, depth) {
        var ol = el("ol", { "class": "dnf-field-builder__list", "data-depth": depth });
        if (list.length === 0) {
            ol.appendChild(el("li", { "class": "dnf-field-builder__empty" }, depth === 1 ? "No fields yet. Add the first one below." : "Add the fields this " + (depth > 1 ? "holds" : "type has") + "."));
        }
        list.forEach(function (field, index) { ol.appendChild(renderField(field, list, index, depth)); });
        return ol;
    }

    function badges(field) {
        var out = [];
        if (field.required) { out.push("<span class=\"dnf-badge\" data-field-badge>Required</span>"); }
        if (field.unique) { out.push("<span class=\"dnf-badge\" data-field-badge>Unique</span>"); }
        if (field.multiple) { out.push("<span class=\"dnf-badge\" data-field-badge>Many</span>"); }
        if (field.showInList) { out.push("<span class=\"dnf-badge\" data-field-badge>In list</span>"); }
        return out.join("");
    }

    function renderField(field, list, index, depth) {
        var info = typeInfo(field.type);
        var isOpen = !!open[field.id];
        var problem = keyProblem(field, list);
        var li = el("li", { "class": "dnf-field-builder__item", "data-open": isOpen ? "true" : null, "data-invalid": problem ? "true" : null, "data-field-id": field.id });
        var row = el("div", { "class": "dnf-field-builder__row" });
        if (!readOnly) {
            var grip = el("span", { "class": "dnf-field-builder__grip", "title": "Drag to reorder", "aria-hidden": "true" }, icon("grip", "sm"));
            grip.addEventListener("pointerdown", function () { li.setAttribute("draggable", "true"); });
            row.appendChild(grip);
        }
        row.appendChild(el("span", { "class": "dnf-field-builder__type-icon", "aria-hidden": "true" }, icon(info.icon, "sm")));
        var summary = el("button", { "type": "button", "class": "dnf-field-builder__summary", "aria-expanded": isOpen ? "true" : "false" },
            "<span class=\"dnf-field-builder__name\">" + esc(field.label || field.key || "Untitled") + "</span>" +
            "<code class=\"dnf-field-builder__key\">" + esc(field.key) + "</code>" +
            "<span class=\"dnf-field-builder__type-name\">" + esc(info.label) + "</span>" + badges(field));
        summary.addEventListener("click", function () { open[field.id] = !open[field.id]; render(); });
        row.appendChild(summary);
        if (!readOnly) {
            var tools = el("div", { "class": "dnf-field-builder__tools" });
            tools.appendChild(toolButton("chevron-up", "Move up", index === 0, function () { move(list, index, -1); }));
            tools.appendChild(toolButton("chevron-down", "Move down", index === list.length - 1, function () { move(list, index, 1); }));
            tools.appendChild(toolButton("copy", "Duplicate", false, function () { duplicate(list, index); }));
            tools.appendChild(toolButton("trash", "Remove", false, function () { remove(list, index); }));
            row.appendChild(tools);
        }
        li.appendChild(row);
        if (isOpen) { li.appendChild(settings(field, list, depth)); }

        // Drag and drop between siblings (the handle arms dragging, so text in the settings stays selectable).
        li.addEventListener("dragstart", function (event) {
            if (li.getAttribute("draggable") !== "true") { return; }
            event.stopPropagation();
            dragged = { list: list, field: field };
            li.setAttribute("data-dragging", "true");
            event.dataTransfer.effectAllowed = "move";
            try { event.dataTransfer.setData("text/plain", field.key || ""); } catch (e) { /* old browsers */ }
        });
        li.addEventListener("dragend", function () { li.removeAttribute("draggable"); li.removeAttribute("data-dragging"); dragged = null; render(); });
        li.addEventListener("dragover", function (event) {
            if (!dragged || dragged.list !== list || dragged.field === field) { return; }
            event.preventDefault();
            event.stopPropagation();
            var rect = li.getBoundingClientRect();
            var after = event.clientY > rect.top + rect.height / 2;
            var from = list.indexOf(dragged.field);
            var to = list.indexOf(field) + (after ? 1 : 0);
            if (from < to) { to--; }
            if (from !== to) {
                list.splice(from, 1);
                list.splice(to, 0, dragged.field);
                var siblingEl = after ? li.nextSibling : li;
                var draggedEl = li.parentNode.querySelector("[data-field-id=\"" + dragged.field.id + "\"]");
                if (draggedEl && draggedEl !== siblingEl) { li.parentNode.insertBefore(draggedEl, siblingEl); }
                sync();
            }
        });
        li.addEventListener("drop", function (event) { if (dragged) { event.preventDefault(); } });
        return li;
    }

    function toolButton(name, label, disabled, action) {
        var button = el("button", { "type": "button", "class": "dnf-button dnf-button--ghost dnf-button--sm dnf-button--icon-only", "aria-label": label, "data-tooltip": label, "disabled": disabled }, icon(name, "sm"));
        button.addEventListener("click", action);
        return button;
    }

    function move(list, index, delta) {
        var item = list.splice(index, 1)[0];
        list.splice(index + delta, 0, item);
        changed(true);
    }
    function duplicate(list, index) {
        var copy = JSON.parse(JSON.stringify(list[index]));
        (function fresh(f) { f.id = newId(); (f.fields || []).forEach(fresh); })(copy);
        copy.label = (copy.label || copy.key) + " copy";
        copy.key = keyFrom(copy.label, list, copy);
        delete copy.unique;
        list.splice(index + 1, 0, copy);
        open[copy.id] = true;
        changed(true);
    }
    function remove(list, index) {
        var field = list[index];
        var ask = window.dnfUI && window.dnfUI.confirm
            ? window.dnfUI.confirm({ title: "Remove field", text: "Remove \"" + (field.label || field.key) + "\"? Its values are removed from every entry when you save the type.", action: "Remove" })
            : Promise.resolve(window.confirm("Remove this field?"));
        ask.then(function (ok) {
            if (!ok) { return; }
            list.splice(index, 1);
            changed(true);
        });
    }
    function changed(rerender) {
        sync();
        if (rerender) { render(); }
    }

    // ---- The "add field" palette --------------------------------------------------------------------------
    function addControl(list, depth) {
        var wrap = el("div", { "class": "dnf-field-builder__add" });
        var button = el("button", { "type": "button", "class": "dnf-button dnf-button--secondary dnf-button--sm", "aria-expanded": "false" }, icon("plus", "sm") + " Add field");
        var palette = el("div", { "class": "dnf-field-builder__palette", "hidden": true, "role": "group", "aria-label": "Field types" });
        TYPES.forEach(function (t) {
            if ((t.type === "group" || t.type === "repeater") && depth >= MAX_DEPTH) { return; }
            if (t.type === "relationship" && relations.length === 0) { return; }
            var option = el("button", { "type": "button", "class": "dnf-field-builder__palette-item" },
                "<span class=\"dnf-field-builder__type-icon\" aria-hidden=\"true\">" + icon(t.icon, "sm") + "</span><span class=\"dnf-field-builder__palette-text\"><strong>" + esc(t.label) + "</strong><span class=\"dnf-field-builder__palette-hint\">" + esc(t.hint) + "</span></span>");
            option.addEventListener("click", function () { add(list, t); });
            palette.appendChild(option);
        });
        button.addEventListener("click", function () {
            palette.hidden = !palette.hidden;
            button.setAttribute("aria-expanded", palette.hidden ? "false" : "true");
            if (!palette.hidden) { palette.querySelector("button").focus(); }
        });
        palette.addEventListener("keydown", function (event) {
            if (event.key === "Escape") { palette.hidden = true; button.setAttribute("aria-expanded", "false"); button.focus(); }
        });
        wrap.appendChild(button);
        wrap.appendChild(palette);
        return wrap;
    }

    function add(list, info) {
        var label = "New " + info.label.toLowerCase() + " field";
        var field = { id: newId(), key: "", label: label, type: info.type };
        field.key = keyFrom(label, list, field);
        if (info.type === "select") { field.choices = [{ value: "option-1", label: "Option 1" }]; }
        if (info.type === "relationship" && relations.length) { field.relationTo = relations[0].id; }
        if (info.type === "group" || info.type === "repeater") {
            field.fields = [{ id: newId(), key: "title", label: "Title", type: "text" }];
        }
        field.isNew = true;
        list.push(field);
        open[field.id] = true;
        changed(true);
        var input = root.querySelector("[data-field-id=\"" + field.id + "\"] [data-focus]");
        if (input) { input.focus(); input.select(); }
    }

    // ---- Field settings ------------------------------------------------------------------------------------
    function settings(field, list, depth) {
        var panel = el("div", { "class": "dnf-field-builder__settings dnf-form-grid" });
        var t = field.type;
        var top = depth === 1;

        // Label and API name: the name follows the label until it is edited by hand (new fields only).
        var keyInput;
        panel.appendChild(input("Label", field.label || "", { focus: true, required: true, max: 100 }, function (value) {
            field.label = value;
            if (field.isNew && !field.keyEdited) {
                field.key = keyFrom(value || "field", list, field);
                keyInput.value = field.key;
            }
            refreshSummary(field, list);
        }));
        var keyField = input("API name", field.key || "", { mono: true, max: 50, hint: "How the API and content lists name the field, e.g. {name} in a link pattern." }, function (value) {
            field.key = value.trim();
            field.keyEdited = true;
            refreshSummary(field, list);
        });
        keyInput = keyField.querySelector("input");
        panel.appendChild(keyField);
        panel.appendChild(input("Help text", field.help || "", { span: true, max: 500, hint: "Shown under the field in the entry form." }, function (v) { set(field, "help", v.trim()); }));

        if (t === "text" || t === "textarea" || t === "email" || t === "url" || t === "slug" || t === "number") {
            panel.appendChild(input("Placeholder", field.placeholder || "", { max: 200 }, function (v) { set(field, "placeholder", v.trim()); }));
        }
        if (t === "text" || t === "textarea" || t === "email" || t === "url" || t === "slug" || t === "richText") {
            panel.appendChild(number("Min length", field.minLength, function (v) { set(field, "minLength", v); }));
            panel.appendChild(number("Max length", field.maxLength, function (v) { set(field, "maxLength", v); }));
        }
        if (t === "text" || t === "textarea") {
            panel.appendChild(input("Pattern", field.pattern || "", { mono: true, max: 200, hint: "A regular expression the value must match, e.g. ^[A-Z]{3}-\\d+$" }, function (v) { set(field, "pattern", v); }));
        }
        if (t === "slug") {
            var texts = list.filter(function (f) { return f.type === "text" && f !== field; });
            panel.appendChild(select("Generate from", field.slugFrom || "", [{ value: "", label: "Nothing (typed by hand)" }].concat(texts.map(function (f) { return { value: f.key, label: f.label || f.key }; })), function (v) { set(field, "slugFrom", v); }));
        }
        if (t === "number" || (t === "array" && field.itemType === "number")) {
            panel.appendChild(number("Minimum", field.min, function (v) { set(field, "min", v); }, true));
            panel.appendChild(number("Maximum", field.max, function (v) { set(field, "max", v); }, true));
        }
        if (t === "number") {
            panel.appendChild(number("Decimals shown", field.decimals, function (v) { set(field, "decimals", v); }));
            panel.appendChild(check("Whole numbers only", field.integer, function (v) { set(field, "integer", v); }));
        }
        if (t === "date") {
            panel.appendChild(check("With a time of day (UTC)", field.withTime, function (v) { set(field, "withTime", v); }));
        }
        if (t === "select") {
            panel.appendChild(choicesEditor(field));
        }
        if (t === "relationship") {
            panel.appendChild(select("Links to", field.relationTo || "", relations.map(function (r) { return { value: r.id, label: r.name }; }), function (v) { set(field, "relationTo", v); }));
        }
        if (t === "array") {
            panel.appendChild(select("Each item is", field.itemType || "text", [{ value: "text", label: "Text" }, { value: "number", label: "Number" }], function (v) { set(field, "itemType", v); render(); }));
        }
        if (t === "select" || t === "image" || t === "file" || t === "relationship") {
            panel.appendChild(check("Allow several", field.multiple, function (v) { set(field, "multiple", v); refreshSummary(field, list); render(); }));
        }
        if (t === "array" || t === "repeater" || field.multiple) {
            panel.appendChild(number(t === "repeater" ? "Min rows" : "Min items", field.minRows, function (v) { set(field, "minRows", v); }));
            panel.appendChild(number(t === "repeater" ? "Max rows" : "Max items", field.maxRows, function (v) { set(field, "maxRows", v); }));
        }

        // Default value (types with a simple value).
        if (t === "text" || t === "textarea" || t === "email" || t === "url" || t === "slug" || t === "number" || t === "date") {
            panel.appendChild(input("Default value", field["default"] == null ? "" : String(field["default"]), { max: 500 }, function (v) {
                if (v.trim() === "") { delete field["default"]; } else { field["default"] = t === "number" && !isNaN(Number(v)) ? Number(v) : v; }
                sync();
            }));
        } else if (t === "boolean") {
            panel.appendChild(check("On by default", field["default"] === true, function (v) { set(field, "default", v); }));
        } else if (t === "select" && !field.multiple) {
            panel.appendChild(select("Default value", field["default"] || "", [{ value: "", label: "None" }].concat((field.choices || []).map(function (c) { return { value: c.value, label: c.label || c.value }; })), function (v) { set(field, "default", v); }));
        }

        // Flags.
        var flags = el("div", { "class": "dnf-field-builder__flags dnf-form-grid__full" });
        if (t !== "boolean" && t !== "group") {
            flags.appendChild(check("Required (to publish)", field.required, function (v) { set(field, "required", v); refreshSummary(field, list); }));
        }
        if (top && (t === "text" || t === "email" || t === "url" || t === "slug" || t === "number")) {
            flags.appendChild(check("Unique", field.unique, function (v) { set(field, "unique", v); refreshSummary(field, list); }));
        }
        if (top && t !== "group" && t !== "repeater" && t !== "richText") {
            flags.appendChild(check("Show in entry list", field.showInList, function (v) { set(field, "showInList", v); refreshSummary(field, list); }));
        }
        flags.appendChild(check("Half width in the form", field.width === "half", function (v) { set(field, "width", v ? "half" : ""); }));
        panel.appendChild(flags);

        // Nested fields.
        if (t === "group" || t === "repeater") {
            if (!Array.isArray(field.fields)) { field.fields = []; }
            var nested = el("div", { "class": "dnf-field-builder__nested dnf-form-grid__full" });
            nested.appendChild(el("p", { "class": "dnf-field-builder__nested-title" }, t === "group" ? "Fields in this group" : "Fields of each row"));
            nested.appendChild(renderList(field.fields, depth + 1));
            if (!readOnly) { nested.appendChild(addControl(field.fields, depth + 1)); }
            panel.appendChild(nested);
        }

        if (readOnly) { panel.querySelectorAll("input, select, textarea, button").forEach(function (c) { c.disabled = true; }); }
        return panel;
    }

    function set(field, prop, value) {
        if (value === "" || value === false || value == null || (typeof value === "number" && isNaN(value))) { delete field[prop]; } else { field[prop] = value; }
        sync();
    }

    function refreshSummary(field, list) {
        var li = root.querySelector("[data-field-id=\"" + field.id + "\"]");
        if (!li) { return; }
        var summary = li.querySelector(".dnf-field-builder__summary");
        summary.querySelector(".dnf-field-builder__name").textContent = field.label || field.key || "Untitled";
        summary.querySelector(".dnf-field-builder__key").textContent = field.key;
        summary.querySelectorAll("[data-field-badge]").forEach(function (b) { b.remove(); });
        summary.insertAdjacentHTML("beforeend", badges(field));
        var problem = keyProblem(field, list);
        if (problem) { li.setAttribute("data-invalid", "true"); } else { li.removeAttribute("data-invalid"); }
        var keyHint = li.querySelector("[data-key-problem]");
        if (keyHint) { keyHint.textContent = problem || ""; keyHint.hidden = !problem; }
        sync();
    }

    var uid = 0;
    function input(label, value, opts, onInput) {
        var id = "fb-" + (++uid);
        var wrap = el("div", { "class": "dnf-field" + (opts.span ? " dnf-form-grid__full" : "") });
        wrap.appendChild(el("label", { "class": "dnf-field__label", "for": id }, esc(label)));
        var control = el("input", { id: id, type: "text", maxlength: opts.max || null, "class": opts.mono ? "dnf-input dnf-input--sm dnf-input--mono" : "dnf-input dnf-input--sm", "data-focus": opts.focus ? "" : null, autocomplete: "off", spellcheck: opts.mono ? "false" : null });
        control.value = value;
        control.addEventListener("input", function () { onInput(control.value); });
        wrap.appendChild(control);
        if (label === "API name") {
            wrap.appendChild(el("span", { "class": "dnf-field__error", "data-key-problem": "", "hidden": true }));
        }
        if (opts.hint) { wrap.appendChild(el("span", { "class": "dnf-field__description" }, esc(opts.hint))); }
        return wrap;
    }
    function number(label, value, onInput, decimals) {
        var id = "fb-" + (++uid);
        var wrap = el("div", { "class": "dnf-field" });
        wrap.appendChild(el("label", { "class": "dnf-field__label", "for": id }, esc(label)));
        var control = el("input", { id: id, type: "number", "class": "dnf-input dnf-input--sm", step: decimals ? "any" : "1", min: decimals ? null : "0" });
        if (value != null) { control.value = value; }
        control.addEventListener("input", function () { onInput(control.value === "" ? null : Number(control.value)); });
        wrap.appendChild(control);
        return wrap;
    }
    function select(label, value, options, onChange) {
        var id = "fb-" + (++uid);
        var wrap = el("div", { "class": "dnf-field" });
        wrap.appendChild(el("label", { "class": "dnf-field__label", "for": id }, esc(label)));
        var control = el("select", { id: id, "class": "dnf-select dnf-select--sm" }, options.map(function (o) {
            return "<option value=\"" + esc(o.value) + "\"" + (o.value === value ? " selected" : "") + ">" + esc(o.label) + "</option>";
        }).join(""));
        control.addEventListener("change", function () { onChange(control.value); });
        wrap.appendChild(control);
        return wrap;
    }
    function check(label, checked, onChange) {
        var wrap = el("label", { "class": "dnf-check" });
        var box = el("input", { type: "checkbox", "class": "dnf-checkbox" });
        box.checked = !!checked;
        box.addEventListener("change", function () { onChange(box.checked); });
        wrap.appendChild(box);
        wrap.appendChild(el("span", { "class": "dnf-check__text" }, esc(label)));
        return wrap;
    }

    function choicesEditor(field) {
        if (!Array.isArray(field.choices)) { field.choices = []; }
        var wrap = el("div", { "class": "dnf-field dnf-form-grid__full" });
        wrap.appendChild(el("span", { "class": "dnf-field__label" }, "Options"));
        var rows = el("div", { "class": "dnf-field-builder__options" });
        function draw() {
            rows.innerHTML = "";
            field.choices.forEach(function (choice, i) {
                var row = el("div", { "class": "dnf-field-builder__option" });
                var label = el("input", { type: "text", "class": "dnf-input dnf-input--sm", placeholder: "Label", "aria-label": "Option " + (i + 1) + " label", maxlength: "100" });
                label.value = choice.label || "";
                var value = el("input", { type: "text", "class": "dnf-input dnf-input--sm dnf-input--mono", placeholder: "value", "aria-label": "Option " + (i + 1) + " value", maxlength: "100" });
                value.value = choice.value || "";
                label.addEventListener("input", function () {
                    var auto = !choice.value || choice.value === slugValue(choice.label || "");
                    choice.label = label.value;
                    if (auto) { choice.value = slugValue(label.value); value.value = choice.value; }
                    sync();
                });
                value.addEventListener("input", function () { choice.value = value.value; sync(); });
                row.appendChild(label);
                row.appendChild(value);
                row.appendChild(toolButton("x", "Remove option", false, function () { field.choices.splice(i, 1); sync(); draw(); }));
                rows.appendChild(row);
            });
        }
        draw();
        wrap.appendChild(rows);
        var addButton = el("button", { type: "button", "class": "dnf-button dnf-button--ghost dnf-button--sm" }, icon("plus", "sm") + " Add option");
        addButton.addEventListener("click", function () {
            var n = field.choices.length + 1;
            field.choices.push({ value: "option-" + n, label: "Option " + n });
            sync();
            draw();
            var inputs = rows.querySelectorAll("input");
            if (inputs.length) { inputs[inputs.length - 2].focus(); }
        });
        wrap.appendChild(addButton);
        return wrap;
    }
    function slugValue(text) {
        return String(text).normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 100);
    }

    // ---- The JSON view: edits there replace the builder's state ----------------------------------------------
    var jsonPanel = document.querySelector("[data-schema-json]");
    source.addEventListener("change", function () {
        if (load()) { render(); sync(); source.removeAttribute("data-invalid"); } else { source.setAttribute("data-invalid", "true"); }
    });

    // Keys made from labels are fixed once saved: the flags are editor state only, never posted.
    var form = source.form;
    if (form) {
        form.addEventListener("submit", function () {
            (function strip(list) { list.forEach(function (f) { delete f.isNew; delete f.keyEdited; strip(f.fields || []); }); })(fields);
            source.value = JSON.stringify(fields);
        });
    }

    if (!load()) {
        // Unreadable JSON (hand-edited): leave the JSON view open to fix it.
        if (jsonPanel) { jsonPanel.open = true; }
        root.hidden = true;
        return;
    }
    root.hidden = false;
    // Open the first field of a fresh type so its settings are one click away.
    if (fields.length === 1 && !readOnly) { open[fields[0].id] = true; }
    render();
    sync();
})();
