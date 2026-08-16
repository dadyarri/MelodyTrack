import { Empty, Typography } from "antd";
import dayjs, { type Dayjs } from "dayjs";
import { type CSSProperties, type DragEvent, useEffect, useEffectEvent, useRef, useState } from "react";

import type { Appointment } from "@/entities/appointment";
import { getAppointmentStatusColorVars, getAppointmentStatusLabel, renderAppointmentStatusIcon } from "@/entities/appointment";
import type { UserAvailability } from "@/entities/user";
import { getBlockedRanges, isSlotAvailable } from "@/entities/user";
import { formatDate, TIME_FORMAT } from "@/shared/lib";
import { CheckOutlined, SyncOutlined } from "@/shared/ui/icons";

import styles from "./ScheduleCalendar.module.css";

const defaultStartHour = 10;
const defaultEndHour = 21;
const hourHeight = 88;
const stackOffset = 10;

export function AppointmentsCalendar({
  appointments,
  availability,
  canCreateAppointments = true,
  loading,
  range,
  onCreateAt,
  onReschedule,
  onSelect,
  onComplete,
  reschedulePendingAppointmentId,
  selectedAppointmentId,
  visibleHours,
}: {
  appointments: Appointment[];
  availability?: UserAvailability;
  canCreateAppointments?: boolean;
  loading: boolean;
  range: [Dayjs, Dayjs];
  onCreateAt: (startDate: Dayjs) => void;
  onReschedule: (appointment: Appointment, startDate: Dayjs) => void;
  onSelect: (appointment: Appointment) => void;
  onComplete: (appointment: Appointment) => void;
  reschedulePendingAppointmentId: string | null;
  selectedAppointmentId: string | null;
  visibleHours?: { startHour: number; endHour: number };
}) {
  const days = getDays(range);
  const hours = getHours(visibleHours?.startHour, visibleHours?.endHour);
  const appointmentsByDay = groupAppointmentsByDay(appointments);
  const [draggedAppointmentId, setDraggedAppointmentId] = useState<string | null>(null);
  const [dropTarget, setDropTarget] = useState<{
    dayKey: string;
    hour: number;
  } | null>(null);
  const [hoveredSlot, setHoveredSlot] = useState<{
    dayKey: string;
    hour: number;
  } | null>(null);
  const [hoverMotion, setHoverMotion] = useState<{ x: number; y: number }>({ x: 0, y: 0 });
  const desktopScrollRef = useRef<HTMLDivElement | null>(null);
  const desktopHeaderRef = useRef<HTMLDivElement | null>(null);
  const desktopGridRef = useRef<HTMLDivElement | null>(null);
  const [hasHiddenAppointmentsAbove, setHasHiddenAppointmentsAbove] = useState(false);
  const [hasHiddenAppointmentsBelow, setHasHiddenAppointmentsBelow] = useState(false);
  const draggedAppointment = draggedAppointmentId
    ? (appointments.find((appointment) => appointment.id === draggedAppointmentId) ?? null)
    : null;
  const dayIndexByKey = new Map(days.map((day, index) => [day.format("YYYY-MM-DD"), index]));

  const activateHoveredSlot = (nextSlot: { dayKey: string; hour: number }) => {
    setHoveredSlot((current) => {
      setHoverMotion(getHoverMotion(current, nextSlot, dayIndexByKey));
      return current?.dayKey === nextSlot.dayKey && current.hour === nextSlot.hour ? current : nextSlot;
    });
  };

  const updateScrollIndicators = useEffectEvent(() => {
    const container = desktopScrollRef.current;
    const header = desktopHeaderRef.current;
    if (!container || !header) {
      return;
    }

    const entries = container.querySelectorAll<HTMLElement>("[data-schedule-entry='desktop']");
    if (entries.length === 0) {
      setHasHiddenAppointmentsAbove(false);
      setHasHiddenAppointmentsBelow(false);
      return;
    }

    const containerRect = container.getBoundingClientRect();
    const headerRect = header.getBoundingClientRect();
    const visibleTop = headerRect.bottom;
    const visibleBottom = containerRect.bottom;

    let nextHasHiddenAbove = false;
    let nextHasHiddenBelow = false;

    for (const entry of entries) {
      const entryRect = entry.getBoundingClientRect();
      if (entryRect.top < visibleTop - 4) {
        nextHasHiddenAbove = true;
      }

      if (entryRect.bottom > visibleBottom + 4) {
        nextHasHiddenBelow = true;
      }

      if (nextHasHiddenAbove && nextHasHiddenBelow) {
        break;
      }
    }

    setHasHiddenAppointmentsAbove(nextHasHiddenAbove);
    setHasHiddenAppointmentsBelow(nextHasHiddenBelow);
  });

  useEffect(() => {
    const container = desktopScrollRef.current;
    if (!container) {
      return;
    }

    updateScrollIndicators();
    container.addEventListener("scroll", updateScrollIndicators, {
      passive: true,
    });
    window.addEventListener("resize", updateScrollIndicators);
    const resizeObserver = new ResizeObserver(() => {
      updateScrollIndicators();
    });
    resizeObserver.observe(container);
    const header = desktopHeaderRef.current;
    if (header) {
      resizeObserver.observe(header);
    }
    const grid = desktopGridRef.current;
    if (grid) {
      resizeObserver.observe(grid);
    }
    const mutationObserver = new MutationObserver(() => {
      updateScrollIndicators();
    });
    if (grid) {
      mutationObserver.observe(grid, {
        childList: true,
        subtree: true,
        attributes: true,
      });
    }

    return () => {
      container.removeEventListener("scroll", updateScrollIndicators);
      window.removeEventListener("resize", updateScrollIndicators);
      resizeObserver.disconnect();
      mutationObserver.disconnect();
    };
  }, []);

  const handleAppointmentDragStart = (appointment: Appointment) => {
    if (reschedulePendingAppointmentId) {
      return;
    }

    setDraggedAppointmentId(appointment.id);
  };

  const handleAppointmentDragEnd = () => {
    setDraggedAppointmentId(null);
    setDropTarget(null);
  };

  const handleColumnDragOver = (event: DragEvent<HTMLDivElement>, day: Dayjs) => {
    if (!draggedAppointment) {
      return;
    }

    const nextHour = getDropHour(event, hours[0], hours[hours.length - 1]);
    const nextStartDate = buildRescheduledStartDate(day, dayjs(draggedAppointment.startDate), nextHour);
    if (!isSlotAvailable(availability, nextStartDate)) {
      setDropTarget(null);
      return;
    }

    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
    setDropTarget((current) => {
      const nextTarget = { dayKey: day.format("YYYY-MM-DD"), hour: nextHour };
      return current?.dayKey === nextTarget.dayKey && current.hour === nextTarget.hour ? current : nextTarget;
    });
  };

  const handleColumnDrop = (event: DragEvent<HTMLDivElement>, day: Dayjs) => {
    if (!draggedAppointment) {
      return;
    }

    event.preventDefault();

    const nextStartDate = buildRescheduledStartDate(
      day,
      dayjs(draggedAppointment.startDate),
      getDropHour(event, hours[0], hours[hours.length - 1]),
    );

    if (!isSlotAvailable(availability, nextStartDate)) {
      setDraggedAppointmentId(null);
      setDropTarget(null);
      return;
    }

    if (!nextStartDate.isSame(dayjs(draggedAppointment.startDate))) {
      onReschedule(draggedAppointment, nextStartDate);
    }

    setDraggedAppointmentId(null);
    setDropTarget(null);
  };

  const handleColumnDragLeave = (event: DragEvent<HTMLDivElement>, day: Dayjs) => {
    const relatedTarget = event.relatedTarget;
    if (relatedTarget instanceof Node && event.currentTarget.contains(relatedTarget)) {
      return;
    }

    setDropTarget((current) => (current?.dayKey === day.format("YYYY-MM-DD") ? null : current));
  };

  return (
    <section className={`${styles.calendar}${draggedAppointmentId ? ` ${styles.dragActive}` : ""}`} aria-busy={loading}>
      {hasHiddenAppointmentsAbove ? (
        <div className={`${styles.scrollIndicator} ${styles.scrollIndicatorTop}`}>
          <span>Есть записи выше</span>
        </div>
      ) : null}
      {hasHiddenAppointmentsBelow ? (
        <div className={`${styles.scrollIndicator} ${styles.scrollIndicatorBottom}`}>
          <span>Есть записи ниже</span>
        </div>
      ) : null}
      <div className={styles.desktop} ref={desktopScrollRef}>
        <div
          className={styles.header}
          ref={desktopHeaderRef}
          style={{
            gridTemplateColumns: `72px repeat(${String(days.length)}, minmax(144px, 1fr))`,
          }}
        >
          <div className={styles.corner} />
          {days.map((day) => (
            <div
              className={`${day.isSame(dayjs(), "day") ? `${styles.dayHeading} ${styles.dayHeadingToday}` : styles.dayHeading}${hoveredSlot?.dayKey === day.format("YYYY-MM-DD") ? ` ${styles.dayHeadingActive}` : ""}`}
              key={day.format("YYYY-MM-DD")}
            >
              <span>{formatWeekday(day)}</span>
              <strong>{day.format("D")}</strong>
            </div>
          ))}
        </div>
        <div
          ref={desktopGridRef}
          className={styles.grid}
          style={{
            gridTemplateColumns: `72px repeat(${String(days.length)}, minmax(144px, 1fr))`,
            minHeight: hours.length * hourHeight,
          }}
        >
          <div className={styles.timeRail}>
            {hours.map((hour) => (
              <div
                className={`${styles.timeSlot}${hoveredSlot?.hour === hour ? ` ${styles.timeSlotActive}` : ""}`}
                key={hour}
                style={
                  hoveredSlot?.hour === hour
                    ? ({
                        "--hover-enter-y": `${String(hoverMotion.y)}px`,
                      } as CSSProperties)
                    : undefined
                }
              >
                {`${hour.toString().padStart(2, "0")}:00`}
              </div>
            ))}
          </div>
          {days.map((day) => (
            /* biome-ignore lint/a11y/noStaticElementInteractions: this column is a pointer drag-and-drop drop zone; keyboard users interact with the hour buttons inside it. */
            <div
              className={styles.dayColumn}
              key={day.format("YYYY-MM-DD")}
              onDragLeave={(event) => {
                handleColumnDragLeave(event, day);
              }}
              onDragOver={(event) => {
                handleColumnDragOver(event, day);
              }}
              onDrop={(event) => {
                handleColumnDrop(event, day);
              }}
            >
              {getBlockedRanges(availability, day, hours[0], hours[hours.length - 1] + 1).map((range) => (
                <div
                  className={`${styles.blockedRange}${range.isVacation ? ` ${styles.blockedRangeVacation}` : ""}`}
                  key={[day.format("YYYY-MM-DD"), String(range.startMinute), String(range.endMinute)].join(":")}
                  style={getBlockedRangeStyle(range.startMinute, range.endMinute, hours[0])}
                />
              ))}
              {hours.map((hour) => {
                const dayKey = day.format("YYYY-MM-DD");
                const isHovered = hoveredSlot?.dayKey === dayKey && hoveredSlot.hour === hour;

                return (
                  <button
                    type="button"
                    className={`${styles.hourLine} ${styles.hourSlotButton}${isHovered ? ` ${styles.hourSlotButtonActive}` : ""}${dropTarget?.dayKey === dayKey && dropTarget.hour === hour ? ` ${styles.hourSlotDropTarget}` : ""}${!isSlotAvailable(availability, day.hour(hour).minute(0).second(0).millisecond(0)) ? ` ${styles.hourSlotBlocked}` : ""}`}
                    key={hour}
                    style={
                      isHovered
                        ? ({
                            "--hover-enter-x": `${String(hoverMotion.x)}px`,
                            "--hover-enter-y": `${String(hoverMotion.y)}px`,
                          } as CSSProperties)
                        : undefined
                    }
                    aria-label={`Создать запись на ${formatDate(day)} ${hour.toString().padStart(2, "0")}:00`}
                    disabled={!(canCreateAppointments && isSlotAvailable(availability, day.hour(hour).minute(0).second(0).millisecond(0)))}
                    onBlur={() => {
                      setHoveredSlot((current) => (current?.dayKey === dayKey && current.hour === hour ? null : current));
                    }}
                    onClick={() => {
                      onCreateAt(day.hour(hour).minute(0).second(0).millisecond(0));
                    }}
                    onFocus={() => {
                      activateHoveredSlot({ dayKey, hour });
                    }}
                    onMouseEnter={() => {
                      activateHoveredSlot({ dayKey, hour });
                    }}
                    onMouseLeave={() => {
                      setHoveredSlot((current) => (current?.dayKey === dayKey && current.hour === hour ? null : current));
                    }}
                  >
                    <span className={styles.hourSlotHighlight} aria-hidden="true" />
                  </button>
                );
              })}
              {groupAppointmentsBySlot(appointmentsByDay.get(day.format("YYYY-MM-DD")) ?? []).map((appointmentsInSlot) => (
                <AppointmentStack
                  appointments={appointmentsInSlot}
                  day={day}
                  startHour={hours[0]}
                  key={appointmentsInSlot.map((appointment) => appointment.id).join(":")}
                  onDragEnd={handleAppointmentDragEnd}
                  onDragStart={handleAppointmentDragStart}
                  onSelect={onSelect}
                  onComplete={onComplete}
                  reschedulePendingAppointmentId={reschedulePendingAppointmentId}
                  selectedAppointmentId={selectedAppointmentId}
                />
              ))}
            </div>
          ))}
        </div>
      </div>
      <div className={styles.mobile}>
        {days.map((day) => {
          const dayAppointments = appointmentsByDay.get(day.format("YYYY-MM-DD")) ?? [];
          return (
            <section className={styles.agendaDay} key={day.format("YYYY-MM-DD")}>
              <div className={day.isSame(dayjs(), "day") ? `${styles.agendaHeading} ${styles.agendaHeadingToday}` : styles.agendaHeading}>
                <Typography.Text strong>{formatDate(day)}</Typography.Text>
                <Typography.Text type="secondary">{formatWeekday(day)}</Typography.Text>
              </div>
              {dayAppointments.length > 0 ? (
                dayAppointments.map((appointment) => (
                  <AppointmentAgendaItem
                    appointment={appointment}
                    isSelected={appointment.id === selectedAppointmentId}
                    key={appointment.id}
                    onSelect={onSelect}
                    onComplete={onComplete}
                  />
                ))
              ) : (
                <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Нет записей" />
              )}
            </section>
          );
        })}
      </div>
    </section>
  );
}

