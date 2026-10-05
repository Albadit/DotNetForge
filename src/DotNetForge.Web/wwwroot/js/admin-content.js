// Progressive enhancement for the Content Manager. Everything works without JS (full-page posts, the
// Parent dropdown reparents, all fields visible); this just adds live field toggling and drag-to-reorder.
(function () {
    "use strict";

    // --- Conditional fields on the page form -------------------------------------------------
    var pageType = document.getElementById("PageType");
    var published = document.getElementById("Published");
    var disabled = document.getElementById("Disabled");
    var publishOpts = document.getElementById("publishOpts");

    function syncType() {
        if (!pageType) return;
        document.querySelectorAll(".form-row.cond").forEach(function (row) {
            row.style.display = row.getAttribute("data-when-type") === pageType.value ? "" : "none";
        });
    }

    function syncPublish() {
        if (publishOpts && published) publishOpts.style.display = published.checked ? "" : "none";
    }

    if (pageType) pageType.addEventListener("change", syncType);
    // Published and Disabled are mutually exclusive (a disabled page cannot be published).
    if (published) published.addEventListener("change", function () {
        if (published.checked && disabled) disabled.checked = false;
        syncPublish();
    });
    if (disabled) disabled.addEventListener("change", function () {
        if (disabled.checked && published) { published.checked = false; }
        syncPublish();
    });
    syncType();
    syncPublish();

    // --- Drag-and-drop tree reorder ----------------------------------------------------------
    var root = document.querySelector(".tree-pane .page-tree");
    if (!root) return;

    var dragged = null;

    root.addEventListener("dragstart", function (e) {
        var li = e.target.closest(".tree-item");
        if (!li) return;
        dragged = li;
        li.classList.add("dragging");
        e.dataTransfer.effectAllowed = "move";
    });

    root.addEventListener("dragend", function () {
        if (dragged) dragged.classList.remove("dragging");
        document.querySelectorAll(".tree-row.drop-target").forEach(function (r) { r.classList.remove("drop-target"); });
        dragged = null;
    });

    root.addEventListener("dragover", function (e) {
        if (!dragged) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = "move";
        var row = e.target.closest(".tree-row");
        document.querySelectorAll(".tree-row.drop-target").forEach(function (r) { r.classList.remove("drop-target"); });
        if (row && !dragged.contains(row)) row.classList.add("drop-target");
    });

    root.addEventListener("drop", function (e) {
        if (!dragged) return;
        e.preventDefault();
        var row = e.target.closest(".tree-row");
        var target = row ? row.closest(".tree-item") : null;
        // Ignore drops onto self or into our own subtree (would orphan the branch).
        if (!target || target === dragged || dragged.contains(target)) return;

        // Reparent/reorder: insert the dragged item as the sibling immediately before the target.
        target.parentNode.insertBefore(dragged, target);
        persist();
    });

    function persist() {
        var items = [];
        // Walk every list, recording each item's parent (enclosing tree-item, or null) and position.
        document.querySelectorAll(".tree-pane .page-tree").forEach(function (ul) {
            var parentLi = ul.parentNode.closest ? ul.parentNode.closest(".tree-item") : null;
            var parentId = parentLi ? parentLi.getAttribute("data-id") : null;
            Array.prototype.forEach.call(ul.children, function (li, index) {
                if (!li.classList.contains("tree-item")) return;
                items.push({ id: li.getAttribute("data-id"), parentPageId: parentId, sortOrder: index });
            });
        });

        var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
        fetch("/admin/content/reorder", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "X-CSRF-TOKEN": tokenEl ? tokenEl.value : "",
            },
            credentials: "same-origin",
            body: JSON.stringify({ items: items }),
        }).then(function (res) {
            if (res.ok) { window.location.reload(); return; }
            // The server explains rejected moves (duplicate slug, cycle) in { error }; a permission refusal
            // redirects to the access-denied page, so it gets the generic message.
            return res.json().catch(function () { return {}; }).then(function (body) {
                alert(body && body.error ? body.error : "Could not save the new order.");
                window.location.reload();
            });
        }).catch(function () { alert("Could not save the new order."); });
    }
})();
