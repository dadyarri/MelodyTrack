import "@testing-library/jest-dom/vitest";

import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

const localStorageValues = new Map<string, string>();
const localStorageMock: Storage = {
  get length() {
    return localStorageValues.size;
  },
  clear() {
    localStorageValues.clear();
  },
  getItem(key) {
    return localStorageValues.get(key) ?? null;
  },
  key(index) {
    return [...localStorageValues.keys()][index] ?? null;
  },
  removeItem(key) {
    localStorageValues.delete(key);
  },
  setItem(key, value) {
    localStorageValues.set(key, value);
  },
};

Object.defineProperty(globalThis, "localStorage", {
  configurable: true,
  value: localStorageMock,
});

class ResizeObserverMock implements ResizeObserver {
  disconnect() {}
  observe() {}
  unobserve() {}
}

Object.defineProperty(globalThis, "ResizeObserver", {
  configurable: true,
  value: ResizeObserverMock,
});

afterEach(cleanup);
