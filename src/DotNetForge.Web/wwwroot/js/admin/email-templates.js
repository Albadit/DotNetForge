// Email template editor (.docs/pages/email-templates.md).
// - Code editor: the body textarea becomes a Monaco editor (self-hosted in /lib/monaco, HTML mode) with
//   IntelliSense for the template's {{variables}} (plus Monaco's HTML tags and attributes), hovers, validation markers
//   for the same rules the server applies (unknown or incomplete placeholders, a missing required variable) and
//   highlighted variables. It follows the admin's light/dark theme. The textarea stays the form field.
// - Status bar: problems (click: next one), cursor position and selection, length against the server's limit, and the
//   word-wrap toggle (Alt+Z; remembered in this browser).
// - Live preview: the subject and body are posted into the sandboxed preview frame (#previewForm) 400 ms after the
//   last change, or at once with the Refresh button; the server renders them with sample data or lists the errors.
// Without JavaScript the textarea is edited directly and the frame shows the saved template.
// Styles: .dnf-code-editor in /css/admin/tools.css; states are data attributes (data-active on the error/warning
// counts, data-over on the length).
(function () {
    "use strict";

    var doc = document;
    var editorForm = doc.getElementById("templateForm");
    var preview = doc.getElementById("previewForm");
    if (!editorForm) { return; }

    // ---- Live preview -------------------------------------------------------------------------------------------
    var timer = 0;
    function refreshPreview() {
        if (!preview) { return; }
        preview.elements.subject.value = editorForm.elements.subject.value;
        preview.elements.body.value = editorForm.elements.body.value;
        preview.submit();
    }
    editorForm.addEventListener("input", function () {
        window.clearTimeout(timer);
        timer = window.setTimeout(refreshPreview, 400);
    });
    // Refresh: renders the current text again - after a link in the email navigated the frame away, or whenever the
    // preview looks out of date.
    var refreshButton = doc.querySelector("[data-preview-refresh]");
    if (refreshButton) {
        refreshButton.addEventListener("click", function () {
            window.clearTimeout(timer);
            refreshPreview();
        });
    }

    // ---- Code editor ----------------------------------------------------------------------------------------------
    var textarea = editorForm.querySelector("textarea[data-code-editor]");
    var frame = editorForm.querySelector("[data-code-editor-frame]");
    var host = frame && frame.querySelector("[data-code-editor-host]");
    if (!textarea || !host || typeof window.require !== "function" || textarea.disabled) { return; }

    var variables = [];
    try { variables = JSON.parse(textarea.getAttribute("data-template-variables") || "[]"); } catch (e) { variables = []; }
    var byName = {};
    variables.forEach(function (v) { byName[v.name] = v; });
    var placeholder = /\{\{\s*([A-Za-z][A-Za-z0-9]*)\s*\}\}/g;

    window.MonacoEnvironment = {
        getWorkerUrl: function () { return "/lib/monaco/vs/base/worker/workerMain.js"; }
    };
    window.require.config({ paths: { vs: "/lib/monaco/vs" } });
    window.require(["vs/editor/editor.main"], start, function () { /* Monaco failed to load: keep the textarea. */ });

    function isDark() {
        var theme = doc.documentElement.getAttribute("data-theme");
        if (theme) { return theme === "dark"; }
        return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
    }

    // Word wrap is a per-browser preference; storage can be unavailable (private window, blocked site data).
    var wrapKey = "forge.codeEditor.wordWrap";
    function loadWrap() {
        try { return window.localStorage.getItem(wrapKey) !== "off"; } catch (e) { return true; }
    }
    function storeWrap(on) {
        try { window.localStorage.setItem(wrapKey, on ? "on" : "off"); } catch (e) { /* not remembered */ }
    }

    function start(monaco) {
        frame.hidden = false;
        textarea.hidden = true;
        textarea.required = false; // a hidden required field would block the submit; the server validates

        var editor = monaco.editor.create(host, {
            value: textarea.value,
            language: "html",
            theme: isDark() ? "vs-dark" : "vs",
            automaticLayout: true,
            minimap: { enabled: false },
            wordWrap: loadWrap() ? "on" : "off",
            lineNumbersMinChars: 3,
            glyphMargin: false,
            lineDecorationsWidth: 6,
            fontSize: 13,
            tabSize: 2,
            scrollBeyondLastLine: false,
            renderLineHighlight: "line",
            fixedOverflowWidgets: true,
            ariaLabel: "Body (HTML)",
            quickSuggestions: { other: true, comments: false, strings: true }
        });
        var model = editor.getModel();
        model.setEOL(monaco.editor.EndOfLineSequence.LF); // stored with LF, so the length matches the server's
        var maxLength = parseInt(frame.querySelector("[data-status-length]").getAttribute("data-max"), 10) || 0;

        // Follow the admin theme (the menu sets data-theme; "System" follows the OS).
        function applyTheme() { monaco.editor.setTheme(isDark() ? "vs-dark" : "vs"); }
        new MutationObserver(applyTheme).observe(doc.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
        if (window.matchMedia) { window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", applyTheme); }

        // The textarea stays the form field; changes also refresh the preview (the "input" listener above).
        var validateTimer = 0;
        model.onDidChangeContent(function () {
            textarea.value = model.getValue();
            textarea.dispatchEvent(new Event("input", { bubbles: true }));
            window.clearTimeout(validateTimer);
            validateTimer = window.setTimeout(validate, 250);
        });
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, function () { editorForm.requestSubmit(); });

        // IntelliSense: {{variables}} when typing "{", replacing what was typed and any auto-closed braces.
        monaco.languages.registerCompletionItemProvider("html", {
            triggerCharacters: ["{"],
            provideCompletionItems: function (m, position) {
                var line = m.getLineContent(position.lineNumber);
                var before = line.substring(0, position.column - 1);
                var typed = /\{\{?\s*([A-Za-z0-9]*)$/.exec(before);
                if (!typed) { return { suggestions: [] }; }
                var after = /^[A-Za-z0-9]*\s*\}{0,2}/.exec(line.substring(position.column - 1))[0];
                var range = new monaco.Range(position.lineNumber, position.column - typed[0].length,
                    position.lineNumber, position.column + after.length);
                return {
                    suggestions: variables.map(function (v, index) {
                        return {
                            label: "{{" + v.name + "}}",
                            kind: monaco.languages.CompletionItemKind.Variable,
                            insertText: "{{" + v.name + "}}",
                            filterText: "{{" + v.name,
                            range: range,
                            sortText: String(index).padStart(3, "0"),
                            detail: v.required ? "Required variable" : "Variable",
                            documentation: { value: "Example: `" + v.sample + "`" }
                        };
                    })
                };
            }
        });

        // Hover: what a variable is filled with.
        monaco.languages.registerHoverProvider("html", {
            provideHover: function (m, position) {
                var line = m.getLineContent(position.lineNumber);
                var match;
                placeholder.lastIndex = 0;
                while ((match = placeholder.exec(line)) !== null) {
                    var start = match.index + 1, end = match.index + match[0].length + 1;
                    if (position.column >= start && position.column <= end) {
                        var v = byName[match[1]];
                        return {
                            range: new monaco.Range(position.lineNumber, start, position.lineNumber, end),
                            contents: v
                                ? [{ value: "**{{" + v.name + "}}**" + (v.required ? " - required" : "") }, { value: "Example: `" + v.sample + "`" }]
                                : [{ value: "**Unknown variable** - not available in this template." }]
                        };
                    }
                }
                return null;
            }
        });

        // Validation: the server's rules as markers, plus highlighting of the variables.
        var decorations = editor.createDecorationsCollection([]);
        function validate() {
            var text = model.getValue();
            var markers = [];
            var highlights = [];
            var used = {};
            var match;
            placeholder.lastIndex = 0;
            while ((match = placeholder.exec(text)) !== null) {
                var range = rangeOf(match.index, match[0].length);
                used[match[1]] = true;
                if (byName[match[1]]) {
                    highlights.push({ range: range, options: { inlineClassName: "dnf-code-editor__var" } });
                } else {
                    markers.push(marker(range, monaco.MarkerSeverity.Error, "Unknown variable {{" + match[1] + "}}. Available: " +
                        variables.map(function (v) { return "{{" + v.name + "}}"; }).join(", ")));
                }
            }

            // Braces left after removing the valid placeholders, e.g. "{{resetUrl}".
            var rest = text.replace(placeholder, function (all) { return new Array(all.length + 1).join(" "); });
            var stray = /\{\{|\}\}/g;
            while ((match = stray.exec(rest)) !== null) {
                markers.push(marker(rangeOf(match.index, 2), monaco.MarkerSeverity.Error,
                    "Incomplete placeholder: write variables as {{name}}."));
            }

            variables.forEach(function (v) {
                if (v.required && !used[v.name]) {
                    markers.push(marker(new monaco.Range(1, 1, 1, 1), monaco.MarkerSeverity.Error,
                        "The body must contain {{" + v.name + "}} - without it the email is useless."));
                }
            });

            var script = /<script\b/gi;
            while ((match = script.exec(text)) !== null) {
                markers.push(marker(rangeOf(match.index, match[0].length), monaco.MarkerSeverity.Warning,
                    "Email clients don't run scripts."));
            }

            if (!text.trim()) {
                markers.push(marker(new monaco.Range(1, 1, 1, 1), monaco.MarkerSeverity.Error, "Enter a body."));
            } else if (maxLength > 0 && text.trim().length > maxLength) {
                markers.push(marker(new monaco.Range(1, 1, 1, 1), monaco.MarkerSeverity.Error,
                    "The body is limited to " + maxLength + " characters."));
            }

            monaco.editor.setModelMarkers(model, "email-template", markers);
            decorations.set(highlights);
        }

        function rangeOf(offset, length) {
            var from = model.getPositionAt(offset), to = model.getPositionAt(offset + length);
            return new monaco.Range(from.lineNumber, from.column, to.lineNumber, to.column);
        }

        function marker(range, severity, message) {
            return {
                severity: severity, message: message,
                startLineNumber: range.startLineNumber, startColumn: range.startColumn,
                endLineNumber: range.endLineNumber, endColumn: range.endColumn
            };
        }

        // ---- Status bar --------------------------------------------------------------------------------------------
        var status = frame.querySelector("[data-code-editor-status]");
        var errorsEl = status.querySelector("[data-status-errors]");
        var warningsEl = status.querySelector("[data-status-warnings]");
        var problemsButton = status.querySelector("[data-status-problems]");
        var positionEl = status.querySelector("[data-status-position]");
        var lengthEl = status.querySelector("[data-status-length]");
        var wrapButton = status.querySelector("[data-status-wrap]");
        var numberFormat = new Intl.NumberFormat();
        function flag(el, name, on) {
            if (on) { el.setAttribute(name, "true"); } else { el.removeAttribute(name); }
        }

        function showProblems() {
            var errors = 0, warnings = 0;
            monaco.editor.getModelMarkers({ resource: model.uri }).forEach(function (m) {
                if (m.severity === monaco.MarkerSeverity.Error) { errors++; }
                else if (m.severity === monaco.MarkerSeverity.Warning) { warnings++; }
            });
            errorsEl.textContent = errors;
            warningsEl.textContent = warnings;
            flag(errorsEl.parentElement, "data-active", errors > 0);
            flag(warningsEl.parentElement, "data-active", warnings > 0);
            var label = errors + (errors === 1 ? " error, " : " errors, ") + warnings + (warnings === 1 ? " warning" : " warnings");
            problemsButton.setAttribute("aria-label", label + (errors + warnings > 0 ? " - go to the next problem" : ""));
        }
        monaco.editor.onDidChangeMarkers(function (uris) {
            if (uris.some(function (uri) { return uri.toString() === model.uri.toString(); })) { showProblems(); }
        });
        problemsButton.addEventListener("click", function () {
            editor.focus();
            editor.trigger("status-bar", "editor.action.marker.next", null);
        });

        function showPosition() {
            var position = editor.getPosition() || { lineNumber: 1, column: 1 };
            var text = "Ln " + position.lineNumber + ", Col " + position.column;
            var selection = editor.getSelection();
            var selected = selection ? model.getValueInRange(selection).length : 0;
            positionEl.textContent = selected > 0 ? text + " (" + numberFormat.format(selected) + " selected)" : text;
        }
        editor.onDidChangeCursorSelection(showPosition);

        function showLength() {
            var length = model.getValue().trim().length; // the server trims before checking the limit
            lengthEl.textContent = numberFormat.format(length) + " / " + numberFormat.format(maxLength);
            flag(lengthEl, "data-over", maxLength > 0 && length > maxLength);
            lengthEl.title = "Characters (limit " + numberFormat.format(maxLength) + ")";
        }
        model.onDidChangeContent(showLength);

        function setWrap(on) {
            editor.updateOptions({ wordWrap: on ? "on" : "off" });
            wrapButton.setAttribute("aria-pressed", on ? "true" : "false");
        }
        function toggleWrap() {
            var on = wrapButton.getAttribute("aria-pressed") !== "true";
            setWrap(on);
            storeWrap(on);
        }
        wrapButton.addEventListener("click", toggleWrap);
        editor.addCommand(monaco.KeyMod.Alt | monaco.KeyCode.KeyZ, toggleWrap);

        setWrap(loadWrap());
        validate();
        showProblems();
        showPosition();
        showLength();
    }
})();
