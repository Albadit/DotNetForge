// Media library (.docs/pages/media.md):
// - Upload panel: hidden until Upload is clicked or files are dragged onto the page (open after a failed upload);
//   the chosen files are previewed (image thumbnails, size, too large / type not allowed) and can be removed.
// - Edit folder: "Edit…" in a folder's menu opens #folder-dialog (rename, move) for that folder.
// - File info / rename: "Info" and "Rename…" in a file's menu open #file-info-dialog and #file-rename-dialog.
// - Drag and drop (#folders): drag files - or every selected file - and folders onto a folder tile, or onto a
//   breadcrumb to move them up. A drop posts the same forms as the Move buttons (#dragMove), so the server's rules and
//   permission checks apply; the page reloads with the result. Without JavaScript (or on touch screens) the Move
//   controls do the same.
// Without JavaScript the upload card is always shown and the folder's own page has the edit forms.
(function () {
    "use strict";

    uploadPanel();
    folderDialog();
    newFolderDialog();
    fileDialogs();

    var form = document.getElementById("dragMove");
    if (!form) { return; }

    var dragged = null; // { kind: "file" | "folder", ids: [...] }

    function selectedFileIds() {
        return Array.prototype.slice.call(document.querySelectorAll("[data-select-item]:checked"))
            .map(function (box) { return box.value; });
    }

    document.addEventListener("dragstart", function (event) {
        var item = event.target.closest && event.target.closest("[data-drag-file], [data-drag-folder]");
        if (!item) { return; }
        if (item.hasAttribute("data-drag-file")) {
            var id = item.getAttribute("data-drag-file");
            var selected = selectedFileIds();
            // A selected file carries the whole selection along; an unselected one goes alone.
            dragged = { kind: "file", ids: selected.indexOf(id) >= 0 ? selected : [id] };
        } else {
            dragged = { kind: "folder", ids: [item.getAttribute("data-drag-folder")] };
        }

        event.dataTransfer.effectAllowed = "move";
        event.dataTransfer.setData("text/plain", dragged.ids.join(","));
        item.classList.add("is-dragging");
    });

    document.addEventListener("dragend", function (event) {
        var item = event.target.closest && event.target.closest(".is-dragging");
        if (item) { item.classList.remove("is-dragging"); }
        clearTargets();
        dragged = null;
    });

    function target(event) {
        var zone = event.target.closest && event.target.closest("[data-drop-folder]");
        if (!zone || !dragged) { return null; }
        // A folder can't be dropped on itself (the server also refuses its own subfolders).
        if (dragged.kind === "folder" && zone.getAttribute("data-drop-folder") === dragged.ids[0]) { return null; }
        return zone;
    }

    function clearTargets() {
        Array.prototype.forEach.call(document.querySelectorAll(".is-drop-target"), function (el) {
            el.classList.remove("is-drop-target");
        });
    }

    document.addEventListener("dragover", function (event) {
        var zone = target(event);
        if (!zone) { return; }
        event.preventDefault();
        event.dataTransfer.dropEffect = "move";
        if (!zone.classList.contains("is-drop-target")) {
            clearTargets();
            zone.classList.add("is-drop-target");
        }
    });

    document.addEventListener("dragleave", function (event) {
        var zone = target(event);
        if (zone && !zone.contains(event.relatedTarget)) { zone.classList.remove("is-drop-target"); }
    });

    document.addEventListener("drop", function (event) {
        var zone = target(event);
        if (!zone) { return; }
        event.preventDefault();
        var folder = zone.getAttribute("data-drop-folder");
        var drop = dragged;
        clearTargets();
        dragged = null;
        submit(drop, folder);
    });

    function field(name, value) {
        var input = document.createElement("input");
        input.type = "hidden";
        input.name = name;
        input.value = value;
        form.appendChild(input);
    }

    function submit(drop, folder) {
        // Only the antiforgery token stays from an earlier attempt.
        Array.prototype.forEach.call(form.querySelectorAll("input:not([name=__RequestVerificationToken])"), function (el) { el.remove(); });
        if (drop.kind === "file") {
            form.action = "/admin/media/move-selected";
            drop.ids.forEach(function (id) { field("ids", id); });
            field("folderId", folder);
            field("returnFolder", form.getAttribute("data-return-folder") || "");
        } else {
            form.action = "/admin/media/folders/" + drop.ids[0] + "/move";
            field("parentId", folder);
        }

        form.submit();
    }

    // ---- Upload panel ------------------------------------------------------------------------------------------------
    function uploadPanel() {
        var panel = document.querySelector("[data-upload-panel]");
        var modal = panel && panel.closest("dialog");
        if (!modal || typeof modal.showModal !== "function") { return; }
        var input = panel.querySelector("input[type=file]");
        var zone = panel.querySelector(".dropzone");
        var preview = panel.querySelector("[data-upload-preview]");
        var icons = document.getElementById("upload-icons");
        // Upload limits per kind of file (Media Library settings); the server checks them again.
        function list(name) { return (panel.getAttribute(name) || "").toLowerCase().split(",").filter(Boolean); }
        function bytes(name) { return parseInt(panel.getAttribute(name), 10) || 0; }
        var imageTypes = list("data-image-types"), videoTypes = list("data-video-types");
        var limits = { image: bytes("data-max-image"), video: bytes("data-max-video"), other: bytes("data-max-other") };
        function limitFor(ext) {
            return imageTypes.indexOf(ext) >= 0 ? limits.image : videoTypes.indexOf(ext) >= 0 ? limits.video : limits.other;
        }
        var accept = (input.getAttribute("accept") || "").toLowerCase().split(",").filter(Boolean);
        var toggles = document.querySelectorAll("[data-upload-toggle]");
        var chosen = [];

        // A modal popup (Esc, the ×, or a click on the backdrop close it).
        input.autofocus = true; // the dialog focuses the file picker, not the close button

        function isOpen() { return modal.open; }

        function setOpen(open) {
            if (open && !modal.open) { modal.showModal(); }
            if (!open && modal.open) { modal.close(); }
            Array.prototype.forEach.call(toggles, function (t) { t.setAttribute("aria-expanded", open ? "true" : "false"); });
        }

        modal.addEventListener("close", function () { setOpen(false); });
        // A click on the backdrop (the dialog itself, outside the card) closes it.
        modal.addEventListener("click", function (event) { if (event.target === modal) { setOpen(false); } });

        // Open after a failed upload, or when linked to (#upload, e.g. the Dashboard's "Upload media").
        setOpen(panel.getAttribute("data-upload-open") === "true" || window.location.hash === "#upload");
        Array.prototype.forEach.call(toggles, function (t) {
            t.addEventListener("click", function () {
                setOpen(true);
                input.focus();
            });
        });
        panel.querySelector("[data-upload-close]").addEventListener("click", function () {
            chosen = [];
            sync();
            setOpen(false);
        });

        // Files dragged from the computer (not the library's own cards) open the popup; a drop anywhere adds them.
        function hasFiles(event) {
            return event.dataTransfer && Array.prototype.indexOf.call(event.dataTransfer.types || [], "Files") >= 0;
        }
        document.addEventListener("dragenter", function (event) {
            if (hasFiles(event) && !isOpen()) { setOpen(true); }
        });
        document.addEventListener("dragover", function (event) {
            if (!hasFiles(event)) { return; }
            event.preventDefault();
            zone.classList.add("is-dragover");
        });
        document.addEventListener("drop", function (event) {
            if (!hasFiles(event)) { return; }
            event.preventDefault();
            zone.classList.remove("is-dragover");
            add(event.dataTransfer.files);
        });

        // Picked files join the ones already chosen (the picker itself only holds the latest pick).
        input.addEventListener("change", function () {
            add(input.files);
        });

        // The same file (name, size, last change) is added once, however often it is dropped or picked.
        function add(files) {
            var keys = chosen.map(key);
            Array.prototype.forEach.call(files, function (file) {
                if (keys.indexOf(key(file)) < 0) {
                    keys.push(key(file));
                    chosen.push(file);
                }
            });
            sync();
        }

        function key(file) {
            return file.name + "|" + file.size + "|" + file.lastModified;
        }

        // One submit only: a second click while the files go up would upload them twice.
        function lock(locked) {
            Array.prototype.forEach.call(input.form.querySelectorAll("button[type=submit]"), function (button) {
                button.disabled = locked;
            });
        }
        input.form.addEventListener("submit", function () { lock(true); });
        // Back to this page from the browser's history: usable again.
        window.addEventListener("pageshow", function () { lock(false); });

        function sync() {
            var transfer = new DataTransfer();
            chosen.forEach(function (file) { transfer.items.add(file); });
            input.files = transfer.files;
            render();
        }

        function size(bytes) {
            return bytes < 1024 ? bytes + " B" : bytes < 1048576 ? (bytes / 1024).toFixed(1) + " KB" : (bytes / 1048576).toFixed(1) + " MB";
        }

        function problem(file) {
            var dot = file.name.lastIndexOf(".");
            var ext = dot >= 0 ? file.name.substring(dot).toLowerCase() : "";
            if (accept.length && accept.indexOf(ext) < 0) { return "Type not allowed"; }
            var max = limitFor(ext);
            if (max && file.size > max) { return "Too large (max " + size(max) + ")"; }
            return null;
        }

        function icon(name) {
            return icons.content.querySelector("[data-icon=" + name + "]").cloneNode(true);
        }

        function render() {
            preview.textContent = "";
            preview.hidden = chosen.length === 0;
            chosen.forEach(function (file, index) {
                var item = document.createElement("li");
                item.className = "upload-item";
                var thumb = document.createElement("div");
                thumb.className = "upload-thumb";
                thumb.appendChild(icon("file"));
                var meta = document.createElement("div");
                meta.className = "upload-meta";
                var name = document.createElement("span");
                name.className = "upload-name";
                name.textContent = file.name;
                name.title = file.name;
                var info = document.createElement("span");
                info.textContent = size(file.size);
                meta.appendChild(name);
                meta.appendChild(info);
                var issue = problem(file);
                if (issue) {
                    item.classList.add("is-invalid");
                    var warn = document.createElement("span");
                    warn.className = "upload-problem";
                    warn.textContent = issue;
                    meta.appendChild(warn);
                }

                var remove = document.createElement("button");
                remove.type = "button";
                remove.className = "btn btn-secondary btn-icon btn-sm upload-remove";
                remove.setAttribute("aria-label", "Remove " + file.name);
                remove.appendChild(icon("remove"));
                remove.addEventListener("click", function () {
                    chosen.splice(index, 1);
                    sync();
                });
                item.appendChild(thumb);
                item.appendChild(meta);
                item.appendChild(remove);
                preview.appendChild(item);
                if (!issue && /^image\//.test(file.type)) { thumbnail(file, thumb); }
            });
        }

        // A small data: thumbnail - the page's policy allows data: images, not blob: URLs.
        function thumbnail(file, holder) {
            if (typeof createImageBitmap !== "function") { return; }
            createImageBitmap(file).then(function (bitmap) {
                var canvas = document.createElement("canvas");
                var scale = Math.min(1, 240 / Math.max(bitmap.width, bitmap.height));
                canvas.width = Math.max(1, Math.round(bitmap.width * scale));
                canvas.height = Math.max(1, Math.round(bitmap.height * scale));
                canvas.getContext("2d").drawImage(bitmap, 0, 0, canvas.width, canvas.height);
                var img = document.createElement("img");
                img.alt = "";
                img.src = canvas.toDataURL("image/png");
                holder.textContent = "";
                holder.appendChild(img);
            }, function () { /* not decodable here (e.g. AVIF in some browsers): keep the icon */ });
        }
    }

    // ---- Edit folder dialog -----------------------------------------------------------------------------------------
    function folderDialog() {
        var dialog = document.getElementById("folder-dialog");
        if (!dialog || typeof dialog.showModal !== "function") { return; }
        var rename = dialog.querySelector("[data-folder-rename]");
        var move = dialog.querySelector("[data-folder-move]");
        var select = move.querySelector("select");
        document.addEventListener("click", function (event) {
            var button = event.target.closest && event.target.closest("[data-folder-edit]");
            if (!button) { return; }
            var id = button.getAttribute("data-folder-edit");
            var path = button.getAttribute("data-folder-path");
            dialog.querySelector(".dialog-title").textContent = "Edit folder " + path;
            rename.action = "/admin/media/folders/" + id + "/rename";
            rename.elements.name.value = button.getAttribute("data-folder-name");
            move.action = "/admin/media/folders/" + id + "/move";
            // Not into itself or one of its own folders; preselect where it is now.
            Array.prototype.forEach.call(select.options, function (option) {
                var target = option.getAttribute("data-path");
                option.disabled = target === path || target.indexOf(path + "/") === 0;
            });
            select.value = button.getAttribute("data-folder-parent") || "";
            dialog.showModal();
            rename.elements.name.select();
        });
    }

    // File info (the rows come from the button's data-file-info, set as text) and Rename (the name without its
    // extension; the server keeps the extension). Esc, Close/Cancel or the backdrop close them.
    function fileDialogs() {
        var info = document.getElementById("file-info-dialog");
        var rename = document.getElementById("file-rename-dialog");
        [info, rename].forEach(function (dialog) {
            if (dialog) { dialog.addEventListener("click", function (event) { if (event.target === dialog) { dialog.close(); } }); }
        });
        document.addEventListener("click", function (event) {
            var button = event.target.closest && event.target.closest("[data-file-info], [data-file-rename]");
            if (!button) { return; }
            if (button.hasAttribute("data-file-info") && info && typeof info.showModal === "function") {
                var list = info.querySelector("[data-file-info-list]");
                list.textContent = "";
                JSON.parse(button.getAttribute("data-file-info")).forEach(function (row) {
                    var term = document.createElement("dt");
                    var value = document.createElement("dd");
                    term.textContent = row[0];
                    value.textContent = row[1];
                    list.append(term, value);
                });
                info.showModal();
            } else if (button.hasAttribute("data-file-rename") && rename && typeof rename.showModal === "function") {
                var form = rename.querySelector("form[method=post]");
                form.action = "/admin/media/" + button.getAttribute("data-file-rename") + "/rename";
                form.elements.name.value = button.getAttribute("data-file-name");
                rename.querySelector("[data-file-rename-extension]").textContent = button.getAttribute("data-file-extension");
                rename.showModal();
                form.elements.name.select();
            }
        });
    }

    // New folder: the header button opens the dialog with an empty name (Esc, Cancel or the backdrop close it).
    function newFolderDialog() {
        var dialog = document.getElementById("new-folder-dialog");
        if (!dialog || typeof dialog.showModal !== "function") { return; }
        var name = document.getElementById("new-folder");
        dialog.addEventListener("click", function (event) { if (event.target === dialog) { dialog.close(); } });
        Array.prototype.forEach.call(document.querySelectorAll("[data-folder-new]"), function (button) {
            button.addEventListener("click", function () { name.value = ""; dialog.showModal(); });
        });
    }
})();
