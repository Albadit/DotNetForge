// DotNetForge UI theme bootstrap (.docs/ui/theming.md#switching). Load it synchronously in <head>, before the
// stylesheet, so the saved light/dark choice applies before the first paint (no flash). Without a saved choice the
// system preference applies through CSS alone; dnf.js owns the switch ([data-theme-set]).
// It also marks <html class="dnf-js">, so .dnf-js-only controls (the theme menu) show from the first paint.
//
// dnf.js is deferred, so on a real network the page can paint before it runs, and everything it then changes jumps:
// a saved list view over the grid, toolbars pushing the content down, long tables shrinking to one page, the first tab
// giving way to the one in the URL. So the parts
// of its work that change the layout start here (.docs/ui/behaviours.md#before-the-first-paint): a mutation observer
// sees each element as the parser adds it, before the browser can paint it. dnf.js does the same work again afterwards,
// which changes nothing, and adds the behaviour.
(function () {
    "use strict";
    document.documentElement.classList.add("dnf-js");
    try {
        var theme = window.localStorage.getItem("dnf-theme");
        if (theme === "light" || theme === "dark") {
            document.documentElement.setAttribute("data-theme", theme);
        }
    } catch (e) {
        // Storage blocked (privacy mode): the system preference applies.
    }

    function stored(key) {
        try { return window.localStorage.getItem(key); } catch (e) { return null; }
    }

    // A saved view, for the element its switch points at; the switch comes first in the page.
    function applyView(target) {
        if (!target.id) { return; }
        var group = document.querySelector("[data-view-switch][data-view-target='#" + target.id + "']");
        var saved = group ? stored("dnf-view:" + group.getAttribute("data-view-switch")) : null;
        if (!saved) { return; }
        var buttons = Array.prototype.slice.call(group.querySelectorAll("[data-view-button]"));
        if (!buttons.some(function (b) { return b.getAttribute("data-view-button") === saved; })) { return; }
        target.setAttribute("data-view-mode", saved);
        buttons.forEach(function (b) { b.setAttribute("aria-pressed", b.getAttribute("data-view-button") === saved ? "true" : "false"); });
    }

    // A long collection shows only its first page. The same numbers as initCollection in dnf.js.
    var pageSizes = [10, 25, 50, 100];
    var collections = new WeakMap();
    function pageItem(item) {
        var scope = item.closest("[data-collection]");
        if (!scope) { return; }
        var state = collections.get(scope);
        if (!state) {
            var size = parseInt(scope.getAttribute("data-page-size") || "0", 10);
            var index = Array.prototype.indexOf.call(document.querySelectorAll("[data-collection]"), scope);
            var saved = parseInt(stored("dnf-page-size:" + window.location.pathname + ":" + index) || "", 10);
            state = { size: size > 0 && pageSizes.indexOf(saved) !== -1 ? saved : size, seen: 0 };
            collections.set(scope, state);
        }
        state.seen++;
        if (state.size > 0 && state.seen > state.size) { item.hidden = true; }
    }

    // Tree branches the user closed (pages.js owns the toggle).
    var collapsedPages = null;
    function collapseItem(item) {
        if (collapsedPages === null) {
            try { collapsedPages = JSON.parse(stored("dnf-tree-collapsed") || "[]") || []; } catch (e) { collapsedPages = []; }
        }
        if (collapsedPages.indexOf(item.getAttribute("data-id")) !== -1) { item.setAttribute("data-collapsed", "true"); }
    }

    // The open tab of a tab group, picked as initTabs in dnf.js picks it. The tabs come first in the group, so all of
    // them are there by its first panel. data-tabbed hides the panel titles: the tab names the panel.
    var openTabs = new WeakMap();
    function showPanel(panel) {
        var root = panel.closest("[data-tabs]");
        if (!root) { return; }
        if (!openTabs.has(root)) {
            var tabs = Array.prototype.filter.call(root.querySelectorAll("[role=tab][data-tab]"), function (t) { return t.closest("[data-tabs]") === root; });
            if (!tabs.length) { return; }
            var names = tabs.map(function (t) { return t.getAttribute("data-tab"); });
            var hash = root.getAttribute("data-tab-hash") !== "false" ? window.location.hash.slice(1) : "";
            var initial = root.getAttribute("data-initial-tab");
            var open = names.indexOf(hash) !== -1 ? hash : names.indexOf(initial) !== -1 ? initial : names[0];
            tabs.forEach(function (t) { t.setAttribute("aria-selected", t.getAttribute("data-tab") === open ? "true" : "false"); });
            root.setAttribute("data-tabbed", "true");
            openTabs.set(root, open);
        }
        panel.hidden = panel.getAttribute("data-tab-panel") !== openTabs.get(root);
    }

    function settle(element) {
        if (element.hasAttribute("data-js-only")) { element.hidden = false; }
        if (element.hasAttribute("data-view-mode")) { applyView(element); }
        if (element.hasAttribute("data-item")) { pageItem(element); }
        if (element.hasAttribute("data-tree-item")) { collapseItem(element); }
        if (element.hasAttribute("data-tab-panel")) { showPanel(element); }
    }

    var observer = new MutationObserver(function (records) {
        records.forEach(function (record) {
            record.addedNodes.forEach(function (node) {
                if (node.nodeType === 1) { settle(node); }
            });
        });
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener("DOMContentLoaded", function () { observer.disconnect(); });
})();
