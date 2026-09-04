import "@xyflow/react/dist/style.css";

import { useQuery } from "@tanstack/react-query";
import {
  Background,
  BackgroundVariant,
  BaseEdge,
  Controls,
  type Edge,
  type EdgeProps,
  type EdgeTypes,
  Handle,
  MarkerType,
  type Node,
  type NodeProps,
  type NodeTypes,
  Position,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
} from "@xyflow/react";
import { Alert, App as AntdApp, Button, Empty, Form, Input, InputNumber, Modal, Select, Space, Tag, Typography } from "antd";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router";
import * as v from "valibot";

import type {
  CourseEnrollment,
  CourseEnrollmentTheme,
  CourseEnrollmentThemeProgressAction,
  CourseThemeProgressState,
} from "@/entities/course";
import { courseEnrollmentsApi, courseQueryKeys } from "@/entities/course";
import { useUpdateCourseProgress } from "@/features/update-course-progress";
import type { Ulid } from "@/shared/api";
import { pluralizeRu } from "@/shared/lib";
import { jsonDurableFormCodec, useDurableForm } from "@/shared/lib/react";
import { DraftFormModal, DraftModalTitle, PageLayout } from "@/shared/ui";
import { BbcodeContent } from "@/shared/ui/editors";
import { BbcodeEditor } from "@/shared/ui/editors";
import {
  BookOutlined,
  DeleteOutlined,
  DownOutlined,
  LinkOutlined,
  PlusOutlined,
  SaveOutlined,
  SearchOutlined,
  UpOutlined,
} from "@/shared/ui/icons";

import { useCoursesPageController } from "../model/useCoursesPageController";
import styles from "./CourseWorkspace.module.css";

const topicNodeWidth = 250;
const topicNodeHeight = 84;
const branchLabelWidth = 190;
const branchLabelHeight = 40;
const blockNodeWidth = 160;
const blockGap = 112;
const blockToBranchGap = 104;
const branchRowGap = 36;
const branchLabelToTopicGap = 12;
const topicGap = 62;
const canvasInset = 36;
const courseNodeDraftSchema = v.record(v.string(), v.unknown());
const courseNodeDraftCodec = jsonDurableFormCodec<Record<string, unknown>>();

type Controller = ReturnType<typeof useCoursesPageController>;
type EditorCourse = NonNullable<Controller["draftCourse"]>;
type EditorBlock = EditorCourse["blocks"][number];
type EditorBranch = EditorBlock["branches"][number];
type EditorTheme = EditorBranch["themes"][number];
type EditorLevel = EditorCourse["levels"][number];

type DiagramNodeSelection =
  | { kind: "course" }
  | { kind: "block"; blockId: string }
  | { kind: "branch"; blockId: string; branchId: string }
  | { kind: "theme"; blockId: string; branchId: string; themeId: string };

type AddNodeIntent =
  | { kind: "block" }
  | { kind: "branch"; blockId: string }
  | { kind: "theme"; blockId: string; branchId: string; insertIndex?: number };

type DiagramContextMenuState = {
  selection: DiagramNodeSelection;
  x: number;
  y: number;
};

type BlockNodeData = {
  title: string;
};

type BranchNodeData = {
  title: string;
};

type TopicNodeData = {
  themeId: string;
  title: string;
  dependencyCount: number;
  dependencyThemeIds: string[];
  progressState?: CourseThemeProgressState | null;
  isDependencyHighlighted?: boolean;
  isDependencySourceHighlighted?: boolean;
  onHighlightDependencies?: (themeId: string, dependencyThemeIds: string[]) => void;
};

type NodeData = BlockNodeData | BranchNodeData | TopicNodeData;

type DiagramPoint = {
  x: number;
  y: number;
};

type DiagramRect = DiagramPoint & {
  width: number;
  height: number;
  id: string;
};

type DiagramBridge = {
  point: DiagramPoint;
  orientation: "horizontal" | "vertical";
};

type DiagramEdgeData = {
  points: DiagramPoint[];
  bridges?: DiagramBridge[];
  isHighlighted?: boolean;
};

type DependencyHighlight = {
  sourceThemeId: string;
  dependencyThemeIds: string[];
};

const nodeTypes: NodeTypes = {
  block: BlockNode,
  branch: BranchNode,
  topic: TopicNode,
};

const edgeTypes: EdgeTypes = {
  dependency: DiagramEdge,
  sequence: DiagramEdge,
};

export function CourseWorkspace() {
  const controller = useCoursesPageController();
  const { message, modal } = AntdApp.useApp();
  const [searchParams, setSearchParams] = useSearchParams();
  const [selectedNode, setSelectedNode] = useState<DiagramNodeSelection | null>(null);
  const [addIntent, setAddIntent] = useState<AddNodeIntent | null>(null);
  const [contextMenu, setContextMenu] = useState<DiagramContextMenuState | null>(null);
  const courseCount = (controller.coursesQuery.data ?? []).length;
  const requestedCourseId = searchParams.get("course");
  const requestedEnrollmentId = searchParams.get("enrollment");

  useEffect(() => {
    if (requestedCourseId && requestedCourseId !== controller.selectedCourseId) {
      controller.setSelectedCourseId(requestedCourseId);
    }
  }, [controller, requestedCourseId]);

  const enrollmentsQuery = useQuery({
    queryKey: courseQueryKeys.enrollments.list({ courseId: controller.selectedCourseId }),
    queryFn: () => {
      if (!controller.selectedCourseId) {
        throw new Error("Курс не выбран.");
      }

      return courseEnrollmentsApi.list({ courseId: controller.selectedCourseId });
    },
    enabled: controller.selectedCourseId != null,
  });

  const selectedEnrollment =
    requestedEnrollmentId != null
      ? ((enrollmentsQuery.data ?? []).find((enrollment) => enrollment.id === requestedEnrollmentId) ?? null)
      : null;
  const isProgressMode = selectedEnrollment != null;
  const updateThemeProgressMutation = useUpdateCourseProgress({
    onSuccess: ({ action }) => {
      void message.success(getThemeProgressSuccessMessage(action));
    },
  });

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (!(event.ctrlKey || event.metaKey) || event.altKey || event.repeat) {
        return;
      }

      if (event.key.toLowerCase() !== "s") {
        return;
      }

      if (isProgressMode || !controller.hasUnsavedChanges || controller.updateMutation.isPending) {
        return;
      }

      event.preventDefault();
      controller.saveCourse();
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [controller, isProgressMode]);

  return (
    <PageLayout
      title="Курсы"
      actions={
        controller.canManageCourses ? (
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => {
              controller.setCreateOpen(true);
            }}
          >
            Новый курс
          </Button>
        ) : undefined
      }
    >
      <div className={styles.layout} data-onboarding-id="courses-workspace">
        <section className={styles.sidebarPanel}>
          <div className={styles.listHeader}>
            <Input
              value={controller.search}
              onChange={(event) => {
                controller.setSearch(event.target.value);
              }}
              prefix={<SearchOutlined />}
              placeholder="Найти курс"
            />
            <Typography.Text type="secondary" className={styles.listCount}>
              {courseCount} {pluralizeRu(courseCount, { one: "курс", few: "курса", many: "курсов" })}
            </Typography.Text>
          </div>
          <div className={styles.courseList}>
            {(controller.coursesQuery.data ?? []).map((course) => (
              <button
                key={course.id}
                type="button"
                className={`${styles.courseButton} ${course.id === controller.selectedCourseId ? styles.courseButtonActive : ""}`}
                onClick={() => {
                  controller.selectCourse(course.id);
                  const nextParams = new URLSearchParams(searchParams);
                  nextParams.set("course", course.id);
                  nextParams.delete("enrollment");
                  setSearchParams(nextParams);
                  setSelectedNode(null);
                }}
              >
                <div className={styles.stack}>
                  <span className={styles.courseButtonTitle}>{course.name}</span>
                  {course.description ? <Typography.Text type="secondary">{course.description}</Typography.Text> : null}
                  <span className={styles.listMeta}>
                    {course.blockCount} {pluralizeRu(course.blockCount, { one: "блок", few: "блока", many: "блоков" })} ·{" "}
                    {course.themeCount} {pluralizeRu(course.themeCount, { one: "тема", few: "темы", many: "тем" })}
                  </span>
                </div>
                <BookOutlined />
              </button>
            ))}
            {!controller.coursesQuery.isLoading && (controller.coursesQuery.data ?? []).length === 0 ? (
              <Empty description="Курсы пока не созданы" image={Empty.PRESENTED_IMAGE_SIMPLE} />
            ) : null}
          </div>
        </section>

        <section className={styles.editorPanel}>
          {!controller.draftCourse ? (
            <Empty description="Выберите курс слева или создайте новый" />
          ) : (
            <>
              <div className={styles.editorHeader}>
                <div className={styles.headerTitle}>
                  <Typography.Title level={3}>
                    <DraftModalTitle
                      title={controller.draftCourse.name || "Курс без названия"}
                      restored={controller.courseDraft.restored}
                      saveStatus={controller.courseDraft.status}
                      onRetry={controller.courseDraft.retry}
                    />
                  </Typography.Title>
                </div>
                <div className={styles.editorActions}>
                  <Select
                    className={styles.enrollmentSelect}
                    value={selectedEnrollment?.id ?? "course"}
                    loading={enrollmentsQuery.isLoading}
                    options={[
                      { value: "course", label: "Редактирование курса" },
                      ...(enrollmentsQuery.data ?? []).map((enrollment) => ({
                        value: enrollment.id,
                        label: enrollment.clientDisplayName,
                      })),
                    ]}
                    onChange={(value) => {
                      const nextParams = new URLSearchParams(searchParams);
                      nextParams.set("course", controller.draftCourse?.id ?? controller.selectedCourseId ?? "");
                      if (value === "course") {
                        nextParams.delete("enrollment");
                      } else {
                        nextParams.set("enrollment", value);
                      }
                      setSelectedNode(null);
                      setSearchParams(nextParams);
                    }}
                  />
                  {!isProgressMode ? (
                    <>
                      {controller.courseDraft.isStale ? <Button onClick={controller.courseDraft.reapply}>Применить черновик</Button> : null}
                      {controller.courseDraft.status === "failed" ? (
                        <Button onClick={controller.courseDraft.retry}>Повторить сохранение</Button>
                      ) : null}
                      {controller.courseDraft.hasDraft ? (
                        <Button onClick={() => void controller.discardCourseDraft()}>Отбросить черновик</Button>
                      ) : null}
                      <Button
                        icon={<BookOutlined />}
                        onClick={() => {
                          setSelectedNode({ kind: "course" });
                        }}
                      >
                        Свойства курса
                      </Button>
                      <Button
                        type={controller.hasUnsavedChanges ? "primary" : "dashed"}
                        icon={<SaveOutlined />}
                        loading={controller.updateMutation.isPending}
                        onClick={controller.saveCourse}
                      >
                        <span className={styles.saveButtonLabel}>Сохранить</span>
                      </Button>
                    </>
                  ) : null}
                </div>
              </div>

              {controller.draftCourse.blocks.length === 0 ? (
                <div className={styles.emptyDiagram}>
                  <Empty description="В этом курсе пока нет блоков." image={Empty.PRESENTED_IMAGE_SIMPLE}>
                    {!isProgressMode ? (
                      <Button
                        type="primary"
                        icon={<PlusOutlined />}
                        onClick={() => {
                          setAddIntent({ kind: "block" });
                        }}
                      >
                        Добавить блок
                      </Button>
                    ) : null}
                  </Empty>
                </div>
              ) : (
                <ReactFlowProvider>
                  <CourseEditorFlow
                    course={controller.draftCourse}
                    enrollment={selectedEnrollment}
                    hasUnsavedChanges={controller.hasUnsavedChanges && !isProgressMode}
                    onSelectNode={setSelectedNode}
                    onContextMenu={
                      isProgressMode
                        ? undefined
                        : (menu) => {
                            setContextMenu(menu);
                          }
                    }
                    onCloseContextMenu={() => {
                      setContextMenu(null);
                    }}
                  />
                </ReactFlowProvider>
              )}
              {!isProgressMode && contextMenu ? (
                <DiagramContextMenu
                  course={controller.draftCourse}
                  menu={contextMenu}
                  onClose={() => {
                    setContextMenu(null);
                  }}
                  onAdd={(intent) => {
                    setAddIntent(intent);
                    setContextMenu(null);
                  }}
                  onEdit={(selection) => {
                    setSelectedNode(selection);
                    setContextMenu(null);
                  }}
                  onDelete={(selection) => {
                    confirmNodeDelete(modal, controller, selection);
                    setContextMenu(null);
                  }}
                />
              ) : null}
            </>
          )}
        </section>
      </div>

      {controller.draftCourse ? (
        isProgressMode ? (
          <ProgressNodeModal
            course={controller.draftCourse}
            enrollment={selectedEnrollment}
            selection={selectedNode}
            isUpdating={updateThemeProgressMutation.isPending}
            onProgressAction={(themeId, action) => {
              if (action === "pass-homework") {
                modal.confirm({
                  title: "Принять домашнее задание и завершить тему?",
                  onOk: () => {
                    updateThemeProgressMutation.mutate({ themeId, action });
                  },
                });
                return;
              }

              updateThemeProgressMutation.mutate({ themeId, action });
            }}
            onClose={() => {
              setSelectedNode(null);
            }}
          />
        ) : (
          <>
            <CourseNodeModal
              controller={controller}
              selection={selectedNode}
              onClose={() => {
                setSelectedNode(null);
              }}
              onAdd={setAddIntent}
              onDelete={(selection) => {
                confirmNodeDelete(modal, controller, selection);
                setSelectedNode(null);
              }}
            />
            <AddNodeModal
              controller={controller}
              intent={addIntent}
              onClose={() => {
                setAddIntent(null);
              }}
            />
          </>
        )
      ) : null}

      <DraftFormModal
        open={controller.canManageCourses && controller.isCreateOpen}
        title="Новый курс"
        restored={controller.createDraft.restored}
        saveStatus={controller.createDraft.status}
        showClearDraft={controller.createDraft.hasDraft}
        onClearDraft={() => {
          void controller.createDraft.discard().then(() => {
            controller.createForm.resetFields();
          });
        }}
        onRetryDraft={controller.createDraft.retry}
        onCancel={() => {
          controller.setCreateOpen(false);
        }}
        onOk={() => {
          controller.createForm.submit();
        }}
        confirmLoading={controller.createMutation.isPending}
      >
        <Form
          form={controller.createForm}
          layout="vertical"
          onFinish={controller.submitCreate}
          onValuesChange={controller.createDraft.formProps.onValuesChange}
        >
          <Form.Item name="name" label="Название" rules={[{ required: true, message: "Укажите название курса" }]}>
            <Input />
          </Form.Item>
          <Form.Item name="description" label="Описание">
            <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} />
          </Form.Item>
        </Form>
      </DraftFormModal>
    </PageLayout>
  );
}