function getHoverMotion(
  previous: { dayKey: string; hour: number } | null,
  next: { dayKey: string; hour: number },
  dayIndexByKey: Map<string, number>,
) {
  if (!previous) {
    return { x: 0, y: 0 };
  }

  const previousDayIndex = dayIndexByKey.get(previous.dayKey);
  const nextDayIndex = dayIndexByKey.get(next.dayKey);
  if (previousDayIndex === undefined || nextDayIndex === undefined) {
    return { x: 0, y: 0 };
  }

  const dayDelta = nextDayIndex - previousDayIndex;
  const hourDelta = next.hour - previous.hour;

  return {
    x: dayDelta === 0 ? 0 : dayDelta > 0 ? -26 : 26,
    y: hourDelta === 0 ? 0 : hourDelta > 0 ? -18 : 18,
  };
}

function AppointmentStack({
  appointments,
  day,
  onDragEnd,
  onDragStart,
  startHour,
  onSelect,
  onComplete,
  reschedulePendingAppointmentId,
  selectedAppointmentId,
}: {
  appointments: Appointment[];
  day: Dayjs;
  onDragEnd: () => void;
  onDragStart: (appointment: Appointment) => void;
  startHour: number;
  onSelect: (appointment: Appointment) => void;
  onComplete: (appointment: Appointment) => void;
  reschedulePendingAppointmentId: string | null;
  selectedAppointmentId: string | null;
}) {
  const appointment = appointments[0];
  const start = dayjs(appointment.startDate);
  const longestEnd = appointments.reduce((latest, current) => {
    const currentEnd = dayjs(current.endDate);
    return currentEnd.isAfter(latest) ? currentEnd : latest;
  }, dayjs(appointment.endDate));
  const top = Math.max(0, (start.diff(day.hour(startHour).minute(0).second(0), "minute") / 60) * hourHeight);
  const height = Math.max(60, (longestEnd.diff(start, "minute") / 60) * hourHeight);
  const totalOffset = appointments.length > 1 ? stackOffset * (appointments.length - 1) : 0;
  const cardHeight = Math.max(60, height - totalOffset);
  const expandedOffset = 66;
  const slotTop = Math.floor(top / hourHeight) * hourHeight;

  return (
    <div
      className={styles.stack}
      style={
        {
          "--event-top": `${String(top)}px`,
          "--event-height": `${String(height)}px`,
          "--stack-size": String(appointments.length),
          "--stack-expanded-height": `${String(height + expandedOffset * (appointments.length - 1))}px`,
          "--badge-top": `${String(slotTop - top + 4)}px`,
        } as CSSProperties
      }
    >
      {appointments.length > 1 ? <div className={styles.stackBadge}>{appointments.length}</div> : null}
      {appointments.map((item, index) => (
        /* biome-ignore lint/a11y/useSemanticElements: the card container must stay non-button so the nested completion button remains valid interactive HTML. */
        <div
          role="button"
          tabIndex={0}
          className={`${styles.entry} ${styles.event} ${styles.eventStacked} ${styles.entryDraggable} ${getAppointmentClassName(item)}${item.id === selectedAppointmentId ? ` ${styles.entrySelected}` : ""}${item.id === reschedulePendingAppointmentId ? ` ${styles.entryDragDisabled}` : ""}`}
          data-schedule-entry="desktop"
          draggable={reschedulePendingAppointmentId === null}
          style={
            {
              "--stack-index": String(index),
              "--stack-top": `${String(index * stackOffset)}px`,
              "--stack-card-height": `${String(cardHeight)}px`,
              ...getAppointmentStatusColorVars(item.status),
            } as CSSProperties
          }
          key={item.id}
          onDragEnd={onDragEnd}
          onDragStart={(event) => {
            event.dataTransfer.effectAllowed = "move";
            onDragStart(item);
          }}
          onClick={() => {
            onSelect(item);
          }}
          onKeyDown={(event) => {
            if (event.key === "Enter" || event.key === " ") {
              event.preventDefault();
              onSelect(item);
            }
          }}
        >
          <AppointmentContent appointment={item} density={getStackDensity(appointments.length)} onComplete={onComplete} />
        </div>
      ))}
    </div>
  );
}

