import { useCallback, useState, useSyncExternalStore } from "react";

import { getReleaseNotesSeenKey, readReleaseNotesSeenVersion, writeReleaseNotesSeenVersion } from "./releaseNotesStorage";
import { useUnseenReleaseHistory } from "./useUnseenReleaseHistory";

type ReleaseNotesViewState = {
  userId: string | null;
  dismissedVersion: string | null;
  manualOpen: boolean;
};

export function useReleaseNotesController({ userId, automaticEnabled }: { userId: string | null; automaticEnabled: boolean }) {
  const subscribeToAcknowledgement = useCallback(
    (onStoreChange: () => void) => {
      if (!userId) {
        return () => undefined;
      }

      const key = getReleaseNotesSeenKey(userId);
      const handleStorage = (event: StorageEvent) => {
        if (event.key === key || event.key === null) {
          onStoreChange();
        }
      };

      window.addEventListener("storage", handleStorage);
      return () => {
        window.removeEventListener("storage", handleStorage);
      };
    },
    [userId],
  );
  const getAcknowledgedVersion = useCallback(() => (userId ? readReleaseNotesSeenVersion(userId) : null), [userId]);
  const seenVersion = useSyncExternalStore(subscribeToAcknowledgement, getAcknowledgedVersion, () => null);
  const unseenQuery = useUnseenReleaseHistory(seenVersion, Boolean(userId));
  const unseenHistory = unseenQuery.data;
  const currentVersion = unseenHistory?.currentVersion ?? null;
  const [storedView, setStoredView] = useState<ReleaseNotesViewState>(() => createViewState(userId));
  const view = storedView.userId === userId ? storedView : createViewState(userId);
  const automaticOpen = Boolean(
    automaticEnabled &&
      !view.manualOpen &&
      userId &&
      currentVersion &&
      unseenHistory &&
      unseenHistory.releases.length > 0 &&
      seenVersion !== currentVersion &&
      view.dismissedVersion !== currentVersion,
  );

  const openManual = useCallback(() => {
    if (automaticEnabled) {
      setStoredView((current) => ({
        ...(current.userId === userId ? current : createViewState(userId)),
        manualOpen: true,
      }));
    }
  }, [automaticEnabled, userId]);

  const close = useCallback(() => {
    setStoredView((current) => {
      const currentView = current.userId === userId ? current : createViewState(userId);
      if (!userId || !currentVersion) {
        return { ...currentView, manualOpen: false };
      }

      writeReleaseNotesSeenVersion(userId, currentVersion);
      return {
        ...currentView,
        dismissedVersion: currentVersion,
        manualOpen: false,
      };
    });
  }, [currentVersion, userId]);

  return {
    open: automaticEnabled && (view.manualOpen || automaticOpen),
    automaticReleases: automaticOpen ? (unseenHistory?.releases ?? null) : null,
    openManual,
    close,
  };
}

function createViewState(userId: string | null): ReleaseNotesViewState {
  return {
    userId,
    dismissedVersion: null,
    manualOpen: false,
  };
}