function CourseEditorFlow({
  course,
  enrollment,
  hasUnsavedChanges,
  onSelectNode,
  onContextMenu,
  onCloseContextMenu,
}: {
  course: EditorCourse;
  enrollment: CourseEnrollment | null;
  hasUnsavedChanges: boolean;
  onSelectNode: (selection: DiagramNodeSelection) => void;
  onContextMenu?: (menu: DiagramContextMenuState) => void;
  onCloseContextMenu: () => void;
}) {
  const reactFlow = useReactFlow();
  const { nodes, edges } = useMemo(() => layoutEditorCourse(course, enrollment), [course, enrollment]);
  const [dependencyHighlight, setDependencyHighlight] = useState<DependencyHighlight | null>(null);
  const highlightTimeoutRef = useRef<number | null>(null);
  const restartTimeoutRef = useRef<number | null>(null);

  const clearHighlightTimers = useCallback(() => {
    if (highlightTimeoutRef.current != null) {
      window.clearTimeout(highlightTimeoutRef.current);
      highlightTimeoutRef.current = null;
    }

    if (restartTimeoutRef.current != null) {
      window.clearTimeout(restartTimeoutRef.current);
      restartTimeoutRef.current = null;
    }
  }, []);

  const clearDependencyHighlight = useCallback(() => {
    clearHighlightTimers();
    setDependencyHighlight(null);
  }, [clearHighlightTimers]);

  const highlightDependencies = useCallback(
    (sourceThemeId: string, dependencyThemeIds: string[]) => {
      clearHighlightTimers();
      setDependencyHighlight(null);

      restartTimeoutRef.current = window.setTimeout(() => {
        setDependencyHighlight({ sourceThemeId, dependencyThemeIds });
        highlightTimeoutRef.current = window.setTimeout(() => {
          setDependencyHighlight(null);
          highlightTimeoutRef.current = null;
        }, 2200);
        restartTimeoutRef.current = null;
      }, 0);
    },
    [clearHighlightTimers],
  );

  useEffect(() => clearDependencyHighlight, [clearDependencyHighlight]);

  const highlightedDependencyIds = useMemo(() => new Set(dependencyHighlight?.dependencyThemeIds ?? []), [dependencyHighlight]);
  const highlightedDependencyEdgeIds = useMemo(
    () =>
      new Set(
        dependencyHighlight?.dependencyThemeIds.map(
          (dependencyThemeId) => `dependency-${dependencyThemeId}-${dependencyHighlight.sourceThemeId}`,
        ) ?? [],
      ),
    [dependencyHighlight],
  );
  const flowNodes = useMemo(
    () =>
      nodes.map((node) => {
        if (node.type !== "topic") {
          return node;
        }

        const data = node.data;
        if (!("themeId" in data)) {
          return node;
        }

        return {
          ...node,
          data: {
            ...data,
            isDependencyHighlighted: highlightedDependencyIds.has(data.themeId),
            isDependencySourceHighlighted: dependencyHighlight?.sourceThemeId === data.themeId,
            onHighlightDependencies: highlightDependencies,
          },
        };
      }),
    [dependencyHighlight, highlightDependencies, highlightedDependencyIds, nodes],
  );
  const flowEdges = useMemo(
    () =>
      edges.map((edge) => {
        if (!highlightedDependencyEdgeIds.has(edge.id)) {
          return edge;
        }

        return {
          ...edge,
          data: {
            ...edge.data,
            isHighlighted: true,
          },
          markerEnd: { type: MarkerType.ArrowClosed, color: "var(--ant-color-warning)" },
          style: {
            ...edge.style,
            stroke: "var(--ant-color-warning)",
            strokeWidth: 2.6,
          },
        };
      }),
    [edges, highlightedDependencyEdgeIds],
  );

  useEffect(() => {
    requestAnimationFrame(() => {
      void reactFlow.fitView({ duration: 250, padding: 0.16, maxZoom: 1.05 });
    });
  }, [reactFlow]);

  return (
    <div className={styles.flowShell}>
      <ReactFlow
        nodes={flowNodes}
        edges={flowEdges}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable
        minZoom={0.38}
        maxZoom={1.45}
        fitView
        proOptions={{ hideAttribution: true }}
        onNodeClick={(_, node) => {
          onCloseContextMenu();
          onSelectNode(node.data.selection);
        }}
        onNodeContextMenu={(event, node) => {
          if (!onContextMenu) {
            return;
          }

          event.preventDefault();
          onContextMenu({
            selection: node.data.selection,
            x: event.clientX,
            y: event.clientY,
          });
        }}
        onPaneClick={onCloseContextMenu}
      >
        <Background variant={BackgroundVariant.Dots} gap={18} size={1.1} color="var(--course-diagram-gold-border)" />
        <Controls showInteractive={false} position="top-left" />
      </ReactFlow>
      {enrollment ? <ProgressOverlayCard course={course} enrollment={enrollment} /> : null}
      {hasUnsavedChanges ? <Alert className={styles.unsavedWarning} type="warning" showIcon title="Есть несохраненные изменения" /> : null}
      <DiagramLegend />
    </div>
  );
}

