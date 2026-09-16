import type { ReactNode } from "react";

import { RpgCard, RpgProgress } from "@/shared/ui/rpg";

export function PortalCourseCard({
  title,
  description,
  progress,
  action,
  children,
}: {
  title: string;
  description: string;
  progress?: { completed: number; total: number; label: string };
  action?: ReactNode;
  children?: ReactNode;
}) {
  return (
    <RpgCard title={title} description={description} eyebrow="Книга знаний" icon="scroll" footer={action}>
      {progress ? (
        <RpgProgress
          label={progress.label}
          value={progress.completed}
          max={progress.total}
          detail={`${String(progress.completed)} / ${String(progress.total)}`}
        />
      ) : null}
      {children}
    </RpgCard>
  );
}
