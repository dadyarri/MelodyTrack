import { type ButtonHTMLAttributes, type CSSProperties, type HTMLAttributes, type ReactNode, useId } from "react";

import { PortalAppearanceProvider, usePortalAppearance } from "@/shared/config";

import flower from "./assets/flower-twirl.svg";
import guitar from "./assets/guitar.svg";
import pick from "./assets/guitar-pick.svg";
import laurel from "./assets/laurel-crown.svg";
import quill from "./assets/quill-ink.svg";
import scroll from "./assets/scroll-unfurled.svg";
import styles from "./RpgTheme.module.css";

const icons = { flower, guitar, laurel, pick, quill, scroll };
export type RpgIconName = keyof typeof icons;

export function RpgTheme(props: HTMLAttributes<HTMLDivElement>) {
  return <PortalAppearanceProvider><PortalThemeSurface {...props} /></PortalAppearanceProvider>;
}

function PortalThemeSurface({ children, className = "", ...props }: HTMLAttributes<HTMLDivElement>) {
  const { appearance } = usePortalAppearance();
  return (
    <div {...props} className={`${styles.theme} ${className}`} data-portal-appearance={appearance}>
      {children}
    </div>
  );
}

export function PortalAppearanceSwitch() {
  const { appearance, setAppearance, warning } = usePortalAppearance();
  const name = useId();
  return (
    <div className={styles.appearanceControl}>
      <fieldset className={styles.appearanceOptions}>
        <legend>Оформление</legend>
        {(["rpg", "minimal"] as const).map((value) => (
          <label key={value} className={styles.appearanceOption}>
            <input type="radio" name={name} value={value} checked={appearance === value} onChange={() => { setAppearance(value); }} />
            <span>{value === "rpg" ? "RPG" : "Минималистичное"}</span>
          </label>
        ))}
      </fieldset>
      {warning ? <span role="status" className={styles.appearanceWarning}>{warning}</span> : null}
    </div>
  );
}

/** Decorative artwork: its accompanying text supplies the accessible name. */
export function RpgIcon({ name, large = false }: { name: RpgIconName; large?: boolean }) {
  const artworkStyle = { "--rpg-icon-art": `url("${icons[name]}")` } as CSSProperties;
  return (
    <span className={large ? styles.illustration : styles.icon} data-icon={name} aria-hidden="true">
      <span className={styles.iconGlyph} style={artworkStyle} />
    </span>
  );
}

export function RpgFrame({ children, className = "", ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div {...props} className={`${styles.frame} ${className}`}>
      <span className={styles.frameCorners} aria-hidden="true">
        <span data-corner="top-left" />
        <span data-corner="top-right" />
        <span data-corner="bottom-left" />
        <span data-corner="bottom-right" />
      </span>
      {children}
    </div>
  );
}

/** A presentation mark composed from sourced artwork and the product initials. */
export function RpgSignature({ compact = false }: { compact?: boolean }) {
  return (
    <span className={styles.signature} data-compact={compact || undefined} aria-hidden="true">
      <RpgIcon name="pick" />
      {!compact ? <span className={styles.signatureLetters}>MT</span> : null}
    </span>
  );
}

export function RpgDivider() {
  return (
    <div className={styles.divider} aria-hidden="true">
      <span />
      <RpgSignature compact />
      <span />
    </div>
  );
}

type RpgButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "secondary" | "quiet" | "danger";
  shape?: "standard" | "plaque" | "ribbon" | "scroll";
  pending?: boolean;
  pendingLabel?: string;
};

export function RpgButton({
  children,
  variant = "secondary",
  shape = "standard",
  pending = false,
  pendingLabel,
  disabled,
  className = "",
  type = "button",
  ...props
}: RpgButtonProps) {
  return (
    <button
      {...props}
      type={type}
      className={`${styles.button} ${className}`}
      data-variant={variant}
      data-shape={shape}
      disabled={disabled || pending}
      aria-busy={pending}
    >
      {pending ? <span className={styles.spark} aria-hidden="true" /> : null}
      {pending && pendingLabel ? pendingLabel : children}
    </button>
  );
}

export function RpgPanel({ children, className = "", surface = "leather", ...props }: HTMLAttributes<HTMLDivElement> & {
  surface?: "leather" | "parchment";
}) {
  return (
    <div {...props} className={`${styles.panel} ${className}`} data-surface={surface}>
      {children}
    </div>
  );
}

export function RpgBadge({ children, tone = "neutral" }: { children: ReactNode; tone?: "neutral" | "success" | "warning" }) {
  return (
    <span className={styles.badge} data-tone={tone}>
      {children}
    </span>
  );
}

export function RpgCard({ title, eyebrow, description, icon, children, footer, accent = "sage" }: {
  title: string;
  eyebrow: string;
  description?: string;
  icon: RpgIconName;
  children?: ReactNode;
  footer?: ReactNode;
  accent?: "sage" | "copper" | "plum" | "teal";
}) {
  return (
    <article className={`${styles.panel} ${styles.card}`} data-accent={accent}>
      <div className={styles.cardHeading}>
        <div>
          <p className={styles.eyebrow}>{eyebrow}</p>
          <h3>{title}</h3>
        </div>
        <RpgIcon name={icon} large />
      </div>
      {description ? <p className={styles.muted}>{description}</p> : null}
      {children}
      {footer ? <div className={styles.cardFooter}>{footer}</div> : null}
    </article>
  );
}

export function RpgProgress({ label, value, max = 100, detail }: { label: string; value: number; max?: number; detail?: string }) {
  const safeMax = Number.isFinite(max) && max > 0 ? max : 100;
  const safeValue = Number.isFinite(value) ? Math.min(safeMax, Math.max(0, value)) : 0;
  return (
    <div className={styles.progressGroup}>
      <div className={styles.progressLabel}>
        <span>{label}</span>
        <span>{detail ?? `${String(Math.round((safeValue / safeMax) * 100))}%`}</span>
      </div>
      <progress className={styles.progress} aria-label={label} aria-valuetext={detail} value={safeValue} max={safeMax} />
    </div>
  );
}

export function RpgState({ kind, title, children, action }: {
  kind: "loading" | "empty" | "error" | "success";
  title: string;
  children?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className={styles.state} data-kind={kind}>
      <div role={kind === "error" ? "alert" : "status"} className={styles.stateHeading}>
        {kind === "loading" ? <span className={styles.spark} aria-hidden="true" /> : null}
        {kind === "success" ? <RpgSignature compact /> : null}
        <strong>{title}</strong>
      </div>
      {children ? <div className={styles.stateBody}>{children}</div> : null}
      {action}
    </div>
  );
}

/** Accepts anchors or router links directly; routing stays with the consumer. */
export function RpgNavigation({ children, label = "Клиентский кабинет" }: { children: ReactNode; label?: string }) {
  return (
    <nav className={styles.navigation} aria-label={label}>
      {children}
    </nav>
  );
}

/** Text and decorative icon placed inside a navigation link. */
export function RpgNavItem({ children, icon }: { children: ReactNode; icon: RpgIconName }) {
  return (
    <span className={styles.navItem}>
      <RpgIcon name={icon} />
      {children}
    </span>
  );
}
