// DotNetForge UI behaviours (.docs/ui/behaviours.md). Progressive enhancement only: every screen works without
// JavaScript; these data-attribute hooks add convenience. No inline script anywhere (CSP), no dependencies.
// Public API (window.dnfUI):
//   dnfUI.toast(message, tone)       tone: "success" | "info" | "warning" | "danger"
//   dnfUI.confirm({ title, text, action, tone }) → Promise<boolean>   tone "primary" for a non-destructive question
//   dnfUI.init(root)                 wires the per-element behaviours inside content added after the page loaded
//   dnfUI.icon(name, size, class)    the markup of a sprite icon (size "sm" | "lg" | "xl" | undefined; extra classes)
// State is exposed as data attributes (data-selected, data-dragging, data-open, data-tabbed, data-loaded, ...), never
// as classes, so the component CSS and the markup share one vocabulary (.docs/ui/README.md#states).
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
    var sprite = spriteUse ? spriteUse.getAttribute("href").split("#")[0] : "/_content/DotNetForge.UI/icons.svg";
    function iconHtml(name, size, className) {
        return "<svg class=\"dnf-icon" + (size ? " dnf-icon--" + size : "") + (className ? " " + className : "") + "\" aria-hidden=\"true\" focusable=\"false\"><use href=\"" + sprite + "#" + name + "\"></use></svg>";
    }
    function $all(selector, root) { return Array.prototype.slice.call((root || doc).querySelectorAll(selector)); }
    // Elements under root (root included) that match; each behaviour runs once per element. One set per selector:
    // an element can carry several behaviours (data-js-only and data-view-switch), and each must still run.
    var initialized = {};
    function each(selector, root, fn) {
        var done = initialized[selector] || (initialized[selector] = new WeakSet());
        var found = $all(selector, root);
        if (root && root !== doc && root.matches && root.matches(selector)) { found.unshift(root); }
        found.forEach(function (el, index) {
            if (done.has(el)) { return; }
            done.add(el);
            fn(el, index);
        });
    }
    function on(type, selector, handler) {
        doc.addEventListener(type, function (event) {
            var target = event.target instanceof Element ? event.target.closest(selector) : null;
            if (target) { handler(event, target); }
        });
    }

    // ---- Theme: light / dark / system ([data-theme-set="light|dark|system"], aria-checked) ---------------------
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
    on("click", "[data-theme-set]", function (event, button) { applyTheme(button.getAttribute("data-theme-set")); });

    // ---- Dropdowns: <button popovertarget="id"> + <div class="dnf-dropdown__menu" id popover role="menu"> --------
    // The browser opens the menu in the top layer and closes it on Escape and outside clicks (popover="auto" also
    // keeps one open at a time). Here: place it next to its button - below and end-aligned by default, flipped when
    // there is no room - keep it there while the page scrolls, move focus in for keyboard users, arrow keys between
    // items, and close it after a choice. The same placement serves .dnf-popover panels.
    var floating = ".dnf-dropdown__menu[popover], .dnf-popover[popover]";
    function opener(panel) { return doc.querySelector("[popovertarget='" + panel.id + "']"); }
    function isOpen(panel) { try { return panel.matches(":popover-open"); } catch (e) { return false; } }
    function place(panel) {
        var button = opener(panel);
        if (!button) { return; }
        var r = button.getBoundingClientRect();
        var placement = panel.getAttribute("data-placement") || "bottom-end";
        var width = panel.offsetWidth, height = panel.offsetHeight, gap = 4, margin = 8;
        var below = r.bottom + gap, above = r.top - gap - height;
        var top = placement.indexOf("top") === 0
            ? (above >= margin ? above : below)
            : (below + height <= window.innerHeight - margin || above < margin ? below : above);
        var left = /start$/.test(placement) ? r.left : r.right - width;
        panel.style.top = Math.max(margin, top) + "px";
        panel.style.left = Math.max(margin, Math.min(left, window.innerWidth - width - margin)) + "px";
    }
    function menuItems(menu) { return $all(".dnf-dropdown__item", menu).filter(function (item) { return !item.disabled && item.getAttribute("aria-disabled") !== "true"; }); }
    doc.addEventListener("toggle", function (event) {
        var panel = event.target;
        if (!(panel instanceof HTMLElement) || !panel.matches(floating)) { return; }
        var open = event.newState === "open";
        var button = opener(panel);
        if (button) { button.setAttribute("aria-expanded", open ? "true" : "false"); }
        panel.toggleAttribute("data-open", open);
        if (!open) { return; }
        place(panel);
        if (button && button.matches(":focus-visible") && panel.matches(".dnf-dropdown__menu")) {
            var first = menuItems(panel)[0];
            if (first) { first.focus(); }
        }
    }, true);
    function placeOpen() { $all(floating).filter(isOpen).forEach(place); }
    window.addEventListener("scroll", placeOpen, true);
    window.addEventListener("resize", placeOpen);
    on("click", ".dnf-dropdown__menu[popover] .dnf-dropdown__item", function (event, item) {
        var menu = item.closest(".dnf-dropdown__menu");
        if (menu && isOpen(menu)) { menu.hidePopover(); }
    });
    on("keydown", ".dnf-dropdown__menu[popover]", function (event, menu) {
        if (event.key !== "ArrowDown" && event.key !== "ArrowUp" && event.key !== "Home" && event.key !== "End") { return; }
        var items = menuItems(menu);
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
    // parent clips it and no stacking context covers it. Shown on hover and on keyboard focus; placed above the
    // element, below when there is no room, and kept inside the window. Hidden on scroll, pointer down and Escape.
    // Without JS the element's aria-label still names it.
    var tip = null, tipFor = null;
    function showTip(el) {
        var text = el.getAttribute("data-tooltip");
        if (!text) { return; }
        if (!tip) {
            tip = doc.createElement("div");
            tip.className = "dnf-tooltip";
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
        tip.style.setProperty("--dnf-tooltip-arrow-x", Math.max(8, Math.min(center - left, width - 8)) + "px");
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

    // ---- Tabs: <div data-tabs> with button[role=tab][data-tab] and [data-tab-panel] panels -----------------------
    // Without JS every panel shows (stacked). dnf-theme.js opens the right tab before the first paint, picking it the
    // same way as here. Here: one panel at a time (the root gets data-tabbed="true"), arrow keys
    // between tabs, the tab in the URL hash (#seo) so a reload or a redirect after saving reopens it - unless the root
    // has data-tab-hash="false" (many small tab groups on one page) - and an optional form field [data-tab-field] that
    // receives the open tab's name.
    function initTabs(root) {
        // Only this root's own tabs and panels: a panel can hold another tab group.
        function own(el) { return el.closest("[data-tabs]") === root; }
        var tabs = $all("[role=tab][data-tab]", root).filter(own);
        var panels = $all("[data-tab-panel]", root).filter(own);
        if (!tabs.length) { return; }
        var form = root.closest("form");
        var field = form ? form.querySelector("[data-tab-field]") : null;
        var useHash = root.getAttribute("data-tab-hash") !== "false";
        root.setAttribute("data-tabbed", "true");
        function select(name, focus, updateHash) {
            var tab = tabs.find(function (t) { return t.getAttribute("data-tab") === name; }) || tabs[0];
            name = tab.getAttribute("data-tab");
            tabs.forEach(function (t) {
                var selected = t === tab;
                t.setAttribute("aria-selected", selected ? "true" : "false");
                t.tabIndex = selected ? 0 : -1;
            });
            panels.forEach(function (p) { p.hidden = p.getAttribute("data-tab-panel") !== name; });
            if (field) { field.value = name; }
            if (focus) { tab.focus(); }
            if (updateHash && useHash && location.hash.slice(1) !== name) { history.replaceState(null, "", "#" + name); }
        }
        var fromHash = useHash ? location.hash.slice(1) : "";
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
            if (!useHash) { return; }
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
    }

    // ---- Drawer navigation: [data-shell] root, [data-drawer-panel], [data-drawer-open], [data-drawer-close] ------
    // Below the breakpoint the panel slides in (the shell gets data-nav-open="true"); Escape and the backdrop close it.
    var shell = doc.querySelector("[data-shell]");
    function setDrawer(open) {
        if (!shell) { return; }
        var panel = shell.querySelector("[data-drawer-panel]");
        var openButton = doc.querySelector("[data-drawer-open]");
        shell.setAttribute("data-nav-open", open ? "true" : "false");
        $all("[data-drawer-backdrop]").forEach(function (backdrop) { backdrop.hidden = !open; });
        if (openButton) { openButton.setAttribute("aria-expanded", open ? "true" : "false"); }
        if (open && panel) {
            var first = panel.querySelector("a[aria-current]") || panel.querySelector("a[href]");
            if (first) { first.focus(); }
        } else if (openButton && panel && panel.contains(doc.activeElement)) {
            openButton.focus();
        }
    }
    on("click", "[data-drawer-open]", function () { setDrawer(true); });
    on("click", "[data-drawer-close]", function () { setDrawer(false); });
    doc.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && shell && shell.getAttribute("data-nav-open") === "true") { setDrawer(false); }
    });
    window.matchMedia("(min-width: 64.0625rem)").addEventListener("change", function (mq) { if (mq.matches) { setDrawer(false); } });

    // ---- A form that posts back to the same screen keeps its scroll position ----------------------------------
    // Every POST redirects back (post/redirect/get), which would start the screen at the top again. Only a POST
    // that lands on the same URL within a short time restores; links, reloads and Back behave as usual.
    (function () {
        var key = "dnf-scroll";
        var here = window.location.pathname + window.location.search;
        try {
            var saved = JSON.parse(window.sessionStorage.getItem(key) || "null");
            window.sessionStorage.removeItem(key);
            if (saved && saved.url === here && Date.now() - saved.at < 30000) {
                // After every deferred screen script: those can still change the layout.
                doc.addEventListener("DOMContentLoaded", function () { window.scrollTo(0, saved.y); });
            }
        } catch (e) { /* storage blocked */ }
        // On window, so data-confirm (which cancels the first submit and resubmits after Yes) has already run.
        window.addEventListener("submit", function (event) {
            var form = event.target;
            if (event.defaultPrevented || (form.getAttribute("method") || "").toLowerCase() !== "post") { return; }
            try {
                window.sessionStorage.setItem(key, JSON.stringify({ url: here, y: window.scrollY, at: Date.now() }));
            } catch (e) { /* storage blocked */ }
        });
    })();

    // ---- Toasts: [data-toasts] region + <template id="dnf-toast-template"> ----------------------------------------
    var toastIcons = { success: "circle-check", danger: "circle-x", info: "info", warning: "triangle-alert" };
    function dismiss(node) {
        node.setAttribute("data-leaving", "true");
        window.setTimeout(function () { node.remove(); }, 200);
    }
    function arm(node) {
        if (node.getAttribute("data-autohide") === "true") {
            var timer = window.setTimeout(function () { dismiss(node); }, 6000);
            node.addEventListener("mouseenter", function () { window.clearTimeout(timer); });
            node.addEventListener("focusin", function () { window.clearTimeout(timer); });
        }
    }
    function toast(message, tone) {
        var region = doc.querySelector("[data-toasts]");
        var template = doc.getElementById("dnf-toast-template");
        if (!region || !template) { return; }
        tone = toastIcons[tone] ? tone : "info";
        var node = template.content.firstElementChild.cloneNode(true);
        node.classList.add("dnf-toast--" + tone);
        node.setAttribute("role", tone === "danger" ? "alert" : "status");
        if (tone !== "danger") { node.setAttribute("data-autohide", "true"); }
        node.querySelector(".dnf-toast__message").textContent = message;
        var use = node.querySelector(".dnf-toast__icon use");
        if (use) { use.setAttribute("href", use.getAttribute("href").replace(/#.*$/, "#" + toastIcons[tone])); }
        region.appendChild(node);
        arm(node);
    }
    on("click", "[data-toast-close]", function (event, button) { dismiss(button.closest("[data-toast]")); });

    // ---- Confirmation dialog: <form data-confirm="Question?" data-confirm-action="Delete"> ------------------------
    // The dialog (#dnf-confirm-dialog) has [data-confirm-title], [data-confirm-text], [data-confirm-icon] and
    // [data-confirm-ok]; without it (or without <dialog> support) window.confirm asks.
    function confirmAsync(options) {
        var dialog = doc.getElementById("dnf-confirm-dialog");
        if (!dialog || typeof dialog.showModal !== "function") {
            return Promise.resolve(window.confirm(options.text));
        }
        dialog.querySelector("[data-confirm-title]").textContent = options.title || "Are you sure?";
        dialog.querySelector("[data-confirm-text]").textContent = options.text || "";
        var ok = dialog.querySelector("[data-confirm-ok]");
        var danger = options.tone !== "primary";
        ok.textContent = options.action || "Confirm";
        ok.className = danger ? "dnf-button dnf-button--danger" : "dnf-button dnf-button--primary";
        dialog.querySelector("[data-confirm-icon]").className = danger ? "dnf-dialog__icon dnf-dialog__icon--danger" : "dnf-dialog__icon";
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

    // ---- Dialogs: <button data-dialog-open="#id"> opens a <dialog class="dnf-dialog"> as a modal ---------------------
    // [data-dialog-close] inside a dialog closes it; a click on the backdrop closes a dialog marked data-dialog-light-dismiss.
    on("click", "[data-dialog-open]", function (event, button) {
        var dialog = doc.querySelector(button.getAttribute("data-dialog-open"));
        if (dialog && typeof dialog.showModal === "function" && !dialog.open) { event.preventDefault(); dialog.showModal(); }
    });
    on("click", "[data-dialog-close]", function (event, button) {
        var dialog = button.closest("dialog");
        if (dialog && dialog.open) { event.preventDefault(); dialog.close(); }
    });
    doc.addEventListener("click", function (event) {
        var dialog = event.target;
        if (dialog instanceof HTMLDialogElement && dialog.open && dialog.hasAttribute("data-dialog-light-dismiss")) { dialog.close(); }
    });

    // ---- Pending buttons: <form data-pending-on-submit> marks its submit button busy until the next page -------
    doc.addEventListener("submit", function (event) {
        var form = event.target;
        if (event.defaultPrevented || !(form instanceof HTMLFormElement) || !form.hasAttribute("data-pending-on-submit")) { return; }
        var button = event.submitter || form.querySelector("[type=submit]");
        if (button) {
            button.setAttribute("data-pending", "true");
            button.setAttribute("aria-busy", "true");
        }
    });

    // ---- Collections: search, filter, sort, paginate, select (tables, card grids) ---------------------------
    // <div data-collection data-page-size="25"> … [data-item] elements … </div>
    //   search:   <input data-collection-search>            (matches the item's text or data-search)
    //   filter:   <select data-collection-filter="status">   (matches a token of the item's data-status)
    //             <button data-collection-filter="status" data-value="enabled" aria-pressed="…">  (segmented)
    //   sort:     <th data-sort="text|number|date">          (cell text or data-sort-value; tables only)
    //   paging:   <div class="dnf-pagination" data-collection-pagination hidden></div>   count: <span data-collection-count>
    //             (rows-per-page select, chevron buttons; the chosen size is remembered per screen in localStorage)
    //   no match: <… data-collection-empty hidden>
    //   select:   <input type="checkbox" data-select-all> / <input type="checkbox" data-select-item form="bulk" name="ids">
    //             <… data-bulk-bar hidden> <span data-bulk-count></span>; a selected item gets data-selected="true"
    var PageSizes = [10, 25, 50, 100];

    function initCollection(scope, index) {
        var items = $all("[data-item]", scope);
        var pageSize = parseInt(scope.getAttribute("data-page-size") || "0", 10);
        var sizeKey = "dnf-page-size:" + window.location.pathname + ":" + (index || 0);
        var saved = parseInt(storage.get(sizeKey) || "", 10);
        if (pageSize > 0 && PageSizes.indexOf(saved) !== -1) { pageSize = saved; }
        var page = 1;
        var search = scope.querySelector("[data-collection-search]");
        var pager = scope.querySelector("[data-collection-pagination]");
        var count = scope.querySelector("[data-collection-count]");
        // What the server wrote stays until a search or filter changes it: rewriting it on load makes it jump.
        var allCount = count ? count.textContent.trim() : "";
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
            if (count) { count.textContent = visible.length === items.length ? allCount || items.length + " total" : visible.length + " of " + items.length; }
            if (pager) { renderPager(pager, pages, visible.length, first, Math.min(last, visible.length)); }
        }
        // The pager's parts are made once and updated on each render, so the size select keeps its focus.
        var pagerParts = null;
        function buildPager(container) {
            container.textContent = "";
            var info = doc.createElement("div");
            info.className = "dnf-pagination__info";
            var label = doc.createElement("label");
            label.className = "dnf-pagination__size";
            var labelText = doc.createElement("span");
            labelText.textContent = "Rows per page";
            var select = doc.createElement("select");
            select.className = "dnf-select dnf-select--sm";
            PageSizes.concat(PageSizes.indexOf(pageSize) === -1 ? [pageSize] : []).sort(function (a, b) { return a - b; }).forEach(function (size) {
                var option = doc.createElement("option");
                option.value = String(size);
                option.textContent = String(size);
                select.appendChild(option);
            });
            select.value = String(pageSize);
            select.addEventListener("change", function () {
                pageSize = parseInt(select.value, 10);
                storage.set(sizeKey, String(pageSize));
                page = 1;
                render();
            });
            label.appendChild(labelText);
            label.appendChild(select);
            var summary = doc.createElement("span");
            summary.className = "dnf-pagination__summary";
            info.appendChild(label);
            info.appendChild(summary);
            var nav = doc.createElement("nav");
            nav.className = "dnf-pagination__pages";
            nav.setAttribute("aria-label", "Pages");
            container.appendChild(info);
            container.appendChild(nav);
            return { summary: summary, nav: nav };
        }
        function renderPager(container, pages, total, first, last) {
            // Nothing to page or resize while everything fits one page of the smallest size offered.
            container.hidden = total <= Math.min(pageSize, PageSizes[0]);
            if (container.hidden) { return; }
            pagerParts = pagerParts || buildPager(container);
            pagerParts.summary.textContent = total === 0 ? "0 of 0" : (first + 1) + "-" + last + " of " + total;
            var nav = pagerParts.nav;
            nav.textContent = "";
            function add(content, target, current, aria, isIcon) {
                var button = doc.createElement("button");
                button.type = "button";
                button.className = "dnf-button dnf-button--secondary dnf-button--sm" + (isIcon ? " dnf-button--icon-only" : "");
                if (isIcon) { button.innerHTML = content; } else { button.textContent = content; }
                if (aria) { button.setAttribute("aria-label", aria); }
                if (current) { button.setAttribute("aria-current", "page"); }
                if (target < 1 || target > pages) { button.disabled = true; }
                button.addEventListener("click", function () { page = target; render(); scope.scrollIntoView({ block: "nearest" }); });
                nav.appendChild(button);
            }
            add(iconHtml("chevron-left", "sm"), page - 1, false, "Previous page", true);
            for (var p = 1; p <= pages; p++) {
                if (p === 1 || p === pages || Math.abs(p - page) <= 1) { add(String(p), p, p === page, "Page " + p); }
                else if (Math.abs(p - page) === 2) { var gap = doc.createElement("span"); gap.className = "dnf-pagination__gap"; gap.setAttribute("aria-hidden", "true"); gap.textContent = "…"; nav.appendChild(gap); }
            }
            add(iconHtml("chevron-right", "sm"), page + 1, false, "Next page", true);
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
            var column = Array.prototype.indexOf.call(th.parentNode.children, th);
            var label = th.textContent.trim();
            th.textContent = "";
            var button = doc.createElement("button");
            button.type = "button";
            button.className = "dnf-table__sort";
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
                    var cell = row.children[column];
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
                if (holder) {
                    if (box.checked) { holder.setAttribute("data-selected", "true"); } else { holder.removeAttribute("data-selected"); }
                }
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
        scope.addEventListener("click", function (event) {
            if (!(event.target instanceof Element) || !event.target.closest("[data-bulk-clear]")) { return; }
            $all("[data-select-item]", scope).forEach(function (b) { b.checked = false; });
            syncSelection();
        });

        syncSelection();
        render();
    }

    // ---- View switch (grid / list), remembered per screen ----------------------------------------------------
    // <div class="dnf-segmented" data-view-switch="media" data-view-target="#items"> <button data-view-button="grid">
    // Sets data-view-mode on the target; CSS lays the same items out as cards or rows (one DOM, one checkbox each).
    function initViewSwitch(group) {
        var key = "dnf-view:" + group.getAttribute("data-view-switch");
        var target = doc.querySelector(group.getAttribute("data-view-target"));
        if (!target) { return; }
        function show(view) {
            target.setAttribute("data-view-mode", view);
            $all("[data-view-button]", group).forEach(function (b) { b.setAttribute("aria-pressed", b.getAttribute("data-view-button") === view ? "true" : "false"); });
        }
        show(storage.get(key) || group.getAttribute("data-default") || "grid");
        $all("[data-view-button]", group).forEach(function (button) {
            button.addEventListener("click", function () {
                var view = button.getAttribute("data-view-button");
                storage.set(key, view);
                // The same items take another shape, so the page's height changes: the first item in sight stays in
                // sight, in its place, or at the top when it was partly above the screen (a row is shorter than a card).
                var anchor = window.scrollY > 0 ? $all("[data-item]:not([hidden])", target).filter(function (item) { return item.getBoundingClientRect().bottom > 0; })[0] : null;
                var top = anchor ? anchor.getBoundingClientRect().top : 0;
                show(view);
                if (anchor && top < 0) {
                    anchor.scrollIntoView({ block: "start", behavior: "instant" });
                } else if (anchor) {
                    window.scrollBy({ top: anchor.getBoundingClientRect().top - top, behavior: "instant" });
                }
            });
        });
    }

    // ---- Check-all groups: <input data-check-group="content"> + <input data-check-member="content"> -----------
    function syncGroup(name) {
        var master = doc.querySelector("[data-check-group='" + name + "']");
        var members = $all("[data-check-member='" + name + "']");
        if (!master) { return; }
        var checked = members.filter(function (m) { return m.checked; }).length;
        master.checked = checked > 0 && checked === members.length;
        master.indeterminate = checked > 0 && checked < members.length;
    }
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

    // ---- File upload: shows the chosen files and the drag-over state (data-dragging) ----------------------------
    function initFileUpload(zone) {
        var input = zone.querySelector("input[type=file]");
        var list = zone.querySelector("[data-file-upload-files]");
        if (!input) { return; }
        ["dragenter", "dragover"].forEach(function (type) { zone.addEventListener(type, function () { zone.setAttribute("data-dragging", "true"); }); });
        ["dragleave", "drop"].forEach(function (type) { zone.addEventListener(type, function () { zone.removeAttribute("data-dragging"); }); });
        input.addEventListener("change", function () {
            if (!list) { return; }
            list.textContent = "";
            Array.prototype.forEach.call(input.files, function (file) {
                var badge = doc.createElement("span");
                badge.className = "dnf-badge dnf-badge--primary";
                badge.textContent = file.name;
                list.appendChild(badge);
            });
        });
    }

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

    // ---- An embedded frame: [data-frame-host] gets data-loaded="true" once its iframe[data-frame] has loaded ----
    function initFrame(frame) {
        var host = frame.closest("[data-frame-host]");
        function loaded() { if (host) { host.setAttribute("data-loaded", "true"); } }
        frame.addEventListener("load", loaded);
        try { if (frame.contentDocument && frame.contentDocument.readyState === "complete" && frame.contentDocument.URL !== "about:blank") { loaded(); } } catch (e) { loaded(); }
    }

    // ---- Per-element behaviours, for the page and for content added later (dnfUI.init(root)) -------------------
    function init(root) {
        root = root || doc;
        each("[data-js-only]", root, function (el) { el.hidden = false; });
        each("[data-tabs]", root, initTabs);
        each("[data-collection]", root, initCollection);
        each("[data-view-switch]", root, initViewSwitch);
        each(".dnf-file-upload", root, initFileUpload);
        each("iframe[data-frame]", root, initFrame);
        $all("[data-check-group]", root).forEach(function (master) { syncGroup(master.getAttribute("data-check-group")); });
        $all("[data-theme-set]", root).forEach(function (button) {
            button.setAttribute("aria-checked", button.getAttribute("data-theme-set") === currentTheme() ? "true" : "false");
        });
        // Toasts rendered by the server: re-insert so screen readers announce them, then arm auto-hide.
        each("[data-toasts] [data-toast]", root, function (node) { node.parentNode.appendChild(node); arm(node); });
    }

    applyTheme(currentTheme());
    init(doc);

    window.dnfUI = { toast: toast, confirm: confirmAsync, init: init, icon: iconHtml };
})();
