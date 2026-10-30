// Content Manager enhancements (.docs/pages/content-manager.md): the entry form's rich text editor, media and
// relationship pickers, repeater rows and slug generation, and drag-to-reorder in the entry list. Everything posts
// through normal form fields, so the screens also work without JavaScript (plain text, native selects, existing rows);
// the server validates every value (EntryData).
(function () {
    "use strict";

    var doc = document;
    function icon(name, size, className) { return window.dnfUI.icon(name, size, className); }
    function esc(value) {
        return String(value == null ? "" : value).replace(/[&<>"']/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[c]; });
    }
    function token() {
        var input = doc.querySelector("input[name='__RequestVerificationToken']");
        return input ? input.value : "";
    }
    function toast(message, tone) {
        if (window.dnfUI && window.dnfUI.toast) { window.dnfUI.toast(message, tone); } else { window.alert(message); }
    }
    function json(url, options) {
        return fetch(url, Object.assign({ credentials: "same-origin", headers: { "Accept": "application/json", "X-CSRF-TOKEN": token() } }, options || {}))
            .then(function (res) {
                return res.text().then(function (text) {
                    var data;
                    try { data = text ? JSON.parse(text) : null; } catch (e) { data = null; }
                    if (!res.ok || res.redirected) { throw new Error(data && data.message ? data.message : "The server could not complete the request."); }
                    return data;
                });
            });
    }

    // ---- Unsaved changes ------------------------------------------------------------------------------------------
    var entryForm = doc.querySelector("[data-entry-form]");
    var dirty = false;
    if (entryForm) {
        entryForm.addEventListener("input", function () { dirty = true; });
        entryForm.addEventListener("change", function () { dirty = true; });
        entryForm.addEventListener("submit", function () { dirty = false; });
        window.addEventListener("beforeunload", function (event) {
            if (dirty) { event.preventDefault(); event.returnValue = ""; }
        });
    }

    // ---- Slugs: generated from their text field until edited by hand -------------------------------------------
    function slugify(text) {
        return String(text).normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
    }
    function wireSlugs(scope) {
        scope.querySelectorAll("input[data-slug-from]").forEach(function (slug) {
            if (slug.dataset.slugWired) { return; }
            slug.dataset.slugWired = "1";
            var sourceInput = entryForm ? entryForm.querySelector("[name='" + slug.getAttribute("data-slug-from").replace(/'/g, "") + "']") : null;
            if (!sourceInput) { return; }
            var manual = slug.value !== "" && slug.value !== slugify(sourceInput.value);
            slug.addEventListener("input", function () { manual = slug.value !== ""; });
            sourceInput.addEventListener("input", function () { if (!manual) { slug.value = slugify(sourceInput.value); } });
        });
    }

    // ---- Rich text: a small editor whose content is stored as blocks of runs (no HTML is posted) -------------------
    var BLOCK_TAGS = { P: "p", DIV: "p", H1: "h3", H2: "h3", H3: "h3", H4: "h4", H5: "h4", H6: "h4", BLOCKQUOTE: "quote" };
    function collectRuns(node, marks, out) {
        Array.prototype.forEach.call(node.childNodes, function (child) {
            if (child.nodeType === 3) {
                if (child.data) { out.push(Object.assign({ text: child.data.replace(/\u00a0/g, " ") }, marks)); }
                return;
            }
            if (child.nodeType !== 1) { return; }
            var tag = child.tagName;
            if (tag === "BR") { out.push(Object.assign({ text: "\n" }, marks)); return; }
            var m = Object.assign({}, marks);
            if (tag === "B" || tag === "STRONG") { m.b = true; }
            if (tag === "I" || tag === "EM") { m.i = true; }
            if (tag === "U") { m.u = true; }
            if (tag === "S" || tag === "STRIKE" || tag === "DEL") { m.s = true; }
            if (tag === "CODE") { m.code = true; }
            if (tag === "A" && child.getAttribute("href")) { m.href = child.getAttribute("href"); }
            if ((tag === "DIV" || tag === "P") && out.length && !/\n$/.test(out[out.length - 1].text)) { out.push(Object.assign({ text: "\n" }, marks)); }
            collectRuns(child, m, out);
        });
    }
    function mergeRuns(runs) {
        var out = [];
        runs.forEach(function (r) {
            if (!r.text) { return; }
            var last = out[out.length - 1];
            var same = last && ["b", "i", "u", "s", "code", "href"].every(function (k) { return (last[k] || "") === (r[k] || ""); });
            if (same) { last.text += r.text; } else { out.push(r); }
        });
        while (out.length && /^\n+$/.test(out[out.length - 1].text)) { out.pop(); }
        return out;
    }
    function readBlocks(el) {
        var blocks = [];
        var loose = [];
        function flush() {
            var runs = mergeRuns(loose);
            if (runs.length) { blocks.push({ type: "p", runs: runs }); }
            loose = [];
        }
        Array.prototype.forEach.call(el.childNodes, function (child) {
            if (child.nodeType === 1 && BLOCK_TAGS[child.tagName]) {
                flush();
                var runs = [];
                collectRuns(child, {}, runs);
                runs = mergeRuns(runs);
                if (runs.length || child.tagName !== "DIV") { blocks.push({ type: BLOCK_TAGS[child.tagName], runs: runs }); }
            } else if (child.nodeType === 1 && (child.tagName === "UL" || child.tagName === "OL")) {
                flush();
                var items = Array.prototype.filter.call(child.children, function (li) { return li.tagName === "LI"; }).map(function (li) {
                    var r = [];
                    collectRuns(li, {}, r);
                    return mergeRuns(r);
                });
                if (items.length) { blocks.push({ type: child.tagName === "UL" ? "ul" : "ol", items: items }); }
            } else {
                var holder = doc.createElement("span");
                holder.appendChild(child.cloneNode(true));
                collectRuns(holder, {}, loose);
            }
        });
        flush();
        return blocks.filter(function (b) { return b.items || b.runs.length; });
    }
    function runHtml(run) {
        var html = esc(run.text).replace(/\n/g, "<br>");
        if (run.code) { html = "<code>" + html + "</code>"; }
        if (run.s) { html = "<s>" + html + "</s>"; }
        if (run.u) { html = "<u>" + html + "</u>"; }
        if (run.i) { html = "<em>" + html + "</em>"; }
        if (run.b) { html = "<strong>" + html + "</strong>"; }
        if (run.href) { html = "<a href=\"" + esc(run.href) + "\">" + html + "</a>"; }
        return html;
    }
    function blocksHtml(blocks) {
        return (blocks || []).map(function (b) {
            if (b.type === "ul" || b.type === "ol") { return "<" + b.type + ">" + b.items.map(function (i) { return "<li>" + i.map(runHtml).join("") + "</li>"; }).join("") + "</" + b.type + ">"; }
            var tag = b.type === "quote" ? "blockquote" : b.type;
            return "<" + tag + ">" + ((b.runs || []).map(runHtml).join("") || "<br>") + "</" + tag + ">";
        }).join("");
    }

    var TOOLS = [
        { cmd: "formatBlock", arg: "p", icon: "pilcrow", label: "Paragraph" },
        { cmd: "formatBlock", arg: "h3", icon: "heading", label: "Heading" },
        { cmd: "formatBlock", arg: "h4", icon: "type", label: "Subheading" },
        { sep: true },
        { cmd: "bold", icon: "bold", label: "Bold (Ctrl+B)" },
        { cmd: "italic", icon: "italic", label: "Italic (Ctrl+I)" },
        { cmd: "underline", icon: "underline", label: "Underline (Ctrl+U)" },
        { cmd: "link", icon: "link", label: "Link" },
        { sep: true },
        { cmd: "insertUnorderedList", icon: "list", label: "Bulleted list" },
        { cmd: "insertOrderedList", icon: "list-ordered", label: "Numbered list" },
        { cmd: "formatBlock", arg: "blockquote", icon: "quote", label: "Quote" },
        { cmd: "removeFormat", icon: "rotate-ccw", label: "Clear formatting" }
    ];
    function wireRichText(scope) {
        scope.querySelectorAll("[data-rte]").forEach(function (wrap) {
            if (wrap.dataset.rteWired) { return; }
            wrap.dataset.rteWired = "1";
            var source = wrap.querySelector("[data-rte-source]");
            var blocks;
            try { blocks = JSON.parse(wrap.getAttribute("data-rte-value") || "[]"); } catch (e) { blocks = []; }
            var readOnly = wrap.getAttribute("data-readonly") === "true";
            var toolbar = doc.createElement("div");
            toolbar.className = "dnf-rte__toolbar";
            toolbar.setAttribute("role", "toolbar");
            toolbar.setAttribute("aria-label", "Formatting");
            TOOLS.forEach(function (t) {
                if (t.sep) { toolbar.insertAdjacentHTML("beforeend", "<span class=\"dnf-rte__sep\" aria-hidden=\"true\"></span>"); return; }
                var b = doc.createElement("button");
                b.type = "button";
                b.className = "dnf-button dnf-button--ghost dnf-button--sm dnf-button--icon-only";
                b.setAttribute("aria-label", t.label);
                b.setAttribute("data-tooltip", t.label);
                b.disabled = readOnly;
                b.innerHTML = icon(t.icon, "sm");
                b.addEventListener("mousedown", function (event) { event.preventDefault(); });
                b.addEventListener("click", function () { run(t); });
                toolbar.appendChild(b);
            });
            var area = doc.createElement("div");
            area.className = "dnf-rte__area";
            area.contentEditable = readOnly ? "false" : "true";
            area.setAttribute("role", "textbox");
            area.setAttribute("aria-multiline", "true");
            if (source.id) {
                var label = doc.querySelector("label[for='" + source.id + "']");
                if (label) { label.id = label.id || source.id + "-label"; area.setAttribute("aria-labelledby", label.id); label.removeAttribute("for"); }
            }
            area.innerHTML = blocksHtml(blocks) || "<p><br></p>";
            source.hidden = true;
            wrap.insertBefore(toolbar, source);
            wrap.insertBefore(area, source);
            function save() { source.value = JSON.stringify(readBlocks(area)); source.dispatchEvent(new Event("input", { bubbles: true })); }
            function run(t) {
                area.focus();
                if (t.cmd === "link") {
                    var url = window.prompt("Link address (https://…, /page, mailto:…). Leave empty to remove the link.", "https://");
                    if (url === null) { return; }
                    if (url.trim() === "" || url.trim() === "https://") { doc.execCommand("unlink"); } else { doc.execCommand("createLink", false, url.trim()); }
                } else {
                    doc.execCommand(t.cmd, false, t.arg ? "<" + t.arg + ">" : null);
                }
                save();
            }
            area.addEventListener("input", save);
            area.addEventListener("paste", function (event) {
                // Plain text only: formatting from other programs would be dropped anyway.
                event.preventDefault();
                var text = (event.clipboardData || window.clipboardData).getData("text/plain");
                doc.execCommand("insertText", false, text);
            });
            save();
            dirty = false;
        });
    }

    // ---- Repeaters: add, remove and move rows -------------------------------------------------------------------
    function renumber(repeater) {
        var label = (repeater.querySelector(".dnf-repeater__head .dnf-field__label") || {}).firstChild;
        var name = label && label.textContent ? label.textContent.trim() : "Row";
        repeater.querySelectorAll(":scope > [data-repeater-rows] > [data-repeater-row]").forEach(function (row, i, rows) {
            var title = row.querySelector("[data-repeater-title]");
            if (title) { title.textContent = name + " #" + (i + 1); }
            var up = row.querySelector(":scope > .dnf-repeater__row-head [data-repeater-up]");
            var down = row.querySelector(":scope > .dnf-repeater__row-head [data-repeater-down]");
            if (up) { up.disabled = i === 0; }
            if (down) { down.disabled = i === rows.length - 1; }
        });
        var add = repeater.querySelector(":scope > [data-repeater-add]");
        var max = parseInt(repeater.getAttribute("data-max") || "0", 10);
        if (add && max) { add.disabled = repeater.querySelectorAll(":scope > [data-repeater-rows] > [data-repeater-row]").length >= max; }
    }
    doc.addEventListener("click", function (event) {
        var target = event.target instanceof Element ? event.target : null;
        if (!target) { return; }
        var add = target.closest("[data-repeater-add]");
        if (add) {
            var repeater = add.closest("[data-repeater]");
            var template = repeater.querySelector(":scope > template[data-repeater-template]");
            var index = parseInt(repeater.getAttribute("data-next-index") || "0", 10);
            repeater.setAttribute("data-next-index", String(index + 1));
            // Only this repeater's own placeholder (names f.path.__index__, ids f-path-__index__, data-field-path):
            // the templates of repeaters nested in the row keep theirs for their own rows.
            var path = repeater.getAttribute("data-field-path");
            var idPath = "f-" + path.replace(/\./g, "-");
            var html = template.innerHTML
                .split("f." + path + ".__index__").join("f." + path + "." + index)
                .split(idPath + "-__index__").join(idPath + "-" + index)
                .split("\"" + path + ".__index__").join("\"" + path + "." + index);
            var holder = doc.createElement("ol");
            holder.innerHTML = html;
            var row = holder.firstElementChild;
            repeater.querySelector(":scope > [data-repeater-rows]").appendChild(row);
            enhance(row);
            renumber(repeater);
            dirty = true;
            var first = row.querySelector("input:not([type=hidden]), textarea, select");
            if (first) { first.focus(); }
            return;
        }
        var button = target.closest("[data-repeater-remove], [data-repeater-up], [data-repeater-down]");
        if (!button) { return; }
        var rowEl = button.closest("[data-repeater-row]");
        var parent = rowEl.closest("[data-repeater]");
        if (button.hasAttribute("data-repeater-remove")) {
            rowEl.remove();
        } else if (button.hasAttribute("data-repeater-up") && rowEl.previousElementSibling) {
            rowEl.parentNode.insertBefore(rowEl, rowEl.previousElementSibling);
        } else if (button.hasAttribute("data-repeater-down") && rowEl.nextElementSibling) {
            rowEl.parentNode.insertBefore(rowEl.nextElementSibling, rowEl);
        }
        renumber(parent);
        dirty = true;
    });

    // ---- Media pickers: thumbnails, a library dialog with search and upload ------------------------------------
    var dialog = null;
    function mediaDialog() {
        if (dialog) { return dialog; }
        dialog = doc.createElement("dialog");
        dialog.className = "dnf-dialog dnf-dialog--xl dnf-media-dialog";
        dialog.setAttribute("aria-labelledby", "media-dialog-title");
        dialog.innerHTML =
            "<div class=\"dnf-media-dialog__head\"><h2 class=\"dnf-dialog__title\" id=\"media-dialog-title\" data-dialog-title>Choose from Media</h2>" +
            "<button type=\"button\" class=\"dnf-button dnf-button--ghost dnf-button--sm dnf-button--icon-only\" data-close aria-label=\"Close\">" + icon("x", "sm") + "</button></div>" +
            "<div class=\"dnf-toolbar dnf-media-dialog__toolbar\"><div class=\"dnf-toolbar__search dnf-input-group\">" + icon("search", "sm", "dnf-input-group__icon") +
            "<input type=\"search\" class=\"dnf-input dnf-input--sm\" placeholder=\"Search files\" aria-label=\"Search files\" data-search></div>" +
            "<label class=\"dnf-button dnf-button--secondary dnf-button--sm relative overflow-hidden\" data-upload hidden>" + icon("upload", "sm") + " Upload<input type=\"file\" class=\"sr-only\" data-file></label></div>" +
            "<div class=\"dnf-media-dialog__grid\" data-grid role=\"listbox\" aria-label=\"Files\"></div>" +
            "<div class=\"dnf-dialog__footer\"><span class=\"text-sm text-muted me-auto\" data-status></span>" +
            "<button type=\"button\" class=\"dnf-button dnf-button--secondary\" data-close>Cancel</button><button type=\"button\" class=\"dnf-button dnf-button--primary\" data-done>Use selected</button></div>";
        doc.body.appendChild(dialog);
        dialog.querySelectorAll("[data-close]").forEach(function (b) { b.addEventListener("click", function () { dialog.close(); }); });
        return dialog;
    }
    function wireMedia(scope) {
        scope.querySelectorAll("select[data-media-picker]").forEach(function (select) {
            if (select.dataset.pickerWired) { return; }
            select.dataset.pickerWired = "1";
            var images = select.getAttribute("data-kind") === "image";
            var multiple = select.multiple;
            var box = doc.createElement("div");
            box.className = "dnf-media-picker";
            select.hidden = true;
            select.parentNode.insertBefore(box, select.nextSibling);
            function selected() { return Array.prototype.filter.call(select.options, function (o) { return o.selected && o.value; }); }
            function draw() {
                var items = selected();
                box.innerHTML = items.map(function (o) {
                    var thumb = o.getAttribute("data-image") === "true"
                        ? "<img src=\"" + esc(o.getAttribute("data-url")) + "\" alt=\"\" loading=\"lazy\">"
                        : "<span class=\"dnf-media-picker__file\">" + icon("file") + "</span>";
                    return "<figure class=\"dnf-media-picker__item\">" + thumb + "<figcaption class=\"dnf-media-picker__caption\" title=\"" + esc(o.text) + "\">" + esc(o.text) + "</figcaption>" +
                        (select.disabled ? "" : "<button type=\"button\" class=\"dnf-button dnf-button--ghost dnf-button--sm dnf-button--icon-only dnf-media-picker__remove\" data-remove=\"" + esc(o.value) + "\" aria-label=\"Remove " + esc(o.text) + "\">" + icon("x", "sm") + "</button>") + "</figure>";
                }).join("") + (select.disabled ? "" : "<button type=\"button\" class=\"dnf-media-picker__add\" data-choose>" + icon(images ? "image" : "file") +
                    "<span>" + (items.length && !multiple ? "Replace" : multiple ? "Add " + (images ? "images" : "files") : "Choose " + (images ? "an image" : "a file")) + "</span></button>");
            }
            box.addEventListener("click", function (event) {
                var remove = event.target.closest("[data-remove]");
                if (remove) {
                    Array.prototype.forEach.call(select.options, function (o) { if (o.value === remove.getAttribute("data-remove")) { o.selected = false; } });
                    if (!multiple) { select.value = ""; }
                    select.dispatchEvent(new Event("change", { bubbles: true }));
                    draw();
                    return;
                }
                if (event.target.closest("[data-choose]")) { open(); }
            });
            function open() {
                var d = mediaDialog();
                var grid = d.querySelector("[data-grid]");
                var search = d.querySelector("[data-search]");
                var status = d.querySelector("[data-status]");
                var upload = d.querySelector("[data-upload]");
                var fileInput = d.querySelector("[data-file]");
                var picked = selected().map(function (o) { return o.value; });
                var files = [];
                d.querySelector("[data-dialog-title]").textContent = images ? (multiple ? "Choose images" : "Choose an image") : (multiple ? "Choose files" : "Choose a file");
                upload.hidden = select.getAttribute("data-can-upload") !== "true";
                fileInput.accept = images ? "image/*" : "";
                function render() {
                    grid.innerHTML = files.length ? files.map(function (f) {
                        var on = picked.indexOf(f.id) >= 0;
                        return "<button type=\"button\" class=\"dnf-media-tile\" role=\"option\" aria-selected=\"" + on + "\" data-id=\"" + esc(f.id) + "\">" +
                            (f.isImage ? "<img src=\"" + esc(f.url) + "\" alt=\"\" loading=\"lazy\">" : "<span class=\"dnf-media-tile__file\">" + icon("file", "lg") + "</span>") +
                            "<span class=\"dnf-media-tile__name\">" + esc(f.name) + "</span></button>";
                    }).join("") : "<p class=\"text-sm text-muted\">No files match. Upload one, or add files on the Media screen.</p>";
                    status.textContent = picked.length ? picked.length + " selected" : "";
                }
                function load() {
                    json("/admin/content/api/media?kind=" + (images ? "image" : "file") + "&q=" + encodeURIComponent(search.value)).then(function (list) { files = list || []; render(); })
                        .catch(function (e) { grid.innerHTML = "<p class=\"text-sm text-muted\">" + esc(e.message) + "</p>"; });
                }
                grid.onclick = function (event) {
                    var tile = event.target.closest("[data-id]");
                    if (!tile) { return; }
                    var id = tile.getAttribute("data-id");
                    if (multiple) {
                        var at = picked.indexOf(id);
                        if (at >= 0) { picked.splice(at, 1); } else { picked.push(id); }
                        render();
                    } else {
                        picked = [id];
                        done();
                    }
                };
                var timer = null;
                search.oninput = function () { clearTimeout(timer); timer = setTimeout(load, 250); };
                fileInput.onchange = function () {
                    if (!fileInput.files.length) { return; }
                    var data = new FormData();
                    data.append("file", fileInput.files[0]);
                    status.textContent = "Uploading…";
                    json("/admin/content/api/media/upload", { method: "POST", body: data }).then(function (file) {
                        files.unshift(file);
                        if (multiple) { picked.push(file.id); } else { picked = [file.id]; }
                        render();
                        toast("Uploaded " + file.name + ".", "success");
                    }).catch(function (e) { status.textContent = ""; toast(e.message, "danger"); })
                        .finally(function () { fileInput.value = ""; });
                };
                function done() {
                    picked.forEach(function (id) {
                        if (!Array.prototype.some.call(select.options, function (o) { return o.value === id; })) {
                            var f = files.filter(function (x) { return x.id === id; })[0];
                            if (!f) { return; }
                            var option = new Option(f.name, f.id);
                            option.setAttribute("data-url", f.url);
                            option.setAttribute("data-image", f.isImage ? "true" : "false");
                            select.appendChild(option);
                        }
                    });
                    Array.prototype.forEach.call(select.options, function (o) { o.selected = picked.indexOf(o.value) >= 0; });
                    select.dispatchEvent(new Event("change", { bubbles: true }));
                    draw();
                    d.close();
                }
                d.querySelector("[data-done]").onclick = done;
                d.querySelector("[data-done]").hidden = !multiple;
                search.value = "";
                grid.innerHTML = "<p class=\"text-sm text-muted\">Loading…</p>";
                d.showModal();
                load();
                search.focus();
            }
            draw();
        });
    }

    // ---- Relationship pickers: chips and a search box over the linked type's entries ---------------------------
    function wireRelations(scope) {
        scope.querySelectorAll("select[data-relation-picker]").forEach(function (select) {
            if (select.dataset.pickerWired) { return; }
            select.dataset.pickerWired = "1";
            var multiple = select.multiple;
            var typeId = select.getAttribute("data-type-id");
            var box = doc.createElement("div");
            box.className = "dnf-relation-picker";
            box.innerHTML = "<div class=\"dnf-chip-list\" data-chips></div>" +
                (select.disabled ? "" : "<div class=\"dnf-relation-picker__search dnf-input-group\">" + icon("search", "sm", "dnf-input-group__icon") +
                "<input type=\"search\" class=\"dnf-input dnf-input--sm\" autocomplete=\"off\" role=\"combobox\" aria-expanded=\"false\" aria-autocomplete=\"list\" placeholder=\"Search " + esc(select.getAttribute("data-type-name")) + "\">" +
                "<ul class=\"dnf-relation-picker__results\" role=\"listbox\" hidden></ul></div>");
            select.hidden = true;
            select.parentNode.insertBefore(box, select.nextSibling);
            var chips = box.querySelector("[data-chips]");
            var input = box.querySelector("input");
            var results = box.querySelector(".dnf-relation-picker__results");
            if (input && select.id) {
                var label = doc.querySelector("label[for='" + select.id + "']");
                if (label) { label.setAttribute("for", select.id + "-search"); input.id = select.id + "-search"; }
            }
            function selected() { return Array.prototype.filter.call(select.options, function (o) { return o.selected && o.value; }); }
            function draw() {
                var items = selected();
                chips.innerHTML = items.length ? items.map(function (o) {
                    return "<span class=\"dnf-chip\">" + esc(o.text) + (select.disabled ? "" : "<button type=\"button\" class=\"dnf-chip__remove\" data-remove=\"" + esc(o.value) + "\" aria-label=\"Remove " + esc(o.text) + "\">" + icon("x", "sm") + "</button>") + "</span>";
                }).join("") : "<span class=\"text-sm text-subtle\">Nothing linked yet.</span>";
            }
            chips.addEventListener("click", function (event) {
                var remove = event.target.closest("[data-remove]");
                if (!remove) { return; }
                Array.prototype.forEach.call(select.options, function (o) { if (o.value === remove.getAttribute("data-remove")) { o.selected = false; } });
                if (!multiple) { select.value = ""; }
                select.dispatchEvent(new Event("change", { bubbles: true }));
                draw();
            });
            if (input) {
                var timer = null;
                var found = [];
                function show(list) {
                    found = list;
                    results.innerHTML = list.length ? list.map(function (e, i) {
                        return "<li class=\"dnf-relation-picker__result\" role=\"option\" id=\"" + esc(select.id) + "-opt-" + i + "\" data-index=\"" + i + "\">" + esc(e.title) + (e.published ? "" : " <span class=\"text-subtle\">(draft)</span>") + "</li>";
                    }).join("") : "<li class=\"dnf-relation-picker__result dnf-relation-picker__result--empty\">No match</li>";
                    results.hidden = false;
                    input.setAttribute("aria-expanded", "true");
                }
                function hide() { results.hidden = true; input.setAttribute("aria-expanded", "false"); }
                function pick(entry) {
                    var option = Array.prototype.filter.call(select.options, function (o) { return o.value === entry.id; })[0];
                    if (!option) {
                        option = new Option(entry.title + (entry.published ? "" : " (draft)"), entry.id);
                        select.appendChild(option);
                    }
                    if (!multiple) { Array.prototype.forEach.call(select.options, function (o) { o.selected = false; }); }
                    option.selected = true;
                    select.dispatchEvent(new Event("change", { bubbles: true }));
                    input.value = "";
                    hide();
                    draw();
                }
                function search() {
                    json("/admin/content/api/entries?type=" + encodeURIComponent(typeId) + "&q=" + encodeURIComponent(input.value)).then(show).catch(function () { hide(); });
                }
                input.addEventListener("input", function () { clearTimeout(timer); timer = setTimeout(search, 200); });
                input.addEventListener("focus", search);
                input.addEventListener("keydown", function (event) {
                    if (event.key === "Escape") { hide(); }
                    if (event.key === "Enter") {
                        event.preventDefault();
                        if (found.length) { pick(found[0]); }
                    }
                });
                results.addEventListener("mousedown", function (event) {
                    var li = event.target.closest("[data-index]");
                    if (li) { event.preventDefault(); pick(found[parseInt(li.getAttribute("data-index"), 10)]); }
                });
                input.addEventListener("blur", function () { setTimeout(hide, 150); });
            }
            draw();
        });
    }

    // ---- Entry list: drag rows to set the manual order -----------------------------------------------------------
    var rowsBody = doc.querySelector("tbody[data-entry-rows]");
    if (rowsBody) {
        var url = rowsBody.getAttribute("data-entry-rows");
        var draggedRow = null;
        var saveOrder = function () {
            var ids = Array.prototype.map.call(rowsBody.querySelectorAll("tr[data-entry-id]"), function (r) { return r.getAttribute("data-entry-id"); });
            return json(url, { method: "POST", headers: { "Content-Type": "application/json", "Accept": "application/json", "X-CSRF-TOKEN": token() }, body: JSON.stringify({ ids: ids }) });
        };
        // Move up / Move down in a row's menu: the same order without dragging, for touch screens and keyboards.
        rowsBody.addEventListener("click", function (event) {
            var button = event.target.closest("[data-entry-move]");
            if (!button) { return; }
            var tr = button.closest("tr[data-entry-id]");
            if (button.getAttribute("data-entry-move") === "up") {
                rowsBody.insertBefore(tr, tr.previousElementSibling);
            } else {
                rowsBody.insertBefore(tr.nextElementSibling, tr);
            }
            saveOrder()
                .then(function () { window.location.reload(); })
                .catch(function (e) { toast(e.message || "Could not save the new order.", "danger"); });
        });
        rowsBody.querySelectorAll("tr[data-entry-id]").forEach(function (tr) {
            var grip = tr.querySelector(".dnf-table__grip");
            if (!grip) { return; }
            grip.addEventListener("pointerdown", function () { tr.setAttribute("draggable", "true"); });
            tr.addEventListener("dragstart", function (event) { draggedRow = tr; tr.setAttribute("data-dragging", "true"); event.dataTransfer.effectAllowed = "move"; });
            tr.addEventListener("dragend", function () {
                tr.removeAttribute("draggable");
                tr.removeAttribute("data-dragging");
                draggedRow = null;
                saveOrder()
                    .then(function () { toast("Order saved.", "success"); })
                    .catch(function (e) { toast(e.message || "Could not save the new order.", "danger"); });
            });
            tr.addEventListener("dragover", function (event) {
                if (!draggedRow || draggedRow === tr) { return; }
                event.preventDefault();
                var rect = tr.getBoundingClientRect();
                rowsBody.insertBefore(draggedRow, event.clientY > rect.top + rect.height / 2 ? tr.nextSibling : tr);
            });
        });
    }

    function enhance(scope) {
        wireSlugs(scope);
        wireRichText(scope);
        wireMedia(scope);
        wireRelations(scope);
        scope.querySelectorAll("[data-js-only]").forEach(function (el) { el.hidden = false; });
    }
    if (entryForm) {
        enhance(entryForm);
        entryForm.querySelectorAll("[data-repeater]").forEach(renumber);
        dirty = false;
    }
})();
