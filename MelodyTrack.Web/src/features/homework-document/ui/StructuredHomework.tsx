import { getMarkRange, Node } from "@tiptap/core";
import { EditorContent, NodeViewWrapper, type ReactNodeViewProps, ReactNodeViewRenderer, useEditor, useEditorState } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import { Alert, Button, Card, Dropdown, Input, Modal, Space, Switch, Tooltip, Typography } from "antd";
import { lazy, type ReactNode, Suspense, useEffect, useMemo, useState } from "react";

import { createDefaultStrummingConfiguration, parseStrummingConfiguration, TrainerTimingHost, type StrummingConfiguration } from "@/entities/trainer";
import {
  BoldOutlined,
  CodeOutlined,
  DownOutlined,
  GuitarOutlined,
  ItalicOutlined,
  LinkOutlined,
  OrderedListOutlined,
  QuoteOutlined,
  StrikethroughOutlined,
  UnderlineOutlined,
  UnorderedListOutlined,
} from "@/shared/ui/icons";

import { type HomeworkExercise, hydrateExerciseNodes, parseHomeworkDocument, serializeHomeworkDraft } from "../model/document";
import styles from "./StructuredHomework.module.css";

type ExerciseNodeAttributes = { exerciseId: string; exerciseType: string; configuration: string };
type ExerciseType = "strumming";
type LinkDialogState = {
  from: number;
  to: number;
  text: string;
  href: string;
  enabled: boolean;
};
type ExerciseDefinition = Readonly<{
  label: string;
  icon: ReactNode;
  createConfiguration: () => StrummingConfiguration;
}>;

const exerciseDefinitions: Record<ExerciseType, ExerciseDefinition> = {
  strumming: {
    label: "Гитарный бой",
    icon: <GuitarOutlined />,
    createConfiguration: createDefaultStrummingConfiguration,
  },
} as const;

const LazyStrummingTrainer = lazy(() => import("@/entities/trainer").then((module) => ({ default: module.StrummingTrainer })));

function ExerciseNodeView({ node, updateAttributes, editor, deleteNode }: ReactNodeViewProps) {
  const attributes = node.attrs as ExerciseNodeAttributes;
  const definition = (exerciseDefinitions as Partial<Record<string, ExerciseDefinition>>)[attributes.exerciseType];
  const configuration = useMemo(() => {
    if (!definition) return null;
    try {
      return parseStrummingConfiguration(JSON.parse(attributes.configuration) as unknown);
    } catch {
      return null;
    }
  }, [attributes.configuration, definition]);
  return (
    <NodeViewWrapper className={styles.exercise} contentEditable={false}>
      <Typography.Title level={5}>Тренажёр «{definition?.label ?? "Неизвестное упражнение"}»</Typography.Title>
      <Suspense fallback={<Typography.Text type="secondary">Загружаем тренажёр…</Typography.Text>}>
        {configuration ? <LazyStrummingTrainer
          configuration={configuration}
          showConfiguration={editor.isEditable}
          onConfigurationChange={
            editor.isEditable
              ? (value) => {
                  updateAttributes({ configuration: JSON.stringify(value) });
                }
              : undefined
          }
        /> : <Alert type="warning" showIcon title="Не удалось открыть упражнение" description="Конфигурация некорректна или имеет неподдерживаемую версию." />}
        {!configuration && editor.isEditable ? <Button onClick={() => updateAttributes({ exerciseType: "strumming", configuration: JSON.stringify(createDefaultStrummingConfiguration()) })}>Заменить стандартным упражнением</Button> : null}
      </Suspense>
      {editor.isEditable ? (
        <Button danger onClick={deleteNode}>
          Удалить упражнение
        </Button>
      ) : null}
    </NodeViewWrapper>
  );
}

const PracticeExerciseNode = Node.create({
  name: "practiceExercise",
  group: "block",
  atom: true,
  addAttributes() {
    return {
      exerciseId: { default: "" },
      exerciseType: { default: "strumming" },
      configuration: { default: "{}" },
    };
  },
  parseHTML: () => [{ tag: "div[data-practice-exercise]" }],
  renderHTML: () => ["div", { "data-practice-exercise": "" }],
  addNodeView: () => ReactNodeViewRenderer(ExerciseNodeView),
});

const extensions = [StarterKit.configure({ link: { openOnClick: false } }), PracticeExerciseNode];

