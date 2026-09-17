import { Alert, Typography } from "antd";
import { useState } from "react";

import { createDefaultStrummingConfiguration, type StrummingConfiguration, StrummingTrainer } from "@/entities/trainer";

import {
  parseStrummingTrainerConfiguration,
  readTrainerConfiguration,
  strummingTrainerConfigurationId,
  writeTrainerConfiguration,
} from "../model/trainerConfigurationsStorage";
import styles from "./TrainersPage.module.css";

export function TrainersPage() {
  const [configuration, setConfiguration] = useState(
    () =>
      readTrainerConfiguration(strummingTrainerConfigurationId, parseStrummingTrainerConfiguration) ??
      createDefaultStrummingConfiguration(),
  );
  const [storageError, setStorageError] = useState(false);

  const updateConfiguration = (nextConfiguration: StrummingConfiguration) => {
    setConfiguration(nextConfiguration);
    setStorageError(!writeTrainerConfiguration(strummingTrainerConfigurationId, nextConfiguration));
  };

  return (
    <section className={styles.page} aria-labelledby="trainers-page-title">
      <Typography.Title id="trainers-page-title" level={1}>
        Тренажёры
      </Typography.Title>
      <StrummingTrainer configuration={configuration} onConfigurationChange={updateConfiguration} />
      {storageError ? <Alert type="warning" title="Настройки тренажёра изменены, но браузер не смог их сохранить." /> : null}
    </section>
  );
}