function AppointmentAgendaItem({
  appointment,
  isSelected,
  onSelect,
  onComplete,
}: {
  appointment: Appointment;
  isSelected: boolean;
  onSelect: (appointment: Appointment) => void;
  onComplete: (appointment: Appointment) => void;
}) {
  return (
    /* biome-ignore lint/a11y/useSemanticElements: the agenda card container must stay non-button so the nested completion button remains valid interactive HTML. */
    <div
      role="button"
      tabIndex={0}
      className={`${styles.entry} ${styles.agendaItem} ${getAppointmentClassName(appointment)}${isSelected ? ` ${styles.entrySelected}` : ""}`}
      style={getAppointmentStatusColorVars(appointment.status)}
      onClick={() => {
        onSelect(appointment);
      }}
      onKeyDown={(event) => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          onSelect(appointment);
        }
      }}
    >
      <AppointmentContent appointment={appointment} showTime onComplete={onComplete} />
    </div>
  );
}

function AppointmentContent({
  appointment,
  density = "full",
  showTime = false,
  onComplete,
}: {
  appointment: Appointment;
  density?: "full" | "compact" | "dense";
  showTime?: boolean;
  onComplete?: (appointment: Appointment) => void;
}) {
  const start = dayjs(appointment.startDate);
  const end = dayjs(appointment.endDate);
  const clientName = [appointment.client.lastName, appointment.client.firstName].filter(Boolean).join(" ");
  const densityClassName = density === "dense" ? styles.eventContentDense : density === "compact" ? styles.eventContentCompact : "";

  return (
    <div className={`${styles.eventContent} ${densityClassName}`.trim()}>
      <div className={styles.eventText}>
        {showTime ? (
          <div className={styles.eventTime}>
            {start.format(TIME_FORMAT)} - {end.format(TIME_FORMAT)}
          </div>
        ) : null}
        <div className={showTime ? styles.eventTitle : styles.eventTitlePrimary}>{clientName}</div>
        <div className={styles.eventService}>{appointment.service.name}</div>
        {appointment.provider ? (
          <div className={styles.eventProvider}>
            {appointment.provider.lastName} {appointment.provider.firstName}
          </div>
        ) : null}
      </div>
      <div className={styles.eventIcons}>
        <span className={styles.eventStatusIcon} title={getAppointmentStatusLabel(appointment.status)}>
          {renderAppointmentStatusIcon(appointment.status)}
        </span>
        {appointment.recurringRule ? (
          <span className={`${styles.eventStatusIcon} ${styles.eventRecurringIcon}`} title="Повторяющаяся запись">
            <SyncOutlined />
          </span>
        ) : null}
        {appointment.status === "planned" && onComplete ? (
          <button
            className={styles.eventAction}
            type="button"
            aria-label="Отметить запись как завершённую"
            title="Отметить как завершённую"
            onClick={(event) => {
              event.stopPropagation();
              event.preventDefault();
              onComplete(appointment);
            }}
          >
            <CheckOutlined />
          </button>
        ) : null}
      </div>
    </div>
  );
}

