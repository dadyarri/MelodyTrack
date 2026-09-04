import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { App as AntdApp, Form } from "antd";
import { useDeferredValue, useMemo, useState } from "react";
import * as v from "valibot";

import { type Course, courseQueryKeys, coursesApi } from "@/entities/course";
import { hasAdminAccess, useAuth } from "@/entities/session";
import type { Ulid } from "@/shared/api";
import { jsonDurableFormCodec, useDurableForm } from "@/shared/lib/react";

type EditorTheme = {
  localId: string;
  title: string;
  key: string;
  description?: string;
  lessonContent?: string;
  homeworkContent?: string;
  experiencePointsReward: number;
  dependencyKeys: string[];
};

type EditorThemePatch = Partial<Omit<EditorTheme, "localId" | "key">>;

type EditorBranch = {
  localId: string;
  title: string;
  description?: string;
  themes: EditorTheme[];
};

type EditorBlock = {
  localId: string;
  title: string;
  description?: string;
  branches: EditorBranch[];
};

type EditorLevel = {
  localId: string;
  title: string;
  requiredExperiencePoints: number;
};

type EditorCourse = {
  id: Ulid;
  name: string;
  description?: string;
  levels: EditorLevel[];
  blocks: EditorBlock[];
};

type CreateCourseValues = {
  name: string;
  description?: string;
};

const createCourseDraftSchema = v.object({ name: v.optional(v.string()), description: v.optional(v.string()) });
const editorCourseDraftSchema = v.custom<EditorCourse>(isEditorCourseDraft, "Некорректный черновик курса.");
const createCourseDraftCodec = {
  serialize: (values: CreateCourseValues): Partial<CreateCourseValues> => values,
  deserialize: (values: Partial<CreateCourseValues>): Partial<CreateCourseValues> => values,
};
const editorCourseDraftCodec = jsonDurableFormCodec<EditorCourse>();

type ThemeOption = {
  key: string;
  label: string;
};

function createLocalId() {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID();
  }

  return `local-${Math.random().toString(36).slice(2, 10)}`;
}

function createEmptyTheme(index: number, patch?: EditorThemePatch): EditorTheme {
  return {
    localId: createLocalId(),
    title: patch?.title ?? "",
    key: `theme-${String(index + 1)}`,
    description: patch?.description ?? "",
    lessonContent: patch?.lessonContent ?? "",
    homeworkContent: patch?.homeworkContent ?? "",
    experiencePointsReward: patch?.experiencePointsReward ?? 0,
    dependencyKeys: patch?.dependencyKeys ?? [],
  };
}

function buildGeneratedThemeKeys(course: EditorCourse) {
  const generatedKeyByLocalId = new Map<string, string>();
  const usedKeys = new Set<string>();

  for (const block of course.blocks) {
    for (const branch of block.branches) {
      for (const theme of branch.themes) {
        const baseKey = slugify(theme.title) || theme.key || theme.localId;
        let finalKey = baseKey;
        let suffix = 2;

        while (usedKeys.has(finalKey)) {
          finalKey = `${baseKey}-${String(suffix)}`;
          suffix += 1;
        }

        usedKeys.add(finalKey);
        generatedKeyByLocalId.set(theme.localId, finalKey);
      }
    }
  }

  return {
    generatedKeyByLocalId,
  };
}

function createEmptyBranch(patch?: Partial<Pick<EditorBranch, "title" | "description">>): EditorBranch {
  return {
    localId: createLocalId(),
    title: patch?.title ?? "",
    description: patch?.description ?? "",
    themes: [],
  };
}

function createEmptyBlock(patch?: Partial<Pick<EditorBlock, "title" | "description">>): EditorBlock {
  return {
    localId: createLocalId(),
    title: patch?.title ?? "",
    description: patch?.description ?? "",
    branches: [],
  };
}

function createEmptyLevel(patch?: Partial<Omit<EditorLevel, "localId">>): EditorLevel {
  return {
    localId: createLocalId(),
    title: patch?.title ?? "",
    requiredExperiencePoints: patch?.requiredExperiencePoints ?? 0,
  };
}

