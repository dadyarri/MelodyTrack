import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { App, Button, Form, Input, Modal, Space, Switch, Tabs } from "antd";
import { useState } from "react";

import { type AliasDictionaryEntry, type AliasDictionaryKind, playerAliasApi } from "@/entities/player-alias";
import { ActionableEmptyState, ListPageScaffold, ListTable, PageLayout } from "@/shared/ui";
import { DeleteOutlined, PlusOutlined } from "@/shared/ui/icons";

const labels: Record<AliasDictionaryKind, string> = {
  epithets: "Эпитеты",
  creatures: "Существа",
  bardicTerms: "Музыкальные термины",
};

type EntryForm = { value: string; isEnabled: boolean };

export function AliasDictionariesPage() {
  const { message, modal } = App.useApp();
  const queryClient = useQueryClient();
  const [dictionary, setDictionary] = useState<AliasDictionaryKind>("epithets");
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<AliasDictionaryEntry | null>(null);
  const [isEditorOpen, setEditorOpen] = useState(false);
  const [form] = Form.useForm<EntryForm>();
  const queryKey = ["alias-dictionaries", dictionary, search] as const;
  const query = useQuery({ queryKey, queryFn: () => playerAliasApi.list(dictionary, search) });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["alias-dictionaries"] });
  const saveMutation = useMutation({
    mutationFn: (values: EntryForm) =>
      editing ? playerAliasApi.update(dictionary, editing.id, values) : playerAliasApi.create(dictionary, values.value),
    onSuccess: async () => {
      await invalidate();
      setEditorOpen(false);
      setEditing(null);
      form.resetFields();
      void message.success(editing ? "Значение сохранено" : "Значение добавлено");
    },
  });
  const toggleMutation = useMutation({
    mutationFn: (entry: AliasDictionaryEntry) =>
      playerAliasApi.update(dictionary, entry.id, { value: entry.value, isEnabled: !entry.isEnabled }),
    onSuccess: invalidate,
  });
  const deleteMutation = useMutation({
    mutationFn: (id: string) => playerAliasApi.remove(dictionary, id),
    onSuccess: async () => {
      await invalidate();
      void message.success("Значение удалено");
    },
  });

  const openCreate = () => {
    setEditing(null);
    form.setFieldsValue({ value: "", isEnabled: true });
    setEditorOpen(true);
  };

  return (
    <PageLayout
      title="Словари игровых имён"
      actions={
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          Добавить
        </Button>
      }
    >
      <Tabs
        activeKey={dictionary}
        items={(Object.keys(labels) as AliasDictionaryKind[]).map((key) => ({ key, label: labels[key] }))}
        onChange={(key) => {
          setDictionary(key as AliasDictionaryKind);
          setSearch("");
        }}
      />
      <ListPageScaffold
        filters={
          <Input.Search
            allowClear
            value={search}
            placeholder="Поиск по словарю"
            onChange={(event) => {
              setSearch(event.target.value);
            }}
          />
        }
        table={
          <ListTable
            rowKey="id"
            loading={query.isLoading}
            dataSource={query.data}
            pagination={false}
            queryStatus={{ error: query.error, isFetching: query.isFetching, onRetry: () => void query.refetch() }}
            emptyText={<ActionableEmptyState description="В словаре нет значений" actionLabel="Добавить" onAction={openCreate} />}
            columns={[
              { title: "Значение", dataIndex: "value" },
              {
                title: "Используется",
                width: 130,
                render: (_, entry) => (
                  <Switch
                    checked={entry.isEnabled}
                    loading={toggleMutation.isPending && toggleMutation.variables.id === entry.id}
                    aria-label={`Использовать ${entry.value}`}
                    onChange={() => {
                      toggleMutation.mutate(entry);
                    }}
                  />
                ),
              },
              {
                title: "",
                width: 180,
                render: (_, entry) => (
                  <Space>
                    <Button
                      onClick={() => {
                        setEditing(entry);
                        form.setFieldsValue({ value: entry.value, isEnabled: entry.isEnabled });
                        setEditorOpen(true);
                      }}
                    >
                      Изменить
                    </Button>
                    <Button
                      danger
                      icon={<DeleteOutlined />}
                      aria-label={`Удалить ${entry.value}`}
                      onClick={() =>
                        modal.confirm({
                          title: "Удалить значение?",
                          content: "Уже выбранные игровые имена сохранятся.",
                          onOk: () => deleteMutation.mutateAsync(entry.id),
                        })
                      }
                    />
                  </Space>
                ),
              },
            ]}
          />
        }
      />
      <Modal
        open={isEditorOpen}
        title={editing ? "Изменить значение" : "Добавить значение"}
        okText="Сохранить"
        cancelText="Отмена"
        confirmLoading={saveMutation.isPending}
        onOk={() => {
          form.submit();
        }}
        onCancel={() => {
          setEditorOpen(false);
        }}
      >
        <Form
          form={form}
          layout="vertical"
          onFinish={(values) => {
            saveMutation.mutate(values);
          }}
        >
          <Form.Item name="value" label="Значение" rules={[{ required: true, whitespace: true, message: "Укажите значение" }, { max: 60 }]}>
            <Input autoFocus />
          </Form.Item>
          <Form.Item name="isEnabled" valuePropName="checked" hidden={!editing}>
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </PageLayout>
  );
}
