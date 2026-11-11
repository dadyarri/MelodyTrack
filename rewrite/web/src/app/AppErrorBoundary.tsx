import { Component, type ReactNode } from "react";

export class AppErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    if (this.state.failed) {
      return (
        <main className="app-load-error" role="alert">
          <h1>Не удалось открыть экран</h1>
          <p>Проверьте соединение и обновите страницу. Возможно, потребуется войти снова.</p>
          <button type="button" className="text-action" onClick={() => window.location.reload()}>
            Обновить страницу
          </button>
        </main>
      );
    }
    return this.props.children;
  }
}
