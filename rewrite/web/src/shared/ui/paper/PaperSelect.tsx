import { Select } from "@base-ui/react/select";
import type { ButtonHTMLAttributes } from "react";
import { Icon } from "../Icon";
import styles from "./PaperSelect.module.css";

type PaperSelectProps = Pick<
  ButtonHTMLAttributes<HTMLButtonElement>,
  "id" | "disabled" | "aria-label" | "aria-required" | "aria-invalid" | "aria-describedby"
> & {
  value: string | null;
  options: { value: string; label: string }[];
  placeholder?: string;
  onValueChange: (value: string) => void;
};

export function PaperSelect({
  value,
  options,
  placeholder,
  onValueChange,
  disabled,
  ...props
}: PaperSelectProps) {
  return (
    <Select.Root<string>
      value={value}
      items={options}
      disabled={disabled}
      onValueChange={(next) => {
        if (next !== null) onValueChange(next);
      }}
    >
      <Select.Trigger {...props} className={styles.trigger}>
        <Select.Value placeholder={placeholder} />
        <Select.Icon>
          <Icon name="chevronDown" size={16} />
        </Select.Icon>
      </Select.Trigger>
      <Select.Portal>
        <Select.Positioner
          sideOffset={6}
          align="start"
          alignItemWithTrigger={false}
          className={styles.positioner}
        >
          <Select.Popup className={styles.popup} data-popup-scroll-boundary="">
            <Select.List className={styles.list}>
              {options.map((option) => (
                <Select.Item key={option.value} value={option.value} className={styles.item}>
                  <Select.ItemText>{option.label}</Select.ItemText>
                  <Select.ItemIndicator>
                    <Icon name="check" size={16} />
                  </Select.ItemIndicator>
                </Select.Item>
              ))}
            </Select.List>
          </Select.Popup>
        </Select.Positioner>
      </Select.Portal>
    </Select.Root>
  );
}
