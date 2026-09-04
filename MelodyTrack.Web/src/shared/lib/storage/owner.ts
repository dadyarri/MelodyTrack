let ownerUserIdProvider: () => string | null = () => null;

export function configureDraftOwner(provider: () => string | null) {
  ownerUserIdProvider = provider;
}

export function requireStorageOwnerUserId() {
  const ownerUserId = getStorageOwnerUserId();
  if (!ownerUserId) {
    throw new Error("Durable storage requires an authenticated user.");
  }
  return ownerUserId;
}

export function getStorageOwnerUserId() {
  return ownerUserIdProvider();
}
