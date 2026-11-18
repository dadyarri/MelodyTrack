import styles from "./Brand.module.css";

export function Brand({ compact = false }: { compact?: boolean }) {
  return (
    <span className={styles.brand} role="img" aria-label="MelodyTrack">
      <svg className={styles.mark} viewBox="0 0 48 48" aria-hidden="true">
        <rect width="48" height="48" rx="13" fill="currentColor" />
        <path
          d="M13 33V15l11 12 11-12v18"
          fill="none"
          stroke="white"
          strokeWidth="4"
          strokeLinejoin="round"
          strokeLinecap="round"
        />
      </svg>
      {!compact && (
        <span>
          Melody<span className={styles.light}>Track</span>
        </span>
      )}
    </span>
  );
}
