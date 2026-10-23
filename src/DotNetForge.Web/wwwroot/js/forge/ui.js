// Forge UI behaviours (.docs/architecture/design-system.md#behaviours). Progressive enhancement only: every screen
// works without JavaScript; these data-attribute hooks add convenience. No inline script anywhere (CSP), no
// dependencies. Public API for other scripts: window.Forge.toast(message, tone) and window.Forge.confirm(options).
(function () {
    "use strict";

    var doc = document;
    var storage = {
        get: function (key) { try { return window.localStorage.getItem(key); } catch (e) { return null; } },
        set: function (key, value) { try { window.localStorage.setItem(key, value); } catch (e) { /* blocked */ } },
        remove: function (key) { try { window.localStorage.removeItem(key); } catch (e) { /* blocked */ } }
    };
    // The sprite URL the server rendered (cache-busted), reused for icons created here.
    var spriteUse = doc.querySelector("svg use[href*='icons.svg']");
    var sprite = spriteUse ? spriteUse.getAttribute("href").split("#")[0] : "/img/icons.svg";
    function iconHtml(name, size) {
        return "<svg class=\"icon" + (size ? " icon-" + size : "") + "\" aria-hidden=\"true\" focusable=\"false\"><use href=\"" + sprite + "#" + name + "\"></use></svg>";
    }
    function $all(selector, root) { return Array.prototype.slice.call((root || doc).querySelectorAll(selector)); }
    function on(type, selector, handler) {
        doc.addEventListener(type, function (event) {
            var target = event.target instanceof Element ? event.target.closest(selector) : null;
            if (target) { handler(event, target); }
        });
    }

    // ---- JS-only controls ------------------------------------------------------------------------------------
    $all("[data-js-only]").forEach(function (el) { el.hidden = false; });

    // ---- Theme: light / dark / system --------------------------------------------------------------------------
    function currentTheme() { return storage.get("dnf-theme") || "system"; }
    function applyTheme(theme) {
        if (theme === "light" || theme === "dark") {
            doc.documentElement.setAttribute("data-theme", theme);
            storage.set("dnf-theme", theme);
        } else {
            doc.documentElement.removeAttribute("data-theme");
            storage.remove("dnf-theme");
        }
        $all("[data-theme-set]").forEach(function (button) {
            button.setAttribute("aria-checked", button.getAttribute("data-theme-set") === (theme || "system") ? "true" : "false");
        });
    }
    applyTheme(currentTheme());
    on("click", "[data-theme-set]", function (event, button) { applyTheme(button.getAttribute("data-theme-set")); });

    // ---- Menus: <button popovertarget="id"> + <div class="menu-list" id popover role="menu" data-placement> ------
    // The browser opens the list in the top layer and closes it on Escape and outside clicks (popover="auto" also
    // keeps one open at a time). Here: place it next to its button - below and right-aligned by default, flipped
    // when there is no room - keep it there while the page scrolls, move focus in for keyboard users, arrow keys
    // between items, and close it after a choice.
    function menuButton(list) { return doc.querySelector("[popovertarget='" + list.id + "']"); }
    function isOpen(list) { try { return list.matches(":popover-open"); } catch (e) { return false; } }
    function placeMenu(list) {
        var button = menuButton(list);
        if (!button) { return; }
        var r = button.getBoundingClientRect();
        var placement = list.getAttribute("data-placement") || "bottom-end";
        var width = list.offsetWidth, height = list.offsetHeight, gap = 4, margin = 8;
        var below = r.bottom + gap, above = r.top - gap - height;
        var top = placement.indexOf("top") === 0
            ? (above >= margin ? above : below)
            : (below + height <= window.innerHeight - margin || above < margin ? below : above);
        var left = /start$/.test(placement) ? r.left : r.right - width;
        list.style.top = Math.max(margin, top) + "px";
        list.style.left = Math.max(margin, Math.min(left, window.innerWidth - width - margin)) + "px";
    }
    function menuItems(list) { return $all(".menu-item", list).filter(function (item) { return !item.disabled; }); }
    doc.addEventListener("toggle", function (event) {
        var list = event.target;
        if (!(list instanceof HTMLElement) || !list.matches(".menu-list[popover]")) { return; }
        var button = menuButton(list);
        if (button) { button.setAttribute("aria-expanded", event.newState === "open" ? "true" : "false"); }
        if (event.newState !== "open") { return; }
        placeMenu(list);
        if (button && button.matches(":focus-visible")) {
            var first = menuItems(list)[0];
            if (first) { first.focus(); }
        }
    }, true);
    function placeOpenMenus() { $all(".menu-list[popover]").filter(isOpen).forEach(placeMenu); }
    window.addEventListener("scroll", placeOpenMenus, true);
    window.addEventListener("resize", placeOpenMenus);
    on("click", ".menu-list[popover] .menu-item", function (event, item) {
        var list = item.closest(".menu-list");
        if (list && isOpen(list)) { list.hidePopover(); }
    });
    on("keydown", ".menu-list[popover]", function (event, list) {
        if (event.key !== "ArrowDown" && event.key !== "ArrowUp" && event.key !== "Home" && event.key !== "End") { return; }
        var items = menuItems(list);
        if (items.length === 0) { return; }
        event.preventDefault();
        var index = items.indexOf(doc.activeElement);
        var next = event.key === "Home" ? 0
            : event.key === "End" ? items.length - 1
            : event.key === "ArrowDown" ? (index + 1) % items.length
            : (index - 1 + items.length) % items.length;
        items[next].focus();
    });

    // ---- Tooltips: [data-tooltip="…"] ---------------------------------------------------------------------------
    // One shared bubble, opened as a manual popover so it sits in the top layer: no scrolling or overflow-hidden
    // parent (the content tree, tables) clips it and no stacking context covers it. Shown on hover and on keyboard
    // focus; placed above the element, below when there is no room, and kept inside the window. Hidden on scroll,
    // pointer down and Escape. Without JS the element's aria-label still names it.
    var tip = null, tipFor = null;
    function showTip(el) {
        var text = el.getAttribute("data-tooltip");
        if (!text) { return; }
        if (!tip) {
            tip = doc.createElement("div");
            tip.className = "tooltip";
            tip.setAttribute("role", "tooltip");
            tip.setAttribute("popover", "manual");
            doc.body.appendChild(tip);
        }
        tipFor = el;
        tip.textContent = text;
        tip.style.left = "0px";
        tip.style.top = "0px";
        if (tip.showPopover && !tip.matches(":popover-open")) { tip.showPopover(); }
        var r = el.getBoundingClientRect();
        var width = tip.offsetWidth, height = tip.offsetHeight, gap = 8, margin = 8;
        var placement = r.top - gap - height >= margin ? "top" : "bottom";
        var top = placement === "top" ? r.top - gap - height : r.bottom + gap;
        var center = r.left + r.width / 2;
        var left = Math.max(margin, Math.min(center - width / 2, window.innerWidth - width - margin));
        tip.setAttribute("data-placement", placement);
        tip.style.top = top + "px";
        tip.style.left = left + "px";
        tip.style.setProperty("--tooltip-arrow-x", Math.max(8, Math.min(center - left, width - 8)) + "px");
    }
    function hideTip() {
        if (tip && tip.hidePopover && tip.matches(":popover-open")) { tip.hidePopover(); }
        tipFor = null;
    }
    on("mouseover", "[data-tooltip]", function (event, el) { if (el !== tipFor) { showTip(el); } });
    on("mouseout", "[data-tooltip]", function (event, el) {
        if (el === tipFor && !(event.relatedTarget instanceof Node && el.contains(event.relatedTarget))) { hideTip(); }
    });
    on("focusin", "[data-tooltip]", function (event, el) { if (el.matches(":focus-visible")) { showTip(el); } });
    on("focusout", "[data-tooltip]", function (event, el) { if (el === tipFor) { hideTip(); } });
    doc.addEventListener("pointerdown", hideTip, true);
    doc.addEventListener("keydown", function (event) { if (event.key === "Escape" && tipFor) { hideTip(); } });
    window.addEventListener("scroll", function () { if (tipFor) { hideTip(); } }, true);

    // ---- Tabs: <div data-tabs> with button[role=tab][data-tab] and [data-tab-panel] panels ---------------------------
    // Without JS every panel shows (stacked). Here: one panel at a time, arrow keys between tabs, the tab in the URL
    // hash (#seo) so a reload or a redirect after saving reopens it, and an optional form field [data-tab-field].
    $all("[data-tabs]").forEach(function (root) {
        var tabs = $all("[role=tab][data-tab]", root);
        var panels = $all("[data-tab-panel]", root);
        if (!tabs.length) { return; }
        var form = root.closest("form");
        var field = form ? form.querySelector("[data-tab-field]") : null;
        root.classList.add("is-tabbed");
        function select(name, focus, updateHash) {
            var tab = tabs.find(function (t) { return t.getAttribute("data-tab") === name; }) || tabs[0];
            name = tab.getAttribute("data-tab");
            tabs.forEach(function (t) {
                var on = t === tab;
                t.setAttribute("aria-selected", on ? "true" : "false");
                t.tabIndex = on ? 0 : -1;
            });
            panels.forEach(function (p) { p.hidden = p.getAttribute("data-tab-panel") !== name; });
            if (field) { field.value = name; }
            if (focus) { tab.focus(); }
            if (updateHash && location.hash.slice(1) !== name) { history.replaceState(null, "", "#" + name); }
        }
        var fromHash = location.hash.slice(1);
        select(tabs.some(function (t) { return t.getAttribute("data-tab") === fromHash; }) ? fromHash : root.getAttribute("data-initial-tab"), false, false);
        tabs.forEach(function (tab, i) {
            tab.addEventListener("click", function () { select(tab.getAttribute("data-tab"), false, true); });
            tab.addEventListener("keydown", function (e) {
                var next = e.key === "ArrowRight" ? i + 1 : e.key === "ArrowLeft" ? i - 1 : e.key === "Home" ? 0 : e.key === "End" ? tabs.length - 1 : null;
                if (next === null) { return; }
                e.preventDefault();
                select(tabs[(next + tabs.length) % tabs.length].getAttribute("data-tab"), true, true);
            });
        });
        window.addEventListener("hashchange", function () {
            var name = location.hash.slice(1);
            if (tabs.some(function (t) { return t.getAttribute("data-tab") === name; })) { select(name, false, false); }
        });
        // A field that fails native validation on a hidden tab: show its tab first.
        if (form) {
            form.addEventListener("invalid", function (e) {
                var panel = e.target.closest("[data-tab-panel]");
                if (panel && panel.hidden) { select(panel.getAttribute("data-tab-panel"), false, true); }
            }, true);
        }
    });

    // ---- Drawer navigation (below 64rem) ---------------------------------------------------------------------
    var shell = doc.querySelector("[data-shell]");
    var backdrop = doc.querySelector(".sidebar-backdrop");
    var opener = doc.querySelector("[data-drawer-open]");
    function setDrawer(open) {
        if (!shell) { return; }
        shell.classList.toggle("is-nav-open", open);
        if (backdrop) { backdrop.hidden = !open; }
        if (opener) { opener.setAttribute("aria-expanded", open ? "true" : "false"); }
        if (open) {
            var first = doc.querySelector(".sidebar .nav-link[aria-current]") || doc.querySelector(".sidebar .nav-link");
            if (first) { first.focus(); }
        } else if (opener && shell.contains(doc.activeElement) && doc.activeElement.closest(".sidebar")) {
            opener.focus();
        }
    }
    on("click", "[data-drawer-open]", function () { setDrawer(true); });
    on("click", "[data-drawer-close]", function () { setDrawer(false); });
    doc.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && shell && shell.classList.contains("is-nav-open")) { setDrawer(false); }
    });
    window.matchMedia("(min-width: 64.0625rem)").addEventListener("change", function (mq) { if (mq.matches) { setDrawer(false); } });

    // ---- Sidebar groups remember being opened or closed (the active group always stays open) ----------------
    var navState = {};
    try { navState = JSON.parse(storage.get("dnf-nav") || "{}") || {}; } catch (e) { navState = {}; }
    $all("details[data-nav-group]").forEach(function (group) {
        var key = group.getAttribute("data-nav-group");
        if (!group.hasAttribute("data-nav-active") && Object.prototype.hasOwnProperty.call(navState, key)) {
            group.open = !!navState[key];
        }
        group.addEventListener("toggle", function () {
            navState[key] = group.open;
            storage.set("dnf-nav", JSON.stringify(navState));
        });
    });

    // ---- Toasts --------------------------------------------------------------------------------------------
    var region = doc.querySelector("[data-toasts]");
    var template = doc.getElementById("toast-template");
    var toastIcons = { success: "circle-check", danger: "circle-x", info: "info", warning: "triangle-alert" };

    function dismiss(toast) {
        toast.classList.add("is-leaving");
        window.setTimeout(function () { toast.remove(); }, 200);
    }
    function arm(toast) {
        if (toast.hasAttribute("data-autohide")) {
            var timer = window.setTimeout(function () { dismiss(toast); }, 6000);
            toast.addEventListener("mouseenter", function () { window.clearTimeout(timer); });
            toast.addEventListener("focusin", function () { window.clearTimeout(timer); });
        }
    }
    function toast(message, tone) {
        if (!region || !template) { return; }
        tone = tone || "info";
        var node = template.content.firstElementChild.cloneNode(true);
        node.classList.add("toast-" + tone);
        node.setAttribute("role", tone === "danger" ? "alert" : "status");
        if (tone !== "danger") { node.setAttribute("data-autohide", "true"); }
        node.querySelector(".toast-message").textContent = message;
        var use = node.querySelector("svg use");
        if (use) { use.setAttribute("href", use.getAttribute("href").replace(/#.*$/, "#" + (toastIcons[tone] || "info"))); }
        region.appendChild(node);
        arm(node);
    }
    if (region) {
        // Toasts rendered by the server: re-insert so screen readers announce them, then arm auto-hide.
        $all("[data-toast]", region).forEach(function (node) { region.appendChild(node); arm(node); });
    }
    on("click", "[data-toast-close]", function (event, button) { dismiss(button.closest("[data-toast]")); });

    // ---- Confirmation dialog: <form data-confirm="Question?" data-confirm-action="Delete"> ------------------------
    var dialog = doc.getElementById("confirm-dialog");
    function confirmAsync(options) {
        if (!dialog || typeof dialog.showModal !== "function") {
            return Promise.resolve(window.confirm(options.text));
        }
        dialog.querySelector(".dialog-title").textContent = options.title || "Are you sure?";
        dialog.querySelector(".dialog-text").textContent = options.text || "";
        var ok = dialog.querySelector("[data-confirm-ok]");
        var danger = options.tone !== "accent";
        ok.textContent = options.action || "Confirm";
        ok.className = danger ? "btn btn-danger" : "btn btn-primary";
        dialog.querySelector("[data-confirm-icon]").className = danger ? "dialog-icon danger" : "dialog-icon";
        return new Promise(function (resolve) {
            dialog.addEventListener("close", function handler() {
                dialog.removeEventListener("close", handler);
                resolve(dialog.returnValue === "confirm");
            });
            dialog.returnValue = "";
            dialog.showModal();
            ok.focus();
        });
    }
    doc.addEventListener("submit", function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement)) { return; }
        var source = event.submitter && event.submitter.hasAttribute("data-confirm") ? event.submitter : form;
        var question = source.getAttribute("data-confirm");
        if (!question || form.dataset.confirmed === "true") { delete form.dataset.confirmed; return; }
        event.preventDefault();
        var submitter = event.submitter;
        confirmAsync({
            title: source.getAttribute("data-confirm-title"),
            text: question,
            action: source.getAttribute("data-confirm-action"),
            tone: source.getAttribute("data-confirm-tone")
        }).then(function (confirmed) {
            if (!confirmed) { return; }
            form.dataset.confirmed = "true";
            if (typeof form.requestSubmit === "function") { form.requestSubmit(submitter || undefined); } else { form.submit(); }
        });
    });

    // ---- Collections: search, filter, sort, paginate, select (tables, card grids) ---------------------------
    // <div data-collection data-page-size="25"> … [data-item] elements … </div>
    //   search:   <input data-collection-search>            (matches the item's text or data-search)
    //   filter:   <select data-collection-filter="status">   (matches a token of the item's data-status)
    //             <button data-collection-filter="status" data-value="enabled" aria-pressed="…">  (segmented)
    //   sort:     <th data-sort="text|number|date">          (cell text or data-sort-value; tables only)
    //   paging:   <div data-collection-pagination></div>      count:  <span data-collection-count></span>
    //   no match: <… data-collection-empty hidden>
    //   select:   <input type="checkbox" data-select-all> / <input type="checkbox" data-select-item form="bulk" name="ids">
    //             <… data-bulk-bar hidden> <span data-bulk-count></span>
    function initCollection(scope) {
        var items = $all("[data-item]", scope);
        var pageSize = parseInt(scope.getAttribute("data-page-size") || "0", 10);
        var page = 1;
        var search = scope.querySelector("[data-collection-search]");
        var pager = scope.querySelector("[data-collection-pagination]");
        var count = scope.querySelector("[data-collection-count]");
        var empty = scope.querySelector("[data-collection-empty]");

        function filters() {
            var active = [];
            $all("select[data-collection-filter]", scope).forEach(function (select) {
                if (select.value) { active.push([select.getAttribute("data-collection-filter"), select.value]); }
            });
            $all("button[data-collection-filter][aria-pressed='true']", scope).forEach(function (button) {
                var value = button.getAttribute("data-value");
                if (value) { active.push([button.getAttribute("data-collection-filter"), value]); }
            });
            return active;
        }
        function matches(item, query, active) {
            if (query) {
                var text = (item.getAttribute("data-search") || item.textContent || "").toLowerCase();
                if (text.indexOf(query) === -1) { return false; }
            }
            return active.every(function (pair) {
                var tokens = (item.getAttribute("data-" + pair[0]) || "").toLowerCase().split(/\s+/);
                return tokens.indexOf(pair[1].toLowerCase()) !== -1;
            });
        }
        function render() {
            var query = search ? search.value.trim().toLowerCase() : "";
            var active = filters();
            var visible = items.filter(function (item) { return matches(item, query, active); });
            var pages = pageSize > 0 ? Math.max(1, Math.ceil(visible.length / pageSize)) : 1;
            if (page > pages) { page = pages; }
            var first = pageSize > 0 ? (page - 1) * pageSize : 0;
            var last = pageSize > 0 ? first + pageSize : visible.length;
            items.forEach(function (item) { item.hidden = true; });
            visible.slice(first, last).forEach(function (item) { item.hidden = false; });
            if (empty) { empty.hidden = visible.length !== 0 || items.length === 0; }
            if (count) { count.textContent = visible.length === items.length ? items.length + " total" : visible.length + " of " + items.length; }
            if (pager) { renderPager(pager, pages, visible.length, first, Math.min(last, visible.length)); }
        }
        function renderPager(container, pages, total, first, last) {
            container.hidden = pages <= 1;
            container.textContent = "";
            if (pages <= 1) { return; }
            var summary = doc.createElement("span");
            summary.textContent = (first + 1) + "-" + last + " of " + total;
            var nav = doc.createElement("div");
            nav.className = "pagination-pages";
            function add(label, target, current, aria) {
                var button = doc.createElement("button");
                button.type = "button";
                button.className = "btn btn-sm";
                button.textContent = label;
                if (aria) { button.setAttribute("aria-label", aria); }
                if (current) { button.setAttribute("aria-current", "page"); }
                if (target < 1 || target > pages) { button.disabled = true; }
                button.addEventListener("click", function () { page = target; render(); scope.scrollIntoView({ block: "nearest" }); });
                nav.appendChild(button);
            }
            add("‹", page - 1, false, "Previous page");
            for (var p = 1; p <= pages; p++) {
                if (p === 1 || p === pages || Math.abs(p - page) <= 1) { add(String(p), p, p === page, "Page " + p); }
                else if (Math.abs(p - page) === 2) { var gap = doc.createElement("span"); gap.textContent = "…"; nav.appendChild(gap); }
            }
            add("›", page + 1, false, "Next page");
            container.appendChild(summary);
            container.appendChild(nav);
        }

        if (search) { search.addEventListener("input", function () { page = 1; render(); }); }
        $all("select[data-collection-filter]", scope).forEach(function (select) {
            select.addEventListener("change", function () { page = 1; render(); });
        });
        $all("button[data-collection-filter]", scope).forEach(function (button) {
            button.addEventListener("click", function () {
                var key = button.getAttribute("data-collection-filter");
                $all("button[data-collection-filter='" + key + "']", scope).forEach(function (b) { b.setAttribute("aria-pressed", b === button ? "true" : "false"); });
                page = 1;
                render();
            });
        });

        // Sorting (tables).
        $all("th[data-sort]", scope).forEach(function (th) {
            var table = th.closest("table");
            var index = Array.prototype.indexOf.call(th.parentNode.children, th);
            var label = th.textContent.trim();
            th.textContent = "";
            var button = doc.createElement("button");
            button.type = "button";
            button.className = "th-sort";
            button.innerHTML = "<span></span>" + iconHtml("sort", "sm");
            button.firstChild.textContent = label;
            th.appendChild(button);
            button.addEventListener("click", function () {
                var direction = th.getAttribute("aria-sort") === "ascending" ? "descending" : "ascending";
                $all("th[aria-sort]", table).forEach(function (other) { other.removeAttribute("aria-sort"); });
                th.setAttribute("aria-sort", direction);
                var type = th.getAttribute("data-sort");
                var body = table.tBodies[0];
                var rows = items.filter(function (item) { return item.parentNode === body; });
                function key(row) {
                    var cell = row.children[index];
                    var raw = cell ? (cell.getAttribute("data-sort-value") || cell.textContent.trim()) : "";
                    if (type === "number") { return parseFloat(raw) || 0; }
                    if (type === "date") { return Date.parse(raw) || 0; }
                    return raw.toLowerCase();
                }
                rows.sort(function (a, b) {
                    var x = key(a), y = key(b);
                    var result = x < y ? -1 : x > y ? 1 : 0;
                    return direction === "ascending" ? result : -result;
                });
                rows.forEach(function (row) { body.appendChild(row); });
                items = $all("[data-item]", scope);
                render();
            });
        });

        // Selection + bulk bar.
        var selectAlls = $all("[data-select-all]", scope);
        var bulkBar = scope.querySelector("[data-bulk-bar]");
        var bulkCount = scope.querySelector("[data-bulk-count]");
        function syncSelection() {
            var boxes = $all("[data-select-item]", scope);
            var checked = boxes.filter(function (box) { return box.checked; });
            boxes.forEach(function (box) {
                var holder = box.closest("[data-item]");
                if (holder) { holder.classList.toggle("is-selected", box.checked); }
            });
            selectAlls.forEach(function (selectAll) {
                selectAll.checked = checked.length > 0 && checked.length === boxes.length;
                selectAll.indeterminate = checked.length > 0 && checked.length < boxes.length;
            });
            if (bulkBar) { bulkBar.hidden = checked.length === 0; }
            if (bulkCount) { bulkCount.textContent = checked.length + " selected"; }
        }
        selectAlls.forEach(function (selectAll) {
            selectAll.addEventListener("change", function () {
                $all("[data-select-item]", scope).forEach(function (box) {
                    var holder = box.closest("[data-item]");
                    if (!holder || !holder.hidden) { box.checked = selectAll.checked; }
                });
                syncSelection();
            });
        });
        scope.addEventListener("change", function (event) { if (event.target.matches && event.target.matches("[data-select-item]")) { syncSelection(); } });
        on("click", "[data-bulk-clear]", function () { $all("[data-select-item]", scope).forEach(function (b) { b.checked = false; }); syncSelection(); });

        syncSelection();
        render();
    }
    $all("[data-collection]").forEach(initCollection);

    // ---- View switch (grid / list), remembered per screen ----------------------------------------------------
    // <div class="segmented" data-view-switch="media" data-view-target="#items"> <button data-view-button="grid"> …
    // Sets data-view-mode on the target; CSS lays the same items out as cards or rows (one DOM, one checkbox each).
    $all("[data-view-switch]").forEach(function (group) {
        var key = "dnf-view:" + group.getAttribute("data-view-switch");
        var target = doc.querySelector(group.getAttribute("data-view-target"));
        if (!target) { return; }
        function show(view) {
            target.setAttribute("data-view-mode", view);
            $all("[data-view-button]", group).forEach(function (b) { b.setAttribute("aria-pressed", b.getAttribute("data-view-button") === view ? "true" : "false"); });
        }
        show(storage.get(key) || group.getAttribute("data-default") || "grid");
        $all("[data-view-button]", group).forEach(function (button) {
            button.addEventListener("click", function () { var view = button.getAttribute("data-view-button"); storage.set(key, view); show(view); });
        });
    });

    // ---- Check-all groups: <input data-check-group="content"> + <input data-check-member="content"> -----------
    function syncGroup(name) {
        var master = doc.querySelector("[data-check-group='" + name + "']");
        var members = $all("[data-check-member='" + name + "']");
        if (!master) { return; }
        var checked = members.filter(function (m) { return m.checked; }).length;
        master.checked = checked > 0 && checked === members.length;
        master.indeterminate = checked > 0 && checked < members.length;
    }
    $all("[data-check-group]").forEach(function (master) { syncGroup(master.getAttribute("data-check-group")); });
    doc.addEventListener("change", function (event) {
        var el = event.target;
        if (!(el instanceof HTMLInputElement)) { return; }
        if (el.hasAttribute("data-check-group")) {
            var name = el.getAttribute("data-check-group");
            $all("[data-check-member='" + name + "']").forEach(function (m) { m.checked = el.checked; });
        } else if (el.hasAttribute("data-check-member")) {
            syncGroup(el.getAttribute("data-check-member"));
        }
    });

    // ---- Copy to clipboard: <button data-copy-target="#id"> or data-copy-text="…" ---------------------------
    on("click", "[data-copy-target], [data-copy-text]", function (event, button) {
        var text = button.getAttribute("data-copy-text");
        if (text === null) {
            var source = doc.querySelector(button.getAttribute("data-copy-target"));
            text = source ? (source.value !== undefined && source.tagName === "INPUT" ? source.value : source.textContent) : "";
        }
        if (!navigator.clipboard) { toast("Copying is not available in this browser.", "danger"); return; }
        navigator.clipboard.writeText(text.trim()).then(function () { toast("Copied to clipboard.", "success"); },
            function () { toast("Could not copy. Select the text and copy it manually.", "danger"); });
    });

    // ---- Password reveal: <button data-reveal="#password" aria-pressed="false"> --------------------------------
    on("click", "[data-reveal]", function (event, button) {
        var input = doc.querySelector(button.getAttribute("data-reveal"));
        if (!input) { return; }
        var show = input.type === "password";
        input.type = show ? "text" : "password";
        button.setAttribute("aria-pressed", show ? "true" : "false");
        button.setAttribute("aria-label", show ? "Hide password" : "Show password");
        // Icons are <svg> elements, which have no .hidden property: toggle the attribute.
        $all("[data-reveal-icon]", button).forEach(function (icon) { icon.toggleAttribute("hidden", icon.getAttribute("data-reveal-icon") !== (show ? "hide" : "show")); });
    });

    // ---- File dropzone: shows the chosen files and the drag-over state ----------------------------------------
    $all(".dropzone").forEach(function (zone) {
        var input = zone.querySelector("input[type=file]");
        var list = zone.querySelector("[data-dropzone-files]");
        if (!input) { return; }
        ["dragenter", "dragover"].forEach(function (type) { zone.addEventListener(type, function () { zone.classList.add("is-dragover"); }); });
        ["dragleave", "drop"].forEach(function (type) { zone.addEventListener(type, function () { zone.classList.remove("is-dragover"); }); });
        input.addEventListener("change", function () {
            if (!list) { return; }
            list.textContent = "";
            Array.prototype.forEach.call(input.files, function (file) {
                var badge = doc.createElement("span");
                badge.className = "badge badge-accent";
                badge.textContent = file.name;
                list.appendChild(badge);
            });
        });
    });

    // ---- Fill a form from a row: <button data-fill="#form" data-fill-key="…" data-fill-value="…"> --------------
    on("click", "[data-fill]", function (event, button) {
        var form = doc.querySelector(button.getAttribute("data-fill"));
        if (!form) { return; }
        Array.prototype.forEach.call(button.attributes, function (attr) {
            if (attr.name.indexOf("data-fill-") !== 0) { return; }
            var field = form.elements.namedItem(attr.name.substring("data-fill-".length));
            if (field) { field.value = attr.value; }
        });
        var first = form.querySelector("input, select, textarea");
        if (first) { first.focus(); }
    });

    // ---- Embedded admin extension: hide the skeleton once the frame has loaded ---------------------------------
    $all("iframe[data-frame]").forEach(function (frame) {
        var card = frame.closest(".ext-frame-card");
        function loaded() { if (card) { card.classList.add("is-loaded"); } }
        frame.addEventListener("load", loaded);
        try { if (frame.contentDocument && frame.contentDocument.readyState === "complete" && frame.contentDocument.URL !== "about:blank") { loaded(); } } catch (e) { loaded(); }
    });

    window.Forge = { toast: toast, confirm: confirmAsync };
})();
