import { Accessor, batch, Component, createEffect, createMemo, createResource, createSignal, For, Index, on, onMount, Show, untrack } from 'solid-js';
import styles from './index.module.css';
import iconClose from '../../assets/icons/icon24_close.svg';
import iconBreadcrumbFill from '../../assets/icons/label_important_24dp_FFFFFF_FILL1_wght300_GRAD0_opsz24.svg';
import iconBreadcrumb from '../../assets/icons/label_important_24dp_FFFFFF_FILL0_wght300_GRAD0_opsz24.svg';
import iconFolder from '../../assets/icons/folder_24dp_FFFFFF_FILL0_wght300_GRAD0_opsz24.svg';
import iconSearch from '../../assets/icons/icon24_search.svg';
import iconFile from '../../assets/icons/draft_24dp_FFFFFF_FILL0_wght300_GRAD0_opsz24.svg';
import iconImageFile from '../../assets/icons/photo_24dp_FFFFFF_FILL0_wght300_GRAD0_opsz24.svg';
import iconPhotos from '../../assets/icons/photo_library_24dp_FFFFFF_FILL0_wght300_GRAD0_opsz24.svg';
import iconVolume from '../../assets/icons/hard_disk_24dp_FFFFFF_FILL0_wght300_GRAD0_opsz24.svg';
import UIOverlay from '../../state/UIOverlay';
import ScrollContainer from '../../components/containers/ScrollContainer';
import VirtualList from '../../components/containers/VirtualList';
import InputText from '../../components/basics/inputs/InputText';
import { updateDataArray } from '../../utility';
import { Event1 } from '../../utility/Event';
import { focusScope } from '../../focusScope'; void focusScope;
import { focusable } from '../../focusable'; void focusable;
import { FileRow, LocalBackend, QuickAccessRow } from '../../backend/LocalBackend';
import { useFocus } from '../../FocusProvider';
import Dropdown from '../../components/basics/inputs/Dropdown';
import { CustomDialogLocal } from '../OverlayRoot';
import CenteredLoader from '../../components/basics/loaders/CenteredLoader';

export type PickerSelectionMode = 'file' | 'folder';
export interface OverlayFilePickerProps {
  onPick?: (paths: string[]) => void;
  selectionMode?: PickerSelectionMode;
  allowMultiple?: boolean;
  mode?: 'open' | 'save';
  defaultFileName?: string;
  filters?: { name: string, pattern: string }[];
}