function ToolbarButton({ label, active, icon, onClick }: { label: string; active: boolean; icon: ReactNode; onClick: () => void }) {
  return (
    <Tooltip title={label}>
      <Button size="small" type={active ? "primary" : "default"} icon={icon} aria-label={label} aria-pressed={active} onClick={onClick} />
    </Tooltip>
  );
}

export function StructuredHomeworkEditor({
  value,
  exercises,
  onChange,
  title = "Домашнее задание",
  allowExercises = true,
}: {
  value?: string;
  exercises: readonly HomeworkExercise[];
  onChange: (document: string, exercises: HomeworkExercise[]) => void;
  title?: string;
  allowExercises?: boolean;
}) {
  const [linkDialog, setLinkDialog] = useState<LinkDialogState | null>(null);
  const initialContent = useMemo(() => hydrateExerciseNodes(parseHomeworkDocument(value), exercises), [exercises, value]);
  const editor = useEditor({
    extensions,
    content: initialContent,
    immediatelyRender: false,
    onUpdate: ({ editor: current }) => {
      const serialized = serializeHomeworkDraft(current.getJSON());
      onChange(
        serialized.homeworkDocument,
        serialized.practiceExercises.map((exercise) => ({
          id: exercise.reference ?? "",
          type: "strumming",
          configuration: exercise.configuration ?? "{}",
        })),
      );
    },
  });
  const toolbarState = useEditorState({
    editor,
    selector: ({ editor: current }) => ({
      bold: current?.isActive("bold") ?? false,
      italic: current?.isActive("italic") ?? false,
      underline: current?.isActive("underline") ?? false,
      strike: current?.isActive("strike") ?? false,
      bulletList: current?.isActive("bulletList") ?? false,
      orderedList: current?.isActive("orderedList") ?? false,
      blockquote: current?.isActive("blockquote") ?? false,
      codeBlock: current?.isActive("codeBlock") ?? false,
      link: current?.isActive("link") ?? false,
    }),
  });
  useEffect(() => {
    if (editor && JSON.stringify(editor.getJSON()) !== JSON.stringify(initialContent))
      editor.commands.setContent(initialContent, { emitUpdate: false });
  }, [editor, initialContent]);
  if (!editor) return null;
  const openLinkDialog = () => {
    const { from: selectionFrom, to: selectionTo, $from } = editor.state.selection;
    const existingRange = getMarkRange($from, editor.schema.marks.link);
    const from = existingRange?.from ?? selectionFrom;
    const to = existingRange?.to ?? selectionTo;
    setLinkDialog({
      from,
      to,
      text: editor.state.doc.textBetween(from, to, " "),
      href: (editor.getAttributes("link").href as string | undefined) ?? "https://",
      enabled: true,
    });
  };
  const applyLink = () => {
    if (!linkDialog) return;
    const text = linkDialog.text.trim();
    const href = linkDialog.href.trim();
    if (!text || (linkDialog.enabled && !href)) return;

    const chain = editor
      .chain()
      .focus()
      .setTextSelection({ from: linkDialog.from, to: linkDialog.to })
      .insertContent({ type: "text", text })
      .setTextSelection({ from: linkDialog.from, to: linkDialog.from + text.length });
    if (linkDialog.enabled) chain.setLink({ href });
    else chain.unsetLink();
    chain.setTextSelection(linkDialog.from + text.length).run();
    setLinkDialog(null);
  };
  const insertExercise = (exerciseType: ExerciseType) => {
    const definition = exerciseDefinitions[exerciseType];
    editor
      .chain()
      .focus()
      .insertContent({
        type: "practiceExercise",
        attrs: {
          exerciseId: `draft-${crypto.randomUUID()}`,
          exerciseType,
          configuration: JSON.stringify(definition.createConfiguration()),
        },
      })
      .run();
  };
  return (
    <TrainerTimingHost>
      <Card size="small" title={title} className={styles.editor}>
        <div className={styles.toolbar} role="toolbar" aria-label={`Форматирование: ${title}`}>
        <Space.Compact>
          <ToolbarButton
            label="Жирный"
            active={toolbarState?.bold ?? false}
            icon={<BoldOutlined />}
            onClick={() => editor.chain().focus().toggleBold().run()}
          />
          <ToolbarButton
            label="Курсив"
            active={toolbarState?.italic ?? false}
            icon={<ItalicOutlined />}
            onClick={() => editor.chain().focus().toggleItalic().run()}
          />
          <ToolbarButton
            label="Подчёркнутый"
            active={toolbarState?.underline ?? false}
            icon={<UnderlineOutlined />}
            onClick={() => editor.chain().focus().toggleUnderline().run()}
          />
          <ToolbarButton
            label="Зачёркнутый"
            active={toolbarState?.strike ?? false}
            icon={<StrikethroughOutlined />}
            onClick={() => editor.chain().focus().toggleStrike().run()}
          />
        </Space.Compact>
        <Space.Compact>
          <ToolbarButton
            label="Маркированный список"
            active={toolbarState?.bulletList ?? false}
            icon={<UnorderedListOutlined />}
            onClick={() => editor.chain().focus().toggleBulletList().run()}
          />
          <ToolbarButton
            label="Нумерованный список"
            active={toolbarState?.orderedList ?? false}
            icon={<OrderedListOutlined />}
            onClick={() => editor.chain().focus().toggleOrderedList().run()}
          />
          <ToolbarButton
            label="Цитата"
            active={toolbarState?.blockquote ?? false}
            icon={<QuoteOutlined />}
            onClick={() => editor.chain().focus().toggleBlockquote().run()}
          />
          <ToolbarButton
            label="Блок кода"
            active={toolbarState?.codeBlock ?? false}
            icon={<CodeOutlined />}
            onClick={() => editor.chain().focus().toggleCodeBlock().run()}
          />
          <ToolbarButton label="Ссылка" active={toolbarState?.link ?? false} icon={<LinkOutlined />} onClick={openLinkDialog} />
        </Space.Compact>
        {allowExercises ? (
          <Dropdown
            trigger={["click"]}
            menu={{
              items: Object.entries(exerciseDefinitions).map(([key, definition]) => ({
                key,
                icon: definition.icon,
                label: definition.label,
              })),
              onClick: ({ key }) => {
                insertExercise(key as ExerciseType);
              },
            }}
          >
            <Button size="small" icon={<GuitarOutlined />}>
              Добавить тренажёр <DownOutlined />
            </Button>
          </Dropdown>
        ) : null}
        </div>
        <EditorContent editor={editor} className={styles.content} />
      </Card>
      <Modal
        open={linkDialog != null}
        title="Настроить ссылку"
        okText="Применить"
        cancelText="Отмена"
        okButtonProps={{
          disabled: !linkDialog?.text.trim() || (linkDialog.enabled && !linkDialog.href.trim()),
        }}
        onOk={applyLink}
        onCancel={() => {
          setLinkDialog(null);
        }}
      >
        {linkDialog ? (
          <Space orientation="vertical" size={16} className="wide">
            <label className={styles.linkField}>
              <Typography.Text>Текст ссылки</Typography.Text>
              <Input
                value={linkDialog.text}
                onChange={(event) => {
                  setLinkDialog({ ...linkDialog, text: event.target.value });
                }}
              />
            </label>
            <label className={styles.linkSwitch}>
              <Switch
                checked={linkDialog.enabled}
                onChange={(enabled) => {
                  setLinkDialog({ ...linkDialog, enabled });
                }}
              />
              <Typography.Text>Ссылка включена</Typography.Text>
            </label>
            <label className={styles.linkField}>
              <Typography.Text>Адрес ссылки</Typography.Text>
              <Input
                type="url"
                value={linkDialog.href}
                disabled={!linkDialog.enabled}
                onChange={(event) => {
                  setLinkDialog({ ...linkDialog, href: event.target.value });
                }}
              />
            </label>
          </Space>
        ) : null}
      </Modal>
    </TrainerTimingHost>
  );
}

export function StructuredHomeworkContent({
  value,
  exercises,
  className = "",
}: {
  value?: string | null;
  exercises: readonly HomeworkExercise[];
  className?: string;
}) {
  const content = useMemo(() => hydrateExerciseNodes(parseHomeworkDocument(value), exercises), [exercises, value]);
  const editor = useEditor({ extensions, content, editable: false, immediatelyRender: false });
  return <TrainerTimingHost><EditorContent editor={editor} className={`${styles.content} ${className}`} /></TrainerTimingHost>;
}
