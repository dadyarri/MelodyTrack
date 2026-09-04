const legacyReferenceLabelStorageKeyPrefix = "melodytrack:reference-labels:";
const maximumLabelsPerKind = 250;

export type ReferenceLabelKind = "client" | "service" | "user" | "role" | "expense-category" | "client-source";

const labelsByKind = new Map<ReferenceLabelKind, Map<string, string>>();

if (typeof window !== "undefined") {
  discardLegacyReferenceLabels(window.localStorage);
}

export function getCachedReferenceLabel(kind: ReferenceLabelKind, id?: string) {
  return id ? labelsByKind.get(kind)?.get(id) : undefined;
}

export function rememberReferenceLabel(kind: ReferenceLabelKind, id?: string, label?: string) {
  if (!id || !label) {
    return;
  }

  const labels = getKindLabels(kind);
  labels.delete(id);
  labels.set(id, label);
  while (labels.size > maximumLabelsPerKind) {
    const oldestId = labels.keys().next().value;
    if (typeof oldestId !== "string") {
      break;
    }
    labels.delete(oldestId);
  }
}

export function rememberReferenceLabels(kind: ReferenceLabelKind, items: Array<{ id: string; label: string }>) {
  for (const item of items) {
    rememberReferenceLabel(kind, item.id, item.label);
  }
}

export function clearReferenceLabels() {
  labelsByKind.clear();
}

export function discardLegacyReferenceLabels(storage: Storage) {
  const keysToRemove: string[] = [];
  for (let index = 0; index < storage.length; index += 1) {
    const key = storage.key(index);
    if (key?.startsWith(legacyReferenceLabelStorageKeyPrefix)) {
      keysToRemove.push(key);
    }
  }
  for (const key of keysToRemove) {
    storage.removeItem(key);
  }
}

function getKindLabels(kind: ReferenceLabelKind) {
  let labels = labelsByKind.get(kind);
  if (!labels) {
    labels = new Map();
    labelsByKind.set(kind, labels);
  }
  return labels;
}
