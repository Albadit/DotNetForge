// Sidebar state (.docs/architecture/pages.md#navigation): the .dnf-nav of _Sidebar.cshtml. The sub-groups the user
// opened or closed are kept in the dnf-nav cookie, so the server renders them that way and a full page arrives with
// them in place; this applies them again (a page from before the cookie) and keeps the cookie up to date. The group
// holding the active screen is open and counts as opened, so it stays open after leaving it. Loaded synchronously right
// after the sidebar markup, so the scroll position is restored before the first paint (navigation.js swaps most
// screens in place, and the sidebar then simply stays).
(function () {
    "use strict";
    var nav = document.querySelector(".dnf-sidebar .dnf-nav");
    if (!nav) { return; }

    function read(store, key) { try { return window[store].getItem(key); } catch (e) { return null; } }
    function write(store, key, value) { try { window[store].setItem(key, value); } catch (e) { /* storage blocked */ } }

    // Earlier releases kept the groups in localStorage; they move to the cookie the server reads.
    function load() {
        var cookie = /(?:^|;\s*)dnf-nav=([^;]*)/.exec(document.cookie);
        try { return JSON.parse(cookie ? decodeURIComponent(cookie[1]) : read("localStorage", "dnf-nav") || "{}") || {}; } catch (e) { return {}; }
    }
    function save() {
        document.cookie = "dnf-nav=" + encodeURIComponent(JSON.stringify(state)) + "; path=/admin; max-age=31536000; samesite=lax"
            + (window.location.protocol === "https:" ? "; secure" : "");
    }

    var state = load();
    try { window.localStorage.removeItem("dnf-nav"); } catch (e) { /* storage blocked */ }
    Array.prototype.forEach.call(nav.querySelectorAll("details[data-nav-group]"), function (group) {
        var key = group.getAttribute("data-nav-group");
        if (group.getAttribute("data-nav-active") === "true") {
            state[key] = true;
        } else if (Object.prototype.hasOwnProperty.call(state, key)) {
            group.open = !!state[key];
        }
        group.addEventListener("toggle", function () {
            state[key] = group.open;
            save();
        });
    });
    save();

    // Keep the sidebar where it was scrolled to (per tab); without a saved position, bring the active link into view.
    var top = parseInt(read("sessionStorage", "dnf-nav-scroll"), 10);
    if (!isNaN(top)) {
        nav.scrollTop = top;
    } else {
        var active = nav.querySelector(".dnf-nav__link[aria-current]");
        if (active && active.offsetTop > nav.clientHeight) { nav.scrollTop = active.offsetTop - nav.clientHeight / 2; }
    }
    window.addEventListener("pagehide", function () { write("sessionStorage", "dnf-nav-scroll", String(nav.scrollTop)); });
})();
