function signal(initial, equals = Object.is) {
  let current = initial;
  const listeners = /* @__PURE__ */ new Set();
  return {
    get value() {
      return current;
    },
    set(value) {
      if (equals(current, value)) return;
      current = value;
      for (const l of [...listeners]) l(current);
    },
    update(fn) {
      this.set(fn(current));
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    }
  };
}
export {
  signal
};