const OverlayFilePicker: Component<OverlayFilePickerProps> = (props) => {
  const focus = useFocus();

  const isSaveMode = createMemo(() => (props.mode ?? 'open') === 'save');
  const selectionMode = createMemo(() => props.selectionMode ?? 'file');
  const allowMultiple = createMemo(() => props.allowMultiple ?? false);
  const filters = createMemo(() => props.filters && props.filters.length ? props.filters : [ { name: "All files (*.*)", pattern: "*.*" } ])

  const [search, setSearch] = createSignal<string>("");
  const [inBreadcrumbMode, setInBreadcrumbMode] = createSignal(true);
  const [currentDirectory, setCurrentDirectory] = createSignal<string>("");
  const [errorMsg, setErrorMsg] = createSignal<string>();
  LocalBackend.defaultPath().then((v) => {
    if (currentDirectory().length === 0)
      setCurrentDirectory(v.path);
  });

  const [path, setPath] = createSignal<string>();
  const isWindowsDrive = (p: string) => /^[a-zA-Z]:([\\/]|$)/.test(p); // C:\ or C:/
  const isUnc = (p: string) => /^\\\\[^\\\/]+[\\\/][^\\\/]+/.test(p); // \\server\share\...
  const isDevice = (p: string) => /^\\\\\?\\/.test(p); // \\?\C:\...
  const isWinRootOnly = (p: string) => /^\\(?!\\)/.test(p);
  const isPosixRoot = (p: string) => p.startsWith('/');
  const isRooted = (p: string) => isWindowsDrive(p) || isUnc(p) || isDevice(p) || isWinRootOnly(p) || isPosixRoot(p);
  const pickSep = (a: string, b: string) => (/[\\]/.test(a) || /[\\]/.test(b)) ? '\\' : '/';

  const joinPaths = (base: string, rel: string) => {
    if (!base) return rel;
    const sep = pickSep(base, rel);
    const b = base.replace(/[\\\/]+$/g, '');
    const r = rel.replace(/^[\\\/]+/g, '');
    return `${b}${sep}${r}`;
  };

  const collapseSlashes = (p: string) => {
    if (!p) return p;
    if (isDevice(p)) return p;
    if (p.startsWith('\\\\')) {
      const tail = p.slice(2).replace(/[\\\/]+/g, m => (m.includes('\\') ? '\\' : '/'));
      return '\\\\' + tail;
    }

    return p.replace(/[\\\/]+/g, m => (m.includes('\\') ? '\\' : '/'));
  };
  const normalizePath = (p: string): string => {
    if (isDevice(p)) return p;

    const sep = /[\\]/.test(p) ? '\\' : '/';
    const parts = p.split(/[\\\/]+/);
    const out: string[] = [];

    let prefix = '';
    if (isWindowsDrive(p)) {
      prefix = parts[0].toUpperCase();
      parts.splice(0, 1);
    } else if (isUnc(p)) {
      const server = parts[1] ?? '';
      const share  = parts[2] ?? '';
      prefix = '\\\\' + server + '\\' + share;
      parts.splice(0, 3);
    } else if (isPosixRoot(p) || isWinRootOnly(p)) {
      prefix = sep;
      parts.splice(0, 1);
    }

    for (const part of parts) {
      if (!part || part === '.') continue;
      if (part === '..') {
        if (out.length && out[out.length - 1] !== '..') out.pop();
        else if (!prefix) out.push('..');
      } else {
        out.push(part);
      }
    }

    const body = out.join(sep);
    if (!prefix) return body || '.';
    const needsSep = body && !prefix.endsWith(sep);
    return needsSep ? `${prefix}${sep}${body}` : (body ? `${prefix}${body}` : prefix);
  };

  const getFullPath = (dir: string, path: string) => {
    const value = collapseSlashes(path);
    if (isRooted(value)) return normalizePath(value);
    return normalizePath(joinPaths(dir, value));
  }

  const selectedPaths = createMemo<string[]>(() => {
    const dir = currentDirectory() ?? '';
    const raw = path() ?? '';
    if (!raw.trim()) return [];

    return raw
      .split(';')
      .map(s => s.trim())
      .filter(Boolean)
      .map(v => getFullPath(dir, v));
  });

  const setSelectedPaths = (paths: string[]) => {
    const dir = normalizePath(currentDirectory() ?? '');
    const dirWithSep = /[\\]$/.test(dir) || /[/]$/.test(dir) ? dir : (/[\\]/.test(dir) ? `${dir}\\` : `${dir}/`);
    const pretty = paths.map(p => (p.startsWith(dirWithSep) ? p.slice(dirWithSep.length) : p));
    setPath(pretty.join('; '));
  };

  type ConfirmState = {
    title: string;
    message: string;
    confirmLabel: string;
    cancelLabel: string;
    resolve: (ok: boolean) => void;
  };
  const [confirmState, setConfirmState] = createSignal<ConfirmState | null>(null);

  function askConfirm(
    message: string,
    opts?: { title?: string; confirmLabel?: string; cancelLabel?: string }
  ): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      setConfirmState({
        title: opts?.title ?? 'Confirm',
        message,
        confirmLabel: opts?.confirmLabel ?? 'OK',
        cancelLabel: opts?.cancelLabel ?? 'Cancel',
        resolve,
      });
    });
  }

  function resolveConfirm(ok: boolean) {
    const st = confirmState();
    if (!st) return;
    st.resolve(ok);
    setConfirmState(null);
  }

  const dismiss = () => UIOverlay.dismiss();

  const INVALID_FILENAME_CHARS = /[<>:"/\\|?*]/;
  function validateFileName(inputPathOrName: string): string | undefined {
    const raw = (inputPathOrName ?? '').trim();
    if (!raw) return "Enter a file name.";
    const base = raw.split(/[\\/]+/).pop() ?? '';

    if (!base) return "Enter a file name.";
    if (base === '.' || base === '..') return "Invalid file name.";
    if (INVALID_FILENAME_CHARS.test(base)) {
      return `Invalid characters in file name. Don’t use: < > : " / \\ | ? *`;
    }
    return undefined;
  }

  const saveSelected = async () => {
      const dir = currentDirectory() ?? '';
      const raw = (path() ?? '').trim();
      if (!raw) { setErrorMsg("Enter a file name."); return; }
      const fileNameError = validateFileName(raw);
      if (fileNameError) { setErrorMsg(fileNameError); return; }

      let full = getFullPath(dir, raw);
      try {
        const st = await LocalBackend.stat(full);
        if (st?.type === 'folder') { setErrorMsg("That's a folder. Enter a file name."); return; }
      } catch { /* not found => fine */ }

      const extMap: Record<string, string[]> = { img: ['jpg','jpeg','png'], txt: ['txt','md'] };
      const exts = extMap[fileFilter()] ?? [];
      if (exts.length) {
        const lower = full.toLowerCase();
        if (!exts.some(e => lower.endsWith('.' + e))) full = `${full}.${exts[0]}`;
      }

      try {
        const st2 = await LocalBackend.stat(full);
        if (st2?.type === 'folder') { setErrorMsg("Target is a folder. Pick a file name."); return; }
        if (st2?.type === 'file') {
          const ok = await askConfirm(`${full} already exists. Replace it?`, {
            title: 'File exists',
            confirmLabel: 'Replace',
            cancelLabel: 'Cancel',
          });
          if (!ok) return;
        }
      } catch { /* missing => OK */ }

      props.onPick?.([full]);
      dismiss();
  };
  const openSelected = async () => {
    if (isSaveMode()) {
      await saveSelected();
      return;
    }

    const wanted = selectionMode;
    const items = selectedPaths();

    if (items.length === 0) {
      setErrorMsg(wanted() === 'folder' ? "Select a folder." : "Select at least one file.");
      return;
    }

    try {
      const dir = currentDirectory();
      const byPath = new Map<string, FileRow>();
      for (const it of currentFiles) byPath.set(getFullPath(dir, it.path), it);

      const stats = await Promise.all(items.map(async (p) => {
        const quick = byPath.get(p);
        if (quick) return { path: p, type: quick.type as 'file'|'folder' };
        try {
          const s = await LocalBackend.stat(p);
          return { path: p, type: (s?.type ?? 'unknown') as 'file'|'folder' };
        } catch {
          return { path: p, type: 'unknown' as const };
        }
      }));

      if (wanted() === 'folder') {
        const bad = stats.filter(s => s.type !== 'folder');
        if (bad.length) {
          setErrorMsg("This dialog expects folders. Tip: navigate into a folder and select it.");
          return;
        }
      } else {
        const bad = stats.filter(s => s.type !== 'file');
        if (bad.length) {
          setErrorMsg("This dialog expects files. Folders can’t be opened here.");
          return;
        }
      }

      if (!allowMultiple && stats.length > 1) {
        setErrorMsg("Multiple selection isn’t allowed here. Pick exactly one item.");
        return;
      }

      props.onPick?.(stats.map(s => s.path));
      dismiss();
    } catch (e: any) {
      setErrorMsg(e?.message ?? "Failed to validate selection.");
    }
  };

  const submitDirectoryPath = async (value: string) => {
    try {
      const s = await LocalBackend.stat(value);
      if (s?.type === 'folder') {
        batch(() => {
          setCurrentDirectory(s.path);
          setInBreadcrumbMode(true);
          setErrorMsg();
          setSearch('');
        });
      } else {
        setErrorMsg("Not a folder. Enter a valid directory path.");
      }
    } catch (err: any) {
      setErrorMsg("Path not found or inaccessible.");
    }
  };

  const breadcrumbs = createMemo(() => {
    const dir = normalizePath(currentDirectory());
    const sep = /[\\]/.test(dir) ? "\\" : "/";
    const parts = dir.split(/[\\/]+/);

    let rootLabel: string;
    let rootPath: string;

    if (isWindowsDrive(dir)) {
      rootLabel = parts[0].toUpperCase();
      rootPath = rootLabel + sep;
      parts.splice(0, 1);
    } else if (isUnc(dir)) {
      const server = parts[1] ?? "";
      const share  = parts[2] ?? "";
      rootLabel = "\\\\" + server + (share ? "\\" + share : "");
      rootPath = rootLabel;
      parts.splice(0, 3);
    } else if ((sep === "/" && dir === "/") || (sep === "\\" && isWinRootOnly(dir))) {
      rootLabel = sep;
      rootPath = sep;
      parts.splice(0, parts.length);
    } else if (sep === "/" && dir.startsWith("/")) {
      rootLabel = "/";
      rootPath = "/";
      parts.splice(0, 1);
    } else {
      rootLabel = dir;
      rootPath = dir;
      parts.splice(0, parts.length);
    }

    for (let i = parts.length - 1; i >= 0; i--) {
      if (!parts[i]) parts.splice(i, 1);
    }

    const crumbs: { name: string; path: string }[] = [{ name: rootLabel, path: rootPath }];

    let acc = rootPath;
    for (const p of parts) {
      acc = joinPaths(acc, p);
      crumbs.push({ name: p, path: acc });
    }
    return crumbs;
  });

  const [fileFilter, setFileFilter] = createSignal<number>(0);
  const [quickAccess] = createResource(async () => {
    const res = await fetch('/Local/QuickAccess');
    if (!res.ok) throw new Error(await res.text());
    return (await res.json()) as QuickAccessRow[];
  });

  const [files] = createResource(
    () => ({ dir: currentDirectory(), q: search(), filter: fileFilter(), filters: filters(), mode: selectionMode() }),
    async ({ dir, q, filter, filters, mode }) => {
      console.info("query files", {dir,q,filter});
      const params = new URLSearchParams({
        path: dir,
        q: q ?? '',
        includeHidden: 'false',
        includeFiles: (isSaveMode() || mode === "file") ? 'true' : 'false',
        dirsFirst: 'true',
        filter: filters?.[filter]?.pattern ?? '',
        limit: '2000',
      });

      const res = await fetch(`/Local/List?${params}`);
      if (!res.ok) throw new Error(await res.text());
      return (await res.json()) as FileRow[];
    }
  );

  let currentFiles: FileRow[] = [];
  let modifiedItemsEvent = new Event1<{startIndex: number, endIndex: number}>();
  let removedItemsEvent = new Event1<{startIndex: number, endIndex: number}>();
  let addedItemsEvent = new Event1<{startIndex: number, endIndex: number}>();

  createEffect(on(() => files(), (next, prev) => {
    if (!next || next === prev) return;
    console.info("files changed", next);
    updateDataArray(currentFiles, next, 
      (startIndex, endIndex) => modifiedItemsEvent.invoke({ startIndex, endIndex }), 
      (startIndex, endIndex) => addedItemsEvent.invoke({ startIndex, endIndex }),
      (startIndex, endIndex) => removedItemsEvent.invoke({ startIndex, endIndex }));
  }, { defer: true }));

  createEffect(() => {
    const isSaveMode = props.mode === "save";
    const defaultFileName = props.defaultFileName;
    const p = untrack(path);
    if ((!p || p.length < 1) && isSaveMode && defaultFileName) {
      console.info("path changed to defaultFileName", {p, defaultFileName});
      setPath(defaultFileName);
    }
  });
  createEffect(() => console.info("path changed", path()));

  const getQuickAccessIconMemo = (quickAccess: Accessor<QuickAccessRow>) => createMemo(() => {
    const type = quickAccess()?.type;
    switch (type) {
      case "folder": return iconFolder;
      case "volume": return iconVolume;
      case "desktop": return iconFolder;
      case "documents": return iconFolder;
      case "music": return iconFolder;
      case "pictures": return iconPhotos;
      case "videos": return iconFolder;
      case "home": return iconFolder;
      default: return iconFolder;
    }
  });

  const getFileIconMemo = (quickAccess: Accessor<FileRow>) => createMemo(() => {
    const type = quickAccess()?.type;
    switch (type) {
      case "folder": return iconFolder;
      case "file": return iconFile;
      default: return iconFolder;
    }
  });


  const isSelected = (fullPath?: string) => {
    if (!fullPath) return false;
    return selectedPaths().some(p => p === fullPath);
  };

  let lastClickedIndex: Accessor<number | undefined> | undefined = undefined;
  const selectSingle = (fullPath: string) => setSelectedPaths([fullPath]);
  const toggleOne = (fullPath: string) => {
    const set = new Set(selectedPaths());
    if (set.has(fullPath)) set.delete(fullPath); else set.add(fullPath);
    setSelectedPaths(Array.from(set));
  };
  const selectRange = (fromIndex: number, toIndex: number) => {
    const [start, end] = fromIndex < toIndex ? [fromIndex, toIndex] : [toIndex, fromIndex];
    const dir = currentDirectory();
    const rangePaths = [];
    for (let i = start; i <= end; i++) {
      const it = currentFiles[i];
      if (!it) continue;
      rangePaths.push(getFullPath(dir, it.path));
    }
    const set = allowMultiple() ? new Set(selectedPaths()) : new Set<string>();
    rangePaths.forEach(p => set.add(p));
    setSelectedPaths(Array.from(set));
  };

  const globalBack = () => {
    const currentBreadcrumbs = breadcrumbs();
    if (currentBreadcrumbs.length >= 2)
      setCurrentDirectory(currentBreadcrumbs[currentBreadcrumbs.length - 2].path);
    else
      UIOverlay.dismiss();
    return true;
  }

  let quickAccessScrollContainerRef!: HTMLDivElement;
  let fileScrollContainerRef!: HTMLDivElement;
  return (
    <div class={styles.root} onMouseDown={() => dismiss()} use:focusScope={{
        initialMode: 'trap'
    }}>
      <div class={styles.dialog} onClick={e => e.stopPropagation()} onMouseDown={e => e.stopPropagation()}>
        <div class={styles.dialogHeader}>
          <div class={styles.titleRow}>
            <div class={styles.dialogTitle}>{isSaveMode() ? 'Save' : 'Open'}</div>
            <div class={styles.titleSpacer} />
            <div class={styles.closeButton} onClick={() => dismiss()} title="Close">
              <img src={iconClose} alt="Close"/>
            </div>
          </div>

          <div class={styles.toolbar}>
            <div style="position: relative">
              <Show when={inBreadcrumbMode()} fallback={
                <InputText style={{"height": "36px"}} focusable={true} value={currentDirectory()} onSubmit={submitDirectoryPath} />
              }>
                <div class={styles.breadcrumb} role="navigation" aria-label="Breadcrumb">
                  <Index each={breadcrumbs()}>
                    {(crumb, i) => (
                      <>
                        <span
                          class={styles.crumb}
                          onClick={() => setCurrentDirectory(crumb().path)}
                          use:focusable={{
                            onPress: () => setCurrentDirectory(crumb().path),
                            onBack: globalBack
                          }}
                        >
                          {crumb().name}
                        </span>
                        <Show when={i < breadcrumbs().length - 1}>
                          <span class={styles.crumbDivider}>›</span>
                        </Show>
                      </>
                    )}
                  </Index>
                </div>
              </Show>
              <div style="position: absolute; right: 8px; top: 0px; height: 100%; display: flex; justify-content: center; align-items: center;">
                <img style="width: 24px; height: 24px" src={inBreadcrumbMode() ? iconBreadcrumbFill : iconBreadcrumb} onClick={() => {
                  batch(() => {
                    setInBreadcrumbMode(!inBreadcrumbMode());
                    setErrorMsg();
                  });
                }} />
              </div>
            </div>

            <InputText icon={iconSearch} style={{"height": "36px"}} placeholder='Search' value={search()} onTextChanged={(v) => setSearch(v)} focusable={true} />
          </div>

          <Show when={errorMsg()}>
            <div class={styles.errorBanner} role="alert">{errorMsg()}</div>
          </Show>
        </div>

        <div class={styles.body}>
          <ScrollContainer ref={quickAccessScrollContainerRef}>
            <Show when={!quickAccess.loading} fallback={<CenteredLoader />}>
              <VirtualList outerContainerRef={quickAccessScrollContainerRef}
                items={quickAccess() ?? []}
                itemHeight={34}
                builder={(index, item) => {
                  return (
                    <Show when={item()?.type !== "divider"} fallback={<div style="width: 100%; height: 34px; display: flex; align-items: center; justify-content: center;"><div style="width: 100%; height: 1px; background-color: #2A2A2A"></div></div>}>
                      <div
                        class={styles.navItem}
                        classList={{ [styles.active]: currentDirectory() === item()?.path }}
                        onClick={() => {
                          batch(() => {
                            setCurrentDirectory(item()?.path);
                            setErrorMsg();
                          });
                        }}
                        use:focusable={{
                          onPress: () => {
                            batch(() => {
                              setCurrentDirectory(item()?.path);
                              setErrorMsg();
                            });
                          },
                          onBack: globalBack
                        }}
                      >
                        <img src={getQuickAccessIconMemo(item)()} style="height: 16px; width: 16x; flex-shrink: 0;" />
                        <p class={styles.navItemText}>{item()?.name}</p>
                      </div>
                    </Show>
                  );
                }} />
              </Show>
          </ScrollContainer>

          <section class={styles.main}>
            <div class={styles.tableHeader}>
              <div>Name</div>
              <div>Date modified</div>
            </div>

            <ScrollContainer ref={fileScrollContainerRef}>
              <Show when={!files.loading} fallback={<CenteredLoader />}>
                <VirtualList outerContainerRef={fileScrollContainerRef}
                  items={currentFiles}
                  addedItems={addedItemsEvent}
                  modifiedItems={modifiedItemsEvent}
                  removedItems={removedItemsEvent}
                  itemHeight={36}
                  builder={(index, item) => {
                    const fullPath = createMemo(() => {
                      const dir = currentDirectory();
                      const p = item()?.path;
                      return dir && p ? getFullPath(dir, p) : undefined;
                    });

                    const onRowClick = (e?: MouseEvent) => {
                      const it = item();
                      if (!it) return;
                      const fp = fullPath();
                      if (!fp) return;

                      if (allowMultiple() && e?.shiftKey && lastClickedIndex !== undefined) {
                        const lci = lastClickedIndex?.();
                        const i = index();
                        if (lci !== undefined && i !== undefined)
                          selectRange(lci, i);
                      } else if (allowMultiple() && (e?.metaKey || e?.ctrlKey)) {
                        toggleOne(fp);
                        lastClickedIndex = index;
                      } else {
                        selectSingle(fp);
                        lastClickedIndex = index;
                      }

                      setErrorMsg();
                    };

                    const onRowDblClick = () => {
                      const it = item();
                      if (!it) return;
                      if (it.type === 'folder') {
                        setCurrentDirectory(it.path);
                        setSearch('');
                      } else {
                        setSelectedPaths([fullPath()!]);
                        openSelected();
                      }
                    };

                    return (
                      <div
                        class={styles.row}
                        classList={{ [styles.selected]: selectedPaths().some(v => v === fullPath() )}}
                        onClick={onRowClick}
                        onDblClick={onRowDblClick}
                        use:focusable={{
                          onPress: () => onRowClick(),
                          onPressLabel: "Select",
                          onAction: () => onRowDblClick(),
                          onActionLabel: "Open",
                          onBack: globalBack
                        }}
                      >
                        <div class={styles.cellName} title={item()?.name}>
                          <img src={getFileIconMemo(item)()} style="height: 16px; width: 16x;" />
                          <span class="mono">{item()?.name}</span>
                        </div>
                        <div class={styles.cellSubtle} title={item()?.date}>{item()?.date}</div>
                      </div>
                    );
                  }} />
                </Show>
            </ScrollContainer>
          </section>
        </div>

        <div class={styles.footer}>
          <div class={styles.fileNameWrap}>
            <div class={styles.label}>File name:</div>
            <InputText style={{"height": "34px"}} value={path() ?? ""} onTextChanged={(v) => setPath(v)} focusable={true} />
          </div>

          <Show when={props.selectionMode === "file" || isSaveMode()}>
            <Dropdown onSelectedChanged={(v) => setFileFilter(v)} value={fileFilter()} options={filters().map(v => v.name)} direction='up' style={{
              "width": "200px",
              "height": "34px"
            }} selectStyle={{ padding: "6px 12px" }} />
          </Show>

          <div class={styles.buttons}>
            <button class={styles.btn} onClick={() => dismiss()} use:focusable={{
                onPress: dismiss,
                onBack: globalBack
            }}>Cancel</button>
            <button class={`${styles.btn} ${styles.btnPrimary}`} onClick={() => openSelected()} use:focusable={{ onPress: openSelected, onBack: globalBack }}>
              {isSaveMode() ? 'Save' : 'Open'}
            </button>
          </div>
        </div>
      </div>
      <Show when={!!confirmState()}>
        <div
          style="
            position:absolute; inset:0; 
            background: rgba(15,15,15,0.6);
            display:grid; place-items:center; z-index: 3;
          "
          onClick={(e) => {
            resolveConfirm(false); 
            e.stopPropagation();
          }}
          onMouseDown={(e) => {
            e.stopPropagation();
          }}
          use:focusScope={{ initialMode: "trap" }}
        >
          <div
            role="dialog" aria-modal="true" aria-labelledby="confirm-title"
            class={styles.dialog}
            style="
              position:relative;
              width: 420px; height: auto;
              grid-template-rows: auto 1fr auto;
              border-radius: 12px;
              border: 1px solid #2E2E2E;
              background: #141414;
              box-shadow:
                0 1.6px 3.1px rgba(0,0,0,0.10),
                0 7.25px 6.52px rgba(0,0,0,0.16),
                0 17.8px 13px rgba(0,0,0,0.21),
                0 34.28px 25.48px rgba(0,0,0,0.26),
                0 57.68px 46.85px rgba(0,0,0,0.32),
                0 89px 80px rgba(0,0,0,0.42);
            "
            onClick={(e) => e.stopPropagation()} // keep clicks inside from closing
            use:focusScope={{ initialMode: 'trap' }}
            onKeyDown={(e) => {
              if (e.key === 'Escape') { e.stopPropagation(); resolveConfirm(false); }
              if (e.key === 'Enter') { e.stopPropagation(); resolveConfirm(true); }
            }}
          >
            <div class={styles.dialogHeader} style="border-bottom: 1px solid #2A2A2A; padding: 16px 16px 12px;">
              <div class={styles.titleRow}>
                <div id="confirm-title" class={styles.dialogTitle}>
                  {confirmState()!.title}
                </div>
                <div class={styles.titleSpacer} />
                <div class={styles.closeButton}
                    title="Cancel"
                    onClick={() => resolveConfirm(false)}
                    use:focusable={{ onPress: () => resolveConfirm(false) }}>
                  <img src={iconClose} alt="Close"/>
                </div>
              </div>
            </div>

            <div style="padding: 16px; color:#EDEDED; line-height:1.5; white-space:pre-wrap;">
              {confirmState()!.message}
            </div>

            <div class={styles.footer} style="grid-template-columns: 1fr auto; padding: 12px 16px;">
              <div />
              <div class={styles.buttons}>
                <button
                  class={styles.btn}
                  onClick={() => resolveConfirm(false)}
                  use:focusable={{ onPress: () => resolveConfirm(false), onBack: () => (resolveConfirm(true), true) }}
                >
                  {confirmState()!.cancelLabel}
                </button>
                <button
                  class={`${styles.btn} ${styles.btnPrimary}`}
                  onClick={() => resolveConfirm(true)}
                  use:focusable={{ onPress: () => resolveConfirm(true), onBack: () => (resolveConfirm(true), true) }}
                >
                  {confirmState()!.confirmLabel}
                </button>
              </div>
            </div>
          </div>
        </div>
      </Show>
    </div>
  );
};

export default OverlayFilePicker;
