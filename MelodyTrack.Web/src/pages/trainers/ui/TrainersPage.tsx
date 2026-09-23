import { Alert, FloatButton, InputNumber, Popover, Switch, Typography } from "antd";
import { Metronome } from "lucide-react";
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
  const [isTrainerRunning, setIsTrainerRunning] = useState(false);

  const updateConfiguration = (nextConfiguration: StrummingConfiguration) => {
    setConfiguration(nextConfiguration);
    setStorageError(!writeTrainerConfiguration(strummingTrainerConfigurationId, nextConfiguration));
  };

  return (
    <section className={styles.page} aria-labelledby="trainers-page-title">
      <Typography.Title id="trainers-page-title" level={1}>
        Тренажёры
      </Typography.Title>
      <StrummingTrainer configuration={configuration} onConfigurationChange={updateConfiguration} onPlaybackChange={setIsTrainerRunning} />
      {storageError ? <Alert type="warning" title="Настройки тренажёра изменены, но браузер не смог их сохранить." /> : null}
      <Popover
        trigger="click"
        title={isTrainerRunning ? "Метроном: гитарный бой" : "Метроном"}
        content={
          <div className={styles.metronomeControls}>
            <Switch
              checked={configuration.metronome.enabled}
              disabled={isTrainerRunning}
              checkedChildren="Вкл"
              unCheckedChildren="Выкл"
              onChange={(enabled) => {
                updateConfiguration({ ...configuration, metronome: { ...configuration.metronome, enabled } });
              }}
            />
            <label htmlFor="trainer-metronome-volume">
              Громкость
              <InputNumber
                id="trainer-metronome-volume"
                min={0}
                max={100}
                value={Math.round(configuration.metronome.volume * 100)}
                disabled={isTrainerRunning || !configuration.metronome.enabled}
                onChange={(volume) => {
                  updateConfiguration({ ...configuration, metronome: { ...configuration.metronome, volume: (volume ?? 60) / 100 } });
                }}
              />
            </label>
          </div>
        }
      >
        <FloatButton icon={<Metronome />} aria-label="Настроить метроном" tooltip="Метроном" />
      </Popover>
    </section>
  );
}
