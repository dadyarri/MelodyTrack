import { useId } from "react";

import { RpgBadge, RpgIcon, RpgPanel, RpgState } from "@/shared/ui/rpg";

import styles from "./PortalLeaderboard.module.css";

export type PortalLeaderboardEntry = { id: string; rank: number; alias: string; result: string; isCurrentPlayer?: boolean };

/** Presentation only: the caller owns ordering, ranks, and result formatting. */
export function PortalLeaderboard({ title, description, entries }: { title: string; description: string; entries: readonly PortalLeaderboardEntry[] }) {
  const titleId = useId();
  return (
    <RpgPanel>
      <div className={styles.heading}><div><h3 id={titleId}>{title}</h3><p>{description}</p></div><RpgIcon name="laurel" large /></div>
      {entries.length ? (
        <ol className={styles.list} aria-labelledby={titleId}>
          {entries.map((entry) => (
            <li key={entry.id} className={styles.row} data-current={entry.isCurrentPlayer || undefined}>
              <span className={styles.rank} aria-label={`Место ${String(entry.rank)}`}>{entry.rank}</span>
              <span className={styles.name}>{entry.alias}{entry.isCurrentPlayer ? <RpgBadge>Это вы</RpgBadge> : null}</span>
              <span className={styles.result}>{entry.result}</span>
            </li>
          ))}
        </ol>
      ) : <RpgState kind="empty" title="Записей пока нет">Здесь появятся результаты участников.</RpgState>}
    </RpgPanel>
  );
}
