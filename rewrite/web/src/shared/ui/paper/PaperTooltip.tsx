import { Tooltip } from "@base-ui/react/tooltip";
import type { ReactElement, ReactNode } from "react";
import styles from "./PaperTooltip.module.css";

export function PaperTooltip({ title, children }: { title: ReactNode; children: ReactElement }) {
  if (title === null || title === undefined || title === false) return children;

  return (
    <Tooltip.Provider>
      <Tooltip.Root>
        <Tooltip.Trigger render={children} />
        <Tooltip.Portal>
          <Tooltip.Positioner sideOffset={8} className={styles.positioner}>
            <Tooltip.Popup className={styles.tooltip}>{title}</Tooltip.Popup>
          </Tooltip.Positioner>
        </Tooltip.Portal>
      </Tooltip.Root>
    </Tooltip.Provider>
  );
}
