import { Space } from "antd";
import type { ReactNode } from "react";

import { PageHeader } from "./PageHeader";

type PageLayoutProps = {
  title: string;
  description?: string;
  actions?: ReactNode;
  children: ReactNode;
  size?: number;
  customClass?: string;
};

export function PageLayout({ title, description, actions, children, size = 20, customClass = "" }: PageLayoutProps) {
  return (
    <Space orientation="vertical" size={size} className="wide">
      {customClass ? (
        <div className={customClass}>
          <PageHeader title={title} description={description} actions={actions} />
          {children}
        </div>
      ) : (
        <>
          <PageHeader title={title} description={description} actions={actions} />
          {children}
        </>
      )}
    </Space>
  );
}
