// Passkeys (.docs/features/mfa.md#passkeys): WebAuthn in the browser, verified on the server (Fido2).
// - [data-passkey-signin] (the two-step sign-in page): "Use passkey" asks /login/mfa/passkey/options for a challenge,
//   lets the browser sign it (navigator.credentials.get) and posts the result to /login/mfa/passkey.
// - [data-passkey-register] (Profile → Sign-in security): "Add passkey" creates a credential
//   (navigator.credentials.create) and posts it with its name to /admin/profile/security/passkey.
// Binary values travel as base64url, as the server's options and the Fido2 models expect. Without WebAuthn the buttons
// stay hidden and a note says so.
(function () {
    "use strict";

    var signin = document.querySelector("[data-passkey-signin]");
    var register = document.querySelector("[data-passkey-register]");
    if (!signin && !register) { return; }

    if (!window.PublicKeyCredential || !navigator.credentials) {
        [signin, register].forEach(function (root) {
            if (!root) { return; }
            var error = root.querySelector("[data-passkey-error]");
            error.textContent = "This browser can't use passkeys. Use another browser or another way.";
            error.hidden = false;
            Array.prototype.forEach.call(root.querySelectorAll("button"), function (b) { b.disabled = true; });
        });
        return;
    }

    function decode(text) {
        var base64 = text.replace(/-/g, "+").replace(/_/g, "/");
        while (base64.length % 4) { base64 += "="; }
        var binary = atob(base64);
        var bytes = new Uint8Array(binary.length);
        for (var i = 0; i < binary.length; i++) { bytes[i] = binary.charCodeAt(i); }
        return bytes.buffer;
    }

    function encode(buffer) {
        var bytes = new Uint8Array(buffer);
        var binary = "";
        for (var i = 0; i < bytes.length; i++) { binary += String.fromCharCode(bytes[i]); }
        return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    }

    // The browser rejects null where it expects an enum or a dictionary, so drop empty values.
    function clean(value) {
        if (Array.isArray(value)) { return value.map(clean); }
        if (value && typeof value === "object" && !(value instanceof ArrayBuffer)) {
            var result = {};
            Object.keys(value).forEach(function (key) {
                if (value[key] !== null && value[key] !== undefined) { result[key] = clean(value[key]); }
            });
            return result;
        }
        return value;
    }

    function creationOptions(options) {
        options = clean(options);
        options.challenge = decode(options.challenge);
        options.user.id = decode(options.user.id);
        (options.excludeCredentials || []).forEach(function (c) { c.id = decode(c.id); });
        return options;
    }

    function requestOptions(options) {
        options = clean(options);
        options.challenge = decode(options.challenge);
        (options.allowCredentials || []).forEach(function (c) { c.id = decode(c.id); });
        return options;
    }

    function credentialJson(credential) {
        var r = credential.response;
        var json = {
            id: credential.id,
            rawId: encode(credential.rawId),
            type: credential.type,
            clientExtensionResults: credential.getClientExtensionResults ? credential.getClientExtensionResults() : {},
            response: { clientDataJSON: encode(r.clientDataJSON) }
        };
        if (r.attestationObject) {
            json.response.attestationObject = encode(r.attestationObject);
            if (r.getTransports) { json.response.transports = r.getTransports(); }
        } else {
            json.response.authenticatorData = encode(r.authenticatorData);
            json.response.signature = encode(r.signature);
            if (r.userHandle) { json.response.userHandle = encode(r.userHandle); }
        }
        return json;
    }

    function post(root, url, body) {
        return fetch(url, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json",
                "X-CSRF-TOKEN": root.querySelector("input[name='__RequestVerificationToken']").value
            },
            body: JSON.stringify(body || {})
        }).then(function (response) {
            if (!response.ok) { throw new Error("HTTP " + response.status); }
            return response.json();
        });
    }

    function failure(error) {
        return error && error.name === "NotAllowedError"
            ? "The passkey prompt was closed or timed out. Try again."
            : error && error.name === "InvalidStateError"
                ? "This passkey is already added."
                : "The passkey didn't work. Try again.";
    }

    function wire(root, start, run) {
        var button = root.querySelector(start);
        var error = root.querySelector("[data-passkey-error]");
        function fail(message) {
            error.textContent = message;
            error.hidden = false;
            button.disabled = false;
        }
        button.addEventListener("click", function (event) {
            event.preventDefault();
            button.disabled = true;
            error.hidden = true;
            run().then(function (result) {
                if (result && result.redirect) { window.location.href = result.redirect; return; }
                fail(result && result.error ? result.error : "The passkey didn't work. Try again.");
            }).catch(function (e) { fail(failure(e)); });
        });
    }

    if (signin) {
        wire(signin, "[data-passkey-start]", function () {
            return post(signin, "/login/mfa/passkey/options").then(function (options) {
                if (options.redirect || options.error) { return options; }
                return navigator.credentials.get({ publicKey: requestOptions(options) }).then(function (credential) {
                    return post(signin, "/login/mfa/passkey", credentialJson(credential));
                });
            });
        });
    }

    if (register) {
        wire(register, "[data-passkey-add]", function () {
            var name = register.querySelector("[name=name]");
            return post(register, "/admin/profile/security/passkey/options").then(function (data) {
                if (data.error) { return data; }
                return navigator.credentials.create({ publicKey: creationOptions(JSON.parse(data.options)) }).then(function (credential) {
                    return post(register, "/admin/profile/security/passkey", { token: data.token, name: name ? name.value : "", credential: credentialJson(credential) });
                });
            });
        });
    }
})();
