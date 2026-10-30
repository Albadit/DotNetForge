// JavaScript lint (.docs/guides/testing.md#javascript): `npm ci` once, then `npm run lint`. Development tooling only.
import js from "@eslint/js";
import globals from "globals";

export default [
  { ignores: ["**/bin/**", "**/obj/**", "**/wwwroot/lib/**", "**/TestResults/**", "storage/**", "tools/**"] },
  js.configs.recommended,
  {
    rules: {
      // `catch (e)` and an empty catch around storage are this code's idiom: a failed read just means "nothing saved".
      "no-unused-vars": ["error", { caughtErrors: "none" }],
      "no-empty": ["error", { allowEmptyCatch: true }],
    },
  },
  {
    // Plain scripts: each screen's script, the UI library and extension scripts.
    files: ["src/**/*.js", "extensions/**/*.js"],
    languageOptions: { sourceType: "script", globals: globals.browser },
  },
  {
    // The page builder: ES modules.
    files: ["src/DotNetForge.Web/wwwroot/js/builder/**/*.js"],
    languageOptions: { sourceType: "module", globals: globals.browser },
  },
  {
    // The sanitizer: control characters are what it removes.
    files: ["src/DotNetForge.Web/wwwroot/js/builder/core/values.js"],
    rules: { "no-control-regex": "off" },
  },
  {
    files: ["tests/js/*.mjs", "eslint.config.mjs"],
    languageOptions: { sourceType: "module", globals: globals.node },
  },
];
