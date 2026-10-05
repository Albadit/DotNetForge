// DotNetForge CMS - shared admin script, loaded by _AdminLayout.
// The CMS is server-rendered (ASP.NET Core MVC + Razor). Pages carry no inline script, so the
// Content-Security-Policy can forbid it; behaviour hooks are data attributes handled here.

(function () {
  "use strict";

  // Confirm destructive actions (e.g. revoking an API token, deleting).
  document.addEventListener("submit", function (event) {
    const form = event.target;
    if (form instanceof HTMLFormElement && form.dataset.confirm) {
      if (!window.confirm(form.dataset.confirm)) {
        event.preventDefault();
      }
    }
  });
})();