function getDays(range: [Dayjs, Dayjs]) {
  const days: Dayjs[] = [];
  let cursor = range[0].startOf("day");
  const end = range[1].startOf("day");
  while (cursor.isBefore(end) || cursor.isSame(end, "day")) {
    days.push(cursor);
    cursor = cursor.add(1, "day");
  }
  return days;
}

function getHours(startHour = defaultStartHour, endHour = defaultEndHour) {
  return Array.from({ length: Math.max(1, endHour - startHour) }, (_, index) => startHour + index);
}

function groupAppointmentsByDay(appointments: Appointment[]) {
  return appointments.reduce((map, appointment) => {
    const key = dayjs(appointment.startDate).format("YYYY-MM-DD");
    const items = map.get(key) ?? [];
    items.push(appointment);
    items.sort((a, b) => dayjs(a.startDate).valueOf() - dayjs(b.startDate).valueOf());
    map.set(key, items);
    return map;
  }, new Map<string, Appointment[]>());
}

function groupAppointmentsBySlot(appointments: Appointment[]) {
  const groups = new Map<string, Appointment[]>();

  for (const appointment of appointments) {
    const key = dayjs(appointment.startDate).format("YYYY-MM-DDTHH:mm");
    const items = groups.get(key) ?? [];
    items.push(appointment);
    groups.set(key, items);
  }

  return [...groups.values()].map((items) =>
    items.sort((left, right) => {
      const leftRank = getAppointmentStatusRank(left);
      const rightRank = getAppointmentStatusRank(right);
      if (leftRank !== rightRank) {
        return leftRank - rightRank;
      }

      return dayjs(left.endDate).valueOf() - dayjs(right.endDate).valueOf();
    }),
  );
}

