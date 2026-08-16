import { Button, DatePicker, Form, Input, Space } from "antd";
import type { DefaultOptionType } from "antd/es/select";
import dayjs, { type Dayjs } from "dayjs";

import { formatPhone, formatPhoneInput, isValidPhone, normalizePhone, normalizeSocialLink } from "@/entities/client";
import { ClientSourceSelect } from "@/entities/reference-book";
import { DATE_FORMAT } from "@/shared/lib";

export function ClientFormFields({
  sourceOptions,
  onCreateSource,
  onSourceLabelChange,
}: {
  sourceOptions?: DefaultOptionType[];
  onCreateSource?: () => void;
  onSourceLabelChange?: (label?: string) => void;
}) {
  return (
    <>
      <Form.Item name="lastName" label="Фамилия" rules={[{ required: true }]}>
        <Input />
      </Form.Item>
      <Form.Item name="firstName" label="Имя" rules={[{ required: true }]}>
        <Input />
      </Form.Item>
      <Form.Item name="patronymic" label="Отчество">
        <Input />
      </Form.Item>
      <Form.Item name="dateOfBirth" label="Дата рождения">
        <BirthDateInput />
      </Form.Item>
      <Form.Item
        name="phone"
        label="Телефон"
        rules={[
          {
            validator: (_, value?: string) =>
              !value?.trim() || isValidPhone(value) ? Promise.resolve() : Promise.reject(new Error("Введите корректный номер телефона")),
          },
        ]}
      >
        <PhoneInput />
      </Form.Item>
      <Form.Item
        name="telegram"
        label="Telegram"
        rules={[
          {
            validator: (_, value?: string) =>
              !value?.trim() || normalizeSocialLink(value, "telegram")
                ? Promise.resolve()
                : Promise.reject(new Error("Введите Telegram как @nickname, nickname или ссылку t.me")),
          },
        ]}
      >
        <Input placeholder="@nickname или https://t.me/nickname" />
      </Form.Item>
      <Form.Item
        name="vk"
        label="VK"
        rules={[
          {
            validator: (_, value?: string) =>
              !value?.trim() || normalizeSocialLink(value, "vk")
                ? Promise.resolve()
                : Promise.reject(new Error("Введите VK как nickname или ссылку vk.com/vk.ru")),
          },
        ]}
      >
        <Input placeholder="nickname или https://vk.com/nickname" />
      </Form.Item>
      <Form.Item label="Источник">
        <Space.Compact className="wide">
          <Form.Item name="sourceId" noStyle>
            <ClientSourceSelect extraOptions={sourceOptions} onResolvedLabelChange={onSourceLabelChange} />
          </Form.Item>
          {onCreateSource ? <Button onClick={onCreateSource}>Новый источник</Button> : null}
        </Space.Compact>
      </Form.Item>
    </>
  );
}

function BirthDateInput({ value, onChange }: { value?: string | null; onChange?: (value?: string) => void }) {
  return (
    <DatePicker
      className="wide"
      format={DATE_FORMAT}
      value={value ? dayjs(value, "YYYY-MM-DD") : null}
      onChange={(date: Dayjs | null) => {
        onChange?.(date ? date.format("YYYY-MM-DD") : undefined);
      }}
    />
  );
}

function PhoneInput({ value, onChange }: { value?: string | null; onChange?: (value?: string) => void }) {
  return (
    <Input
      value={value ?? ""}
      inputMode="tel"
      autoComplete="tel"
      placeholder="+49 1512 3456789"
      onChange={(event) => {
        onChange?.(formatPhoneInput(event.target.value) || undefined);
      }}
      onBlur={(event) => {
        const normalized = normalizePhone(event.target.value);
        onChange?.((normalized ? formatPhone(normalized) : formatPhoneInput(event.target.value)) || undefined);
      }}
    />
  );
}
