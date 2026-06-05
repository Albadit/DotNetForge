// DotNetForge CMS - minimal frontend script.
// The CMS is server-rendered (ASP.NET Core MVC + Razor); this file is the entry point
// for any progressive enhancement and is covered by ESLint (see .eslintrc.json).

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