function ProgressOverlayCard({ course, enrollment }: { course: EditorCourse; enrollment: CourseEnrollment }) {
  const previousXpRef = useRef(enrollment.earnedExperiencePoints);
  const xpGainTimeoutRef = useRef<number | null>(null);
  const [xpGain, setXpGain] = useState<number | null>(null);
  const [isXpAnimating, setXpAnimating] = useState(false);
  const sortedLevels = useMemo(
    () =>
      course.levels
        .slice()
        .sort(
          (left, right) =>
            left.requiredExperiencePoints - right.requiredExperiencePoints || course.levels.indexOf(left) - course.levels.indexOf(right),
        ),
    [course.levels],
  );
  const currentLevel =
    enrollment.currentLevel ??
    sortedLevels.filter((level) => level.requiredExperiencePoints <= enrollment.earnedExperiencePoints).at(-1) ??
    null;
  const nextLevel = sortedLevels.find((level) => level.requiredExperiencePoints > enrollment.earnedExperiencePoints) ?? null;
  const currentLevelTitle = currentLevel?.title ?? "Не задан";
  const currentLevelGoal = currentLevel?.requiredExperiencePoints ?? 0;
  const nextLevelGoal = nextLevel?.requiredExperiencePoints ?? currentLevelGoal;
  const progressWithinLevel = nextLevel
    ? Math.max(enrollment.earnedExperiencePoints - currentLevelGoal, 0)
    : Math.max(enrollment.earnedExperiencePoints - currentLevelGoal, 0);
  const levelSpan = nextLevel ? Math.max(nextLevel.requiredExperiencePoints - currentLevelGoal, 1) : Math.max(progressWithinLevel, 1);
  const progressPercent = nextLevel ? Math.min(100, Math.max(0, (progressWithinLevel / levelSpan) * 100)) : 100;
  const currentLevelIndex = currentLevel
    ? sortedLevels.findIndex(
        (level) =>
          ("id" in currentLevel && level.localId === currentLevel.id) ||
          ("localId" in currentLevel && level.localId === currentLevel.localId),
      ) + 1
    : 0;

  useEffect(() => {
    const previousXp = previousXpRef.current;
    const gainedXp = enrollment.earnedExperiencePoints - previousXp;

    if (gainedXp > 0) {
      if (xpGainTimeoutRef.current != null) {
        window.clearTimeout(xpGainTimeoutRef.current);
      }

      setXpGain(gainedXp);
      setXpAnimating(true);

      xpGainTimeoutRef.current = window.setTimeout(() => {
        setXpAnimating(false);
        setXpGain(null);
        xpGainTimeoutRef.current = null;
      }, 1900);
    }

    previousXpRef.current = enrollment.earnedExperiencePoints;

    return () => {
      if (xpGainTimeoutRef.current != null) {
        window.clearTimeout(xpGainTimeoutRef.current);
        xpGainTimeoutRef.current = null;
      }
    };
  }, [enrollment.earnedExperiencePoints]);

  return (
    <div className={styles.progressOverlayCard}>
      <Typography.Text className={styles.progressOverlayEyebrow}>Прогресс по курсу</Typography.Text>
      <div className={styles.progressHero}>
        <div className={styles.progressLevelBadge}>
          <span className={styles.progressLevelBadgeLabel}>LVL</span>
          <strong>{currentLevelIndex > 0 ? currentLevelIndex : "?"}</strong>
        </div>
        <div className={styles.progressHeroBody}>
          <Typography.Title level={5} className={styles.progressOverlayTitle}>
            {currentLevelTitle}
          </Typography.Title>
          <Typography.Text type="secondary">{enrollment.clientDisplayName}</Typography.Text>
        </div>
      </div>
      <div className={styles.progressBarPanel}>
        <div className={styles.progressBarHeader}>
          <strong>{enrollment.earnedExperiencePoints} XP</strong>
          <span>{nextLevel ? `${String(nextLevel.requiredExperiencePoints)} XP` : "Максимум"}</span>
        </div>
        <div className={styles.progressBarTrack} aria-hidden="true">
          <div
            className={`${styles.progressBarFill} ${isXpAnimating ? styles.progressBarFillAnimated : ""}`}
            style={{ width: `${String(progressPercent)}%` }}
          />
        </div>
        {xpGain ? <div className={styles.xpGainBurst}>+{xpGain} XP</div> : null}
        <div className={styles.progressBarFooter}>
          {nextLevel ? <strong>{Math.max(nextLevelGoal - enrollment.earnedExperiencePoints, 0)} XP осталось</strong> : null}
        </div>
      </div>
    </div>
  );
}

function BlockNode({ data }: NodeProps<Node<BlockNodeData>>) {
  return (
    <div className={styles.blockNode}>
      <Handle id="block-target" type="target" position={Position.Left} className={styles.blockHandle} />
      <Typography.Title level={4} className={styles.blockNodeTitle}>
        {data.title || "Блок без названия"}
      </Typography.Title>
    </div>
  );
}

function BranchNode({ data }: NodeProps<Node<BranchNodeData>>) {
  return (
    <div className={styles.branchNode}>
      <Typography.Text strong>{data.title || "Ветка без названия"}</Typography.Text>
    </div>
  );
}

function TopicNode({ data }: NodeProps<Node<TopicNodeData>>) {
  return (
    <div
      className={`${styles.topicNode} ${data.isDependencyHighlighted ? styles.topicNodeDependencyHighlighted : ""} ${data.isDependencySourceHighlighted ? styles.topicNodeDependencySourceHighlighted : ""}`}
    >
      <Handle type="target" position={Position.Left} className={styles.topicHandle} />
      <Handle id="dependency-target" type="target" position={Position.Left} className={styles.dependencyTargetHandle} />
      <div className={styles.topicNodeText}>
        <Typography.Text strong>{data.title || "Тема без названия"}</Typography.Text>
        <span className={styles.topicBadges}>
          {data.progressState != null ? (
            <span className={`${styles.progressBadge} ${getProgressStateClassName(data.progressState)}`}>
              {getCourseThemeProgressStateLabel(data.progressState)}
            </span>
          ) : null}
          {data.dependencyCount > 0 ? (
            <button
              type="button"
              className={styles.dependencyBadge}
              aria-label="Подсветить зависимости темы"
              onClick={(event) => {
                event.stopPropagation();
                data.onHighlightDependencies?.(data.themeId, data.dependencyThemeIds);
              }}
              onPointerDown={(event) => {
                event.stopPropagation();
              }}
            >
              <LinkOutlined /> {data.dependencyCount}
            </button>
          ) : null}
        </span>
      </div>
      <Handle type="source" position={Position.Right} className={styles.topicHandle} />
      <Handle id="dependency-source" type="source" position={Position.Left} className={styles.dependencySourceHandle} />
    </div>
  );
}

function DiagramLegend() {
  return (
    <div className={styles.flowLegend}>
      <div className={styles.legendTitle}>Условные обозначения</div>
      <div className={styles.legendItem}>
        <span className={styles.legendStageSample} />
        <span>Блок курса</span>
      </div>
      <div className={styles.legendItem}>
        <span className={styles.legendBranchSample}>Ветка</span>
        <span>Ветка внутри блока</span>
      </div>
      <div className={styles.legendItem}>
        <span className={`${styles.legendLine} ${styles.legendLineSolid}`} />
        <span>Основной поток обучения</span>
      </div>
      <div className={styles.legendItem}>
        <span className={`${styles.legendLine} ${styles.legendLineDashed}`} />
        <span>Зависимость темы</span>
      </div>
      <div className={styles.legendItem}>
        <span className={styles.legendBridge} />
        <span>Пересечение стрелок без связи</span>
      </div>
    </div>
  );
}

function DiagramContextMenu({
  course,
  menu,
  onClose,
  onAdd,
  onEdit,
  onDelete,
}: {
  course: EditorCourse;
  menu: DiagramContextMenuState;
  onClose: () => void;
  onAdd: (intent: AddNodeIntent) => void;
  onEdit: (selection: DiagramNodeSelection) => void;
  onDelete: (selection: Exclude<DiagramNodeSelection, { kind: "course" }>) => void;
}) {
  const selected = getSelectedEditorNode(course, menu.selection);

  if (selected == null || selected.kind === "course") {
    return null;
  }

  return (
    <div
      className={styles.contextMenu}
      style={{ left: menu.x, top: menu.y }}
      role="menu"
      onContextMenu={(event) => {
        event.preventDefault();
      }}
    >
      <div className={styles.contextMenuTitle}>{getContextMenuTitle(selected)}</div>
      {selected.kind === "block" ? (
        <>
          <ContextMenuButton
            label="Создать блок"
            onClick={() => {
              onAdd({ kind: "block" });
            }}
          />
          <ContextMenuButton
            label="Создать ветку"
            onClick={() => {
              onAdd({ kind: "branch", blockId: selected.block.localId });
            }}
          />
          <ContextMenuButton
            label="Редактировать блок"
            onClick={() => {
              onEdit(menu.selection);
            }}
          />
          <ContextMenuButton
            danger
            label="Удалить блок"
            onClick={() => {
              onDelete({ kind: "block", blockId: selected.block.localId });
            }}
          />
        </>
      ) : null}
      {selected.kind === "branch" ? (
        <>
          <ContextMenuButton
            label="Создать тему"
            onClick={() => {
              onAdd({ kind: "theme", blockId: selected.block.localId, branchId: selected.branch.localId });
            }}
          />
          <ContextMenuButton
            label="Редактировать ветку"
            onClick={() => {
              onEdit(menu.selection);
            }}
          />
          <ContextMenuButton
            danger
            label="Удалить ветку"
            onClick={() => {
              onDelete({ kind: "branch", blockId: selected.block.localId, branchId: selected.branch.localId });
            }}
          />
        </>
      ) : null}
      {selected.kind === "theme" ? (
        <>
          <ContextMenuButton
            label="Создать тему слева"
            onClick={() => {
              onAdd({
                kind: "theme",
                blockId: selected.block.localId,
                branchId: selected.branch.localId,
                insertIndex: getThemeIndex(selected.branch, selected.theme.localId),
              });
            }}
          />
          <ContextMenuButton
            label="Создать тему справа"
            onClick={() => {
              onAdd({
                kind: "theme",
                blockId: selected.block.localId,
                branchId: selected.branch.localId,
                insertIndex: getThemeIndex(selected.branch, selected.theme.localId) + 1,
              });
            }}
          />
          <ContextMenuButton
            label="Редактировать тему"
            onClick={() => {
              onEdit(menu.selection);
            }}
          />
          <ContextMenuButton
            danger
            label="Удалить тему"
            onClick={() => {
              onDelete({
                kind: "theme",
                blockId: selected.block.localId,
                branchId: selected.branch.localId,
                themeId: selected.theme.localId,
              });
            }}
          />
        </>
      ) : null}
      <button type="button" className={styles.contextMenuClose} onClick={onClose} aria-label="Закрыть меню" />
    </div>
  );
}

function ContextMenuButton({ danger, label, onClick }: { danger?: boolean; label: string; onClick: () => void }) {
  return (
    <button type="button" className={`${styles.contextMenuItem} ${danger ? styles.contextMenuItemDanger : ""}`} onClick={onClick}>
      {label}
    </button>
  );
}

function getContextMenuTitle(selected: Exclude<SelectedEditorNode, { kind: "course" }>) {
  switch (selected.kind) {
    case "block":
      return selected.block.title || "Блок без названия";
    case "branch":
      return selected.branch.title || "Ветка без названия";
    case "theme":
      return selected.theme.title || "Тема без названия";
  }
}

