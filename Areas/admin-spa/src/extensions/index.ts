// Admin SPA extensions.
//
// Each import below self-registers an extension (a sidebar tab + its own page) into the module
// registry. To add a new admin extension later, drop a folder under src/extensions/<name>/ that
// calls registerExtension(...) and add a single import line here - the shell and router pick it up
// automatically, with no changes to the layout.
import "./hello";
