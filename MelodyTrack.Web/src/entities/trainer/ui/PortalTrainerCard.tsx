import type { ReactNode } from "react";

import { RpgBadge, RpgCard } from "@/shared/ui/rpg";

export function PortalTrainerCard({
  title,
  description,
  detail,
  action,
}: {
  title: string;
  description: string;
  detail: string;
  action?: ReactNode;
}) {
  return (
    <RpgCard title={title} description={description} eyebrow="Мастерская музыканта" icon="guitar" accent="plum" footer={action}>
      <RpgBadge>{detail}</RpgBadge>
    </RpgCard>
  );
}
