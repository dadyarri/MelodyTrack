type RouteModuleLoader = () => Promise<unknown>;

export function createRoutePrefetcher<TContext = undefined>(
  loaders: Readonly<Partial<Record<string, RouteModuleLoader>>>,
  prepare?: (module: unknown, context: TContext) => unknown,
) {
  const requests = new Map<string, Promise<void>>();

  return (path: string, context: TContext) => {
    const routePath = path.split(/[?#]/, 1)[0];
    const load = loaders[routePath];

    if (!load) {
      return Promise.resolve();
    }

    const existingRequest = requests.get(routePath);
    if (existingRequest) {
      return existingRequest;
    }

    const request = load()
      .then(async (module) => {
        await prepare?.(module, context);
      })
      .catch(() => {
        if (requests.get(routePath) === request) {
          requests.delete(routePath);
        }
      });

    requests.set(routePath, request);
    return request;
  };
}
