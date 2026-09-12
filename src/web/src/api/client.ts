export type ApiClientOptions = {
  baseUrl?: string;
  fetchImpl?: typeof fetch;
};

export function createApiClient(options: ApiClientOptions = {}) {
  const baseUrl = options.baseUrl ?? import.meta.env.VITE_API_BASE_URL ?? "/api";
  const fetchImpl = options.fetchImpl ?? fetch;

  return {
    async get<T>(path: string): Promise<T> {
      const response = await fetchImpl(`${baseUrl}${path}`, { credentials: "include" });
      if (!response.ok) throw new Error(`API request failed: ${response.status}`);
      return response.json() as Promise<T>;
    }
  };
}