function getThemeIndex(branch: EditorBranch, themeId: string) {
  return Math.max(
    branch.themes.findIndex((theme) => theme.localId === themeId),
    0,
  );
}

function CourseNodeModal({
  controller,
  selection,
  onClose,
  onAdd,
  onDelete,
}: {
  controller: Controller;
  selection: DiagramNodeSelection | null;
  onClose: () => void;
  onAdd: (intent: AddNodeIntent) => void;
  onDelete: (selection: Exclude<DiagramNodeSelection, { kind: "course" }>) => void;
}) {
  const course = controller.draftCourse;
  const [form] = Form.useForm<Record<string, unknown>>();
  const selected = useMemo(() => (course && selection ? getSelectedEditorNode(course, selection) : null), [course, selection]);
  const nodeDraft = useDurableForm({
    key: course && selection ? `draft:courses:node:${course.id}:${getSelectionDraftIdentity(selection)}` : null,
    schema: courseNodeDraftSchema,
    form,
    codec: courseNodeDraftCodec,
    enabled: Boolean(course && selection),
    entity: course ? { id: course.id } : undefined,
  });

  useEffect(() => {
    if (selected == null) {
      form.resetFields();
      return;
    }

    form.setFieldsValue(getSelectionFormValues(selected));
  }, [form, selected]);

  if (course == null || selection == null || selected == null) {
    return null;
  }

  const title = getModalTitle(selected);

  return (
    <Modal
      open
      width={selected.kind === "theme" || selected.kind === "course" ? 760 : 560}
      className={selected.kind === "theme" || selected.kind === "course" ? styles.themeEditorModal : undefined}
      title={<DraftModalTitle title={title} restored={nodeDraft.restored} saveStatus={nodeDraft.status} onRetry={nodeDraft.retry} />}
      onCancel={onClose}
      onOk={() => {
        form.submit();
      }}
      footer={(_, { OkBtn, CancelBtn }) => (
        <div className={styles.modalFooter}>
          <div className={styles.modalFooterSecondary}>
            {nodeDraft.hasDraft ? (
              <Button
                onClick={() =>
                  void nodeDraft.discard().then(() => {
                    form.setFieldsValue(getSelectionFormValues(selected));
                  })
                }
              >
                Отбросить черновик
              </Button>
            ) : null}
            {selected.kind === "block" ? (
              <Button
                icon={<PlusOutlined />}
                onClick={() => {
                  onClose();
                  onAdd({ kind: "branch", blockId: selected.block.localId });
                }}
              >
                Ветка
              </Button>
            ) : null}
            {selected.kind === "branch" ? (
              <Button
                icon={<PlusOutlined />}
                onClick={() => {
                  onClose();
                  onAdd({ kind: "theme", blockId: selected.block.localId, branchId: selected.branch.localId });
                }}
              >
                Тема
              </Button>
            ) : null}
            {selected.kind !== "course" ? (
              <Button
                danger
                icon={<DeleteOutlined />}
                onClick={() => {
                  onDelete(selection as Exclude<DiagramNodeSelection, { kind: "course" }>);
                }}
              >
                Удалить
              </Button>
            ) : null}
            {selected.kind === "course" ? (
              <Button danger icon={<DeleteOutlined />} loading={controller.deleteMutation.isPending} onClick={controller.confirmDelete}>
                Удалить курс
              </Button>
            ) : null}
          </div>
          <Space>
            <CancelBtn />
            <OkBtn />
          </Space>
        </div>
      )}
    >
      <Form
        form={form}
        layout="vertical"
        onValuesChange={nodeDraft.formProps.onValuesChange}
        onFinish={() => {
          applySelectionValues(controller, selected, form.getFieldsValue(true) as Record<string, unknown>);
          void nodeDraft.clearAfterSuccess();
          onClose();
        }}
      >
        {selected.kind === "course" ? (
          <>
            <Form.Item name="name" label="Название курса" rules={[{ required: true, message: "Укажите название курса" }]}>
              <Input />
            </Form.Item>
            <Form.Item name="description" label="Описание">
              <Input.TextArea autoSize={{ minRows: 2, maxRows: 5 }} />
            </Form.Item>
            <CourseLevelsEditor course={selected.course} controller={controller} />
          </>
        ) : null}

        {selected.kind === "block" ? (
          <>
            <Form.Item name="title" label="Название блока" rules={[{ required: true, message: "Укажите название блока" }]}>
              <Input />
            </Form.Item>
            <Form.Item name="description" label="Описание">
              <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} />
            </Form.Item>
            <MoveButtons
              onUp={() => {
                controller.moveBlock(selected.block.localId, "up");
              }}
              onDown={() => {
                controller.moveBlock(selected.block.localId, "down");
              }}
            />
          </>
        ) : null}

        {selected.kind === "branch" ? (
          <>
            <Form.Item name="title" label="Название ветки" rules={[{ required: true, message: "Укажите название ветки" }]}>
              <Input />
            </Form.Item>
            <Form.Item name="description" label="Описание">
              <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} />
            </Form.Item>
            <MoveButtons
              onUp={() => {
                controller.moveBranch(selected.block.localId, selected.branch.localId, "up");
              }}
              onDown={() => {
                controller.moveBranch(selected.block.localId, selected.branch.localId, "down");
              }}
            />
          </>
        ) : null}

        {selected.kind === "theme" ? (
          <>
            <Form.Item name="title" label="Название темы" rules={[{ required: true, message: "Укажите название темы" }]}>
              <Input />
            </Form.Item>
            <Form.Item name="description" label="Описание">
              <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} />
            </Form.Item>
            <Form.Item name="dependencyKeys" label="Зависимости">
              <Select mode="multiple" options={buildDependencyOptions(selected.block, selected.theme.localId)} />
            </Form.Item>
            <div className={styles.numberGridSingle}>
              <Form.Item name="experiencePointsReward" label="Очки опыта">
                <InputNumber min={0} className="wide" />
              </Form.Item>
            </div>
            <MoveButtons
              onUp={() => {
                controller.moveTheme(selected.block.localId, selected.branch.localId, selected.theme.localId, "up");
              }}
              onDown={() => {
                controller.moveTheme(selected.block.localId, selected.branch.localId, selected.theme.localId, "down");
              }}
            />
            <Form.Item noStyle shouldUpdate>
              {() => (
                <Space orientation="vertical" size={16} className="wide">
                  <BbcodeEditor
                    label="Текст занятия"
                    value={readFormString(form.getFieldValue("lessonContent"))}
                    onChange={(value) => {
                      form.setFieldValue("lessonContent", value);
                      nodeDraft.formProps.onValuesChange?.({ lessonContent: value }, form.getFieldsValue());
                    }}
                    helper="Используйте BBCode для форматирования текста, списков, ссылок, цитат и вставок кода."
                  />
                  <BbcodeEditor
                    label="Домашнее задание"
                    value={readFormString(form.getFieldValue("homeworkContent"))}
                    onChange={(value) => {
                      form.setFieldValue("homeworkContent", value);
                      nodeDraft.formProps.onValuesChange?.({ homeworkContent: value }, form.getFieldsValue());
                    }}
                    helper="Используйте BBCode для форматирования текста, списков, ссылок, цитат и вставок кода."
                  />
                </Space>
              )}
            </Form.Item>
          </>
        ) : null}
      </Form>
    </Modal>
  );
}

function ProgressNodeModal({
  course,
  enrollment,
  selection,
  isUpdating,
  onProgressAction,
  onClose,
}: {
  course: EditorCourse;
  enrollment: CourseEnrollment;
  selection: DiagramNodeSelection | null;
  isUpdating: boolean;
  onProgressAction: (themeId: Ulid, action: CourseEnrollmentThemeProgressAction) => void;
  onClose: () => void;
}) {
  const selected = useMemo(() => (selection ? getSelectedEditorNode(course, selection) : null), [course, selection]);

  if (selected == null) {
    return null;
  }

  const enrollmentThemesByCourseThemeId = new Map(enrollment.themes.map((theme) => [theme.courseThemeId, theme]));
  const title =
    selected.kind === "block"
      ? "Прогресс блока"
      : selected.kind === "branch"
        ? "Прогресс ветки"
        : selected.kind === "theme"
          ? "Прогресс темы"
          : "Прогресс курса";

  return (
    <Modal open width={selected.kind === "theme" ? 720 : 560} title={title} onCancel={onClose} footer={null}>
      {selected.kind === "block" ? (
        <ProgressSummary
          course={course}
          block={selected.block}
          title={selected.block.title || "Блок без названия"}
          themes={selected.block.branches.flatMap((branch) => branch.themes)}
          enrollmentThemesByCourseThemeId={enrollmentThemesByCourseThemeId}
        />
      ) : null}

      {selected.kind === "branch" ? (
        <ProgressSummary
          course={course}
          block={selected.block}
          title={selected.branch.title || "Ветка без названия"}
          themes={selected.branch.themes}
          enrollmentThemesByCourseThemeId={enrollmentThemesByCourseThemeId}
        />
      ) : null}

      {selected.kind === "theme" ? (
        <ThemeProgressDetails
          course={course}
          block={selected.block}
          branch={selected.branch}
          theme={selected.theme}
          enrollmentTheme={enrollmentThemesByCourseThemeId.get(selected.theme.localId) ?? null}
          enrollmentThemesByCourseThemeId={enrollmentThemesByCourseThemeId}
          isUpdating={isUpdating}
          onProgressAction={onProgressAction}
        />
      ) : null}
    </Modal>
  );
}

