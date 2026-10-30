// API Endpoints editor (.docs/pages/api-endpoints.md): the endpoint's operations as cards - method and sub-path, SQL
// with live hints, a parameter table, result settings and a test run each - kept in sync with the OperationsJson field
// the form posts. Parameters are added for new {name} path segments and @name SQL placeholders. Without JavaScript the
// JSON field is edited directly. Styles: .dnf-endpoint-op in /css/admin/tools.css; a closed card has
// data-collapsed="true", invalid JSON and a failed test run data-invalid="true".
(function () {
    "use strict";

    var doc = document;
    var form = doc.querySelector("[data-endpoint-form]");
    if (!form) { return; }

    var source = form.querySelector("[data-operations-source]");
    var host = form.querySelector("[data-operations]");
    var basePath = form.querySelector("[data-endpoint-path]");
    var methods = JSON.parse(form.getAttribute("data-methods") || "[\"GET\"]");
    var types = JSON.parse(form.getAttribute("data-types") || "[\"string\"]");
    var sources = [["route", "URL path"], ["query", "Query string"], ["body", "JSON body"]];
    var results = [["Rows", "All rows - { data: [...] }"], ["FirstRow", "First row - { data: {...} }, 404 if none"],
        ["Value", "One value - { data: 42 }"], ["AffectedRows", "Rows changed - { affected: 3 }"]];
    var toggleAll = form.querySelector("[data-op-toggle-all]");
    var placeholder = /(?<![@\w])@([A-Za-z_][A-Za-z0-9_]*)/g;
    var ops = read();

    function esc(value) {
        return String(value == null ? "" : value).replace(/[&<>"']/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[c]; });
    }
    function token() {
        var input = form.querySelector("input[name='__RequestVerificationToken']");
        return input ? input.value : "";
    }
    function icon(name) {
        return window.dnfUI ? window.dnfUI.icon(name) : "";
    }
    function flag(el, name, on) {
        if (on) { el.setAttribute(name, "true"); } else { el.removeAttribute(name); }
    }
    function options(list, selected) {
        return list.map(function (o) {
            var value = Array.isArray(o) ? o[0] : o, label = Array.isArray(o) ? o[1] : o;
            return "<option value=\"" + esc(value) + "\"" + (value === selected ? " selected" : "") + ">" + esc(label) + "</option>";
        }).join("");
    }

    // ---- State <-> JSON ----------------------------------------------------------------------------------------
    function blank() {
        return { name: "", method: "GET", path: "", sql: "", parameters: [], result: "Rows", timeoutSeconds: 30, maxRows: 1000, _test: {} };
    }
    function read() {
        try {
            var list = JSON.parse(source.value || "[]");
            if (!Array.isArray(list)) { return null; }
            return list.filter(function (o) { return o && typeof o === "object"; }).map(function (o) {
                o.parameters = Array.isArray(o.parameters) ? o.parameters : [];
                o._test = {};
                return o;
            });
        } catch (e) {
            return null;
        }
    }
    function write() {
        source.value = JSON.stringify(ops.map(function (o) {
            return {
                id: o.id || null, name: o.name || "", method: o.method || "GET", path: o.path || "", sql: o.sql || "",
                parameters: o.parameters.map(function (p) {
                    return { name: p.name || "", type: p.type || "string", source: p.source || "query", required: !!p.required || p.source === "route", "default": p["default"] || null };
                }),
                result: o.result || "Rows", timeoutSeconds: Number(o.timeoutSeconds) || 30, maxRows: Number(o.maxRows) || 1000
            };
        }), null, 2);
        source.removeAttribute("data-invalid");
    }
    function card(i) { return host.querySelector("[data-op='" + i + "']"); }
    function indexOf(el) { var c = el.closest("[data-op]"); return c ? Number(c.getAttribute("data-op")) : -1; }
    function base() { return basePath.value.trim().replace(/^\/+|\/+$/g, ""); }
    function fullPath(op) { var sub = (op.path || "").trim().replace(/^\/+|\/+$/g, ""); return base() + (sub ? "/" + sub : ""); }

    // ---- Cards -------------------------------------------------------------------------------------------------
    function render() {
        host.innerHTML = ops.map(cardHtml).join("");
        ops.forEach(function (_, i) { renderParams(i); renderHints(i); renderTests(i); });
        updatePrefixes();
        updateToggleAll();
    }
    function cardHtml(op, i) {
        var id = "op" + i;
        var open = op._open !== false;
        return "<section class=\"dnf-card dnf-endpoint-op\"" + (open ? "" : " data-collapsed=\"true\"") + " data-op=\"" + i + "\" aria-label=\"Operation " + (i + 1) + "\">" +
            "<div class=\"dnf-card__header flex-wrap items-end\">" +
                "<div class=\"dnf-endpoint-op__request\">" +
                    "<div class=\"dnf-field\"><label class=\"dnf-field__label\" for=\"" + id + "-method\">Method</label>" +
                    "<select id=\"" + id + "-method\" class=\"dnf-select\" data-op-key=\"method\">" + options(methods, op.method || "GET") + "</select></div>" +
                    "<div class=\"dnf-field\"><label class=\"dnf-field__label\" for=\"" + id + "-path\">Sub-path<span class=\"dnf-field__optional\">optional</span></label>" +
                    "<div class=\"dnf-input-group\"><span class=\"dnf-input-group__prefix font-mono max-sm:hidden\" aria-hidden=\"true\" data-op-prefix></span>" +
                    "<input id=\"" + id + "-path\" class=\"dnf-input dnf-input--mono max-sm:rounded-md\" maxlength=\"200\" placeholder=\"e.g. {id}\" data-op-key=\"path\" value=\"" + esc(op.path) + "\" /></div></div>" +
                "</div>" +
                "<div class=\"dnf-cluster\">" +
                "<button type=\"button\" class=\"dnf-button dnf-button--ghost dnf-button--icon-only dnf-button--sm\" data-op-remove aria-label=\"Remove this operation\"" + (ops.length === 1 ? " disabled" : "") + ">" + icon("trash") + "</button>" +
                "<button type=\"button\" class=\"dnf-button dnf-button--ghost dnf-button--icon-only dnf-button--sm dnf-endpoint-op__toggle\" data-op-toggle aria-controls=\"" + id + "-body\" aria-expanded=\"" + open + "\" aria-label=\"" + (open ? "Close" : "Open") + " this operation\">" + icon("chevron-right") + "</button>" +
                "</div>" +
                "<span class=\"basis-full truncate text-sm text-muted\" data-op-summary" + (open || !summary(op) ? " hidden" : "") + ">" + esc(summary(op)) + "</span>" +
            "</div>" +
            "<div class=\"dnf-card__body dnf-stack\" id=\"" + id + "-body\" data-op-body" + (open ? "" : " hidden") + ">" +
                "<div class=\"dnf-field\"><label class=\"dnf-field__label\" for=\"" + id + "-name\">Name<span class=\"dnf-field__optional\">optional</span></label>" +
                "<input id=\"" + id + "-name\" class=\"dnf-input\" maxlength=\"100\" placeholder=\"Get one product\" data-op-key=\"name\" value=\"" + esc(op.name) + "\" /></div>" +
                "<div class=\"dnf-field\"><label class=\"dnf-field__label\" for=\"" + id + "-sql\">SQL</label>" +
                "<textarea id=\"" + id + "-sql\" rows=\"7\" class=\"dnf-textarea dnf-textarea--code\" spellcheck=\"false\" data-op-key=\"sql\" placeholder=\"SELECT * FROM products WHERE id = @id\">" + esc(op.sql) + "</textarea>" +
                "<span class=\"dnf-field__description\">Any statement. Write @name where a value goes - values are sent as parameters, never pasted into the SQL.</span>" +
                "<ul class=\"list-disc ps-4\" data-op-hints hidden></ul></div>" +
                "<div class=\"dnf-stack dnf-stack--sm\"><div class=\"dnf-spread\"><span class=\"text-sm font-semibold\">Parameters</span>" +
                "<button type=\"button\" class=\"dnf-button dnf-button--secondary dnf-button--sm\" data-param-add>" + icon("plus") + " Add parameter</button></div>" +
                "<div class=\"dnf-table\" data-param-wrap hidden><table class=\"dnf-table__table\"><thead><tr>" +
                "<th scope=\"col\">Name</th><th scope=\"col\">Type</th><th scope=\"col\">From</th><th scope=\"col\">Required</th><th scope=\"col\">Default</th>" +
                "<th scope=\"col\" class=\"dnf-table__cell--actions\"><span class=\"sr-only\">Remove</span></th></tr></thead><tbody data-param-rows></tbody></table></div>" +
                "<p class=\"text-sm text-muted\" data-param-empty hidden>No parameters. Add one for every @name in the SQL.</p></div>" +
                "<div class=\"dnf-endpoint-op__settings\">" +
                    "<div class=\"dnf-field\"><label class=\"dnf-field__label\" for=\"" + id + "-result\">Answers with</label><select id=\"" + id + "-result\" class=\"dnf-select\" data-op-key=\"result\">" + options(results, op.result || "Rows") + "</select></div>" +
                    "<div class=\"dnf-field\"><label class=\"dnf-field__label\" for=\"" + id + "-timeout\">Timeout (s)</label><input id=\"" + id + "-timeout\" type=\"number\" class=\"dnf-input\" min=\"1\" max=\"120\" data-op-key=\"timeoutSeconds\" value=\"" + esc(op.timeoutSeconds || 30) + "\" /></div>" +
                    "<div class=\"dnf-field\"><label class=\"dnf-field__label\" for=\"" + id + "-rows\">Row limit</label><input id=\"" + id + "-rows\" type=\"number\" class=\"dnf-input\" min=\"1\" max=\"10000\" data-op-key=\"maxRows\" value=\"" + esc(op.maxRows || 1000) + "\" /></div>" +
                "</div>" +
                "<details class=\"dnf-endpoint-op__test\"><summary>Test this operation</summary><div class=\"dnf-stack\">" +
                    "<div class=\"dnf-form-grid\" data-test-values></div>" +
                    "<label class=\"dnf-check\"><input type=\"checkbox\" class=\"dnf-checkbox\" checked data-test-rollback /><span class=\"dnf-check__text\"><span>Undo changes after the run</span>" +
                    "<span class=\"dnf-check__description\">The SQL runs in a transaction that is rolled back, so test INSERT, UPDATE and DELETE safely.</span></span></label>" +
                    "<div class=\"dnf-cluster\"><button type=\"button\" class=\"dnf-button dnf-button--secondary\" data-test-run>" + icon("send") + " Run test</button>" +
                    "<span class=\"text-sm text-muted\" data-test-status role=\"status\"></span></div>" +
                    "<pre class=\"dnf-code-block\" data-test-output hidden></pre>" +
                "</div></details>" +
            "</div></section>";
    }
    // What a closed card shows under its method and path: its name, if it has one (never the SQL).
    function summary(op) {
        return (op.name || "").trim();
    }
    function setOpen(i, open) {
        var c = card(i);
        ops[i]._open = open;
        flag(c, "data-collapsed", !open);
        c.querySelector("[data-op-body]").hidden = !open;
        var summaryEl = c.querySelector("[data-op-summary]");
        summaryEl.textContent = summary(ops[i]);
        summaryEl.hidden = open || !summaryEl.textContent;
        var toggle = c.querySelector("[data-op-toggle]");
        toggle.setAttribute("aria-expanded", open ? "true" : "false");
        toggle.setAttribute("aria-label", (open ? "Close" : "Open") + " this operation");
        updateToggleAll();
    }
    // "Open all" while any card is closed, else "Close all".
    function updateToggleAll() {
        toggleAll.querySelector("[data-op-toggle-all-label]").textContent = ops.some(function (o) { return o._open === false; }) ? "Open all" : "Close all";
        toggleAll.hidden = ops.length < 2;
    }
    function updatePrefixes() {
        ops.forEach(function (_, i) {
            var prefix = card(i).querySelector("[data-op-prefix]");
            prefix.textContent = "/api/custom/" + (base() ? base() + "/" : "");
        });
    }

    // ---- Parameters --------------------------------------------------------------------------------------------
    function renderParams(i) {
        var op = ops[i], c = card(i);
        c.querySelector("[data-param-rows]").innerHTML = op.parameters.map(function (p, j) {
            var route = p.source === "route";
            return "<tr data-index=\"" + j + "\">" +
                "<td><input type=\"text\" class=\"dnf-input dnf-input--mono min-w-24\" data-key=\"name\" value=\"" + esc(p.name) + "\" aria-label=\"Name\" maxlength=\"64\" /></td>" +
                "<td><select class=\"dnf-select min-w-24\" data-key=\"type\" aria-label=\"Type\">" + options(types, p.type || "string") + "</select></td>" +
                "<td><select class=\"dnf-select min-w-24\" data-key=\"source\" aria-label=\"From\">" + options(sources, p.source || "query") + "</select></td>" +
                "<td><input type=\"checkbox\" class=\"dnf-checkbox\" data-key=\"required\" aria-label=\"Required\"" + (p.required || route ? " checked" : "") + (route ? " disabled title=\"Route values are always required\"" : "") + " /></td>" +
                "<td><input type=\"text\" class=\"dnf-input min-w-24\" data-key=\"default\" value=\"" + esc(p["default"]) + "\" aria-label=\"Default\"" + (route ? " disabled" : "") + " /></td>" +
                "<td class=\"dnf-table__cell--actions\"><button type=\"button\" class=\"dnf-button dnf-button--ghost dnf-button--icon-only dnf-button--sm\" data-param-remove aria-label=\"Remove " + esc(p.name || "parameter") + "\">" + icon("trash") + "</button></td>" +
                "</tr>";
        }).join("");
        c.querySelector("[data-param-wrap]").hidden = op.parameters.length === 0;
        c.querySelector("[data-param-empty]").hidden = op.parameters.length > 0;
    }
    function defaultSource(op) { return /^(GET|DELETE)$/.test(op.method) ? "query" : "body"; }
    function has(op, name) { return op.parameters.some(function (p) { return (p.name || "").toLowerCase() === name.toLowerCase(); }); }
    function addMissing(i) {
        var op = ops[i], added = false;
        var routeNames = (fullPath(op).match(/\{([A-Za-z_][A-Za-z0-9_]*)\}/g) || []).map(function (s) { return s.slice(1, -1); });
        // A route parameter whose {name} left the path (e.g. {id} renamed to {slug}) would be refused on save.
        var kept = op.parameters.filter(function (p) {
            return p.source !== "route" || routeNames.some(function (n) { return n.toLowerCase() === (p.name || "").toLowerCase(); });
        });
        if (kept.length !== op.parameters.length) { op.parameters = kept; added = true; }
        routeNames.forEach(function (name) {
            var existing = op.parameters.filter(function (p) { return (p.name || "").toLowerCase() === name.toLowerCase(); })[0];
            if (!existing) {
                op.parameters.push({ name: name, type: "string", source: "route", required: true, "default": null });
                added = true;
            } else if (existing.source !== "route") {
                existing.source = "route";
                existing.required = true;
                added = true;
            }
        });
        var seen = {};
        ((op.sql || "").match(placeholder) || []).forEach(function (match) {
            var name = match.slice(1);
            if (seen[name.toLowerCase()] || has(op, name)) { return; }
            seen[name.toLowerCase()] = true;
            op.parameters.push({ name: name, type: "string", source: defaultSource(op), required: true, "default": null });
            added = true;
        });
        if (added) { write(); renderParams(i); renderTests(i); }
        renderHints(i);
    }
    function renderHints(i) {
        var op = ops[i], list = card(i).querySelector("[data-op-hints]");
        var used = ((op.sql || "").match(placeholder) || []).map(function (m) { return m.slice(1).toLowerCase(); });
        var hints = [];
        used.filter(function (n, k) { return used.indexOf(n) === k && !has(op, n); }).forEach(function (n) {
            hints.push("The SQL uses @" + n + ", but there is no parameter \"" + n + "\" - the database will reject the statement.");
        });
        op.parameters.filter(function (p) { return p.name && used.indexOf(p.name.toLowerCase()) < 0; }).forEach(function (p) {
            hints.push("Parameter \"" + p.name + "\" is not used in the SQL (write @" + p.name + " where its value goes).");
        });
        list.innerHTML = hints.map(function (h) { return "<li>" + esc(h) + "</li>"; }).join("");
        list.hidden = hints.length === 0;
    }

    // ---- Test run ----------------------------------------------------------------------------------------------
    function renderTests(i) {
        var op = ops[i], hostEl = card(i).querySelector("[data-test-values]");
        var named = op.parameters.filter(function (p) { return p.name; });
        hostEl.innerHTML = named.length === 0
            ? "<p class=\"text-sm text-muted dnf-form-grid__full\">No parameters to fill in.</p>"
            : named.map(function (p, k) {
                var id = "op" + i + "-test-" + k;
                var where = p.source === "route" ? "path" : p.source === "body" ? "body" : "query";
                return "<div class=\"dnf-field\"><label class=\"dnf-field__label\" for=\"" + id + "\"><span class=\"font-mono\">" + esc(p.name) + "</span>" +
                    "<span class=\"dnf-field__optional\">" + esc(p.type || "string") + " · " + where + (p.required || p.source === "route" ? "" : " · optional") + "</span></label>" +
                    "<input type=\"text\" class=\"dnf-input\" id=\"" + id + "\" data-test-name=\"" + esc(p.name) + "\" value=\"" + esc(op._test[p.name] || "") + "\"" +
                    (p["default"] ? " placeholder=\"" + esc(p["default"]) + "\"" : "") + " /></div>";
            }).join("");
    }
    function runTest(i, button) {
        write();
        var c = card(i), op = ops[i];
        var status = c.querySelector("[data-test-status]"), output = c.querySelector("[data-test-output]");
        var data = new FormData(form);
        data.append("operation", JSON.stringify(JSON.parse(source.value)[i]));
        data.append("testValues", JSON.stringify(op._test));
        data.append("rollback", c.querySelector("[data-test-rollback]").checked ? "true" : "false");
        button.disabled = true;
        status.textContent = "Running…";
        fetch("/admin/api-endpoints/test", { method: "POST", body: data, credentials: "same-origin", headers: { "Accept": "application/json", "X-CSRF-TOKEN": token() } })
            .then(function (res) {
                if (!res.ok || res.redirected) { throw new Error("The server could not run the test (" + res.status + ")."); }
                return res.json();
            })
            .then(function (result) { show(result, status, output); })
            .catch(function (error) { show({ ok: false, errors: [error.message] }, status, output); })
            .then(function () { button.disabled = false; });
    }
    function show(result, status, output) {
        output.hidden = false;
        flag(output, "data-invalid", !result.ok);
        if (result.ok) {
            status.textContent = result.status + " · " + result.ms + " ms" + (result.rolledBack ? " · changes undone" : " · changes kept");
            output.textContent = JSON.stringify(result.body, null, 2);
        } else {
            status.textContent = result.status ? result.status + " · failed" : "Not run";
            output.textContent = (result.errors || ["The test failed."]).join("\n");
        }
    }

    // ---- Events ------------------------------------------------------------------------------------------------
    host.addEventListener("input", function (event) {
        var i = indexOf(event.target);
        if (i < 0) { return; }
        var key = event.target.getAttribute("data-op-key");
        if (key) {
            ops[i][key] = event.target.value;
            if (key === "sql") { renderHints(i); }
            write();
            return;
        }
        var name = event.target.getAttribute("data-test-name");
        if (name) { ops[i]._test[name] = event.target.value; return; }
        updateParam(event, i);
    });
    host.addEventListener("change", function (event) {
        var i = indexOf(event.target);
        if (i < 0) { return; }
        var key = event.target.getAttribute("data-op-key");
        if (key === "path" || key === "sql" || key === "method") { addMissing(i); return; }
        if (event.target.closest("[data-param-rows]")) {
            updateParam(event, i);
            if (event.target.getAttribute("data-key") === "source") { renderParams(i); }
            renderTests(i);
            renderHints(i);
        }
    });
    function updateParam(event, i) {
        var row = event.target.closest("tr[data-index]");
        if (!row) { return; }
        var p = ops[i].parameters[Number(row.getAttribute("data-index"))];
        var key = event.target.getAttribute("data-key");
        p[key] = event.target.type === "checkbox" ? event.target.checked : event.target.value;
        if (key === "source" && p.source === "route") { p.required = true; p["default"] = null; }
        write();
    }
    host.addEventListener("click", function (event) {
        var i = indexOf(event.target);
        if (i < 0) { return; }
        var removeParam = event.target.closest("[data-param-remove]");
        if (removeParam) {
            ops[i].parameters.splice(Number(removeParam.closest("tr").getAttribute("data-index")), 1);
            write(); renderParams(i); renderTests(i); renderHints(i);
            return;
        }
        if (event.target.closest("[data-param-add]")) {
            ops[i].parameters.push({ name: "", type: "string", source: defaultSource(ops[i]), required: false, "default": null });
            write(); renderParams(i); renderTests(i);
            var inputs = card(i).querySelectorAll("input[data-key='name']");
            if (inputs.length) { inputs[inputs.length - 1].focus(); }
            return;
        }
        if (event.target.closest("[data-op-toggle]")) { setOpen(i, ops[i]._open === false); return; }
        var run = event.target.closest("[data-test-run]");
        if (run) { runTest(i, run); return; }
        if (event.target.closest("[data-op-remove]") && ops.length > 1) {
            var removeOp = function () { ops.splice(i, 1); write(); render(); };
            if (window.dnfUI && window.dnfUI.confirm) {
                window.dnfUI.confirm({ title: "Remove operation", text: "Remove " + (ops[i].method || "") + " /" + (ops[i].path || "") + "? It is gone once you save.", action: "Remove" })
                    .then(function (ok) { if (ok) { removeOp(); } });
            } else {
                removeOp();
            }
        }
    });
    form.querySelector("[data-op-add]").addEventListener("click", function () {
        var op = blank();
        // A second operation usually works on one item, so it starts on the {id} sub-path.
        op.path = ops.length ? "{id}" : "";
        ops.forEach(function (o) { o._open = false; });
        ops.push(op);
        write();
        render();
        var added = card(ops.length - 1);
        added.scrollIntoView({ block: "start", behavior: "smooth" });
        added.querySelector("[data-op-key='method']").focus();
        addMissing(ops.length - 1);
    });
    toggleAll.addEventListener("click", function () {
        var open = ops.some(function (o) { return o._open === false; });
        ops.forEach(function (_, i) { setOpen(i, open); });
    });
    basePath.addEventListener("input", updatePrefixes);
    basePath.addEventListener("change", function () { ops.forEach(function (_, i) { addMissing(i); }); });

    // The JSON field edited by hand (shown under "Operations as JSON") redraws the cards when it is valid.
    source.addEventListener("change", function () {
        var list = read();
        flag(source, "data-invalid", list === null);
        if (list !== null) {
            ops = list;
            ops.forEach(function (o) { o._open = ops.length === 1; });
            render();
        }
    });

    // ---- Start -------------------------------------------------------------------------------------------------
    if (ops === null) {
        // Invalid JSON (e.g. re-shown after a failed save): leave it for the author to fix by hand.
        ops = [];
        source.setAttribute("data-invalid", "true");
        source.closest("details").open = true;
        host.hidden = true;
    } else {
        if (ops.length === 0) { ops.push(blank()); write(); }
        // One operation starts open; several start closed, so the list stays short.
        ops.forEach(function (o) { o._open = ops.length === 1; });
        render();
    }
})();