function formatWeekday(day: Dayjs) {
  return day.format("dd").toUpperCase();
}

function getStackDensity(stackSize: number): "full" | "compact" | "dense" {
  if (stackSize <= 1) {
    return "full";
  }

  return stackSize >= 3 ? "dense" : "compact";
}

function getAppointmentClassName(appointment: Appointment) {
  switch (appointment.status) {
    case "completed":
      return styles.eventCompleted;
    case "cancelled":
      return styles.eventCanceled;
    case "burned":
      return styles.eventBurned;
    default:
      return styles.eventPlanned;
  }
}

function getAppointmentStatusRank(appointment: Appointment) {
  switch (appointment.status) {
    case "planned":
      return 0;
    case "completed":
      return 1;
    case "burned":
      return 2;
    case "cancelled":
      return 3;
    default:
      return 4;
  }
}

function getDropHour(event: DragEvent<HTMLDivElement>, startHour: number, endHour: number) {
  const bounds = event.currentTarget.getBoundingClientRect();
  const offset = Math.max(0, event.clientY - bounds.top);
  const relativeHour = Math.floor(offset / hourHeight);
  return Math.min(endHour, Math.max(startHour, startHour + relativeHour));
}

function buildRescheduledStartDate(day: Dayjs, originalStart: Dayjs, nextHour: number) {
  return day.hour(nextHour).minute(originalStart.minute()).second(originalStart.second()).millisecond(originalStart.millisecond());
}

function getBlockedRangeStyle(startMinute: number, endMinute: number, startHour: number): CSSProperties {
  const top = ((startMinute - startHour * 60) / 60) * hourHeight;
  const height = ((endMinute - startMinute) / 60) * hourHeight;
  return {
    top: `${String(top)}px`,
    height: `${String(height)}px`,
  };
}
