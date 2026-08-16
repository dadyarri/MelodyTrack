import { describe, expect, it } from "vitest";

import { auditActionLabels, auditCategoryLabels, formatAuditLabel } from "./auditLabels";

describe("audit labels", () => {
  it("translates recently added client, payment, and course events", () => {
    expect(formatAuditLabel("client_portal_link_created", auditActionLabels)).toBe("Создана ссылка на кабинет клиента");
    expect(formatAuditLabel("client_vacations_updated", auditActionLabels)).toBe("Периоды отсутствия клиента обновлены");
    expect(formatAuditLabel("payment_updated", auditActionLabels)).toBe("Платеж изменен");
    expect(formatAuditLabel("course_theme_homework_passed", auditActionLabels)).toBe("Домашняя работа принята");
  });

  it("translates new course categories and preserves unknown server values", () => {
    expect(formatAuditLabel("course_progress", auditCategoryLabels)).toBe("Прогресс по курсам");
    expect(formatAuditLabel("future_event", auditActionLabels)).toBe("future_event");
  });
});
