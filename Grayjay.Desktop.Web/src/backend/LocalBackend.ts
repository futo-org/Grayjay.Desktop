import { Backend } from "./Backend";

export type FileType = "folder" | "file";

export type QuickAccessType = "folder" | "volume" | "desktop" | "documents" | "music" | "pictures" | "videos" | "home";
export interface QuickAccessRow {
  name: string;
  path: string;
  type: QuickAccessType;
}

export interface FileRow {
  name: string;
  path: string;
  date: string;
  type: FileType;
}

export interface ListOptions {
  q?: string;
  includeHidden?: boolean;
  dirsFirst?: boolean;
  offset?: number;
  limit?: number;
  filterExtCsv?: string;
}

function buildQuery(params: Record<string, unknown>) {
  const qs = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null) continue;
    if (typeof v === "boolean") qs.set(k, v ? "true" : "false");
    else qs.set(k, String(v));
  }
  return qs.toString();
}

export abstract class LocalBackend {
  static async open(uri: string): Promise<void> {
    await Backend.GET("/local/Open?uri=" + encodeURIComponent(uri));
  }

  static async quickAccess(): Promise<QuickAccessRow[]> {
    return await Backend.GET("/local/QuickAccess");
  }

  static async defaultPath(): Promise<QuickAccessRow> {
    return await Backend.GET("/local/DefaultPath");
  }

  static async list(path: string, opts: ListOptions = {}): Promise<FileRow[]> {
    const {
      q,
      includeHidden = false,
      dirsFirst = true,
      offset = 0,
      limit = 2000,
      filterExtCsv,
    } = opts;

    const query = buildQuery({
      path,
      q,
      includeHidden,
      dirsFirst,
      offset,
      limit,
      filterExtCsv,
    });

    return await Backend.GET("/local/List?" + query);
  }

  static async stat(path: string): Promise<FileRow | null> {
    try {
      return await Backend.GET("/local/Stat?path=" + encodeURIComponent(path));
    } catch (err: any) {
      const status = err?.status ?? err?.response?.status;
      if (status === 404) return null;
      throw err;
    }
  }

  static async parent(path: string): Promise<string | null> {
    try {
      return await Backend.GET("/local/Parent?path=" + encodeURIComponent(path));
    } catch (err: any) {
      const status = err?.status ?? err?.response?.status;
      if (status === 404) return null;
      throw err;
    }
  }
}
