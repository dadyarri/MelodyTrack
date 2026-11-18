import { type ComponentPropsWithRef, useState } from "react";
import { Icon } from "../Icon";
import styles from "./PaperPasswordInput.module.css";

type PaperPasswordInputProps = ComponentPropsWithRef<"input"> & {
  toggleLabel?: string;
};

export function PaperPasswordInput({
  className = "",
  disabled = false,
  toggleLabel = "пароль",
  ...props
}: PaperPasswordInputProps) {
  const [visible, setVisible] = useState(false);

  return (
    <span className={styles.field}>
      <input
        {...props}
        className={[styles.input, className].filter(Boolean).join(" ")}
        disabled={disabled}
        type={visible ? "text" : "password"}
      />
      <button
        type="button"
        className={styles.toggle}
        disabled={disabled}
        aria-pressed={visible}
        aria-label={`${visible ? "Скрыть" : "Показать"} ${toggleLabel}`}
        title={`${visible ? "Скрыть" : "Показать"} ${toggleLabel}`}
        onClick={() => setVisible(!visible)}
      >
        <Icon name={visible ? "eyeOff" : "eye"} />
      </button>
    </span>
  );
}
