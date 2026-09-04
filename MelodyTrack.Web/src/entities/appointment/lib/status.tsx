import { Tag } from "antd";
import type { CSSProperties } from "react";

import type { AppointmentStatus } from "@/entities/appointment";
import { CheckOutlined, ClockCircleOutlined, CloseOutlined, FireOutlined } from "@/shared/ui/icons";

export function getAppointmentStatusLabel(status: AppointmentStatus) {
  switch (status) {
    case "completed":
      return "Завершено";
    case "cancelled":
      return "Отменено";
    case "burned":
      return "Сгорело";
    default:
      return "Запланировано";
  }
}

export function renderAppointmentStatusIcon(status: AppointmentStatus) {
  switch (status) {
    case "completed":
      return <CheckOutlined />;
    case "cancelled":
      return <CloseOutlined />;
    case "burned":
      return <FireOutlined />;
    default:
      return <ClockCircleOutlined />;
  }
}

export function getAppointmentStatusTagColor(status: AppointmentStatus) {
  switch (status) {
    case "completed":
      return "green";
    case "cancelled":
      return "default";
    case "burned":
      return "orange";
    default:
      return "blue";
  }
}

export function renderAppointmentStatusTag(status: AppointmentStatus) {
  return <Tag color={getAppointmentStatusTagColor(status)}>{getAppointmentStatusLabel(status)}</Tag>;
}

export function isPlannedAppointment(status: AppointmentStatus) {
  return status === "planned";
}

export function getAppointmentStatusColorVars(status: AppointmentStatus): CSSProperties {
  switch (status) {
    case "completed":
      return {
        "--schedule-entry-border": "#10B981",
        "--schedule-entry-background": "#A7F3D0",
        "--schedule-entry-background-dark": "#0f3f33",
        "--schedule-entry-border-dark": "#0f4f3a",
        "--schedule-entry-selected-ring": "rgba(16, 185, 129, 0.4)",
        "--schedule-entry-icon-color": "#065F46",
        "--schedule-entry-text": "#064E3B",
        "--schedule-entry-text-dark": "#ECFDF5",
      } as CSSProperties;
    case "cancelled":
      return {
        "--schedule-entry-border": "#DC2626",
        "--schedule-entry-background": "#FECACA",
        "--schedule-entry-background-dark": "#4b1213",
        "--schedule-entry-border-dark": "#5e1517",
        "--schedule-entry-selected-ring": "rgba(220, 38, 38, 0.4)",
        "--schedule-entry-icon-color": "#7F1D1D",
        "--schedule-entry-text": "#4C0519",
        "--schedule-entry-text-dark": "#FFF1F2",
      } as CSSProperties;
    case "burned":
      return {
        "--schedule-entry-border": "#D97706",
        "--schedule-entry-background": "#FDE68A",
        "--schedule-entry-background-dark": "#603f0b",
        "--schedule-entry-border-dark": "#6f4a0d",
        "--schedule-entry-selected-ring": "rgba(180, 113, 9, 0.4)",
        "--schedule-entry-icon-color": "#78350F",
        "--schedule-entry-text": "#4A2A06",
        "--schedule-entry-text-dark": "#FFFBEB",
      } as CSSProperties;
    default:
      return {
        "--schedule-entry-border": "#2563EB",
        "--schedule-entry-background": "#BFDBFE",
        "--schedule-entry-background-dark": "#0f3558",
        "--schedule-entry-border-dark": "#112f5a",
        "--schedule-entry-selected-ring": "rgba(37, 99, 235, 0.4)",
        "--schedule-entry-icon-color": "#1E3A8A",
        "--schedule-entry-text": "#0B3B73",
        "--schedule-entry-text-dark": "#EFF6FF",
      } as CSSProperties;
  }
}