function ProgressSummary({
  course,
  block,
  title,
  themes,
  enrollmentThemesByCourseThemeId,
}: {
  course: EditorCourse;
  block: EditorBlock;
  title: string;
  themes: EditorTheme[];
  enrollmentThemesByCourseThemeId: Map<string, CourseEnrollmentTheme>;
}) {
  const progressStates = themes
    .map((theme) => {
      const branch = block.branches.find((item) => item.themes.some((branchTheme) => branchTheme.localId === theme.localId));

      return branch ? resolveEffectiveProgressState(course, block, branch, theme, enrollmentThemesByCourseThemeId) : null;
    })
    .filter((state): state is CourseThemeProgressState => state != null);
  const completedCount = progressStates.filter((state) => state === 5).length;
  const inProgressCount = progressStates.filter((state) => state === 3 || state === 4).length;
  const blockedCount = progressStates.filter((state) => state === 0).length;

  return (
    <Space orientation="vertical" size={14} className="wide">
      <Typography.Title level={4} style={{ margin: 0 }}>
        {title}
      </Typography.Title>
      <Space wrap>
        <Tag color="green">Завершено: {completedCount}</Tag>
        <Tag color="processing">В работе: {inProgressCount}</Tag>
        <Tag>Заблокировано: {blockedCount}</Tag>
        <Tag>Всего тем: {progressStates.length}</Tag>
      </Space>
      <div className={styles.progressThemeList}>
        {themes.map((theme) => {
          const enrollmentTheme = enrollmentThemesByCourseThemeId.get(theme.localId);
          const branch = block.branches.find((item) => item.themes.some((branchTheme) => branchTheme.localId === theme.localId));
          const effectiveState = branch
            ? resolveEffectiveProgressState(course, block, branch, theme, enrollmentThemesByCourseThemeId)
            : enrollmentTheme?.state;

          return (
            <div key={theme.localId} className={styles.progressThemeRow}>
              <Typography.Text>{theme.title || "Тема без названия"}</Typography.Text>
              {effectiveState != null ? (
                <Tag color={getCourseThemeProgressStateTagColor(effectiveState)}>{getCourseThemeProgressStateLabel(effectiveState)}</Tag>
              ) : (
                <Tag>Нет прогресса</Tag>
              )}
            </div>
          );
        })}
      </div>
    </Space>
  );
}

function ThemeProgressDetails({
  course,
  block,
  branch,
  theme,
  enrollmentTheme,
  enrollmentThemesByCourseThemeId,
  isUpdating,
  onProgressAction,
}: {
  course: EditorCourse;
  block: EditorBlock;
  branch: EditorBranch;
  theme: EditorTheme;
  enrollmentTheme: CourseEnrollmentTheme | null;
  enrollmentThemesByCourseThemeId: Map<string, CourseEnrollmentTheme>;
  isUpdating: boolean;
  onProgressAction: (themeId: Ulid, action: CourseEnrollmentThemeProgressAction) => void;
}) {
  const canCompleteTheme = isThemeEligibleForCompletion(course, block, branch, theme, enrollmentThemesByCourseThemeId);
  const effectiveState = resolveEffectiveProgressState(course, block, branch, theme, enrollmentThemesByCourseThemeId);

  return (
    <Space orientation="vertical" size={16} className="wide">
      <div className={styles.detailSection}>
        <Typography.Text type="secondary">
          {block.title || "Блок без названия"} → {branch.title || "Ветка без названия"}
        </Typography.Text>
        <Typography.Title level={4} style={{ margin: 0 }}>
          {theme.title || "Тема без названия"}
        </Typography.Title>
        <Space wrap>
          {effectiveState != null ? (
            <Tag color={getCourseThemeProgressStateTagColor(effectiveState)}>{getCourseThemeProgressStateLabel(effectiveState)}</Tag>
          ) : (
            <Tag>Нет прогресса</Tag>
          )}
          <Tag color="purple">Опыт: +{theme.experiencePointsReward}</Tag>
        </Space>
        {theme.description ? <Typography.Paragraph className={styles.contentText}>{theme.description}</Typography.Paragraph> : null}
      </div>

      {enrollmentTheme ? (
        <Space wrap>
          {effectiveState != null
            ? getAvailableProgressActions(effectiveState).map((action) => (
                <Button
                  key={action.action}
                  type={action.primary ? "primary" : "default"}
                  loading={isUpdating}
                  disabled={action.action === "pass-homework" && !canCompleteTheme}
                  title={
                    action.action === "pass-homework" && !canCompleteTheme ? "Сначала завершите предыдущие темы и зависимости." : undefined
                  }
                  onClick={() => {
                    onProgressAction(enrollmentTheme.id, action.action);
                  }}
                >
                  {action.label}
                </Button>
              ))
            : null}
        </Space>
      ) : null}

      <div className={styles.detailSection}>
        <Typography.Text strong>Материал урока</Typography.Text>
        {theme.lessonContent ? (
          <BbcodeContent value={theme.lessonContent} />
        ) : (
          <Typography.Text type="secondary">Материал урока пока не заполнен.</Typography.Text>
        )}
      </div>

      <div className={styles.detailSection}>
        <Typography.Text strong>Домашнее задание</Typography.Text>
        {theme.homeworkContent ? (
          <BbcodeContent value={theme.homeworkContent} />
        ) : (
          <Typography.Text type="secondary">Домашнее задание пока не заполнено.</Typography.Text>
        )}
      </div>
    </Space>
  );
}

function AddNodeModal({ controller, intent, onClose }: { controller: Controller; intent: AddNodeIntent | null; onClose: () => void }) {
  const [form] = Form.useForm<Record<string, unknown>>();
  const addDraft = useDurableForm({
    key: intent && controller.draftCourse ? `draft:courses:add:${controller.draftCourse.id}:${getAddIntentDraftIdentity(intent)}` : null,
    schema: courseNodeDraftSchema,
    form,
    codec: courseNodeDraftCodec,
    enabled: Boolean(intent && controller.draftCourse),
    entity: controller.draftCourse ? { id: controller.draftCourse.id } : undefined,
  });

  if (intent == null || controller.draftCourse == null) {
    return null;
  }

  const title = intent.kind === "block" ? "Добавить блок" : intent.kind === "branch" ? "Добавить ветку в блок" : "Добавить тему в ветку";
  const targetBlock = intent.kind === "theme" ? controller.draftCourse.blocks.find((block) => block.localId === intent.blockId) : null;

  return (
    <DraftFormModal
      open
      width={intent.kind === "theme" ? 760 : 560}
      className={intent.kind === "theme" ? styles.themeEditorModal : undefined}
      title={title}
      restored={addDraft.restored}
      saveStatus={addDraft.status}
      showClearDraft={addDraft.hasDraft}
      onClearDraft={() => {
        void addDraft.discard().then(() => {
          form.resetFields();
        });
      }}
      onRetryDraft={addDraft.retry}
      onCancel={onClose}
      onOk={() => {
        form.submit();
      }}
    >
      <Form
        form={form}
        layout="vertical"
        onValuesChange={addDraft.formProps.onValuesChange}
        onFinish={() => {
          addEditorNode(controller, intent, form.getFieldsValue(true) as Record<string, unknown>);
          void addDraft.clearAfterSuccess();
          onClose();
        }}
      >
        {intent.kind === "block" ? (
          <>
            <Form.Item name="title" label="Название блока" rules={[{ required: true, message: "Укажите название блока" }]}>
              <Input />
            </Form.Item>
            <Form.Item name="description" label="Описание">
              <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} />
            </Form.Item>
          </>
        ) : null}

        {intent.kind === "branch" ? (
          <>
            <Form.Item name="title" label="Название ветки" rules={[{ required: true, message: "Укажите название ветки" }]}>
              <Input />
            </Form.Item>
            <Form.Item name="description" label="Описание">
              <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} />
            </Form.Item>
          </>
        ) : null}

        {intent.kind === "theme" ? (
          <>
            <Form.Item name="title" label="Название темы" rules={[{ required: true, message: "Укажите название темы" }]}>
              <Input />
            </Form.Item>
            <Form.Item name="description" label="Описание">
              <Input.TextArea autoSize={{ minRows: 2, maxRows: 4 }} />
            </Form.Item>
            <Form.Item name="dependencyKeys" label="Зависимости">
              <Select mode="multiple" options={targetBlock ? buildDependencyOptions(targetBlock, "") : []} />
            </Form.Item>
            <div className={styles.numberGridSingle}>
              <Form.Item name="experiencePointsReward" label="Очки опыта">
                <InputNumber min={0} className="wide" />
              </Form.Item>
            </div>
            <Form.Item noStyle shouldUpdate>
              {() => (
                <Space orientation="vertical" size={16} className="wide">
                  <BbcodeEditor
                    label="Текст занятия"
                    value={readFormString(form.getFieldValue("lessonContent"))}
                    onChange={(value) => {
                      form.setFieldValue("lessonContent", value);
                      addDraft.formProps.onValuesChange?.({ lessonContent: value }, form.getFieldsValue());
                    }}
                    helper="Используйте BBCode для форматирования текста, списков, ссылок, цитат и вставок кода."
                  />
                  <BbcodeEditor
                    label="Домашнее задание"
                    value={readFormString(form.getFieldValue("homeworkContent"))}
                    onChange={(value) => {
                      form.setFieldValue("homeworkContent", value);
                      addDraft.formProps.onValuesChange?.({ homeworkContent: value }, form.getFieldsValue());
                    }}
                    helper="Используйте BBCode для форматирования текста, списков, ссылок, цитат и вставок кода."
                  />
                </Space>
              )}
            </Form.Item>
          </>
        ) : null}
      </Form>
    </DraftFormModal>
  );
}

function getSelectionDraftIdentity(selection: DiagramNodeSelection) {
  switch (selection.kind) {
    case "course":
      return "course";
    case "block":
      return `block:${selection.blockId}`;
    case "branch":
      return `branch:${selection.blockId}:${selection.branchId}`;
    case "theme":
      return `theme:${selection.blockId}:${selection.branchId}:${selection.themeId}`;
  }
}

function getAddIntentDraftIdentity(intent: AddNodeIntent) {
  switch (intent.kind) {
    case "block":
      return "block";
    case "branch":
      return `branch:${intent.blockId}`;
    case "theme":
      return `theme:${intent.blockId}:${intent.branchId}`;
  }
}

function MoveButtons({ onUp, onDown }: { onUp: () => void; onDown: () => void }) {
  return (
    <div className={styles.moveButtons}>
      <Button icon={<UpOutlined />} onClick={onUp}>
        Выше
      </Button>
      <Button icon={<DownOutlined />} onClick={onDown}>
        Ниже
      </Button>
    </div>
  );
}

