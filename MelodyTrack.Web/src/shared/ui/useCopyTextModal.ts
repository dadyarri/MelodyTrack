import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";

import type { CopyTextModalContent, CopyTextModalProps } from "./CopyTextModal";

type StoredCopyTextModal = {
  ownerKey: string | null;
  content: CopyTextModalContent;
};

export function useCopyTextModal(ownerKey?: string | null) {
  const normalizedOwnerKey = ownerKey ?? null;
  const [storedModal, setStoredModal] = useState<StoredCopyTextModal | null>(null);
  const activeOwnerKeyRef = useRef(normalizedOwnerKey);

  useLayoutEffect(() => {
    activeOwnerKeyRef.current = normalizedOwnerKey;
  }, [normalizedOwnerKey]);

  const openCopyTextModal = useCallback(
    (content: CopyTextModalContent) => {
      if (activeOwnerKeyRef.current !== normalizedOwnerKey) {
        return;
      }
      setStoredModal({ ownerKey: normalizedOwnerKey, content });
    },
    [normalizedOwnerKey],
  );
  const closeCopyTextModal = useCallback(() => {
    setStoredModal(null);
  }, []);

  useEffect(() => {
    const cleanupTimer = window.setTimeout(() => {
      setStoredModal((current) => (current?.ownerKey === normalizedOwnerKey ? current : null));
    }, 0);
    return () => {
      window.clearTimeout(cleanupTimer);
    };
  }, [normalizedOwnerKey]);

  const content = storedModal?.ownerKey === normalizedOwnerKey ? storedModal.content : null;
  const copyTextModalProps = useMemo<CopyTextModalProps>(
    () => ({
      content,
      open: content !== null,
      onClose: closeCopyTextModal,
    }),
    [closeCopyTextModal, content],
  );

  return { openCopyTextModal, closeCopyTextModal, copyTextModalProps };
}
