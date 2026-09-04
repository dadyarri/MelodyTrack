import type { CopyTextModalContent, CopyTextModalProps } from "./CopyTextModal";
import { CopyTextModal } from "./CopyTextModal";

export type UrlCopyModalContent = Omit<CopyTextModalContent, "value" | "copyFailure"> & {
  url: string;
  copyFailure?: string;
};

export type UrlCopyModalProps = {
  open: boolean;
  content: UrlCopyModalContent | null;
  onClose: () => void;
};

const urlDefaults = {
  title: "Ссылка готова",
  description: "Скопируйте ссылку и отправьте её нужному человеку.",
  fieldLabel: "Ссылка",
  copyButtonLabel: "Скопировать ссылку",
  copiedConfirmation: "Ссылка скопирована",
  copyFailure: "Не удалось скопировать автоматически. Выделите ссылку и скопируйте её вручную.",
};

export function UrlCopyModal({ open, content, onClose }: UrlCopyModalProps) {
  const textContent: CopyTextModalProps["content"] = content
    ? {
        value: content.url,
        title: content.title ?? urlDefaults.title,
        description: content.description ?? urlDefaults.description,
        fieldLabel: content.fieldLabel ?? urlDefaults.fieldLabel,
        copyButtonLabel: content.copyButtonLabel ?? urlDefaults.copyButtonLabel,
        copiedConfirmation: content.copiedConfirmation ?? urlDefaults.copiedConfirmation,
        copyFailure: content.copyFailure ?? urlDefaults.copyFailure,
        closeLabel: content.closeLabel,
        warning: content.warning,
        context: content.context,
        followUpAction: content.followUpAction,
      }
    : null;

  return <CopyTextModal open={open} content={textContent} onClose={onClose} />;
}