function CourseLevelsEditor({ course, controller }: { course: EditorCourse; controller: Controller }) {
  return (
    <div className={styles.detailSection}>
      <div className={styles.listJustify}>
        <Typography.Text strong>Уровни курса</Typography.Text>
        <Button
          icon={<PlusOutlined />}
          onClick={() => {
            controller.addLevel({
              title: "",
              requiredExperiencePoints: getNextLevelThreshold(course.levels),
            });
          }}
        >
          Уровень
        </Button>
      </div>
      <Typography.Text type="secondary">
        Уровень определяется автоматически по сумме опыта. Настройте названия уровней и пороги опыта для этого курса.
      </Typography.Text>
      {course.levels.length === 0 ? (
        <Alert
          type="info"
          showIcon
          title="Уровни пока не настроены"
          description="Клиенты будут получать опыт, но название уровня определяться не будет."
        />
      ) : (
        <div className={styles.levelList}>
          {course.levels.map((level, index) => (
            <CourseLevelRow
              key={level.localId}
              level={level}
              index={index}
              canMoveUp={index > 0}
              canMoveDown={index < course.levels.length - 1}
              onChange={(patch) => {
                controller.updateLevel(level.localId, patch);
              }}
              onMove={(direction) => {
                controller.moveLevel(level.localId, direction);
              }}
              onRemove={() => {
                controller.removeLevel(level.localId);
              }}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function CourseLevelRow({
  level,
  index,
  canMoveUp,
  canMoveDown,
  onChange,
  onMove,
  onRemove,
}: {
  level: EditorLevel;
  index: number;
  canMoveUp: boolean;
  canMoveDown: boolean;
  onChange: (patch: Partial<Omit<EditorLevel, "localId">>) => void;
  onMove: (direction: "up" | "down") => void;
  onRemove: () => void;
}) {
  return (
    <div className={styles.levelRow}>
      <Tag variant="filled" color="gold">
        {index + 1}
      </Tag>
      <Input
        value={level.title}
        placeholder="Название уровня"
        onChange={(event) => {
          onChange({ title: event.target.value });
        }}
      />
      <Space.Compact className={styles.levelThresholdInput}>
        <Input value="XP" readOnly aria-label="Единица измерения" />
        <InputNumber
          min={0}
          value={level.requiredExperiencePoints}
          onChange={(value) => {
            onChange({ requiredExperiencePoints: typeof value === "number" ? value : 0 });
          }}
        />
      </Space.Compact>
      <Space>
        <Button
          icon={<UpOutlined />}
          disabled={!canMoveUp}
          onClick={() => {
            onMove("up");
          }}
        />
        <Button
          icon={<DownOutlined />}
          disabled={!canMoveDown}
          onClick={() => {
            onMove("down");
          }}
        />
        <Button danger icon={<DeleteOutlined />} aria-label="Удалить уровень курса" title="Удалить" onClick={onRemove} />
      </Space>
    </div>
  );
}

function layoutEditorCourse(
  course: EditorCourse,
  enrollment: CourseEnrollment | null,
): {
  nodes: Array<Node<NodeData & { selection: DiagramNodeSelection }>>;
  edges: Edge[];
} {
  const nodes: Array<Node<NodeData & { selection: DiagramNodeSelection }>> = [];
  const edges: Edge[] = [];
  const themePositionById = new Map<string, { blockId: string; themeId: string; rect: DiagramRect }>();
  const themeRectById = new Map<string, DiagramRect>();
  const nodeRects: DiagramRect[] = [];
  const protectedSegments: Array<[DiagramPoint, DiagramPoint]> = [];
  const enrollmentThemesByCourseThemeId = new Map((enrollment?.themes ?? []).map((theme) => [theme.courseThemeId, theme]));
  let currentX = canvasInset;

  for (const block of course.blocks) {
    const branches = block.branches.length > 0 ? block.branches : [null];
    const rowHeight = branchLabelHeight + branchLabelToTopicGap + topicNodeHeight;
    const blockHeight = Math.max(branches.length * rowHeight + Math.max(branches.length - 1, 0) * branchRowGap, 180);
    const branchMaxTopics = Math.max(1, ...block.branches.map((branch) => Math.max(branch.themes.length, 1)));
    const branchWidth = branchMaxTopics * topicNodeWidth + Math.max(branchMaxTopics - 1, 0) * topicGap;
    const firstTopicX = currentX + blockNodeWidth + blockToBranchGap;

    nodes.push({
      id: `block:${block.localId}`,
      type: "block",
      position: { x: currentX, y: canvasInset },
      selectable: true,
      draggable: false,
      style: { width: blockNodeWidth, height: blockHeight },
      data: {
        title: block.title,
        selection: { kind: "block", blockId: block.localId },
      },
    });
    nodeRects.push({
      id: `block:${block.localId}`,
      x: currentX,
      y: canvasInset,
      width: blockNodeWidth,
      height: blockHeight,
    });

    for (const [branchIndex, maybeBranch] of branches.entries()) {
      if (maybeBranch == null) {
        continue;
      }

      const branch = maybeBranch;
      const rowY = canvasInset + branchIndex * (rowHeight + branchRowGap);
      nodes.push({
        id: `branch:${branch.localId}`,
        type: "branch",
        position: { x: firstTopicX, y: rowY },
        selectable: true,
        draggable: false,
        style: { width: branchLabelWidth, height: branchLabelHeight },
        data: {
          title: branch.title,
          selection: { kind: "branch", blockId: block.localId, branchId: branch.localId },
        },
      });
      nodeRects.push({
        id: `branch:${branch.localId}`,
        x: firstTopicX,
        y: rowY,
        width: branchLabelWidth,
        height: branchLabelHeight,
      });

      for (const [themeIndex, theme] of branch.themes.entries()) {
        const themeX = firstTopicX + themeIndex * (topicNodeWidth + topicGap);
        const themeY = rowY + branchLabelHeight + branchLabelToTopicGap;
        const themeRect = {
          id: `theme:${theme.localId}`,
          x: themeX,
          y: themeY,
          width: topicNodeWidth,
          height: topicNodeHeight,
        };
        themePositionById.set(theme.localId, { blockId: block.localId, themeId: theme.localId, rect: themeRect });
        themeRectById.set(theme.localId, themeRect);
        nodeRects.push(themeRect);
        nodes.push({
          id: `theme:${theme.localId}`,
          type: "topic",
          position: { x: themeX, y: themeY },
          selectable: true,
          draggable: false,
          style: { width: topicNodeWidth, height: topicNodeHeight },
          data: {
            themeId: theme.localId,
            title: theme.title,
            dependencyCount: theme.dependencyKeys.length,
            dependencyThemeIds: theme.dependencyKeys,
            progressState: resolveEffectiveProgressState(course, block, branch, theme, enrollmentThemesByCourseThemeId),
            selection: { kind: "theme", blockId: block.localId, branchId: branch.localId, themeId: theme.localId },
          },
        });

        if (themeIndex > 0) {
          const previousTheme = branch.themes[themeIndex - 1];
          const previousThemeRect = themeRectById.get(previousTheme.localId);
          if (previousThemeRect) {
            const sequencePoints = [getAnchorPoint(previousThemeRect, "right"), getAnchorPoint(themeRect, "left")];
            edges.push(createSequenceEdge(`theme:${previousTheme.localId}`, `theme:${theme.localId}`, sequencePoints));
            protectedSegments.push([sequencePoints[0], sequencePoints[1]]);
          }
        }
      }
    }

    currentX = firstTopicX + branchWidth + blockGap;
  }

  for (const block of course.blocks) {
    const blockThemeIds = new Set(block.branches.flatMap((branch) => branch.themes.map((theme) => theme.localId)));
    for (const branch of block.branches) {
      for (const theme of branch.themes) {
        for (const dependencyThemeId of theme.dependencyKeys) {
          const dependency = themePositionById.get(dependencyThemeId);
          if (dependency == null || dependency.blockId !== block.localId || !blockThemeIds.has(dependency.themeId)) {
            continue;
          }

          const target = themePositionById.get(theme.localId);
          if (!target) {
            continue;
          }

          const dependencyPoints = routeDependencyEdge(dependency.rect, target.rect, nodeRects, protectedSegments);
          const dependencyBridges = getRouteBridges(dependencyPoints, protectedSegments);
          protectedSegments.push(...pointsToSegments(dependencyPoints));

          edges.push({
            id: `dependency-${dependency.themeId}-${theme.localId}`,
            source: `theme:${dependency.themeId}`,
            target: `theme:${theme.localId}`,
            type: "dependency",
            markerEnd: { type: MarkerType.ArrowClosed, color: "var(--course-diagram-dependency-line)" },
            data: {
              points: dependencyPoints,
              bridges: dependencyBridges,
            } satisfies DiagramEdgeData,
            style: {
              stroke: "var(--course-diagram-dependency-line)",
              strokeWidth: 1.35,
              strokeDasharray: "6 5",
            },
          });
        }
      }
    }
  }

  return { nodes, edges };
}

function createSequenceEdge(source: string, target: string, points: DiagramPoint[]): Edge {
  return {
    id: `sequence-${source}-${target}`,
    source,
    target,
    type: "sequence",
    markerEnd: { type: MarkerType.ArrowClosed, color: "var(--course-diagram-gold-line)" },
    data: {
      points,
    } satisfies DiagramEdgeData,
    style: {
      stroke: "var(--course-diagram-gold-line)",
      strokeWidth: 1.8,
    },
  };
}

type AnchorSide = "left" | "right" | "top" | "bottom";

function routeDependencyEdge(
  sourceRect: DiagramRect,
  targetRect: DiagramRect,
  nodeRects: DiagramRect[],
  protectedSegments: Array<[DiagramPoint, DiagramPoint]>,
) {
  const candidates: DiagramPoint[][] = [];
  const sides: AnchorSide[] = ["left", "right", "top", "bottom"];

  for (const sourceSide of sides) {
    for (const targetSide of sides) {
      const source = getAnchorPoint(sourceRect, sourceSide);
      const target = getAnchorPoint(targetRect, targetSide);
      const sourceOut = moveOutward(source, sourceSide, 42);
      const targetOut = moveOutward(target, targetSide, 42);
      const midX = sourceOut.x + (targetOut.x - sourceOut.x) / 2;
      const midY = sourceOut.y + (targetOut.y - sourceOut.y) / 2;

      candidates.push(compactRoute([source, sourceOut, { x: midX, y: sourceOut.y }, { x: midX, y: targetOut.y }, targetOut, target]));
      candidates.push(compactRoute([source, sourceOut, { x: sourceOut.x, y: midY }, { x: targetOut.x, y: midY }, targetOut, target]));
      candidates.push(compactRoute([source, sourceOut, { x: sourceOut.x, y: targetOut.y }, targetOut, target]));
      candidates.push(compactRoute([source, sourceOut, { x: targetOut.x, y: sourceOut.y }, targetOut, target]));
    }
  }

  return (
    candidates
      .map((points) => ({
        points,
        score: scoreRoute(points, sourceRect, targetRect, nodeRects, protectedSegments),
      }))
      .sort((left, right) => left.score - right.score)[0]?.points ?? [
      getAnchorPoint(sourceRect, "right"),
      getAnchorPoint(targetRect, "left"),
    ]
  );
}

function getAnchorPoint(rect: DiagramRect, side: AnchorSide): DiagramPoint {
  switch (side) {
    case "left":
      return { x: rect.x, y: rect.y + rect.height / 2 };
    case "right":
      return { x: rect.x + rect.width, y: rect.y + rect.height / 2 };
    case "top":
      return { x: rect.x + rect.width / 2, y: rect.y };
    case "bottom":
      return { x: rect.x + rect.width / 2, y: rect.y + rect.height };
  }
}

function moveOutward(point: DiagramPoint, side: AnchorSide, offset: number): DiagramPoint {
  switch (side) {
    case "left":
      return { x: point.x - offset, y: point.y };
    case "right":
      return { x: point.x + offset, y: point.y };
    case "top":
      return { x: point.x, y: point.y - offset };
    case "bottom":
      return { x: point.x, y: point.y + offset };
  }
}

function compactRoute(points: DiagramPoint[]) {
  return points.filter((point, index) => {
    if (index === 0) {
      return true;
    }

    const previous = points[index - 1];
    return previous.x !== point.x || previous.y !== point.y;
  });
}

function scoreRoute(
  points: DiagramPoint[],
  sourceRect: DiagramRect,
  targetRect: DiagramRect,
  nodeRects: DiagramRect[],
  protectedSegments: Array<[DiagramPoint, DiagramPoint]>,
) {
  let score = getRouteLength(points);
  const obstacles = nodeRects.filter((rect) => rect.id !== sourceRect.id && rect.id !== targetRect.id);
  const candidateSegments = pointsToSegments(points);

  for (const [from, to] of candidateSegments) {
    if (from.x !== to.x && from.y !== to.y) {
      score += 2000;
    }

    for (const obstacle of obstacles) {
      if (segmentIntersectsRect(from, to, expandRect(obstacle, 10))) {
        score += 10000;
      }
    }

    for (const [protectedFrom, protectedTo] of protectedSegments) {
      if (segmentsIntersect(from, to, protectedFrom, protectedTo)) {
        score += 1600;
      }
    }
  }

  return score + countRouteBends(points) * 90;
}

function pointsToSegments(points: DiagramPoint[]): Array<[DiagramPoint, DiagramPoint]> {
  const segments: Array<[DiagramPoint, DiagramPoint]> = [];

  for (let index = 1; index < points.length; index += 1) {
    segments.push([points[index - 1], points[index]]);
  }

  return segments;
}

function getRouteLength(points: DiagramPoint[]) {
  return points.reduce((total, point, index) => {
    if (index === 0) {
      return total;
    }

    const previous = points[index - 1];
    return total + Math.abs(point.x - previous.x) + Math.abs(point.y - previous.y);
  }, 0);
}

function countRouteBends(points: DiagramPoint[]) {
  let bends = 0;

  for (let index = 2; index < points.length; index += 1) {
    const first = points[index - 2];
    const second = points[index - 1];
    const third = points[index];
    const firstHorizontal = first.y === second.y;
    const secondHorizontal = second.y === third.y;

    if (firstHorizontal !== secondHorizontal) {
      bends += 1;
    }
  }

  return bends;
}

function expandRect(rect: DiagramRect, padding: number): DiagramRect {
  return {
    ...rect,
    x: rect.x - padding,
    y: rect.y - padding,
    width: rect.width + padding * 2,
    height: rect.height + padding * 2,
  };
}

function segmentIntersectsRect(from: DiagramPoint, to: DiagramPoint, rect: DiagramRect) {
  const segmentLeft = Math.min(from.x, to.x);
  const segmentRight = Math.max(from.x, to.x);
  const segmentTop = Math.min(from.y, to.y);
  const segmentBottom = Math.max(from.y, to.y);
  const rectRight = rect.x + rect.width;
  const rectBottom = rect.y + rect.height;

  if (from.y === to.y) {
    return from.y >= rect.y && from.y <= rectBottom && segmentRight >= rect.x && segmentLeft <= rectRight;
  }

  if (from.x === to.x) {
    return from.x >= rect.x && from.x <= rectRight && segmentBottom >= rect.y && segmentTop <= rectBottom;
  }

  return segmentRight >= rect.x && segmentLeft <= rectRight && segmentBottom >= rect.y && segmentTop <= rectBottom;
}

function segmentsIntersect(firstFrom: DiagramPoint, firstTo: DiagramPoint, secondFrom: DiagramPoint, secondTo: DiagramPoint) {
  const firstLeft = Math.min(firstFrom.x, firstTo.x);
  const firstRight = Math.max(firstFrom.x, firstTo.x);
  const firstTop = Math.min(firstFrom.y, firstTo.y);
  const firstBottom = Math.max(firstFrom.y, firstTo.y);
  const secondLeft = Math.min(secondFrom.x, secondTo.x);
  const secondRight = Math.max(secondFrom.x, secondTo.x);
  const secondTop = Math.min(secondFrom.y, secondTo.y);
  const secondBottom = Math.max(secondFrom.y, secondTo.y);

  if (firstRight < secondLeft || secondRight < firstLeft || firstBottom < secondTop || secondBottom < firstTop) {
    return false;
  }

  const firstHorizontal = firstFrom.y === firstTo.y;
  const secondHorizontal = secondFrom.y === secondTo.y;

  if (firstHorizontal && secondHorizontal) {
    return firstFrom.y === secondFrom.y;
  }

  if (!firstHorizontal && !secondHorizontal) {
    return firstFrom.x === secondFrom.x;
  }

  return true;
}

function getRouteBridges(points: DiagramPoint[], protectedSegments: Array<[DiagramPoint, DiagramPoint]>): DiagramBridge[] {
  const bridges: DiagramBridge[] = [];

  for (const [from, to] of pointsToSegments(points)) {
    const orientation = from.y === to.y ? "horizontal" : from.x === to.x ? "vertical" : null;
    if (orientation == null) {
      continue;
    }

    for (const [protectedFrom, protectedTo] of protectedSegments) {
      const crossing = getPerpendicularCrossing(from, to, protectedFrom, protectedTo);
      if (crossing == null || isNearSegmentEndpoint(crossing, from, to) || isNearSegmentEndpoint(crossing, protectedFrom, protectedTo)) {
        continue;
      }

      bridges.push({ point: crossing, orientation });
    }
  }

  return bridges;
}

function getPerpendicularCrossing(
  firstFrom: DiagramPoint,
  firstTo: DiagramPoint,
  secondFrom: DiagramPoint,
  secondTo: DiagramPoint,
): DiagramPoint | null {
  const firstHorizontal = firstFrom.y === firstTo.y;
  const secondHorizontal = secondFrom.y === secondTo.y;

  if (firstHorizontal === secondHorizontal) {
    return null;
  }

  const horizontalFrom = firstHorizontal ? firstFrom : secondFrom;
  const horizontalTo = firstHorizontal ? firstTo : secondTo;
  const verticalFrom = firstHorizontal ? secondFrom : firstFrom;
  const verticalTo = firstHorizontal ? secondTo : firstTo;
  const x = verticalFrom.x;
  const y = horizontalFrom.y;

  if (
    x < Math.min(horizontalFrom.x, horizontalTo.x) ||
    x > Math.max(horizontalFrom.x, horizontalTo.x) ||
    y < Math.min(verticalFrom.y, verticalTo.y) ||
    y > Math.max(verticalFrom.y, verticalTo.y)
  ) {
    return null;
  }

  return { x, y };
}

function isNearSegmentEndpoint(point: DiagramPoint, from: DiagramPoint, to: DiagramPoint) {
  const endpointPadding = 12;

  return (
    (Math.abs(point.x - from.x) <= endpointPadding && Math.abs(point.y - from.y) <= endpointPadding) ||
    (Math.abs(point.x - to.x) <= endpointPadding && Math.abs(point.y - to.y) <= endpointPadding)
  );
}

function DiagramEdge({ markerEnd, style, data }: EdgeProps<Edge<DiagramEdgeData>>) {
  const points = data?.points ?? [];
  const path = points.length > 0 ? pointsToSvgPath(points, data?.bridges ?? []) : "";
  return (
    <BaseEdge
      path={path}
      markerEnd={markerEnd}
      style={{ ...style, stroke: style?.stroke }}
      className={data?.isHighlighted ? styles.dependencyEdgeHighlighted : undefined}
    />
  );
}

function pointsToSvgPath(points: DiagramPoint[], bridges: DiagramBridge[] = []) {
  const [firstPoint] = points;
  const commands = [`M ${String(firstPoint.x)},${String(firstPoint.y)}`];

  for (const [from, to] of pointsToSegments(points)) {
    const segmentBridges = getSegmentBridges(from, to, bridges);

    if (segmentBridges.length === 0) {
      commands.push(`L ${String(to.x)},${String(to.y)}`);
      continue;
    }

    for (const bridge of segmentBridges) {
      appendBridgeCommands(commands, from, to, bridge);
    }

    commands.push(`L ${String(to.x)},${String(to.y)}`);
  }

  return commands.join(" ");
}

function getSegmentBridges(from: DiagramPoint, to: DiagramPoint, bridges: DiagramBridge[]) {
  const orientation = from.y === to.y ? "horizontal" : from.x === to.x ? "vertical" : null;
  if (orientation == null) {
    return [];
  }

  return bridges
    .filter((bridge) => {
      if (bridge.orientation !== orientation) {
        return false;
      }

      if (orientation === "horizontal") {
        return bridge.point.y === from.y && bridge.point.x >= Math.min(from.x, to.x) && bridge.point.x <= Math.max(from.x, to.x);
      }

      return bridge.point.x === from.x && bridge.point.y >= Math.min(from.y, to.y) && bridge.point.y <= Math.max(from.y, to.y);
    })
    .sort((left, right) => {
      if (orientation === "horizontal") {
        return from.x <= to.x ? left.point.x - right.point.x : right.point.x - left.point.x;
      }

      return from.y <= to.y ? left.point.y - right.point.y : right.point.y - left.point.y;
    });
}

function appendBridgeCommands(commands: string[], from: DiagramPoint, to: DiagramPoint, bridge: DiagramBridge) {
  const radius = 8;
  const height = 9;

  if (bridge.orientation === "horizontal") {
    const direction = from.x <= to.x ? 1 : -1;
    const startX = bridge.point.x - radius * direction;
    const endX = bridge.point.x + radius * direction;

    commands.push(`L ${String(startX)},${String(bridge.point.y)}`);
    commands.push(`Q ${String(bridge.point.x)},${String(bridge.point.y - height)} ${String(endX)},${String(bridge.point.y)}`);
    return;
  }

  const direction = from.y <= to.y ? 1 : -1;
  const startY = bridge.point.y - radius * direction;
  const endY = bridge.point.y + radius * direction;

  commands.push(`L ${String(bridge.point.x)},${String(startY)}`);
  commands.push(`Q ${String(bridge.point.x + height)},${String(bridge.point.y)} ${String(bridge.point.x)},${String(endY)}`);
}

type SelectedEditorNode =
  | { kind: "course"; course: EditorCourse }
  | { kind: "block"; block: EditorBlock }
  | { kind: "branch"; block: EditorBlock; branch: EditorBranch }
  | { kind: "theme"; block: EditorBlock; branch: EditorBranch; theme: EditorTheme };

function getSelectedEditorNode(course: EditorCourse, selection: DiagramNodeSelection): SelectedEditorNode | null {
  if (selection.kind === "course") {
    return { kind: "course", course };
  }

  const block = course.blocks.find((item) => item.localId === selection.blockId);
  if (block == null) {
    return null;
  }

  if (selection.kind === "block") {
    return { kind: "block", block };
  }

  const branch = block.branches.find((item) => item.localId === selection.branchId);
  if (branch == null) {
    return null;
  }

  if (selection.kind === "branch") {
    return { kind: "branch", block, branch };
  }

  const theme = branch.themes.find((item) => item.localId === selection.themeId);
  return theme ? { kind: "theme", block, branch, theme } : null;
}

function getSelectionFormValues(selected: SelectedEditorNode) {
  switch (selected.kind) {
    case "course":
      return { name: selected.course.name, description: selected.course.description };
    case "block":
      return { title: selected.block.title, description: selected.block.description };
    case "branch":
      return { title: selected.branch.title, description: selected.branch.description };
    case "theme":
      return {
        title: selected.theme.title,
        description: selected.theme.description,
        dependencyKeys: selected.theme.dependencyKeys,
        experiencePointsReward: selected.theme.experiencePointsReward,
        lessonContent: selected.theme.lessonContent,
        homeworkContent: selected.theme.homeworkContent,
      };
  }
}

function buildDependencyOptions(block: EditorBlock, selectedThemeId: string) {
  return block.branches.flatMap((branch) =>
    branch.themes
      .filter((theme) => theme.localId !== selectedThemeId)
      .map((theme) => ({
        value: theme.localId,
        label: `${branch.title.trim() || "Ветка без названия"} / ${theme.title.trim() || theme.key}`,
      })),
  );
}

function getModalTitle(selected: SelectedEditorNode) {
  switch (selected.kind) {
    case "course":
      return "Свойства курса";
    case "block":
      return "Блок курса";
    case "branch":
      return "Ветка блока";
    case "theme":
      return "Тема";
  }
}

function applySelectionValues(controller: Controller, selected: SelectedEditorNode, values: Record<string, unknown>) {
  switch (selected.kind) {
    case "course":
      controller.updateCourseMeta({
        name: readFormString(values.name),
        description: readFormString(values.description),
      });
      return;
    case "block":
      controller.updateBlock(selected.block.localId, {
        title: readFormString(values.title),
        description: readFormString(values.description),
      });
      return;
    case "branch":
      controller.updateBranch(selected.block.localId, selected.branch.localId, {
        title: readFormString(values.title),
        description: readFormString(values.description),
      });
      return;
    case "theme":
      controller.updateTheme(selected.block.localId, selected.branch.localId, selected.theme.localId, {
        title: readFormString(values.title),
        description: readFormString(values.description),
        dependencyKeys: readFormStringArray(values.dependencyKeys),
        experiencePointsReward: readFormNumber(values.experiencePointsReward),
        lessonContent: readFormString(values.lessonContent),
        homeworkContent: readFormString(values.homeworkContent),
      });
      return;
  }
}

function readFormString(value: unknown) {
  return typeof value === "string" ? value : "";
}

function readFormStringArray(value: unknown) {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : [];
}

function readFormNumber(value: unknown) {
  return typeof value === "number" && Number.isFinite(value) ? value : 0;
}

function getNextLevelThreshold(levels: EditorLevel[]) {
  const highestThreshold = levels.reduce((max, level) => Math.max(max, level.requiredExperiencePoints), 0);
  return highestThreshold === 0 ? 10 : highestThreshold + 10;
}

function addEditorNode(controller: Controller, intent: AddNodeIntent, values: Record<string, unknown>) {
  if (intent.kind === "block") {
    controller.addBlock({ title: readFormString(values.title), description: readFormString(values.description) });
    return;
  }

  if (intent.kind === "branch") {
    controller.addBranch(intent.blockId, { title: readFormString(values.title), description: readFormString(values.description) });
    return;
  }

  controller.addTheme(
    intent.blockId,
    intent.branchId,
    {
      title: readFormString(values.title),
      description: readFormString(values.description),
      dependencyKeys: readFormStringArray(values.dependencyKeys),
      experiencePointsReward: readFormNumber(values.experiencePointsReward),
      lessonContent: readFormString(values.lessonContent),
      homeworkContent: readFormString(values.homeworkContent),
    },
    intent.insertIndex,
  );
}

function isThemeEligibleForCompletion(
  course: EditorCourse,
  block: EditorBlock,
  branch: EditorBranch,
  theme: EditorTheme,
  enrollmentThemesByCourseThemeId: Map<string, CourseEnrollmentTheme>,
) {
  const previousTheme = branch.themes.filter((item) => branch.themes.indexOf(item) < branch.themes.indexOf(theme)).at(-1);

  if (previousTheme != null && enrollmentThemesByCourseThemeId.get(previousTheme.localId)?.state !== 5) {
    return false;
  }

  const themesById = new Map(
    block.branches.flatMap((item) => item.themes.map((branchTheme) => [branchTheme.localId, branchTheme] as const)),
  );
  for (const dependencyThemeId of theme.dependencyKeys) {
    const dependencyTheme = themesById.get(dependencyThemeId);
    if (dependencyTheme == null || enrollmentThemesByCourseThemeId.get(dependencyTheme.localId)?.state !== 5) {
      return false;
    }
  }

  const currentBlockIndex = course.blocks.indexOf(block);
  const previousBlockThemes = course.blocks
    .slice(0, Math.max(currentBlockIndex, 0))
    .flatMap((courseBlock) => courseBlock.branches)
    .flatMap((courseBranch) => courseBranch.themes);

  return previousBlockThemes.every((previousBlockTheme) => enrollmentThemesByCourseThemeId.get(previousBlockTheme.localId)?.state === 5);
}

function resolveEffectiveProgressState(
  course: EditorCourse,
  block: EditorBlock,
  branch: EditorBranch,
  theme: EditorTheme,
  enrollmentThemesByCourseThemeId: Map<string, CourseEnrollmentTheme>,
): CourseThemeProgressState | null {
  const enrollmentTheme = enrollmentThemesByCourseThemeId.get(theme.localId);

  if (enrollmentTheme == null) {
    return null;
  }

  if (enrollmentTheme.state === 3 || enrollmentTheme.state === 4 || enrollmentTheme.state === 5) {
    return enrollmentTheme.state;
  }

  if (!isThemeEligibleForCompletion(course, block, branch, theme, enrollmentThemesByCourseThemeId)) {
    return 0;
  }

  return 2;
}

function confirmNodeDelete(
  modal: ReturnType<typeof AntdApp.useApp>["modal"],
  controller: Controller,
  selection: Exclude<DiagramNodeSelection, { kind: "course" }>,
) {
  const content =
    selection.kind === "block"
      ? "Удаление блока уберет его ветки, темы, зависимости и может затронуть прогресс клиентов после сохранения."
      : selection.kind === "branch"
        ? "Удаление ветки уберет ее темы, зависимости и может затронуть прогресс клиентов после сохранения."
        : "Удаление темы уберет связанные зависимости и может затронуть прогресс клиентов после сохранения.";

  modal.confirm({
    title: "Удалить узел?",
    content,
    okButtonProps: { danger: true },
    onOk: () => {
      if (selection.kind === "block") {
        controller.removeBlock(selection.blockId);
        return;
      }

      if (selection.kind === "branch") {
        controller.removeBranch(selection.blockId, selection.branchId);
        return;
      }

      controller.removeTheme(selection.blockId, selection.branchId, selection.themeId);
    },
  });
}

function getCourseThemeProgressStateLabel(state: CourseThemeProgressState) {
  switch (state) {
    case 0:
      return "Заблокировано";
    case 1:
      return "Можно открыть";
    case 2:
      return "Открыто";
    case 3:
      return "В процессе";
    case 4:
      return "Ждет ДЗ";
    case 5:
      return "Завершено";
  }
}

function getCourseThemeProgressStateTagColor(state: CourseThemeProgressState) {
  switch (state) {
    case 0:
      return "default";
    case 1:
      return "gold";
    case 2:
      return "blue";
    case 3:
      return "processing";
    case 4:
      return "orange";
    case 5:
      return "green";
  }
}

function getProgressStateClassName(state: CourseThemeProgressState) {
  switch (state) {
    case 0:
      return styles.progressBlocked;
    case 1:
      return styles.progressAvailable;
    case 2:
      return styles.progressUnlocked;
    case 3:
      return styles.progressInProgress;
    case 4:
      return styles.progressHomework;
    case 5:
      return styles.progressCompleted;
  }
}

function getAvailableProgressActions(state: CourseThemeProgressState): Array<{
  action: CourseEnrollmentThemeProgressAction;
  label: string;
  primary?: boolean;
}> {
  switch (state) {
    case 0:
      return [];
    case 1:
      return [{ action: "unlock", label: "Открыть", primary: true }];
    case 2:
      return [{ action: "start", label: "Начать", primary: true }];
    case 3:
      return [{ action: "send-to-homework", label: "Отправить на ДЗ", primary: true }];
    case 4:
      return [
        { action: "pass-homework", label: "Принять ДЗ", primary: true },
        { action: "return-to-progress", label: "Вернуть в работу" },
      ];
    case 5:
      return [{ action: "return-to-progress", label: "Вернуть в работу" }];
  }
}

function getThemeProgressSuccessMessage(action: CourseEnrollmentThemeProgressAction) {
  switch (action) {
    case "unlock":
      return "Тема открыта";
    case "start":
      return "Тема переведена в работу";
    case "send-to-homework":
      return "Тема отправлена на домашнее задание";
    case "pass-homework":
      return "Домашнее задание принято, тема завершена";
    case "return-to-progress":
      return "Тема возвращена в работу";
  }
}
