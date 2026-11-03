// UI docs (.docs/pages/ui-docs.md): "On this page" from the article's h2/h3 headings (the one in view is marked),
// the navigation search (filters the pages; Enter opens the first match; "/" focuses it), the examples' demo buttons
// ([data-docs-toast], [data-docs-confirm]), the Copy menu ([data-docs-copy]: the page or all pages as Markdown) and the
// current page kept in view in the navigation. Everything else
// (example tabs, copy buttons, dialogs, the drawer, the theme) is dnf.js.
(function () {
    "use strict";

    var article = document.querySelector("[data-docs-content]");
    var toc = document.querySelector("[data-docs-toc]");

    // ---- On this page ------------------------------------------------------------------------------------------
    if (article && toc) {
        var headings = Array.prototype.slice.call(article.querySelectorAll("h2[id], h3[id]"))
            .filter(function (h) { return !h.closest("[data-doc-preview]"); });
        var links = headings.map(function (heading) {
            var item = document.createElement("li");
            var link = document.createElement("a");
            link.className = "dnf-docs__toc-link" + (heading.tagName === "H3" ? " dnf-docs__toc-link--sub" : "");
            link.href = "#" + heading.id;
            link.textContent = heading.textContent;
            item.appendChild(link);
            toc.appendChild(item);
            return link;
        });

        // The current section: the last heading above a line a little below the header.
        var current = -1;
        function mark() {
            var line = 120;
            var index = -1;
            headings.forEach(function (heading, i) { if (heading.getBoundingClientRect().top <= line) { index = i; } });
            if (index === -1 && headings.length) { index = 0; }
            if (index === current) { return; }
            current = index;
            links.forEach(function (link, i) {
                if (i === index) { link.setAttribute("aria-current", "true"); } else { link.removeAttribute("aria-current"); }
            });
        }
        var pending = false;
        window.addEventListener("scroll", function () {
            if (pending) { return; }
            pending = true;
            window.requestAnimationFrame(function () { pending = false; mark(); });
        }, { passive: true });
        mark();
    }

    // ---- Navigation search ---------------------------------------------------------------------------------------
    var search = document.querySelector("[data-docs-search]");
    var nav = document.querySelector("[data-docs-nav]");
    if (search && nav) {
        var empty = nav.querySelector("[data-docs-nav-empty]");
        function filter() {
            var query = search.value.trim().toLowerCase();
            var any = false;
            Array.prototype.forEach.call(nav.querySelectorAll("[data-docs-nav-group]"), function (group) {
                var shown = 0;
                Array.prototype.forEach.call(group.querySelectorAll("li"), function (item) {
                    var match = !query || item.textContent.toLowerCase().indexOf(query) !== -1;
                    item.hidden = !match;
                    if (match) { shown++; }
                });
                group.hidden = shown === 0;
                if (shown) { any = true; }
            });
            if (empty) { empty.hidden = any; }
        }
        search.addEventListener("input", filter);
        search.addEventListener("keydown", function (event) {
            if (event.key === "Enter") {
                var first = nav.querySelector("li:not([hidden]) a");
                if (first) { window.location.href = first.href; }
            } else if (event.key === "Escape") {
                search.value = "";
                filter();
            }
        });
        document.addEventListener("keydown", function (event) {
            var target = event.target;
            var typing = target instanceof HTMLElement && (target.isContentEditable || /^(input|textarea|select)$/i.test(target.tagName));
            if (event.key === "/" && !typing && !event.ctrlKey && !event.metaKey && !event.altKey) {
                event.preventDefault();
                search.focus();
            }
        });
    }

    // ---- Reference tables: wrapped so a wide one scrolls sideways - and a scrolling one must be reachable by keyboard
    // (a focusable region named after its heading). The table itself keeps its table semantics.
    if (article) {
        Array.prototype.forEach.call(article.querySelectorAll("table"), function (table) {
            if (table.closest("[data-doc-preview], .dnf-docs-api")) { return; }
            var wrapper = document.createElement("div");
            wrapper.className = "dnf-docs-table-scroll";
            table.parentNode.insertBefore(wrapper, table);
            wrapper.appendChild(table);
            if (wrapper.scrollWidth <= wrapper.clientWidth + 1) { return; }
            var heading = wrapper.previousElementSibling;
            while (heading && !/^H[23]$/.test(heading.tagName)) { heading = heading.previousElementSibling; }
            wrapper.setAttribute("tabindex", "0");
            wrapper.setAttribute("role", "region");
            wrapper.setAttribute("aria-label", (heading ? heading.textContent.trim() : "Reference") + " table");
        });
    }

    // ---- Demo buttons of the examples: a toast, a confirmation (nothing is sent or deleted) ------------------------
    document.addEventListener("click", function (event) {
        var target = event.target instanceof Element ? event.target : null;
        if (!target || !window.dnfUI) { return; }
        var toast = target.closest("[data-docs-toast]");
        if (toast) {
            window.dnfUI.toast(toast.getAttribute("data-docs-toast"), toast.getAttribute("data-docs-toast-tone") || "info");
            return;
        }
        var confirm = target.closest("[data-docs-confirm]");
        if (confirm) {
            window.dnfUI.confirm({
                title: confirm.getAttribute("data-docs-confirm-title") || "Are you sure?",
                text: confirm.getAttribute("data-docs-confirm"),
                action: confirm.getAttribute("data-docs-confirm-action") || "Confirm",
                tone: confirm.getAttribute("data-docs-confirm-tone")
            }).then(function (confirmed) { window.dnfUI.toast(confirmed ? "Confirmed - nothing was changed." : "Cancelled.", "info"); });
        }
    });

    // ---- Copy: the page (or every page) as Markdown ----------------------------------------------------------
    // Built from the rendered article: headings, prose, reference and API tables, and each example's Razor source as a
    // fenced code block (not its preview). "Copy all docs" fetches every page of the navigation (same origin, the
    // signed-in session) and joins them.
    function inline(node) {
        var out = "";
        Array.prototype.forEach.call(node.childNodes, function (child) {
            if (child.nodeType === 3) { out += child.textContent.replace(/\s+/g, " "); return; }
            if (child.nodeType !== 1) { return; }
            var tag = child.tagName;
            if (tag === "CODE" || tag === "KBD") { out += "`" + child.textContent + "`"; }
            else if (tag === "STRONG" || tag === "B") { out += "**" + inline(child).trim() + "**"; }
            else if (tag === "EM" || tag === "I") { out += "_" + inline(child).trim() + "_"; }
            else if (tag === "A") { out += "[" + inline(child).trim() + "](" + new URL(child.getAttribute("href"), window.location.href).pathname + ")"; }
            else if (tag === "SVG" || child.getAttribute("aria-hidden") === "true") { /* icons */ }
            else { out += inline(child); }
        });
        return out;
    }
    function cell(node) { return inline(node).trim().replace(/\|/g, "\\|"); }
    function table(node) {
        var rows = Array.prototype.slice.call(node.querySelectorAll("tr"));
        if (!rows.length) { return ""; }
        var lines = rows.map(function (row) {
            return "| " + Array.prototype.map.call(row.children, cell).join(" | ") + " |";
        });
        var columns = rows[0].children.length;
        lines.splice(1, 0, "|" + new Array(columns + 1).join(" --- |"));
        return lines.join("\n");
    }
    function fence(code, lang) {
        var text = code.replace(/\s+$/, "");
        var ticks = /```/.test(text) ? "````" : "```";
        return ticks + (lang || "") + "\n" + text + "\n" + ticks;
    }
    function lang(name) { return name === "razor" ? "cshtml" : name || ""; }
    function blocks(container) {
        var parts = [];
        Array.prototype.forEach.call(container.children, function (el) {
            var tag = el.tagName;
            if (el.matches("header.dnf-docs__page-header, nav, [data-js-only][hidden]")) { return; }
            if (/^H[2-4]$/.test(tag)) { parts.push(new Array(parseInt(tag[1], 10) + 1).join("#") + " " + el.textContent.trim()); }
            else if (tag === "P") { var text = inline(el).trim(); if (text) { parts.push(text); } }
            else if (tag === "UL" || tag === "OL") {
                parts.push(Array.prototype.map.call(el.children, function (li, i) {
                    return (tag === "OL" ? (i + 1) + ". " : "- ") + inline(li).trim();
                }).join("\n"));
            }
            else if (tag === "TABLE") { parts.push(table(el)); }
            else if (el.matches(".dnf-docs-code")) {
                var snippet = el.querySelector("code");
                parts.push(fence(snippet ? snippet.textContent : "", lang(el.getAttribute("data-lang"))));
            }
            else if (tag === "PRE") {
                var code = el.querySelector("code");
                parts.push(fence((code || el).textContent, lang(el.getAttribute("data-lang"))));
            }
            else if (el.matches("[data-doc-example]")) {
                var title = el.querySelector(".dnf-docs-example__title");
                var description = el.querySelector(".dnf-docs-example__description");
                var razor = el.querySelector("[data-tab-panel='razor'] code");
                if (title) { parts.push("### " + title.textContent.trim()); }
                if (description) { parts.push(inline(description).trim()); }
                if (razor) { parts.push(fence(razor.textContent, "cshtml")); }
            }
            else if (el.matches(".dnf-docs-api, .dnf-docs-table-scroll")) {
                var inner = el.querySelector("table");
                if (inner) { parts.push(table(inner)); }
            }
            else if (el.matches(".dnf-docs-cards")) {
                parts.push(Array.prototype.map.call(el.querySelectorAll("a"), function (a) {
                    var name = a.querySelector(".dnf-docs-cards__title");
                    var text = a.querySelector(".dnf-docs-cards__description");
                    return "- [" + (name ? name.textContent.trim() : a.textContent.trim()) + "](" + new URL(a.href).pathname + ")" +
                        (text ? " - " + text.textContent.trim() : "");
                }).join("\n"));
            }
            else if (!el.matches("[data-doc-preview], script, template")) { parts.push(blocks(el)); }
        });
        return parts.filter(Boolean).join("\n\n");
    }
    function pageMarkdown(doc, path) {
        var content = doc.querySelector("[data-docs-content]");
        if (!content) { return ""; }
        var title = content.querySelector(".dnf-docs__title");
        var lead = content.querySelector(".dnf-docs__lead");
        var eyebrow = content.querySelector(".dnf-docs__eyebrow");
        return "# " + (title ? title.textContent.trim() : "") + "\n\n" +
            (lead ? "> " + lead.textContent.trim() + "\n\n" : "") +
            "Category: " + (eyebrow ? eyebrow.textContent.trim() : "") + " · Page: " + path + "\n\n" +
            blocks(content);
    }
    var preamble = "DotNetForge UI - the component library of DotNetForge CMS (ASP.NET Core MVC, server-rendered Razor). " +
        "Components are BEM classes with the dnf- prefix (block dnf-card, element dnf-card__title, modifier dnf-card--danger) " +
        "and <dnf-*> tag helpers that render the same classes; tokens are --dnf-* CSS custom properties; layout uses " +
        "Tailwind CSS v4 utilities; behaviours are data-* attributes handled by dnf.js (window.dnfUI). No inline script or " +
        "style (strict CSP). State is data-*/aria-* attributes, never is-* classes.";

    function allMarkdown() {
        var links = Array.prototype.map.call(document.querySelectorAll("[data-docs-nav] a"), function (a) { return a.getAttribute("href"); })
            .filter(function (href, i, all) { return href && all.indexOf(href) === i; });
        return Promise.all(links.map(function (href) {
            return fetch(href, { credentials: "same-origin" }).then(function (response) { return response.ok ? response.text() : ""; })
                .then(function (html) { return html ? pageMarkdown(new DOMParser().parseFromString(html, "text/html"), href) : ""; });
        })).then(function (pages) {
            return preamble + "\n\n---\n\n" + pages.filter(Boolean).join("\n\n---\n\n") + "\n";
        });
    }

    function copyText(produce, done) {
        // ClipboardItem with a promise keeps the click's permission while the text is still being built (Safari needs it).
        var text = produce();
        if (window.ClipboardItem && navigator.clipboard && navigator.clipboard.write) {
            var blob = Promise.resolve(text).then(function (value) { return new Blob([value], { type: "text/plain" }); });
            return navigator.clipboard.write([new ClipboardItem({ "text/plain": blob })]).then(function () { return text; }).then(done);
        }
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return Promise.resolve(text).then(function (value) { return navigator.clipboard.writeText(value).then(function () { return value; }); }).then(done);
        }
        return Promise.reject(new Error("no clipboard"));
    }

    document.addEventListener("click", function (event) {
        var button = event.target instanceof Element ? event.target.closest("[data-docs-copy]") : null;
        if (!button || !window.dnfUI) { return; }
        var all = button.getAttribute("data-docs-copy") === "all";
        var trigger = document.querySelector(".dnf-docs__page-meta .dnf-dropdown > button");
        if (trigger) { trigger.setAttribute("data-pending", "true"); }
        copyText(function () {
            return all ? allMarkdown() : preamble + "\n\n---\n\n" + pageMarkdown(document, window.location.pathname) + "\n";
        }, function (text) {
            var size = Math.max(1, Math.round(text.length / 1024));
            window.dnfUI.toast(all ? "Copied all docs as Markdown (" + size + " KB)." : "Copied this page as Markdown (" + size + " KB).", "success");
        }).catch(function () {
            window.dnfUI.toast("Could not copy. Your browser blocked the clipboard.", "danger");
        }).then(function () {
            if (trigger) { trigger.removeAttribute("data-pending"); }
        });
    });

    // ---- Keep the current page in view in the navigation ----------------------------------------------------------
    var active = document.querySelector(".dnf-docs__nav-link[aria-current='page']");
    var panel = document.querySelector(".dnf-docs__nav");
    if (active && panel && active.offsetTop > panel.clientHeight - 80) {
        panel.scrollTop = active.offsetTop - panel.clientHeight / 2;
    }
})();
