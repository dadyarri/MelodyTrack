import { useCallback, useMemo } from "react";

import type { UrlCopyModalContent, UrlCopyModalProps } from "./UrlCopyModal";
import { useCopyTextModal } from "./useCopyTextModal";

export function useUrlCopyModal(ownerKey?: string | null) {
  const { openCopyTextModal, closeCopyTextModal, copyTextModalProps } = useCopyTextModal(ownerKey);
  const openUrlModal = useCallback(
    (content: UrlCopyModalContent) => {
      openCopyTextModal({
        value: content.url,
        title: content.title,
        description: content.description,
        fieldLabel: content.fieldLabel,
        copyButtonLabel: content.copyButtonLabel,
        copiedConfirmation: content.copiedConfirmation,
        copyFailure: content.copyFailure,
        closeLabel: content.closeLabel,
        warning: content.warning,
        context: content.context,
        followUpAction: content.followUpAction,
      });
    },
    [openCopyTextModal],
  );
  const urlModalProps = useMemo<UrlCopyModalProps>(
    () => ({
      content: copyTextModalProps.content
        ? {
            url: copyTextModalProps.content.value,
            title: copyTextModalProps.content.title,
            description: copyTextModalProps.content.description,
            fieldLabel: copyTextModalProps.content.fieldLabel,
            copyButtonLabel: copyTextModalProps.content.copyButtonLabel,
            copiedConfirmation: copyTextModalProps.content.copiedConfirmation,
            copyFailure: copyTextModalProps.content.copyFailure,
            closeLabel: copyTextModalProps.content.closeLabel,
            warning: copyTextModalProps.content.warning,
            context: copyTextModalProps.content.context,
            followUpAction: copyTextModalProps.content.followUpAction,
          }
        : null,
      open: copyTextModalProps.open,
      onClose: closeCopyTextModal,
    }),
    [closeCopyTextModal, copyTextModalProps],
  );

  return { openUrlModal, closeUrlModal: closeCopyTextModal, urlModalProps };
}