function mapCourseToEditor(course: Course): EditorCourse {
  const keysByThemeId = new Map<Ulid, string>();
  for (const block of course.blocks) {
    for (const branch of block.branches) {
      for (const theme of branch.themes) {
        keysByThemeId.set(theme.id, theme.key);
      }
    }
  }

  return {
    id: course.id,
    name: course.name,
    description: course.description ?? "",
    levels: course.levels
      .slice()
      .sort((a, b) => a.order - b.order)
      .map((level) => ({
        localId: level.id,
        title: level.title,
        requiredExperiencePoints: level.requiredExperiencePoints,
      })),
    blocks: course.blocks
      .slice()
      .sort((a, b) => a.order - b.order)
      .map((block) => ({
        localId: block.id,
        title: block.title,
        description: block.description ?? "",
        branches: block.branches
          .slice()
          .sort((a, b) => a.order - b.order)
          .map((branch) => ({
            localId: branch.id,
            title: branch.title,
            description: branch.description ?? "",
            themes: branch.themes
              .slice()
              .sort((a, b) => a.order - b.order)
              .map((theme) => ({
                localId: theme.id,
                title: theme.title,
                key: keysByThemeId.get(theme.id) || theme.key,
                description: theme.description ?? "",
                lessonContent: theme.lessonContent ?? "",
                homeworkContent: theme.homeworkContent ?? "",
                experiencePointsReward: theme.experiencePointsReward,
                dependencyKeys: theme.dependencyThemeIds,
              })),
          })),
      })),
  };
}

function slugify(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9а-яё]+/gi, "-")
    .replace(/^-+|-+$/g, "");
}

function moveItem<T>(items: T[], index: number, direction: "up" | "down") {
  const nextIndex = direction === "up" ? index - 1 : index + 1;
  if (nextIndex < 0 || nextIndex >= items.length) {
    return items;
  }

  const nextItems = [...items];
  const [item] = nextItems.splice(index, 1);
  nextItems.splice(nextIndex, 0, item);
  return nextItems;
}

