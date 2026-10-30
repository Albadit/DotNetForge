// Pages screen enhancements (.docs/pages/pages.md). Everything works without JS (full-page posts, the
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
        var title = item.querySelector(":scope > .dnf-tree__row .dnf-tree__title");
        var button = item.querySelector(":scope > .dnf-tree__row [data-tree-toggle]");
        if (value) { item.setAttribute("data-collapsed", "true"); } else { item.removeAttribute("data-collapsed"); }
        if (button) {
            button.setAttribute("aria-expanded", value ? "false" : "true");
            button.setAttribute("aria-label", (value ? "Show the pages under " : "Hide the pages under ") + (title ? title.textContent : ""));
        }
        if (value) { collapsed[id] = true; } else { delete collapsed[id]; }
    }
    var active = document.querySelector(".dnf-content-manager__tree .dnf-tree__row[data-active='true']");
    for (var up = active ? active.closest(".dnf-tree__item") : null; up; up = up.parentElement.closest(".dnf-tree__item")) {
        if (up !== active.closest(".dnf-tree__item")) { delete collapsed[up.getAttribute("data-id")]; }
    }
    // Close all / Open all (tree header): closes every page with children while any is open, else opens them all.
    var collapseAll = document.querySelector("[data-tree-collapse-all]");
    var toggles = Array.prototype.slice.call(document.querySelectorAll("[data-tree-toggle]"));
    function syncCollapseAll() {
        if (!collapseAll) { return; }
        collapseAll.hidden = toggles.length === 0;
        var anyOpen = toggles.some(function (b) { return b.closest(".dnf-tree__item").getAttribute("data-collapsed") !== "true"; });
        var label = anyOpen ? "Close all" : "Open all";
        collapseAll.setAttribute("aria-label", label + " pages");
        collapseAll.setAttribute("data-tooltip", label);
        var use = collapseAll.querySelector("use");
        if (use) { use.setAttribute("href", use.getAttribute("href").replace(/#.*$/, anyOpen ? "#chevrons-down-up" : "#chevrons-up-down")); }
    }
    toggles.forEach(function (button) {
        var item = button.closest(".dnf-tree__item");
        setCollapsed(item, !!collapsed[item.getAttribute("data-id")]);
        button.addEventListener("click", function () {
            setCollapsed(item, item.getAttribute("data-collapsed") !== "true");
            saveCollapsed();
            syncCollapseAll();
        });
    });
    if (collapseAll) {
        collapseAll.addEventListener("click", function () {
            var close = toggles.some(function (b) { return b.closest(".dnf-tree__item").getAttribute("data-collapsed") !== "true"; });
            toggles.forEach(function (b) { setCollapsed(b.closest(".dnf-tree__item"), close); });
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
            var tree = document.querySelector(".dnf-content-manager__tree .dnf-tree");
            if (tree) { if (query !== "") { tree.setAttribute("data-filtering", "true"); } else { tree.removeAttribute("data-filtering"); } }
            var items = Array.prototype.slice.call(document.querySelectorAll("[data-tree-item]"));
            // An item stays visible when it or any descendant matches, so the tree keeps its shape.
            items.reverse().forEach(function (item) {
                var self = (item.getAttribute("data-search") || "").toLowerCase().indexOf(query) !== -1;
                var child = !!item.querySelector(":scope > .dnf-tree > [data-tree-item]:not([hidden])");
                item.hidden = !(query === "" || self || child);
            });
            if (emptyNote) { emptyNote.hidden = query === "" || items.some(function (i) { return !i.hidden; }); }
        });
    }

    // ---- Drag-and-drop reorder -------------------------------------------------------------------------------
    var root = document.querySelector(".dnf-content-manager__tree .dnf-tree");
    if (!root) { return; }
    var dragged = null;
    var drop = null; // { item, place }: "before" or "after" the item (a line), "inside" it, or "first" among its children

    function clearTargets() {
        document.querySelectorAll(".dnf-tree__item[data-drop]").forEach(function (item) { item.removeAttribute("data-drop"); });
        drop = null;
    }
    // A row's edges are the gaps between pages, its middle is the page itself.
    function dropAt(e) {
        var row = e.target.closest(".dnf-tree__row");
        var item = row ? row.closest(".dnf-tree__item") : null;
        // Not onto itself or into its own branch: that would orphan it.
        if (!item || dragged.contains(item)) { return null; }
        // The home page stays first and has no children, so the only place next to it is right below it.
        if (item.getAttribute("data-tree-home") === "true") { return { item: item, place: "after" }; }
        var box = row.getBoundingClientRect();
        var y = (e.clientY - box.top) / box.height;
        if (y < 0.3) { return { item: item, place: "before" }; }
        if (y <= 0.7) { return { item: item, place: "inside" }; }
        // Below a page with its children showing comes its first child, not what follows them.
        var children = item.querySelector(":scope > .dnf-tree");
        var open = children && children.children.length > 0 && item.getAttribute("data-collapsed") !== "true";
        return { item: item, place: open ? "first" : "after" };
    }
    // The page moves with its branch: its children are inside its list item.
    function land(target) {
        var item = target.item;
        if (target.place === "before") { item.parentNode.insertBefore(dragged, item); return; }
        if (target.place === "after") { item.parentNode.insertBefore(dragged, item.nextElementSibling); return; }
        var list = item.querySelector(":scope > .dnf-tree");
        if (!list) {
            list = document.createElement("ul");
            list.className = "dnf-tree";
            list.setAttribute("role", "list");
            item.appendChild(list);
        }
        list.insertBefore(dragged, target.place === "first" ? list.firstElementChild : null);
        // Its new parent shows it after the reload.
        delete collapsed[item.getAttribute("data-id")];
        saveCollapsed();
    }
    root.addEventListener("dragstart", function (e) {
        var li = e.target.closest(".dnf-tree__item");
        if (!li || li.getAttribute("draggable") !== "true") { return; }
        dragged = li;
        li.setAttribute("data-dragging", "true");
        e.dataTransfer.effectAllowed = "move";
        // Firefox starts a drag only with data set.
        e.dataTransfer.setData("text/plain", li.getAttribute("data-id") || "");
    });
    root.addEventListener("dragend", function () {
        if (dragged) { dragged.removeAttribute("data-dragging"); }
        clearTargets();
        dragged = null;
    });
    root.addEventListener("dragover", function (e) {
        if (!dragged) { return; }
        var target = dropAt(e);
        if (!target) { clearTargets(); return; }
        e.preventDefault();
        e.dataTransfer.dropEffect = "move";
        if (drop && drop.item === target.item && drop.place === target.place) { return; }
        clearTargets();
        drop = target;
        target.item.setAttribute("data-drop", target.place);
    });
    // By the pointer's position: browsers often leave relatedTarget empty, also between two rows of the tree.
    root.addEventListener("dragleave", function (e) {
        var box = root.getBoundingClientRect();
        if (e.clientX < box.left || e.clientX > box.right || e.clientY < box.top || e.clientY > box.bottom) { clearTargets(); }
    });
    root.addEventListener("drop", function (e) {
        var target = dragged ? dropAt(e) : null;
        if (!target) { return; }
        e.preventDefault();
        land(target);
        persist();
    });

    // Move… in a row's menu: the dialog (move-dialog.js) chooses the new parent and posts with #page-move.
    var moveDialog = document.getElementById("page-move-dialog");
    var picker = moveDialog && typeof moveDialog.showModal === "function" ? window.dnfMoveDialog(moveDialog) : null;
    root.addEventListener("click", function (e) {
        var button = e.target.closest("[data-page-move]");
        if (!button || !picker) { return; }
        var li = button.closest(".dnf-tree__item");
        var parent = li.parentNode.closest(".dnf-tree__item");
        var id = li.getAttribute("data-id");
        document.getElementById("page-move").action = "/admin/pages/move/" + id;
        picker.open([id], parent ? parent.getAttribute("data-id") : "", li.querySelector(".dnf-tree__title").textContent);
    });

    // Move up / Move down in a row's menu: the same reorder without dragging, for touch screens and keyboards.
    root.addEventListener("click", function (e) {
        var button = e.target.closest("[data-tree-move]");
        if (!button) { return; }
        var li = button.closest(".dnf-tree__item");
        if (button.getAttribute("data-tree-move") === "up") {
            li.parentNode.insertBefore(li, li.previousElementSibling);
        } else {
            li.parentNode.insertBefore(li.nextElementSibling, li);
        }
        persist();
    });

    function fail(message) {
        if (window.dnfUI) { window.dnfUI.toast(message, "danger"); }
        window.setTimeout(function () { window.location.reload(); }, 1500);
    }
    function persist() {
        var items = [];
        // Each item's parent (enclosing tree item, or none) and position.
        document.querySelectorAll(".dnf-content-manager__tree .dnf-tree").forEach(function (ul) {
            var parentLi = ul.parentNode.closest ? ul.parentNode.closest(".dnf-tree__item") : null;
            var parentId = parentLi ? parentLi.getAttribute("data-id") : null;
            Array.prototype.forEach.call(ul.children, function (li, index) {
                if (!li.classList.contains("dnf-tree__item")) { return; }
                items.push({ id: li.getAttribute("data-id"), parentPageId: parentId, sortOrder: index });
            });
        });

        var token = document.querySelector('input[name="__RequestVerificationToken"]');
        fetch("/admin/pages/reorder", {
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
