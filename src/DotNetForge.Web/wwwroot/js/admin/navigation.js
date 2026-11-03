// Moving between screens (.docs/architecture/pages.md#navigation). Deferred, right after nav-state.js: it runs before
// any screen's own script (deferred too, later in the page), without holding up the sidebar's first paint.
//
// Like a Nuxt or Next layout, the shell stays and only the screen changes: a sidebar link asks for the next screen
// only (X-Dnf-Screen: the server leaves out the head, sidebar and templates this page already has) and swaps its
// .dnf-shell__main (top bar and content) in place, with the address, title, history (Back/Forward), the active link
// and the toasts brought up to date. The sidebar is never rebuilt and keeps its scroll and open groups.
//
// A screen joins only when every script of its own is marked data-screen, which promises two things (dnfScreen below):
// what the script binds outside its screen (document, window) goes through dnfScreen.on, so it is dropped when the
// screen is left; and work that leaving would lose is reported through dnfScreen.guard. Anything else is an ordinary
// page load: a shell that changed (another release of a file, another sidebar or account - the server's dnf-shell
// fingerprint), a screen with other scripts, a different layout, an error, a session that has ended. So a screen that
// hasn't joined works exactly as before.
(function () {
    "use strict";

    // ---- The screen's lifetime ------------------------------------------------------------------------------------
    var screen = { listeners: new AbortController(), guards: [] };
    window.dnfScreen = {
        // addEventListener for targets that outlive the screen (document, window); removed when the screen is left.
        on: function (target, type, handler, options) {
            var settings = typeof options === "boolean" ? { capture: options } : Object.assign({}, options);
            settings.signal = screen.listeners.signal;
            target.addEventListener(type, handler, settings);
        },
        // isDirty() true: leaving would lose work. The next screen then comes as a full page load, so the browser asks.
        guard: function (isDirty) { screen.guards.push(isDirty); },
    };
    function leaveScreen() {
        screen.listeners.abort();
        screen = { listeners: new AbortController(), guards: [] };
    }

    var nav = document.querySelector(".dnf-sidebar .dnf-nav");
    if (!nav) { return; }

    function read(store, key) { try { return window[store].getItem(key); } catch (e) { return null; } }
    function write(store, key, value) { try { window[store].setItem(key, value); } catch (e) { /* storage blocked */ } }
    function each(selector, root, action) { Array.prototype.forEach.call(root.querySelectorAll(selector), action); }
    function joins(link) {
        var href = link.getAttribute("href") || "";
        return href.indexOf("/admin") === 0 && href.indexOf("/admin/ext/") !== 0; // extension tabs run other people's code
    }

    // ---- A click shows at once ----------------------------------------------------------------------------------
    // The chosen link looks chosen and, if the screen takes a moment, a bar runs along the top (css/admin/shell.css).
    function going(link) {
        each("[data-pressed]", nav, function (other) { other.removeAttribute("data-pressed"); });
        link.setAttribute("data-pressed", "");
        document.documentElement.setAttribute("data-navigating", "");
    }
    function arrived() {
        document.documentElement.removeAttribute("data-navigating");
        each("[data-pressed]", nav, function (link) { link.removeAttribute("data-pressed"); });
    }
    window.addEventListener("pageshow", function (event) { if (event.persisted) { arrived(); } });

    // ---- Loading a screen ---------------------------------------------------------------------------------------
    // Hovering a link loads its screen ahead (like NuxtLink), so the click usually finds it ready. A screen loaded
    // ahead is used for a few seconds only; the server leaves the flash messages alone for it (NavigationWarmUp).
    var ahead = {};
    var aheadFor = 10000;
    // The screens shown so far, in memory only (admin pages are never stored by the browser): asked for again, an
    // unchanged screen comes back as 304 without a body (ScreenUnchangedMiddleware) and this copy is used.
    var kept = {};
    var keptOrder = [];
    function keep(url, copy) {
        if (!kept[url]) { keptOrder.push(url); }
        kept[url] = copy;
        while (keptOrder.length > 12) { delete kept[keptOrder.shift()]; }
    }
    function get(url, purpose) {
        var headers = { Accept: "text/html", "X-Dnf-Screen": "1" };
        if (purpose) { headers["X-Dnf-Purpose"] = purpose; }
        var copy = kept[url];
        if (copy) { headers["If-None-Match"] = copy.etag; }
        return fetch(url, { credentials: "same-origin", headers: headers }).then(function (response) {
            if (response.status === 304 && copy) { return { url: copy.url, text: copy.text }; }
            var type = response.headers.get("content-type") || "";
            if (!response.ok || type.indexOf("text/html") === -1) { return null; }
            return response.text().then(function (text) {
                var etag = response.headers.get("ETag");
                if (etag) { keep(url, { etag: etag, url: response.url, text: text }); }
                return { url: response.url, text: text };
            });
        });
    }
    function load(url) {
        var early = ahead[url];
        delete ahead[url];
        return early && Date.now() - early.at < aheadFor ? early.page : get(url, null);
    }
    var hoverTimer = 0;
    nav.addEventListener("pointerover", function (event) {
        var link = event.target.closest("a[href]");
        if (!link || !joins(link) || link.hasAttribute("aria-current")) { return; }
        window.clearTimeout(hoverTimer);
        hoverTimer = window.setTimeout(function () {
            var url = link.href;
            if (link.hasAttribute("aria-current") || (ahead[url] && Date.now() - ahead[url].at < aheadFor)) { return; }
            ahead[url] = { at: Date.now(), page: get(url, "prefetch").catch(function () { return null; }) };
        }, 80);
    });
    nav.addEventListener("pointerout", function () { window.clearTimeout(hoverTimer); });

    // ---- Does the next screen fit this page? --------------------------------------------------------------------
    function meta(root, name) {
        var tag = root.querySelector("meta[name='" + name + "']");
        return tag ? tag.getAttribute("content") : null;
    }
    // A script that would stay running after its screen: not the screen's own (data-screen), not the shell's
    // (data-lasting), not the development server's live reload (/_framework/).
    function foreign(root) {
        return Array.prototype.some.call(root.querySelectorAll("body script"), function (s) {
            return !s.hasAttribute("data-screen") && !s.hasAttribute("data-lasting") && (s.getAttribute("src") || "").indexOf("/_framework/") !== 0;
        });
    }
    function fits(next) {
        var shell = meta(document, "dnf-shell");
        return !!shell && meta(next, "dnf-shell") === shell && !!next.querySelector(".dnf-shell__main") && !foreign(next) && !foreign(document);
    }

    // ---- Swapping it in -----------------------------------------------------------------------------------------
    var announcer = document.createElement("div");
    announcer.className = "sr-only";
    announcer.setAttribute("aria-live", "polite");
    document.body.appendChild(announcer);

    function swap(next, scrollTo) {
        leaveScreen();
        var shell = document.querySelector("[data-shell]");
        if (shell && shell.getAttribute("data-nav-open") === "true") {
            var close = document.querySelector("[data-drawer-close]");
            if (close) { close.click(); }
        }

        document.title = next.title;
        var main = document.adoptNode(next.querySelector(".dnf-shell__main"));
        document.querySelector(".dnf-shell__main").replaceWith(main);

        // The active link and its group, as the server marked them for the next screen (dnf-active).
        var active = meta(next, "dnf-active");
        each("a[href]", nav, function (link) {
            if (active && link.getAttribute("href") === active) { link.setAttribute("aria-current", "page"); } else { link.removeAttribute("aria-current"); }
        });
        each("details[data-nav-group]", nav, function (group) {
            var holds = !!group.querySelector("a[aria-current]");
            group.setAttribute("data-nav-active", holds ? "true" : "false");
            if (holds) { group.open = true; }
        });
        arrived();

        // Toasts of the next screen.
        var toasts = document.querySelector("[data-toasts]");
        var nextToasts = next.querySelector("[data-toasts]");
        if (toasts && nextToasts) {
            toasts.replaceChildren.apply(toasts, Array.prototype.map.call(nextToasts.childNodes, function (n) { return document.adoptNode(n); }));
        }

        // Before the first paint, as on a page load: saved views, page sizes, closed branches, open tabs, then behaviours.
        if (window.dnfSettle) { window.dnfSettle(main); }
        if (window.dnfUI) {
            window.dnfUI.init(main);
            if (toasts) { window.dnfUI.init(toasts); }
        }
        window.scrollTo(0, scrollTo || 0);

        // The screen's own scripts, in order.
        each("script[data-screen]", document.body, function (old) { old.remove(); });
        each("body script[data-screen]", next, function (source) {
            var script = document.createElement("script");
            Array.prototype.forEach.call(source.attributes, function (a) { script.setAttribute(a.name, a.value); });
            script.async = false;
            document.body.appendChild(script);
        });

        var target = document.getElementById("main");
        if (target) { target.focus({ preventScroll: true }); }
        announcer.textContent = document.title.split(" · ")[0];
    }

    // ---- Going there ----------------------------------------------------------------------------------------------
    var positions = {};
    var entry = 0;
    function newEntry() { return Date.now() + "-" + Math.random().toString(36).slice(2); }
    function current() { return (history.state && history.state.dnf) || null; }
    if (!current()) { history.replaceState(Object.assign({}, history.state, { dnf: newEntry() }), ""); }
    window.addEventListener("scroll", function () { positions[current()] = window.scrollY; }, { passive: true });

    // Work that leaving would lose: the next screen comes as a full load instead, so the browser asks first.
    function blocked() { return screen.guards.some(function (isDirty) { try { return isDirty(); } catch (e) { return false; } }); }
    function visit(url, push, scrollTo) {
        var mine = ++entry;
        load(url).then(function (page) {
            if (mine !== entry) { return; } // another click came after this one
            if (!page) { window.location.assign(url); return; }
            var next = new DOMParser().parseFromString(page.text, "text/html");
            if (!fits(next)) { window.location.assign(page.url); return; }
            if (push) { history.pushState({ dnf: newEntry() }, "", page.url); }
            swap(next, scrollTo);
        }, function () {
            if (mine === entry) { window.location.assign(url); }
        });
    }
    window.addEventListener("popstate", function (event) {
        if (!event.state || !event.state.dnf) { return; }
        if (blocked()) { window.location.assign(window.location.href); return; }
        visit(window.location.href, false, positions[event.state.dnf]);
    });

    // ---- Following a sidebar link -------------------------------------------------------------------------------
    // On the press, not the click: a click needs its press and release on one screen, so a screen arriving between
    // the two would swallow it. Only a plain left mouse press; touch and the keyboard come as a click.
    function plain(event) { return event.button === 0 && !event.ctrlKey && !event.shiftKey && !event.altKey && !event.metaKey; }
    function linkOf(event) {
        var link = event.target.closest("a[href]");
        return link && !link.target && !link.hasAttribute("download") && joins(link) ? link : null;
    }
    function follow(link) {
        window.clearTimeout(hoverTimer); // clicked before the hover counted: no load ahead after all
        if (blocked()) { window.location.assign(link.href); return; } // the browser's question is the feedback
        going(link);
        visit(link.href, true, 0);
    }
    var pressed = null;
    nav.addEventListener("pointerdown", function (event) {
        if (event.pointerType !== "mouse" || !plain(event)) { return; }
        var link = linkOf(event);
        if (!link) { return; }
        pressed = link;
        follow(link);
    });
    nav.addEventListener("click", function (event) {
        var link = linkOf(event);
        if (link && plain(event) && !event.defaultPrevented) {
            event.preventDefault();
            if (link !== pressed) { follow(link); }
        }
        pressed = null;
    });

    // ---- Warm-up after a start ------------------------------------------------------------------------------------
    // Each screen is slow the first time after the app starts, while its code is compiled. So the first screen a
    // browser shows after a start (a new data-started) loads the other sidebar screens in the background, one at a
    // time when the browser is idle.
    var started = document.body.getAttribute("data-started");
    if (started && read("localStorage", "dnf-warm") !== started) {
        write("localStorage", "dnf-warm", started);
        var queue = [];
        each("a[href^='/admin']", nav, function (link) {
            var href = link.getAttribute("href");
            if (joins(link) && !link.hasAttribute("aria-current") && queue.indexOf(href) === -1) { queue.push(href); }
        });
        var idle = function (next) {
            return window.requestIdleCallback ? window.requestIdleCallback(next, { timeout: 1000 }) : window.setTimeout(next, 50);
        };
        var warmNext = function () {
            var href = queue.shift();
            if (!href) { return; }
            fetch(href, { credentials: "same-origin", cache: "no-store", headers: { "X-Dnf-Purpose": "warm-up", "X-Dnf-Screen": "1" } })
                .catch(function () { return null; })
                .then(function () { idle(warmNext); });
        };
        window.addEventListener("load", function () { idle(warmNext); });
    }
})();