export function useCoursesPageController() {
  const auth = useAuth();
  const queryClient = useQueryClient();
  const { message, modal } = AntdApp.useApp();
  const [createForm] = Form.useForm<CreateCourseValues>();
  const [courseDraftForm] = Form.useForm<EditorCourse>();
  const [isCreateOpen, setCreateOpen] = useState(false);
  const [search, setSearch] = useState("");
  const deferredSearch = useDeferredValue(search);
  const [requestedCourseId, setRequestedCourseId] = useState<Ulid | null>(null);
  const [draftCourseState, setDraftCourseState] = useState<EditorCourse | null>(null);

  const canManageCourses = hasAdminAccess(auth.user);
  const coursesQuery = useQuery({
    queryKey: courseQueryKeys.list(deferredSearch),
    queryFn: () => coursesApi.list(deferredSearch.trim() || undefined),
  });

  const selectedCourseId = useMemo(() => {
    const courses = coursesQuery.data ?? [];
    if (courses.length === 0) {
      return null;
    }

    return requestedCourseId && courses.some((course) => course.id === requestedCourseId) ? requestedCourseId : courses[0].id;
  }, [coursesQuery.data, requestedCourseId]);

  const selectedCourseQuery = useQuery({
    queryKey: courseQueryKeys.selected(selectedCourseId ?? undefined),
    queryFn: async () => {
      if (!selectedCourseId) {
        throw new Error("Course is not selected.");
      }

      return coursesApi.get(selectedCourseId);
    },
    enabled: Boolean(selectedCourseId),
  });
  const createDraft = useDurableForm({
    key: "draft:courses:create",
    schema: createCourseDraftSchema,
    form: createForm,
    codec: createCourseDraftCodec,
    enabled: isCreateOpen,
  });
  const courseDraft = useDurableForm({
    key: selectedCourseId ? `draft:courses:edit:${selectedCourseId}` : null,
    schema: editorCourseDraftSchema,
    form: courseDraftForm,
    codec: editorCourseDraftCodec,
    enabled: Boolean(selectedCourseId && canManageCourses),
    entity: selectedCourseId ? { id: selectedCourseId, baselineVersion: selectedCourseQuery.data?.updatedAtUtc ?? null } : undefined,
    onRestore: setDraftCourseState,
  });

  const draftCourse = useMemo(() => {
    if (draftCourseState && draftCourseState.id === selectedCourseId) {
      return draftCourseState;
    }

    return selectedCourseQuery.data ? mapCourseToEditor(selectedCourseQuery.data) : null;
  }, [draftCourseState, selectedCourseId, selectedCourseQuery.data]);

  const applyDraft = (transform: (course: EditorCourse) => EditorCourse) => {
    setDraftCourseState((current) => {
      const base = current ?? draftCourse;
      const next = base ? transform(base) : base;
      if (next) courseDraft.formProps.onValuesChange?.({}, next);
      return next;
    });
  };

  const createMutation = useMutation({
    mutationFn: (values: CreateCourseValues) =>
      coursesApi.create({
        name: values.name,
        description: values.description,
      }),
    onSuccess: async (result) => {
      void message.success("Курс создан");
      await createDraft.clearAfterSuccess();
      setCreateOpen(false);
      createForm.resetFields();
      setRequestedCourseId(result.id);
      setDraftCourseState(null);
      await queryClient.invalidateQueries({ queryKey: courseQueryKeys.all });
    },
  });

  const updateMutation = useMutation({
    mutationFn: (course: EditorCourse) => {
      const { generatedKeyByLocalId } = buildGeneratedThemeKeys(course);

      return coursesApi.update(course.id, {
        name: course.name,
        description: course.description,
        levels: course.levels.map((level, index) => ({
          title: level.title,
          order: index + 1,
          requiredExperiencePoints: level.requiredExperiencePoints,
        })),
        blocks: course.blocks.map((block, blockIndex) => ({
          title: block.title,
          description: block.description,
          order: blockIndex + 1,
          branches: block.branches.map((branch, branchIndex) => ({
            title: branch.title,
            description: branch.description,
            order: branchIndex + 1,
            themes: branch.themes.map((theme, themeIndex) => ({
              key: generatedKeyByLocalId.get(theme.localId) ?? theme.key,
              title: theme.title,
              description: theme.description,
              lessonContent: theme.lessonContent,
              homeworkContent: theme.homeworkContent,
              order: themeIndex + 1,
              experiencePointsReward: theme.experiencePointsReward,
              dependencyKeys: theme.dependencyKeys.map(
                (dependencyThemeId) => generatedKeyByLocalId.get(dependencyThemeId) ?? dependencyThemeId,
              ),
            })),
          })),
        })),
      });
    },
    onSuccess: async () => {
      void message.success("Курс сохранен");
      await courseDraft.clearAfterSuccess();
      setDraftCourseState(null);
      await queryClient.invalidateQueries({ queryKey: courseQueryKeys.all });
      await queryClient.invalidateQueries({ queryKey: courseQueryKeys.selected(selectedCourseId ?? undefined) });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: Ulid) => coursesApi.remove(id),
    onSuccess: async () => {
      void message.success("Курс удален");
      setRequestedCourseId(null);
      setDraftCourseState(null);
      await queryClient.invalidateQueries({ queryKey: courseQueryKeys.all });
    },
  });

  const themeOptions = useMemo<ThemeOption[]>(() => {
    if (!draftCourse) {
      return [];
    }

    return draftCourse.blocks.flatMap((block) =>
      block.branches.flatMap((branch) =>
        branch.themes.map((theme) => ({
          key: theme.key,
          label: theme.title.trim() || theme.key,
        })),
      ),
    );
  }, [draftCourse]);

  const selectedCourseSummary = useMemo(
    () => (coursesQuery.data ?? []).find((course) => course.id === selectedCourseId) ?? null,
    [coursesQuery.data, selectedCourseId],
  );

  return {
    canManageCourses,
    createForm,
    createDraft,
    courseDraft,
    discardCourseDraft: async () => {
      await courseDraft.discard();
      setDraftCourseState(null);
    },
    coursesQuery,
    selectedCourseQuery,
    selectedCourseId,
    selectedCourseSummary,
    draftCourse,
    hasUnsavedChanges: draftCourseState != null && draftCourseState.id === selectedCourseId,
    search,
    setSearch,
    isCreateOpen,
    setCreateOpen,
    createMutation,
    updateMutation,
    deleteMutation,
    themeOptions,
    setSelectedCourseId: setRequestedCourseId,
    selectCourse: (courseId: Ulid) => {
      setRequestedCourseId(courseId);
      setDraftCourseState(null);
    },
    submitCreate: (values: CreateCourseValues) => {
      if (values.name) createMutation.mutate(values);
    },
    saveCourse: () => {
      if (!draftCourse) {
        return;
      }

      updateMutation.mutate(draftCourse);
    },
    confirmDelete: () => {
      if (!selectedCourseId) {
        return;
      }

      modal.confirm({
        title: "Удалить курс?",
        content: "Курс будет удален только если он еще не назначен клиентам и не связан с занятиями.",
        onOk: () => {
          deleteMutation.mutate(selectedCourseId);
        },
      });
    },
    updateCourseMeta: (patch: Partial<Pick<EditorCourse, "name" | "description">>) => {
      applyDraft((current) => ({ ...current, ...patch }));
    },
    addLevel: (patch?: Partial<Omit<EditorLevel, "localId">>) => {
      applyDraft((current) => ({
        ...current,
        levels: [...current.levels, createEmptyLevel(patch)],
      }));
    },
    updateLevel: (levelId: string, patch: Partial<Omit<EditorLevel, "localId">>) => {
      applyDraft((current) => ({
        ...current,
        levels: current.levels.map((level) => (level.localId === levelId ? { ...level, ...patch } : level)),
      }));
    },
    removeLevel: (levelId: string) => {
      applyDraft((current) => ({
        ...current,
        levels: current.levels.filter((level) => level.localId !== levelId),
      }));
    },
    moveLevel: (levelId: string, direction: "up" | "down") => {
      applyDraft((current) => {
        const index = current.levels.findIndex((level) => level.localId === levelId);
        return index === -1 ? current : { ...current, levels: moveItem(current.levels, index, direction) };
      });
    },
    addBlock: (patch?: Partial<Pick<EditorBlock, "title" | "description">>) => {
      applyDraft((current) => ({
        ...current,
        blocks: [...current.blocks, createEmptyBlock(patch)],
      }));
    },
    updateBlock: (blockId: string, patch: Partial<Pick<EditorBlock, "title" | "description">>) => {
      applyDraft((current) => ({
        ...current,
        blocks: current.blocks.map((block) => (block.localId === blockId ? { ...block, ...patch } : block)),
      }));
    },
    removeBlock: (blockId: string) => {
      applyDraft((current) => ({
        ...current,
        blocks: current.blocks.filter((block) => block.localId !== blockId),
      }));
    },
    moveBlock: (blockId: string, direction: "up" | "down") => {
      applyDraft((current) => {
        const index = current.blocks.findIndex((block) => block.localId === blockId);
        return index === -1 ? current : { ...current, blocks: moveItem(current.blocks, index, direction) };
      });
    },
    addBranch: (blockId: string, patch?: Partial<Pick<EditorBranch, "title" | "description">>) => {
      applyDraft((current) => ({
        ...current,
        blocks: current.blocks.map((block) =>
          block.localId === blockId ? { ...block, branches: [...block.branches, createEmptyBranch(patch)] } : block,
        ),
      }));
    },
    updateBranch: (blockId: string, branchId: string, patch: Partial<Pick<EditorBranch, "title" | "description">>) => {
      applyDraft((current) => ({
        ...current,
        blocks: current.blocks.map((block) =>
          block.localId === blockId
            ? {
                ...block,
                branches: block.branches.map((branch) => (branch.localId === branchId ? { ...branch, ...patch } : branch)),
              }
            : block,
        ),
      }));
    },
    removeBranch: (blockId: string, branchId: string) => {
      applyDraft((current) => ({
        ...current,
        blocks: current.blocks.map((block) =>
          block.localId === blockId ? { ...block, branches: block.branches.filter((branch) => branch.localId !== branchId) } : block,
        ),
      }));
    },
    moveBranch: (blockId: string, branchId: string, direction: "up" | "down") => {
      applyDraft((current) => {
        return {
          ...current,
          blocks: current.blocks.map((block) => {
            if (block.localId !== blockId) {
              return block;
            }

            const index = block.branches.findIndex((branch) => branch.localId === branchId);
            return index === -1 ? block : { ...block, branches: moveItem(block.branches, index, direction) };
          }),
        };
      });
    },
    addTheme: (blockId: string, branchId: string, patch?: EditorThemePatch, insertIndex?: number) => {
      applyDraft((current) => ({
        ...current,
        blocks: current.blocks.map((block) =>
          block.localId === blockId
            ? {
                ...block,
                branches: block.branches.map((branch) =>
                  branch.localId === branchId ? { ...branch, themes: insertTheme(branch.themes, patch, insertIndex) } : branch,
                ),
              }
            : block,
        ),
      }));
    },
    updateTheme: (blockId: string, branchId: string, themeId: string, patch: Partial<EditorTheme>) => {
      applyDraft((current) => ({
        ...current,
        blocks: current.blocks.map((block) =>
          block.localId === blockId
            ? {
                ...block,
                branches: block.branches.map((branch) =>
                  branch.localId === branchId
                    ? {
                        ...branch,
                        themes: branch.themes.map((theme) => (theme.localId === themeId ? { ...theme, ...patch } : theme)),
                      }
                    : branch,
                ),
              }
            : block,
        ),
      }));
    },
    removeTheme: (blockId: string, branchId: string, themeId: string) => {
      applyDraft((current) => ({
        ...current,
        blocks: current.blocks.map((block) =>
          block.localId === blockId
            ? {
                ...block,
                branches: block.branches.map((branch) => {
                  if (branch.localId !== branchId) {
                    return branch;
                  }

                  return {
                    ...branch,
                    themes: branch.themes
                      .filter((theme) => theme.localId !== themeId)
                      .map((theme) => ({
                        ...theme,
                        dependencyKeys: theme.dependencyKeys.filter((dependencyThemeId) => dependencyThemeId !== themeId),
                      })),
                  };
                }),
              }
            : block,
        ),
      }));
    },
    moveTheme: (blockId: string, branchId: string, themeId: string, direction: "up" | "down") => {
      applyDraft((current) => {
        return {
          ...current,
          blocks: current.blocks.map((block) =>
            block.localId === blockId
              ? {
                  ...block,
                  branches: block.branches.map((branch) => {
                    if (branch.localId !== branchId) {
                      return branch;
                    }

                    const index = branch.themes.findIndex((theme) => theme.localId === themeId);
                    return index === -1 ? branch : { ...branch, themes: moveItem(branch.themes, index, direction) };
                  }),
                }
              : block,
          ),
        };
      });
    },
  };
}

