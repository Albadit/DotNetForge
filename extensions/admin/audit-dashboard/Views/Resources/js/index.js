// Client-side enhancement for the Audit Dashboard extension view.
// Loaded by Views/Shared/_Layout.cshtml; served from Views/Resources/js via the extension resource endpoint.
(function () {
  "use strict";
  var loaded = document.getElementById("ext-loaded");
  if (loaded) {
    loaded.textContent = "Loaded at " + new Date().toLocaleTimeString();
  }
})();
