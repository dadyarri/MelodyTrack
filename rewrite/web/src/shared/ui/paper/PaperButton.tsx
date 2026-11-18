import type { ButtonHTMLAttributes, ReactNode } from "react";
import styles from "./PaperButton.module.css";

type PaperButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "danger";
  loading?: boolean;
  icon?: ReactNode;
  size?: "small";
};

export function PaperButton({
  variant,
  loading,
  icon,
  size,
  className = "",
  children,
  disabled,
  type = "button",
  ...props
}: PaperButtonProps) {
  const classes = [
    styles.button,
    variant === "primary" ? styles.primary : "",
    variant === "danger" ? styles.danger : "",
    size === "small" ? styles.small : "",
    className,
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <button
      {...props}
      type={type}
      className={classes}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
    >
      {loading ? <span className={styles.loading} aria-hidden="true" /> : icon}
      {children}
    </button>
  );
}
