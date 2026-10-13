import { getStrummingCountLabels, type HandMotion, type StrummingConfiguration } from "../model/practiceExercise";

export const actionLabels = {
  down: "Вниз",
  up: "Вверх",
  "down-click": "Вниз с щелчком",
  "down-dead-notes": "Вниз по заглушенным струнам",
  "up-dead-notes": "Вверх по заглушенным струнам",
  "down-bass-strings": "Вниз по басовым струнам",
  "up-bass-strings": "Вверх по басовым струнам",
  "down-treble-strings": "Вниз по тонким струнам",
  "up-treble-strings": "Вверх по тонким струнам",
  rest: "Пауза",
} as const;
export const actionCodes = {
  down: "↓",
  up: "↑",
  "down-click": "↓•",
  "down-dead-notes": "↓×",
  "up-dead-notes": "↑×",
  "down-bass-strings": "↓B",
  "up-bass-strings": "↑B",
  "down-treble-strings": "↓T",
  "up-treble-strings": "↑T",
  rest: "—",
} as const;
export const handMotionLabels: Record<HandMotion, string> = {
  down: "Вниз",
  up: "Вверх",
  muted: "Глушь",
  rest: "Без движения",
  "down-up": "Вниз-вверх",
  "up-down": "Вверх-вниз",
};
export const handMotionCodes: Record<HandMotion, string> = { down: "↓", up: "↑", muted: "×", rest: "—", "down-up": "↓↑", "up-down": "↑↓" };
export type TrainerPosition = Readonly<{
  bar: number | null;
  slot: number | null;
  countIn: number | null;
  repetition: number;
  bpm: number;
}>;
export function drawStrummingTrainer(canvas: HTMLCanvasElement, configuration: StrummingConfiguration, position: TrainerPosition) {
  const context = canvas.getContext("2d");
  if (!context) return;
  context.fillStyle = "#172033";
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.fillStyle = "#e8edf7";
  context.font = "700 36px sans-serif";
  context.fillText("Гитарный бой", 30, 52);
  context.font = "24px sans-serif";
  context.fillStyle = "#b9c7dd";
  context.fillText(`${configuration.timeSignature} · ${String(position.bpm)} BPM`, 30, 92);
  context.textAlign = "right";
  context.fillText(
    `Повтор ${String(position.repetition)}${configuration.loop ? " · цикл" : ` из ${String(configuration.repetitions)}`}`,
    canvas.width - 30,
    92,
  );
  context.textAlign = "start";
  const barIndex = position.bar ?? 0;
  const bar = configuration.bars[barIndex];
  const cellWidth = (canvas.width - 150) / bar.strokes.length;
  const labels = getStrummingCountLabels(configuration.timeSignature, bar.strokes.length);
  context.fillText(`Такт ${String(barIndex + 1)} из ${String(configuration.bars.length)}`, 30, 137);
  context.font = "18px sans-serif";
  context.fillText("Удары", 15, 221);
  context.fillText("Рука", 15, 331);
  bar.strokes.forEach((stroke, index) => {
    const x = 125 + index * cellWidth;
    const active = position.slot === index && position.countIn === null;
    for (const y of [170, 280]) {
      context.fillStyle = active ? "#f4b860" : "#2d3c59";
      context.fillRect(x, y, cellWidth - 4, 85);
    }
    context.textAlign = "center";
    context.fillStyle = active ? "#172033" : "#e8edf7";
    context.font = `700 ${String(Math.min(40, cellWidth * 0.55))}px sans-serif`;
    context.fillText(actionCodes[stroke.action], x + (cellWidth - 4) / 2, 224);
    context.fillText(handMotionCodes[bar.handMotions[index]], x + (cellWidth - 4) / 2, 334);
    if (stroke.accent) {
      context.font = "700 18px sans-serif";
      context.fillText("!", x + (cellWidth - 4) / 2, 189);
    }
    context.fillStyle = "#b9c7dd";
    context.font = "18px sans-serif";
    context.fillText(labels[index], x + (cellWidth - 4) / 2, 397);
    context.textAlign = "start";
  });
  if (position.countIn !== null) {
    context.fillStyle = "rgba(0,0,0,0.7)";
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.fillStyle = "#ffffff";
    context.textAlign = "center";
    context.font = "24px sans-serif";
    context.fillText("Приготовьтесь", canvas.width / 2, 140);
    context.font = "700 160px sans-serif";
    context.fillText(String(position.countIn), canvas.width / 2, 315);
    context.textAlign = "start";
  }
}
