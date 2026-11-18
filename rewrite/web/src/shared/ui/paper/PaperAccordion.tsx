import { Accordion } from "@base-ui/react/accordion";
import type { ReactNode } from "react";
import { Icon } from "../Icon";
import styles from "./PaperAccordion.module.css";

export type PaperAccordionItem = { value: string; title: ReactNode; content: ReactNode };

export function PaperAccordion({
  items,
  className = "",
}: {
  items: PaperAccordionItem[];
  className?: string;
}) {
  return (
    <Accordion.Root className={`${styles.root} ${className}`} multiple>
      {items.map((item) => (
        <Accordion.Item key={item.value} value={item.value} className={styles.item}>
          <Accordion.Header className={styles.header}>
            <Accordion.Trigger className={styles.trigger}>
              {item.title}
              <span className={styles.indicator}>
                <Icon name="chevronDown" size={16} />
              </span>
            </Accordion.Trigger>
          </Accordion.Header>
          <Accordion.Panel className={styles.panel}>
            <div className={styles.content}>{item.content}</div>
          </Accordion.Panel>
        </Accordion.Item>
      ))}
    </Accordion.Root>
  );
}
