import { Card, Typography } from "antd";

import { getSupportedWebmMimeType } from "@/shared/lib";

import { rhythmicValues } from "../model/practiceExercise";
import styles from "./TrainersPage.module.css";

export function TrainersPage() {
  const canRecordVideo = getSupportedWebmMimeType() !== null;

  return (
    <section className={styles.page} aria-labelledby="trainers-page-title">
      <div>
        <Typography.Title id="trainers-page-title" level={1}>
          Тренажёры
        </Typography.Title>
        <Typography.Paragraph className={styles.intro}>
          Здесь будут самостоятельные упражнения для сотрудников. Настройки упражнений остаются только в текущем сеансе браузера.
        </Typography.Paragraph>
      </div>
      <Card>
        <Typography.Title level={2}>Первый тренажёр готовится</Typography.Title>
        <Typography.Paragraph>
          Платформа для запуска упражнений и локальной записи видео уже создана. Гитарный бой, смена аккордов и ритм появятся здесь
          следующими.
        </Typography.Paragraph>
        <Typography.Paragraph type="secondary">
          Общая ритмическая модель поддерживает {rhythmicValues.length} значений.{" "}
          {canRecordVideo ? "Этот браузер готов к локальной записи WebM." : "Локальная запись WebM в этом браузере недоступна."}
        </Typography.Paragraph>
      </Card>
    </section>
  );
}
