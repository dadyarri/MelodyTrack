import { Alert, Button, Card, Collapse, InputNumber, Modal, Popover, Segmented, Space, Switch, Typography } from "antd";
import { useId, useMemo, useState, useSyncExternalStore } from "react";

import { downloadBlob, maximumCanvasVideoSeconds } from "@/shared/lib";
import {
  changeStrummingGrid, createDefaultStrummingBar, createDefaultStrummingConfiguration, getBarSlotCount,
  getCountInDurationSeconds, getStrummingCountLabels, getStrummingPlaybackDurationSeconds, handMotions,
  maximumStrummingBars, maximumStrummingRepetitions, parseStrummingConfiguration, rhythmicValues, strumActions,
  strummingTimeSignatures, type HandMotion, type RhythmicValue, type StrummingBar, type StrummingConfiguration,
  type StrummingStroke,
} from "../model/practiceExercise";
import { TrainerTimingHost } from "./TrainerTimingHost";
import { useStrummingPlayback } from "./useStrummingPlayback";
import { actionCodes, actionLabels, handMotionCodes, handMotionLabels } from "./drawStrummingTrainer";
import styles from "./StrummingTrainer.module.css";

const smallScreenQuery = "(max-width: 767px)";
function subscribeScreen(listener: () => void) {
  const query = window.matchMedia(smallScreenQuery);
  query.addEventListener("change", listener);
  return () => query.removeEventListener("change", listener);
}
function isSmallScreen() { return window.matchMedia(smallScreenQuery).matches; }
export type StrummingTrainerProps = Readonly<{
  configuration?: StrummingConfiguration;
  onConfigurationChange?: (configuration: StrummingConfiguration) => void;
  onPlaybackChange?: (isRunning: boolean) => void;
  showConfiguration?: boolean;
}>;
export function StrummingTrainer(props: StrummingTrainerProps) {
  const smallScreen = useSyncExternalStore(subscribeScreen, isSmallScreen, () => true);
  if (smallScreen) return <Alert type="info" showIcon title="Тренажёр пока не поддерживает небольшие экраны" description="Откройте его на компьютере или увеличьте ширину окна до 768 px. Остальная часть задания доступна." />;
  if (props.configuration !== undefined && !parseStrummingConfiguration(props.configuration)) return <Alert type="warning" showIcon title="Не удалось открыть упражнение" description="Конфигурация тренажёра некорректна или имеет неподдерживаемую версию. Попросите преподавателя заново настроить упражнение." />;
  return <TrainerTimingHost><StrummingTrainerRuntime {...props} /></TrainerTimingHost>;
}
function StrummingTrainerRuntime({ configuration: suppliedConfiguration, onConfigurationChange, onPlaybackChange, showConfiguration = true }: StrummingTrainerProps) {
  const [uncontrolledConfiguration, setUncontrolledConfiguration] = useState(createDefaultStrummingConfiguration);
  const configuration = suppliedConfiguration ?? uncontrolledConfiguration;
  const [manualTempo, setManualTempo] = useState<number | null>(null);
  const overrideTempo = showConfiguration ? manualTempo : null;
  const playbackConfiguration = useMemo(() => overrideTempo === null ? configuration : { ...configuration, bpm: overrideTempo, tempoProgression: { ...configuration.tempoProgression, enabled: false } }, [configuration, overrideTempo]);
  const [recordingSecondsOverride, setRecordingSecondsOverride] = useState<number | null>(null);
  const [pendingChange, setPendingChange] = useState<StrummingConfiguration | null>(null);
  const [selectedBar, setSelectedBar] = useState(0);
  const id = useId();
  const currentBar = Math.min(selectedBar, configuration.bars.length - 1);
  const playback = useStrummingPlayback(playbackConfiguration, onPlaybackChange, currentBar);
  const { isBusy, isRunning, isRecording, position } = playback;
  const sessionConfiguration = isRunning ? playback.activeConfiguration : configuration;
  const countInDuration = getCountInDurationSeconds(playbackConfiguration.timeSignature, playbackConfiguration.bpm);
  const maximumRecordingSeconds = Math.floor(maximumCanvasVideoSeconds - countInDuration - 0.06);
  const defaultRecordingSeconds = Math.min(maximumRecordingSeconds, Math.max(1, getStrummingPlaybackDurationSeconds(playbackConfiguration)));
  const recordingSeconds = Math.min(maximumRecordingSeconds, recordingSecondsOverride ?? defaultRecordingSeconds);
  const applyConfiguration = (next: StrummingConfiguration) => {
    if (isBusy || !showConfiguration) return;
    if (suppliedConfiguration === undefined) setUncontrolledConfiguration(next);
    if (showConfiguration) onConfigurationChange?.(next);
  };
  const updateBar = (barIndex: number, update: (bar: StrummingBar) => StrummingBar) => {
    applyConfiguration({ ...configuration, bars: configuration.bars.map((bar, index) => index === barIndex ? update(bar) : bar) });
  };
  const changeGrid = (barIndex: number, rhythmicValue: RhythmicValue) => {
    const bar = changeStrummingGrid(configuration.bars[barIndex], configuration.timeSignature, rhythmicValue);
    if (bar) updateBar(barIndex, () => bar);
    else setPendingChange({ ...configuration, bars: configuration.bars.map((current, index) => index === barIndex ? createDefaultStrummingBar(configuration.timeSignature, rhythmicValue) : current) });
  };
  const changeTimeSignature = (timeSignature: StrummingConfiguration["timeSignature"]) => {
    setPendingChange({ ...configuration, timeSignature, bars: configuration.bars.map(() => createDefaultStrummingBar(timeSignature)) });
  };
  const renderRow = (bar: StrummingBar, barIndex: number, kind: "stroke" | "motion") => {
    const labels = getStrummingCountLabels(configuration.timeSignature, bar.strokes.length);
    const items = kind === "stroke" ? bar.strokes : bar.handMotions;
    return <div className={styles.editorRow}>
      <Typography.Text className={styles.rowLabel}>{kind === "stroke" ? "Удары" : "Движения руки"}</Typography.Text>
      <div className={styles.barCells} style={{ gridTemplateColumns: `repeat(${items.length}, 64px)` }}>
        {items.map((item, index) => {
          const stroke = kind === "stroke" ? item as StrummingStroke : null;
          const motion = kind === "motion" ? item as HandMotion : null;
          const label = `Такт ${barIndex + 1}, позиция ${index + 1}`;
          return <div className={styles.rhythmColumn} key={index}>
            <Popover trigger="click" content={<div className={styles.picker}>
              {stroke ? strumActions.map(action => <Button key={action} disabled={isBusy} type={action === stroke.action ? "primary" : "default"} aria-label={actionLabels[action]} title={actionLabels[action]} onClick={() => updateBar(barIndex, current => ({ ...current, strokes: current.strokes.map((value, itemIndex) => itemIndex === index ? { action, accent: action === "rest" ? false : value.accent } : value) }))}>{actionCodes[action]}</Button>) : handMotions.map(value => <Button key={value} disabled={isBusy} type={value === motion ? "primary" : "default"} aria-label={handMotionLabels[value]} title={handMotionLabels[value]} onClick={() => updateBar(barIndex, current => ({ ...current, handMotions: current.handMotions.map((currentMotion, itemIndex) => itemIndex === index ? value : currentMotion) }))}>{handMotionCodes[value]}</Button>)}
            </div>}>
              <Button className={styles.rhythmCell} disabled={isBusy} aria-label={`${label}: ${stroke ? `удар ${actionLabels[stroke.action]}` : `рука ${handMotionLabels[motion ?? "rest"]}`}`} title={stroke ? actionLabels[stroke.action] : handMotionLabels[motion ?? "rest"]}>{stroke ? actionCodes[stroke.action] : handMotionCodes[motion ?? "rest"]}</Button>
            </Popover>
            <Typography.Text className={styles.countLabel}>{labels[index]}</Typography.Text>
            {stroke ? <Button className={styles.slotTool} type={stroke.accent ? "primary" : "default"} disabled={isBusy || stroke.action === "rest"} aria-label={`${label}: акцент`} aria-pressed={stroke.accent} title="Акцент — выделить удар, сыграть сильнее" onClick={() => updateBar(barIndex, current => ({ ...current, strokes: current.strokes.map((value, itemIndex) => itemIndex === index ? { ...value, accent: !value.accent } : value) }))}>!</Button> : <div className={styles.slotToolSpacer} />}
            <Button className={styles.slotTool} disabled={isBusy || (stroke ? stroke.action === "rest" : motion === "rest")} aria-label={`${label}: ${stroke ? "убрать удар" : "убрать движение"}`} title="Заменить паузой, не сдвигая остальные действия" onClick={() => updateBar(barIndex, current => stroke ? { ...current, strokes: current.strokes.map((value, itemIndex) => itemIndex === index ? { action: "rest", accent: false } : value) } : { ...current, handMotions: current.handMotions.map((value, itemIndex) => itemIndex === index ? "rest" : value) })}>—</Button>
          </div>;
        })}
      </div>
    </div>;
  };
  const progression = configuration.tempoProgression;
  const completedProgressionUnits = position ? progression.unit === "bars" ? (position.repetition - 1) * configuration.bars.length + (position.bar ?? 0) : position.repetition - 1 : 0;
  const nextIncreaseText = manualTempo === null && progression.enabled && isRunning && position && position.countIn === null ? position.bpm >= 240 ? "Достигнут максимальный темп." : `Следующее повышение через ${progression.every - completedProgressionUnits % progression.every} ${progression.unit === "bars" ? "такт(а)" : "полный повтор(а)"}.` : "";
  const progressionText = progression.enabled ? `Темп повышается на ${progression.increment} BPM каждые ${progression.every} ${progression.unit === "bars" ? "такт(а)" : "повтор(а)"}, максимум до 240 BPM.` : "Ускорение выключено.";
  return <Card className={styles.trainerCard}>
    <div className={styles.presentation}>
      <canvas ref={playback.canvasRef} width="1200" height="430" className={styles.trainerCanvas} aria-label="Рисунок гитарного боя: текущий такт" />
      <Typography.Text role="status" aria-live="polite">
        {isRunning && position ? position.countIn !== null ? `Приготовьтесь: ${position.countIn}` : `Такт ${(position.bar ?? 0) + 1} из ${configuration.bars.length} · повтор ${position.repetition}${sessionConfiguration.loop ? "" : ` из ${sessionConfiguration.repetitions}`} · ${position.bpm} BPM` : playback.isStarting ? "Включаем звук…" : "Нажмите «Старт», чтобы начать с отсчёта."}
      </Typography.Text>
      <Space wrap>
        <Button type="primary" disabled={isBusy} onClick={() => playback.startPlayback()}>Старт</Button>
        <Button disabled={!isBusy} onClick={playback.stopPlayback}>Стоп</Button>
        <Button disabled={!playback.canRecordVideo || isBusy} onClick={() => playback.startPlayback(recordingSeconds, recordingSecondsOverride === null)}>{isRecording ? "Запись…" : "Записать WebM"}</Button>
        {playback.video ? <Button onClick={() => { if (playback.video) downloadBlob(playback.video, "melodytrack-guitar-strumming.webm"); }}>Скачать WebM</Button> : null}
      </Space>
      {playback.video ? <Typography.Text type="success">Видео готово. Нажмите «Скачать WebM», чтобы сохранить его.</Typography.Text> : null}
      {!playback.canRecordVideo ? <Typography.Text type="secondary">Запись WebM в этом браузере недоступна. Проигрывание работает; для записи попробуйте другой браузер.</Typography.Text> : null}
      {playback.error ? <Alert type="warning" showIcon title={playback.error} /> : null}
    </div>
    {showConfiguration ? <div className={styles.progressionControls}>
      <div className={styles.switchControl}><label htmlFor={`${id}-follow`}>Следовать темпу упражнения</label><Switch id={`${id}-follow`} checked={manualTempo === null} disabled={isBusy} onChange={follow => setManualTempo(follow ? null : configuration.bpm)} /></div>
      {manualTempo !== null ? <label className={styles.control} htmlFor={`${id}-manual-tempo`}>Темп проигрывания, BPM<InputNumber id={`${id}-manual-tempo`} min={30} max={240} value={manualTempo} disabled={isBusy} onChange={bpm => { if (bpm !== null && bpm >= 30 && bpm <= 240) setManualTempo(bpm); }} /></label> : null}
      <Typography.Text type="secondary">{manualTempo === null ? "Метроном следует темпу и ускорению упражнения." : "Ручной темп: следование упражнению и автоматическое ускорение отключены. Настройки упражнения сохраняются."}</Typography.Text>
    </div> : null}
    {showConfiguration ? <div className={styles.controls}>
      <label className={styles.control} htmlFor={`${id}-bpm`}>Темп, BPM<InputNumber id={`${id}-bpm`} min={30} max={240} value={configuration.bpm} disabled={isBusy} onChange={bpm => { if (bpm !== null && bpm >= 30 && bpm <= 240) applyConfiguration({ ...configuration, bpm }); }} /></label>
      <label className={styles.control} htmlFor={`${id}-repetitions`}>Повторов<InputNumber id={`${id}-repetitions`} min={1} max={maximumStrummingRepetitions} precision={0} value={configuration.repetitions} disabled={isBusy || configuration.loop} onChange={repetitions => { if (repetitions !== null && repetitions >= 1 && repetitions <= maximumStrummingRepetitions) applyConfiguration({ ...configuration, repetitions: Math.round(repetitions) }); }} /></label>
      <div className={styles.switchControl}><label htmlFor={`${id}-loop`}>Повторять без остановки</label><Switch id={`${id}-loop`} checked={configuration.loop} disabled={isBusy} onChange={loop => applyConfiguration({ ...configuration, loop })} /></div>
      <div className={styles.switchControl}><label htmlFor={`${id}-metronome`}>Метроном</label><Switch id={`${id}-metronome`} checked={configuration.metronome.enabled} disabled={isBusy} onChange={enabled => applyConfiguration({ ...configuration, metronome: { ...configuration.metronome, enabled } })} /></div>
      <label className={styles.control} htmlFor={`${id}-volume`}>Громкость, %<InputNumber id={`${id}-volume`} min={0} max={100} value={Math.round(configuration.metronome.volume * 100)} disabled={isBusy || !configuration.metronome.enabled} onChange={volume => { if (volume !== null && volume >= 0 && volume <= 100) applyConfiguration({ ...configuration, metronome: { ...configuration.metronome, volume: volume / 100 } }); }} /></label>
      <label className={styles.control} htmlFor={`${id}-seconds`}>Видео после отсчёта, сек.<InputNumber id={`${id}-seconds`} min={1} max={maximumRecordingSeconds} precision={2} value={recordingSeconds} disabled={isBusy || !playback.canRecordVideo} onChange={seconds => setRecordingSecondsOverride(seconds === null ? null : Math.max(1, Math.min(maximumRecordingSeconds, seconds)))} /></label>
    </div> : <Typography.Text type="secondary">Упражнение настроено преподавателем: {configuration.bpm} BPM, {configuration.timeSignature}, {configuration.loop ? "непрерывное повторение" : `${configuration.repetitions} повтор(а)`}. Для самостоятельной настройки откройте «Тренажёры» в клиентском портале.</Typography.Text>}
    <Space wrap><Typography.Text type="secondary">{recordingSecondsOverride === null ? "Видео по умолчанию записывает заданное число полных повторов, даже при включённом цикле." : "Видео записывает выбранное число секунд и может закончиться посреди рисунка."}</Typography.Text>{recordingSecondsOverride !== null ? <Button disabled={isBusy} onClick={() => setRecordingSecondsOverride(null)}>Записывать полные повторы</Button> : null}</Space>

    <Typography.Text type="secondary">Чем меньше BPM, тем медленнее. Перед началом прозвучит один такт отсчёта. В 6/8 щелчки идут вдвое чаще значения BPM. Звук — только метроном; тренажёр показывает движения и не оценивает вашу игру.</Typography.Text>
    {showConfiguration ? <div className={styles.progressionControls}>
      <div className={styles.switchControl}><label htmlFor={`${id}-progression`}>Постепенно ускорять</label><Switch id={`${id}-progression`} checked={progression.enabled} disabled={isBusy || manualTempo !== null} onChange={enabled => applyConfiguration({ ...configuration, tempoProgression: { ...progression, enabled } })} /></div>
      {progression.enabled ? <>
        <label className={styles.control} htmlFor={`${id}-increment`}>Прибавка BPM<InputNumber id={`${id}-increment`} min={1} max={60} precision={0} value={progression.increment} disabled={isBusy || manualTempo !== null} onChange={increment => { if (increment !== null && increment >= 1 && increment <= 60) applyConfiguration({ ...configuration, tempoProgression: { ...progression, increment: Math.round(increment) } }); }} /></label>
        <label className={styles.control} htmlFor={`${id}-every`}>Каждые<InputNumber id={`${id}-every`} min={1} max={100} precision={0} value={progression.every} disabled={isBusy || manualTempo !== null} onChange={every => { if (every !== null && every >= 1 && every <= 100) applyConfiguration({ ...configuration, tempoProgression: { ...progression, every: Math.round(every) } }); }} /></label>
        <Segmented aria-label="Когда повышать темп" value={progression.unit} options={[{ label: "Такты", value: "bars" }, { label: "Полные повторы", value: "repetitions" }]} disabled={isBusy || manualTempo !== null} onChange={unit => applyConfiguration({ ...configuration, tempoProgression: { ...progression, unit: unit === "bars" ? "bars" : "repetitions" } })} />
      </> : null}
      <Typography.Text type="secondary">{manualTempo === null ? progressionText : "Автоматическое ускорение отключено ручным темпом."} {nextIncreaseText} Такт — один фрагмент рисунка; полный повтор — все такты по порядку.</Typography.Text>
    </div> : <Typography.Text type="secondary">{progressionText} {nextIncreaseText}</Typography.Text>}
    {recordingSecondsOverride === null && getStrummingPlaybackDurationSeconds(playbackConfiguration) > maximumRecordingSeconds ? <Alert type="info" showIcon title="Полное упражнение длиннее лимита видео" description="Запись завершится через три минуты. Уменьшите число повторов или задайте длительность в секундах." /> : null}
    <Typography.Text type="secondary">Видео ограничено тремя минутами вместе с отсчётом. «Стоп» сохранит уже записанную часть. Если упражнение закончится раньше, запись тоже завершится.</Typography.Text>
    {showConfiguration ? <section className={styles.patternSection}>
      <Typography.Title level={3}>Ритмический рисунок</Typography.Title>
      <div className={styles.barHeading}>
        <div className={styles.control}>Размер такта<Segmented aria-label="Размер такта" value={configuration.timeSignature} options={[...strummingTimeSignatures]} disabled={isBusy} onChange={value => changeTimeSignature(value as StrummingConfiguration["timeSignature"])} /></div>
        <Button disabled={isBusy || configuration.bars.length >= maximumStrummingBars} onClick={() => { applyConfiguration({ ...configuration, bars: [...configuration.bars, createDefaultStrummingBar(configuration.timeSignature)] }); setSelectedBar(configuration.bars.length); }}>Добавить такт</Button>
      </div>
      <Typography.Text type="secondary">Выберите число шагов, затем действие в каждой ячейке. Обе строки идут одновременно слева направо. «—» убирает действие, оставляя его место во времени.</Typography.Text>
      <Segmented aria-label="Такт для редактирования" value={currentBar} options={configuration.bars.map((_, index) => ({ label: `Такт ${index + 1}`, value: index }))} disabled={isBusy} onChange={value => setSelectedBar(Number(value))} />
      <div className={styles.barGroup}>
        <div className={styles.barHeading}>
          <Typography.Text>Такт {currentBar + 1}</Typography.Text>
          <Segmented aria-label="Число шагов в такте" value={configuration.bars[currentBar].rhythmicValue} options={rhythmicValues.filter(value => !(configuration.timeSignature === "6/8" && value === "quarter") && Number.isInteger(getBarSlotCount(configuration.timeSignature, value))).map(value => ({ label: `${getBarSlotCount(configuration.timeSignature, value)} шагов`, value }))} disabled={isBusy} onChange={value => changeGrid(currentBar, value as RhythmicValue)} />
          <Button danger disabled={isBusy || configuration.bars.length === 1} onClick={() => { applyConfiguration({ ...configuration, bars: configuration.bars.filter((_, index) => index !== currentBar) }); setSelectedBar(Math.max(0, currentBar - 1)); }}>Удалить такт</Button>
        </div>
        <div className={styles.gridScroller}>{renderRow(configuration.bars[currentBar], currentBar, "stroke")}{renderRow(configuration.bars[currentBar], currentBar, "motion")}</div>
      </div>
    </section> : null}
    <Collapse items={[{ key: "legend", label: "Что означают значки", children: <div className={styles.legend}>{strumActions.map(action => <Typography.Text key={action}>{actionCodes[action]} — {actionLabels[action]}</Typography.Text>)}{handMotions.filter(motion => motion === "muted" || motion === "down-up" || motion === "up-down").map(motion => <Typography.Text key={motion}>{handMotionCodes[motion]} — {handMotionLabels[motion]}</Typography.Text>)}<Typography.Text>! — акцент: сыграть удар сильнее. Акцент виден в рисунке и видео, звук метронома от него не меняется.</Typography.Text></div> }]} />
    <Modal title="Заменить рисунок?" open={pendingChange !== null} okText="Заменить рисунок" cancelText="Оставить как есть" onCancel={() => setPendingChange(null)} onOk={() => { if (pendingChange) applyConfiguration(pendingChange); setPendingChange(null); }}>
      <Typography.Paragraph>При этой смене размера или числа шагов текущий рисунок не помещается в новую сетку. Он будет заменён стандартным. Остальные настройки сохранятся.</Typography.Paragraph>
    </Modal>
  </Card>;
}
