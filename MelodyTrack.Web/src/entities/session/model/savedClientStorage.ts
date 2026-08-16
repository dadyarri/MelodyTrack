import * as v from "valibot";

const storageKey = "melodytrack.savedClientIdentities";
const storageVersion = 1;

const savedClientIdentitySchema = v.object({
  identityId: v.pipe(v.string(), v.minLength(1)),
  reference: v.pipe(v.string(), v.minLength(1)),
  displayLabel: v.pipe(v.string(), v.minLength(1)),
  lastUsedAtUtc: v.pipe(v.string(), v.isoTimestamp()),
});

const savedClientEnvelopeSchema = v.object({
  version: v.literal(storageVersion),
  identities: v.array(savedClientIdentitySchema),
});

export type SavedClientIdentity = v.InferOutput<typeof savedClientIdentitySchema>;

type SavedClientStorage = Pick<Storage, "getItem" | "setItem" | "removeItem">;
const listeners = new Set<() => void>();

if (typeof window !== "undefined") {
  window.addEventListener("storage", (event) => {
    if (event.key === storageKey || event.key === null) {
      notifyListeners();
    }
  });
}

export const savedClientStorage = {
  read(storage: SavedClientStorage | null = getBrowserStorage()): SavedClientIdentity[] {
    if (!storage) {
      return [];
    }

    try {
      const serialized = storage.getItem(storageKey);
      if (!serialized) {
        return [];
      }

      const parsed = v.safeParse(savedClientEnvelopeSchema, JSON.parse(serialized));
      if (!parsed.success) {
        storage.removeItem(storageKey);
        return [];
      }

      return [...parsed.output.identities].sort((left, right) => right.lastUsedAtUtc.localeCompare(left.lastUsedAtUtc));
    } catch {
      try {
        storage.removeItem(storageKey);
      } catch {
        // Storage is unavailable; the chooser safely falls back to an empty state.
      }
      return [];
    }
  },
  remember(identity: SavedClientIdentity, storage: SavedClientStorage | null = getBrowserStorage()) {
    if (!storage || !v.safeParse(savedClientIdentitySchema, identity).success) {
      return false;
    }

    try {
      const identities = savedClientStorage.read(storage).filter((item) => item.identityId !== identity.identityId);
      storage.setItem(storageKey, JSON.stringify({ version: storageVersion, identities: [identity, ...identities].slice(0, 20) }));
      notifyListeners();
      return true;
    } catch {
      return false;
    }
  },
  forget(identityId: string, storage: SavedClientStorage | null = getBrowserStorage()) {
    if (!storage) {
      return false;
    }

    try {
      const identities = savedClientStorage.read(storage).filter((item) => item.identityId !== identityId);
      if (identities.length === 0) {
        storage.removeItem(storageKey);
      } else {
        storage.setItem(storageKey, JSON.stringify({ version: storageVersion, identities }));
      }
      notifyListeners();
      return true;
    } catch {
      return false;
    }
  },
  subscribe(listener: () => void) {
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  },
};

function getBrowserStorage(): SavedClientStorage | null {
  if (typeof window === "undefined") {
    return null;
  }

  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

function notifyListeners() {
  for (const listener of listeners) {
    listener();
  }
}
