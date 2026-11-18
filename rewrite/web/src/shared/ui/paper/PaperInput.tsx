import type { InputHTMLAttributes } from "react";
import styles from "./PaperInput.module.css";

export function PaperInput({ className = "", ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={`${styles.input} ${className}`} />;
}
