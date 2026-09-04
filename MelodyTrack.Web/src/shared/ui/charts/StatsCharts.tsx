import { theme, Tooltip, Typography } from "antd";
import type { ReactNode } from "react";

import { STATS_CHART_COLORS } from "./chartColors";

function stringifyNumber(value: number) {
  return String(value);
}

type DonutChartItem = {
  key: string;
  label: string;
  value: number;
  valueLabel: string;
  shareLabel?: string;
  tooltip?: ReactNode;
  color?: string;
};

type BarChartItem = {
  key: string;
  label: string;
  value: number;
  valueLabel: string;
  tooltip?: ReactNode;
  color?: string;
};

type TrendChartSeries = {
  key: string;
  label: string;
  color: string;
};

type TrendChartPoint = {
  key: string;
  label: string;
  tooltip?: ReactNode;
  values: Record<string, number>;
};

export function StatsDonutChart({
  items,
  totalLabel = "Всего",
  totalValueLabel,
  emptyText = "Нет данных для диаграммы.",
}: {
  items?: DonutChartItem[];
  totalLabel?: string;
  totalValueLabel?: string;
  emptyText?: string;
}) {
  const { token } = theme.useToken();
  const preparedItems = (items ?? []).filter((item) => item.value > 0);
  const total = preparedItems.reduce((sum, item) => sum + item.value, 0);

  if (total <= 0 || preparedItems.length === 0) {
    return <Typography.Text type="secondary">{emptyText}</Typography.Text>;
  }

  const size = 220;
  const strokeWidth = 34;
  const radius = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;
  const segments = preparedItems.map((item, index) => {
    const previousValue = preparedItems.slice(0, index).reduce((sum, current) => sum + current.value, 0);
    const dash = (item.value / total) * circumference;
    const offset = (previousValue / total) * circumference;

    return {
      item,
      color: item.color ?? STATS_CHART_COLORS[index % STATS_CHART_COLORS.length],
      dash,
      offset,
    };
  });

  return (
    <div
      style={{
        display: "grid",
        gap: 16,
        gridTemplateColumns: "repeat(auto-fit, minmax(min(220px, 100%), 1fr))",
        alignItems: "center",
      }}
    >
      <svg
        viewBox={["0", "0", stringifyNumber(size), stringifyNumber(size)].join(" ")}
        style={{ width: "100%", maxWidth: size, justifySelf: "center" }}
        role="img"
        aria-label={totalLabel}
      >
        <circle cx={size / 2} cy={size / 2} r={radius} fill="none" stroke={token.colorFillSecondary} strokeWidth={strokeWidth} />
        {segments.map(({ item, color, dash, offset }) => {
          const segment = (
            <Tooltip
              key={item.key}
              title={item.tooltip ?? `${item.label}: ${item.valueLabel}${item.shareLabel ? ` (${item.shareLabel})` : ""}`}
            >
              <circle
                cx={size / 2}
                cy={size / 2}
                r={radius}
                fill="none"
                stroke={color}
                strokeWidth={strokeWidth}
                strokeDasharray={[dash, circumference - dash].map(stringifyNumber).join(" ")}
                strokeDashoffset={-offset}
                transform={["rotate(-90", stringifyNumber(size / 2), `${stringifyNumber(size / 2)})`].join(" ")}
                style={{ cursor: "pointer" }}
              />
            </Tooltip>
          );
          return segment;
        })}
        <text x="50%" y="46%" textAnchor="middle" fontSize="15" fill={token.colorTextSecondary}>
          {totalLabel}
        </text>
        <text x="50%" y="56%" textAnchor="middle" fontSize="18" fontWeight="600" fill={token.colorText}>
          {totalValueLabel ?? preparedItems.reduce((sum, item) => sum + item.value, 0).toLocaleString("ru-RU")}
        </text>
      </svg>

      <div style={{ display: "grid", gap: 10 }}>
        {preparedItems.map((item, index) => (
          <Tooltip key={`legend-${item.key}`} title={item.tooltip ?? `${item.label}: ${item.valueLabel}`}>
            <div style={{ display: "grid", gridTemplateColumns: "14px minmax(0, 1fr) auto", gap: 10, alignItems: "center" }}>
              <span
                style={{
                  width: 14,
                  height: 14,
                  borderRadius: "50%",
                  background: item.color ?? STATS_CHART_COLORS[index % STATS_CHART_COLORS.length],
                  display: "inline-block",
                }}
              />
              <Typography.Text ellipsis>{item.label}</Typography.Text>
              <Typography.Text strong>
                {item.valueLabel}
                {item.shareLabel ? ` (${item.shareLabel})` : ""}
              </Typography.Text>
            </div>
          </Tooltip>
        ))}
      </div>
    </div>
  );
}

export function StatsHorizontalBarChart({ items, emptyText = "Нет данных для графика." }: { items?: BarChartItem[]; emptyText?: string }) {
  const { token } = theme.useToken();
  const preparedItems = (items ?? []).filter((item) => item.value !== 0);

  if (preparedItems.length === 0) {
    return <Typography.Text type="secondary">{emptyText}</Typography.Text>;
  }

  const maxAbs = Math.max(...preparedItems.map((item) => Math.abs(item.value)), 1);

  return (
    <div style={{ display: "grid", gap: 12 }}>
      {preparedItems.map((item, index) => {
        const widthPercent = (Math.abs(item.value) / maxAbs) * 100;
        const color = item.color ?? (item.value >= 0 ? STATS_CHART_COLORS[index % STATS_CHART_COLORS.length] : token.colorError);

        return (
          <Tooltip key={item.key} title={item.tooltip ?? `${item.label}: ${item.valueLabel}`}>
            <div style={{ display: "grid", gap: 6 }}>
              <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                <Typography.Text ellipsis>{item.label}</Typography.Text>
                <Typography.Text strong>{item.valueLabel}</Typography.Text>
              </div>
              <div style={{ height: 12, background: token.colorFillSecondary, borderRadius: 999, overflow: "hidden" }}>
                <div style={{ width: `${String(widthPercent)}%`, height: "100%", background: color, borderRadius: 999 }} />
              </div>
            </div>
          </Tooltip>
        );
      })}
    </div>
  );
}

