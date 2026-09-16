import { useId } from "react";

import styles from "./RpgLevelProgress.module.css";

/** Presentation only; the caller supplies the level and experience within it. */
export function RpgLevelProgress({ level, value, max }: { level: number; value: number; max: number }) {
  const labelId = useId();
  const safeMax = Number.isFinite(max) && max > 0 ? max : 100;
  const safeValue = Number.isFinite(value) ? Math.min(safeMax, Math.max(0, value)) : 0;
  const levelLabel = `Уровень ${String(level)}`;

  return (
    <div className={styles.experience}>
      <span className={styles.shield} aria-hidden="true">
        <span>{level}</span>
      </span>
      <div className={styles.body}>
        <div className={styles.track}>
          <progress
            className={styles.progress}
            aria-labelledby={labelId}
            aria-valuetext={`${String(safeValue)} из ${String(safeMax)} опыта до следующего уровня`}
            value={safeValue}
            max={safeMax}
          />
          <span className={styles.levelLabel} aria-hidden="true">
            {levelLabel}
          </span>
        </div>
        <span id={labelId} className={styles.caption}>
          {levelLabel} · {safeValue} / {safeMax} опыта
        </span>
      </div>
    </div>
  );
}
