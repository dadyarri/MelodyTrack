import { mergeTrainingProgress, readProgress, type TrainingProgress } from "./progress";

const databaseName = "melodytrack-rewrite-training";
const storeName = "progress";

function abortError(): DOMException {
  return new DOMException("Training storage operation was cancelled.", "AbortError");
}

function openDatabase(signal: AbortSignal): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) {
      reject(abortError());
      return;
    }
    if (typeof indexedDB === "undefined") {
      reject(new Error("Training storage is unavailable."));
      return;
    }
    const request = indexedDB.open(databaseName, 1);
    let blocked = false;
    const cancel = () => reject(abortError());
    signal.addEventListener("abort", cancel, { once: true });
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains(storeName)) {
        request.result.createObjectStore(storeName, { keyPath: "staffId" });
      }
    };
    request.onerror = () => {
      signal.removeEventListener("abort", cancel);
      reject(new Error("Training storage could not be opened."));
    };
    request.onblocked = () => {
      blocked = true;
      signal.removeEventListener("abort", cancel);
      reject(new Error("Training storage is blocked by another tab."));
    };
    request.onsuccess = () => {
      signal.removeEventListener("abort", cancel);
      if (signal.aborted || blocked) {
        request.result.close();
        reject(abortError());
        return;
      }
      request.result.onversionchange = () => request.result.close();
      resolve(request.result);
    };
  });
}

async function runTransaction<T>(
  mode: IDBTransactionMode,
  signal: AbortSignal,
  operation: (store: IDBObjectStore) => IDBRequest<T>,
): Promise<T> {
  const database = await openDatabase(signal);
  return new Promise<T>((resolve, reject) => {
    if (signal.aborted) {
      database.close();
      reject(abortError());
      return;
    }
    let transaction: IDBTransaction;
    try {
      transaction = database.transaction(storeName, mode);
    } catch {
      database.close();
      reject(new Error("Training storage transaction failed."));
      return;
    }
    const cancel = () => transaction.abort();
    signal.addEventListener("abort", cancel, { once: true });
    const request = operation(transaction.objectStore(storeName));
    const close = () => {
      signal.removeEventListener("abort", cancel);
      database.close();
    };
    transaction.oncomplete = () => {
      close();
      resolve(request.result);
    };
    transaction.onabort = () => {
      close();
      reject(
        signal.aborted ? abortError() : new Error("Training storage transaction was aborted."),
      );
    };
    transaction.onerror = () => {
      /* onabort reports failure without exposing stored data. */
    };
  });
}

export async function loadTrainingProgress(
  staffId: string,
  signal: AbortSignal,
): Promise<TrainingProgress> {
  const value = await runTransaction<unknown>("readonly", signal, (store) => store.get(staffId));
  return readProgress(value, staffId);
}

export async function saveTrainingProgress(
  progress: TrainingProgress,
  signal: AbortSignal,
): Promise<void> {
  await runTransaction<unknown>("readwrite", signal, (store) => {
    const request = store.get(progress.staffId);
    request.onsuccess = () => {
      try {
        const previous = readProgress(request.result, progress.staffId);
        // A stale tab must not erase completion, article reading or explicit deferral.
        store.put(mergeTrainingProgress(previous, progress));
      } catch {
        store.transaction.abort();
      }
    };
    return request;
  });
}