export function StatsTrendChart({
  data,
  series,
  emptyText = "Нет данных для графика.",
}: {
  data?: TrendChartPoint[];
  series: TrendChartSeries[];
  emptyText?: string;
}) {
  const { token } = theme.useToken();

  if (!data || data.length === 0 || series.length === 0) {
    return <Typography.Text type="secondary">{emptyText}</Typography.Text>;
  }

  const shouldRotateLabels = data.length > 8;
  const width = 720;
  const height = 260;
  const padding = { top: 20, right: 20, bottom: shouldRotateLabels ? 68 : 42, left: 56 };
  const innerWidth = width - padding.left - padding.right;
  const innerHeight = height - padding.top - padding.bottom;
  const values = data.flatMap((point) => series.map((item) => point.values[item.key] ?? 0));
  const minValue = Math.min(...values, 0);
  const maxValue = Math.max(...values, 0);
  const range = maxValue - minValue;
  const safeRange = range === 0 ? 1 : range;
  const ticks = [minValue, minValue + safeRange / 2, maxValue];

  const getY = (value: number) => padding.top + innerHeight - ((value - minValue) / safeRange) * innerHeight;
  const zeroY = minValue <= 0 && maxValue >= 0 ? getY(0) : null;

  const pointMap = data.map((item, index) => {
    const x = data.length === 1 ? padding.left + innerWidth / 2 : padding.left + (innerWidth / (data.length - 1)) * index;
    return { item, x };
  });

  return (
    <div style={{ width: "100%", overflowX: "auto" }}>
      <svg
        viewBox={["0", "0", stringifyNumber(width), stringifyNumber(height)].join(" ")}
        style={{ width: "100%", minWidth: 560, display: "block" }}
        role="img"
        aria-label="График динамики"
      >
        {ticks.map((value) => {
          const y = getY(value);
          return (
            <g key={value}>
              <line x1={padding.left} y1={y} x2={width - padding.right} y2={y} stroke={token.colorBorderSecondary} strokeDasharray="4 4" />
              <text x={padding.left - 12} y={y + 4} textAnchor="end" fill={token.colorTextSecondary} fontSize="12">
                {shortNumber(value)}
              </text>
            </g>
          );
        })}

        {zeroY != null ? (
          <g>
            <line
              x1={padding.left}
              y1={zeroY}
              x2={width - padding.right}
              y2={zeroY}
              stroke={token.colorBorderSecondary}
              strokeDasharray="4 4"
            />
            <text x={padding.left - 12} y={zeroY + 4} textAnchor="end" fill={token.colorTextSecondary} fontSize="12">
              0
            </text>
          </g>
        ) : null}

        {series.map((seriesItem) => {
          const linePoints = pointMap
            .map((point) => {
              const y = getY(point.item.values[seriesItem.key] ?? 0);
              return [point.x, y].map(stringifyNumber).join(",");
            })
            .join(" ");

          return (
            <polyline
              key={seriesItem.key}
              points={linePoints}
              fill="none"
              stroke={seriesItem.color}
              strokeWidth="3"
              strokeLinejoin="round"
              strokeLinecap="round"
            />
          );
        })}

        {pointMap.map((point) =>
          series.map((seriesItem) => {
            const y = getY(point.item.values[seriesItem.key] ?? 0);

            return (
              <Tooltip key={`${point.item.key}-${seriesItem.key}`} title={point.item.tooltip}>
                <circle cx={point.x} cy={y} r="4" fill={seriesItem.color} style={{ cursor: "pointer" }} />
              </Tooltip>
            );
          }),
        )}

        {pointMap.map((point) => (
          <text
            key={`label-${point.item.key}`}
            x={point.x}
            y={height - (shouldRotateLabels ? 14 : 12)}
            textAnchor={shouldRotateLabels ? "end" : "middle"}
            fill={token.colorTextSecondary}
            fontSize="12"
            transform={
              shouldRotateLabels ? ["rotate(-28", stringifyNumber(point.x), `${stringifyNumber(height - 14)})`].join(" ") : undefined
            }
          >
            {point.item.label}
          </text>
        ))}
      </svg>

      <div
        style={{
          display: "grid",
          gap: 10,
          gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
          marginTop: 12,
          alignItems: "start",
        }}
      >
        {series.map((item) => (
          <div key={item.key} style={{ display: "flex", alignItems: "flex-start", gap: 8, minWidth: 0 }}>
            <span style={{ width: 12, height: 12, borderRadius: "50%", background: item.color, display: "inline-block" }} />
            <Typography.Text type="secondary" style={{ lineHeight: 1.3 }}>
              {item.label}
            </Typography.Text>
          </div>
        ))}
      </div>
    </div>
  );
}

function shortNumber(value: number) {
  const absoluteValue = Math.abs(value);

  if (absoluteValue >= 1000) {
    const shortened = (absoluteValue / 1000).toFixed(absoluteValue >= 10000 ? 0 : 1);
    return `${value < 0 ? "-" : ""}${shortened}k`;
  }

  return String(Math.round(value));
}
