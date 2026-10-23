// The page builder's server API (.docs/features/page-builder.md#api): JSON under /admin/content/{pageId}/builder/,
// every POST with the antiforgery token in the X-CSRF-TOKEN header. Errors become ApiError with the server's message.
class ApiError extends Error {
  constructor(status, message, body) {
    super(message);
    this.status = status;
    this.body = body;
  }
  status;
  body;
}

class Api {
  constructor(config) {
    this.config = config;
  }
  config;

  async call(method, path, body) {
    const headers = { "Accept": "application/json", "X-CSRF-TOKEN": this.config.token() };
    let payload;
    if (body instanceof FormData) payload = body;
    else if (body !== void 0) {
      headers["Content-Type"] = "application/json";
      payload = JSON.stringify(body);
    }
    let res;
    try {
      res = await fetch(this.config.base + path, { method, headers, body: payload, credentials: "same-origin" });
    } catch {
      throw new ApiError(0, "The server could not be reached. Check your connection.");
    }
    if (res.redirected && /\/account\/(login|denied)/.test(res.url)) {
      throw new ApiError(401, "Your session has expired. Sign in again to continue.");
    }
    const text = await res.text();
    let data = void 0;
    if (text) {
      try {
        data = JSON.parse(text);
      } catch {
        data = text;
      }
    }
    if (!res.ok) {
      const message = data && typeof data === "object" && typeof data.message === "string" ? data.message
        : res.status === 401 ? "Your session has expired. Sign in again to continue."
        : res.status === 403 ? "You do not have permission to do this."
        : `The server returned an error (${res.status}).`;
      throw new ApiError(res.status, message, data);
    }
    return data;
  }

  state() { return this.call("GET", "state"); }
  save(document, baseVersion, force = false) { return this.call("POST", "save", { document, baseVersion, force }); }
  publish() { return this.call("POST", "publish", {}); }
  unpublish() { return this.call("POST", "unpublish", {}); }
  discard() { return this.call("POST", "discard", {}); }
  heartbeat() { return this.call("POST", "heartbeat", {}); }
  leave() { /* no presence tracking on this server */ }
  /** Public images of the media library, newest first: [{ url, name, contentType }]. */
  media() { return this.call("GET", "media"); }
  /**
   * The image picker's listing (the reference's folder browser). The media library has no folders, so there is one
   * level: every public image, and upload when the user may upload media.
   */
  async files() {
    const files = await this.media();
    return { folder: "", parent: null, folders: [], files, canUpload: this.config.canUpload === true };
  }
  /** Uploads an image to the media library (public). The folder argument of the reference API is not used. */
  upload(_folder, file) {
    const form = new FormData();
    form.append("file", file, file.name);
    return this.call("POST", "upload", form);
  }
}

function antiForgeryToken(doc = document) {
  const input = doc.querySelector('input[name="__RequestVerificationToken"]');
  return input?.value ?? "";
}

export {
  Api,
  ApiError,
  antiForgeryToken
};