function isEditorCourseDraft(value: unknown): value is EditorCourse {
  if (!value || typeof value !== "object") return false;
  const course = value as Partial<EditorCourse>;
  return (
    typeof course.id === "string" &&
    typeof course.name === "string" &&
    Array.isArray(course.levels) &&
    course.levels.every(
      (level) => typeof level.localId === "string" && typeof level.title === "string" && typeof level.requiredExperiencePoints === "number",
    ) &&
    Array.isArray(course.blocks) &&
    course.blocks.every(
      (block) =>
        typeof block.localId === "string" &&
        typeof block.title === "string" &&
        Array.isArray(block.branches) &&
        block.branches.every(
          (branch) =>
            typeof branch.localId === "string" &&
            typeof branch.title === "string" &&
            Array.isArray(branch.themes) &&
            branch.themes.every(
              (theme) =>
                typeof theme.localId === "string" &&
                typeof theme.title === "string" &&
                typeof theme.key === "string" &&
                typeof theme.experiencePointsReward === "number" &&
                Array.isArray(theme.dependencyKeys) &&
                theme.dependencyKeys.every((key) => typeof key === "string"),
            ),
        ),
    )
  );
}

function insertTheme(themes: EditorTheme[], patch?: EditorThemePatch, insertIndex?: number) {
  const nextThemes = [...themes];
  const safeIndex = insertIndex == null ? nextThemes.length : Math.max(0, Math.min(insertIndex, nextThemes.length));

  nextThemes.splice(safeIndex, 0, createEmptyTheme(nextThemes.length, patch));

  return nextThemes;
}
