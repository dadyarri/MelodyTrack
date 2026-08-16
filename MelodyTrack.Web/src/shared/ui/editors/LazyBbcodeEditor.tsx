import { lazy, Suspense } from "react";

import type { BbcodeEditorProps } from "./BbcodeEditor";
import styles from "./BbcodeEditor.module.css";

const Editor = lazy(async () => {
  const module = await import("./BbcodeEditor");

  return { default: module.BbcodeEditor };
});

export function LazyBbcodeEditor(props: BbcodeEditorProps) {
  return (
    <Suspense
      fallback={
        <div className={styles.loading} role="status">
          Загружаем редактор…
        </div>
      }
    >
      <Editor {...props} />
    </Suspense>
  );
}
