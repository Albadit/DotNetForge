// Content Manager enhancements (.docs/pages/content-manager.md). Everything works without JS (full-page posts, the
// Parent select re-parents, all fields visible); this adds the tree filter, drag-to-reorder and live field toggling.
(function () {
    "use strict";

    // ---- Conditional fields ----------------------------------------------------------------------------------
    var pageType = document.getElementById("PageType");
    var published = document.getElementById("Published");
    var publishOpts = document.getElementById("publishOpts");

    function syncType() {
        if (!pageType) { return; }
        document.querySelectorAll("[data-when-type]").forEach(function (field) {
            field.hidden = field.getAttribute("data-when-type") !== pageType.value;
        });
    }
    function syncPublish() {
        if (publishOpts && published) { publishOpts.hidden = !published.checked; }
    }
    if (pageType) { pageType.addEventListener("change", syncType); }
    if (published) { published.addEventListener("change", syncPublish); }
    syncType();
    syncPublish();

    // ---- Page permissions: remove a user row (both rules back to "Not set"; the save drops the user) ------------
    document.addEventListener("click", function (event) {
        var button = event.target instanceof Element ? event.target.closest("[data-remove-user-row]") : null;
        if (!button) { return; }
        var row = button.closest("[data-user-row]");
        row.querySelectorAll("select").forEach(function (select) { select.value = ""; });
        row.hidden = true;
    });

    // ---- Open / close child pages ----------------------------------------------------------------------------
    // Pages with children get a chevron; closed pages are remembered per browser. The selected page's parents
    // always open, so the selection is never hidden.
    var collapsedKey = "dnf-tree-collapsed";
    var collapsed = {};
    try { (JSON.parse(window.localStorage.getItem(collapsedKey) || "[]") || []).forEach(function (id) { collapsed[id] = true; }); } catch (e) { /* storage blocked */ }
    function saveCollapsed() {
        try { window.localStorage.setItem(collapsedKey, JSON.stringify(Object.keys(collapsed))); } catch (e) { /* storage blocked */ }
    }
    function setCollapsed(item, value) {
        var id = item.getAttribute("data-id");
        var title = item.querySelector(":scope > .tree-row .tree-title");
        var button = item.querySelector(":scope > .tree-row [data-tree-toggle]");
        item.classList.toggle("is-collapsed", value);
        if (button) {
            button.setAttribute("aria-expanded", value ? "false" : "true");
            button.setAttribute("aria-label", (value ? "Show the pages under " : "Hide the pages under ") + (title ? title.textContent : ""));
        }
        if (value) { collapsed[id] = true; } else { delete collapsed[id]; }
    }
    var active = document.querySelector(".tree-card .tree-row.active");
    for (var up = active ? active.closest(".tree-item") : null; up; up = up.parentElement.closest(".tree-item")) {
        if (up !== active.closest(".tree-item")) { delete collapsed[up.getAttribute("data-id")]; }
    }
    // Close all / Open all (tree header): closes every page with children while any is open, else opens them all.
    var collapseAll = document.querySelector("[data-tree-collapse-all]");
    var toggles = Array.prototype.slice.call(document.querySelectorAll("[data-tree-toggle]"));
    function syncCollapseAll() {
        if (!collapseAll) { return; }
        collapseAll.hidden = toggles.length === 0;
        var anyOpen = toggles.some(function (b) { return !b.closest(".tree-item").classList.contains("is-collapsed"); });
        var label = anyOpen ? "Close all" : "Open all";
        collapseAll.setAttribute("aria-label", label + " pages");
        collapseAll.setAttribute("data-tooltip", label);
        var use = collapseAll.querySelector("use");
        if (use) { use.setAttribute("href", use.getAttribute("href").replace(/#.*$/, anyOpen ? "#chevrons-down-up" : "#chevrons-up-down")); }
    }
    toggles.forEach(function (button) {
        var item = button.closest(".tree-item");
        setCollapsed(item, !!collapsed[item.getAttribute("data-id")]);
        button.addEventListener("click", function () {
            setCollapsed(item, !item.classList.contains("is-collapsed"));
            saveCollapsed();
            syncCollapseAll();
        });
    });
    if (collapseAll) {
        collapseAll.addEventListener("click", function () {
            var close = toggles.some(function (b) { return !b.closest(".tree-item").classList.contains("is-collapsed"); });
            toggles.forEach(function (b) { setCollapsed(b.closest(".tree-item"), close); });
            saveCollapsed();
            syncCollapseAll();
        });
    }
    saveCollapsed();
    syncCollapseAll();

    // ---- Tree filter -----------------------------------------------------------------------------------------
    var filter = document.querySelector("[data-tree-filter]");
    var emptyNote = document.querySelector("[data-tree-empty]");
    if (filter) {
        filter.addEventListener("input", function () {
            var query = filter.value.trim().toLowerCase();
            var tree = document.querySelector(".tree-card .page-tree");
            if (tree) { tree.classList.toggle("is-filtering", query !== ""); }
            var items = Array.prototype.slice.call(document.querySelectorAll("[data-tree-item]"));
            // An item stays visible when it or any descendant matches, so the tree keeps its shape.
            items.reverse().forEach(function (item) {
                var self = (item.getAttribute("data-search") || "").toLowerCase().indexOf(query) !== -1;
                var child = !!item.querySelector(":scope > .page-tree > [data-tree-item]:not([hidden])");
                item.hidden = !(query === "" || self || child);
            });
            if (emptyNote) { emptyNote.hidden = query === "" || items.some(function (i) { return !i.hidden; }); }
        });
    }

    // ---- Drag-and-drop reorder -------------------------------------------------------------------------------
    var root = document.querySelector(".tree-card .page-tree");
    if (!root) { return; }
    var dragged = null;

    function clearTargets() {
        document.querySelectorAll(".tree-row.drop-target").forEach(function (r) { r.classList.remove("drop-target"); });
    }
    root.addEventListener("dragstart", function (e) {
        var li = e.target.closest(".tree-item");
        if (!li || li.getAttribute("draggable") !== "true") { return; }
        dragged = li;
        li.classList.add("dragging");
        e.dataTransfer.effectAllowed = "move";
        // Firefox starts a drag only with data set.
        e.dataTransfer.setData("text/plain", li.getAttribute("data-id") || "");
    });
    root.addEventListener("dragend", function () {
        if (dragged) { dragged.classList.remove("dragging"); }
        clearTargets();
        dragged = null;
    });
    root.addEventListener("dragover", function (e) {
        if (!dragged) { return; }
        e.preventDefault();
        e.dataTransfer.dropEffect = "move";
        var row = e.target.closest(".tree-row");
        clearTargets();
        if (row && !dragged.contains(row)) { row.classList.add("drop-target"); }
    });
    root.addEventListener("drop", function (e) {
        if (!dragged) { return; }
        e.preventDefault();
        var row = e.target.closest(".tree-row");
        var target = row ? row.closest(".tree-item") : null;
        // Ignore drops onto itself or into its own subtree (that would orphan the branch).
        if (!target || target === dragged || dragged.contains(target)) { return; }
        // Move the dragged page to just before the target, as its sibling.
        target.parentNode.insertBefore(dragged, target);
        persist();
    });

    function fail(message) {
        if (window.Forge) { window.Forge.toast(message, "danger"); }
        window.setTimeout(function () { window.location.reload(); }, 1500);
    }
    function persist() {
        var items = [];
        // Each item's parent (enclosing tree item, or none) and position.
        document.querySelectorAll(".tree-card .page-tree").forEach(function (ul) {
            var parentLi = ul.parentNode.closest ? ul.parentNode.closest(".tree-item") : null;
            var parentId = parentLi ? parentLi.getAttribute("data-id") : null;
            Array.prototype.forEach.call(ul.children, function (li, index) {
                if (!li.classList.contains("tree-item")) { return; }
                items.push({ id: li.getAttribute("data-id"), parentPageId: parentId, sortOrder: index });
            });
        });

        var token = document.querySelector('input[name="__RequestVerificationToken"]');
        fetch("/admin/content/reorder", {
            method: "POST",
            headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token ? token.value : "" },
            credentials: "same-origin",
            body: JSON.stringify({ items: items })
        }).then(function (res) {
            if (res.ok) { window.location.reload(); return; }
            // The server explains rejected moves (duplicate slug, cycle) in { error }; a permission refusal redirects
            // to the access-denied page, so it gets the generic message.
            return res.json().catch(function () { return {}; }).then(function (body) {
                fail(body && body.error ? body.error : "Could not save the new order.");
            });
        }).catch(function () { fail("Could not save the new order."); });
    }
})();
