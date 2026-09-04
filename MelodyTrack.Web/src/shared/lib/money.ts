export function formatMoney(value?: number | null) {
  return `${(value ?? 0).toLocaleString("ru-RU", { maximumFractionDigits: 2 })} ₽`;
}
