// Sidebar state (.docs/architecture/pages.md#navigation): the .dnf-nav of _Sidebar.cshtml. Loaded synchronously right
// after the sidebar markup, so the remembered open/closed sub-groups and the scroll position are applied before the
// first paint: every screen is a full page load, and doing this later (deferred) made the sidebar visibly jump on
// each navigation. The group holding the active screen is open and counts as opened, so it stays open after leaving it.
(function () {
    "use strict";
    var nav = document.querySelector(".dnf-sidebar .dnf-nav");
    if (!nav) { return; }

    function read(store, key) { try { return window[store].getItem(key); } catch (e) { return null; } }
    function write(store, key, value) { try { window[store].setItem(key, value); } catch (e) { /* storage blocked */ } }

    var state = {};
    try { state = JSON.parse(read("localStorage", "dnf-nav") || "{}") || {}; } catch (e) { state = {}; }
    Array.prototype.forEach.call(nav.querySelectorAll("details[data-nav-group]"), function (group) {
        var key = group.getAttribute("data-nav-group");
        if (group.getAttribute("data-nav-active") === "true") {
            state[key] = true;
        } else if (Object.prototype.hasOwnProperty.call(state, key)) {
            group.open = !!state[key];
        }
        group.addEventListener("toggle", function () {
            state[key] = group.open;
            write("localStorage", "dnf-nav", JSON.stringify(state));
        });
    });
    write("localStorage", "dnf-nav", JSON.stringify(state));

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
