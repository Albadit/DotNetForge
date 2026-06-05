import { useMemo, useState } from "react";
import type { ReactNode } from "react";
import { FilePlus2, Save, Trash2, Folder, FolderOpen, FileText } from "lucide-react";
import { Collection } from "react-aria-components/Collection";
import { useTreeData } from "react-aria-components/useTreeData";
import { FileTree, useFileTreeDrag } from "@heroui-pro/react";
import { Button, Calendar, Checkbox, DateField, DatePicker, Input, Label, ListBox, Select, TextField } from "@heroui/react";
import { getLocalTimeZone, now, parseAbsoluteToLocal } from "@internationalized/date";
import type { ZonedDateTime } from "@internationalized/date";
import { useQueryClient } from "@tanstack/react-query";
import {
  usePages,
  useCreatePage,
  useUpdatePage,
  useDeletePage,
  type PageRow,
  type PageInput,
} from "../../shared/hooks";
import { apiPut } from "../../shared/api";
import { PageHeader, Loading, ErrorState, Panel } from "../../shared/ui";

const PAGE_TYPES = ["Standard", "ExistingPage", "UrlRedirect", "File"] as const;
const byOrder = (a: PageRow, b: PageRow) => a.sortOrder - b.sortOrder || a.title.localeCompare(b.title);

interface PageNode {
  id: string;
  title: string;
  children: PageNode[];
}

function buildNodes(pages: PageRow[], parentId: string | null): PageNode[] {
  return pages
    .filter((p) => p.parentPageId === parentId)
    .sort(byOrder)
    .map((p) => ({ id: p.id, title: p.title, children: buildNodes(pages, p.id) }));
}

/** The "home" page (slug "/", legacy "home", else the first root) is shown as the tree's root node. */
function findHome(pages: PageRow[]): PageRow | null {
  const roots = pages.filter((p) => p.parentPageId === null);
  return roots.find((p) => p.slug === "/") ?? roots.find((p) => p.slug === "home") ?? roots[0] ?? null;
}

/**
 * Builds the tree with Home as the single root and every other root-level page nested beneath it.
 * This is display-only: pages "under Home" are still root-level (parentPageId null) in the data, so URLs
 * are unaffected; the drag persist maps them back accordingly.
 */
function buildTree(pages: PageRow[], home: PageRow | null): PageNode[] {
  if (!home) return buildNodes(pages, null);
  const underHome = [
    ...pages.filter((p) => p.parentPageId === null && p.id !== home.id),
    ...pages.filter((p) => p.parentPageId === home.id),
  ]
    .sort(byOrder)
    .map((p) => ({ id: p.id, title: p.title, children: buildNodes(pages, p.id) }));
  return [{ id: home.id, title: home.title, children: underHome }];
}

function toInput(p: PageRow): PageInput {
  return {
    title: p.title,
    slug: p.slug,
    metaTitle: p.metaTitle,
    metaDescription: p.metaDescription,
    seoKeywords: p.seoKeywords,
    canonicalUrl: p.canonicalUrl,
    published: p.published,
    disabled: p.disabled,
    displayInMenu: p.displayInMenu,
    parentPageId: p.parentPageId,
    sortOrder: p.sortOrder,
    pageType: p.type,
    targetUrl: p.targetUrl,
    fileReference: p.fileReference,
    scheduledPublishDate: p.scheduledPublishDate,
    scheduledUnpublishDate: p.scheduledUnpublishDate,
  };
}

const folderIcon = ({ isExpanded }: { isExpanded: boolean }) =>
  isExpanded ? <FolderOpen className="size-4" /> : <Folder className="size-4" />;

export default function ContentManagerPage() {
  const { data: pages, isLoading, error } = usePages();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const createPage = useCreatePage();

  if (isLoading) {
    return (
      <>
        <PageHeader title="Content" />
        <Loading />
      </>
    );
  }
  if (error || !pages) {
    return (
      <>
        <PageHeader title="Content" />
        <ErrorState error={error} />
      </>
    );
  }

  const selected = pages.find((p) => p.id === selectedId) ?? null;
  // Re-mounts the tree when the page structure/titles change (so it reflects server state after edits).
  const signature = pages
    .map((p) => `${p.id}:${p.title}:${p.parentPageId}:${p.sortOrder}`)
    .sort()
    .join("|");

  async function addPage() {
    const home = findHome(pages ?? []);
    // "Under Home" (the visual root) means a root-level page (parentPageId null).
    const parentId = selected && selected.id !== home?.id ? selected.id : null;
    const siblings = (pages ?? []).filter((p) => p.parentPageId === parentId).length;
    const input: PageInput = {
      title: "Untitled page",
      slug: `new-page-${Math.floor(Math.random() * 100000)}`,
      metaTitle: null,
      metaDescription: null,
      seoKeywords: null,
      canonicalUrl: null,
      published: false,
      disabled: false,
      displayInMenu: false,
      parentPageId: parentId,
      sortOrder: siblings,
      pageType: "Standard",
      targetUrl: null,
      fileReference: null,
      scheduledPublishDate: null,
      scheduledUnpublishDate: null,
    };
    const created = await createPage.mutateAsync(input);
    setSelectedId(created.id);
  }

  return (
    <>
      <PageHeader
        title="Content"
        description="Drag pages to re-parent or reorder. Select a page to edit its settings."
        actions={
          <Button onPress={() => void addPage()} isDisabled={createPage.isPending}>
            <FilePlus2 className="size-4" />
            New page
          </Button>
        }
      />

      <div className="grid gap-6 lg:grid-cols-[300px_1fr]">
        <PageTree key={signature} pages={pages} selectedId={selectedId} onSelect={setSelectedId} />

        {selected ? (
          <PageSettingsForm key={selected.id} page={selected} onDeleted={() => setSelectedId(null)} />
        ) : (
          <Panel title="Settings">
            <p className="text-muted text-sm">Select a page in the tree to edit its settings.</p>
          </Panel>
        )}
      </div>
    </>
  );
}

// ---------- drag-and-drop page tree ----------

function PageTree({
  pages,
  selectedId,
  onSelect,
}: {
  pages: PageRow[];
  selectedId: string | null;
  onSelect: (id: string | null) => void;
}) {
  const qc = useQueryClient();
  const home = useMemo(() => findHome(pages), [pages]);
  const initialItems = useMemo(() => buildTree(pages, home), [pages, home]);
  const tree = useTreeData<PageNode>({
    getChildren: (item) => item.children,
    getKey: (item) => item.id,
    initialItems,
  });

  const expandedKeys = useMemo(() => {
    const ids: string[] = [];
    const collect = (nodes: PageNode[]) =>
      nodes.forEach((n) => {
        if (n.children.length) {
          ids.push(n.id);
          collect(n.children);
        }
      });
    collect(initialItems);
    return ids;
  }, [initialItems]);

  // After a drag completes, persist the new parent + sibling order to the backend.
  async function persist() {
    const flat: { key: string; parentKey: string | null; index: number }[] = [];
    const walk = (nodes: typeof tree.items, parentKey: string | null) =>
      nodes.forEach((n, i) => {
        flat.push({ key: String(n.key), parentKey, index: i });
        walk(n.children ?? [], String(n.key));
      });
    walk(tree.items, null);

    const updates: { id: string; input: PageInput }[] = [];
    for (const f of flat) {
      if (home && f.key === home.id) continue; // Home stays the tree root; it is never re-parented.
      // A node directly under the Home root maps to a root-level page (parentPageId null).
      const actualParent = home && f.parentKey === home.id ? null : f.parentKey;
      const page = pages.find((p) => p.id === f.key);
      if (page && (page.parentPageId !== actualParent || page.sortOrder !== f.index)) {
        updates.push({ id: page.id, input: { ...toInput(page), parentPageId: actualParent, sortOrder: f.index } });
      }
    }

    if (updates.length) {
      await Promise.all(updates.map((u) => apiPut(`/content/pages/${u.id}`, u.input)));
      await qc.invalidateQueries({ queryKey: ["pages"] });
    }
  }

  const { dragAndDropHooks } = useFileTreeDrag({ tree, onMove: () => void persist() });

  return (
    <FileTree
      aria-label="Pages"
      items={tree.items}
      dragAndDropHooks={dragAndDropHooks}
      defaultExpandedKeys={expandedKeys}
      onAction={(key) => onSelect(String(key) === selectedId ? null : String(key))}
    >
      {function renderItem(item: (typeof tree.items)[number]): ReactNode {
        const hasChildren = (item.children?.length ?? 0) > 0;
        return (
          <FileTree.Item
            icon={hasChildren ? folderIcon : <FileText className="size-4 shrink-0" />}
            id={item.key}
            textValue={item.value.title}
            title={item.value.title}
            className={String(item.key) === selectedId ? "bg-default-200 rounded-md" : undefined}
          >
            {hasChildren ? <Collection items={item.children ?? []}>{renderItem}</Collection> : null}
          </FileTree.Item>
        );
      }}
    </FileTree>
  );
}

// ---------- settings panel ----------

function toDateValue(iso: string | null): ZonedDateTime | null {
  if (!iso) return null;
  try {
    return parseAbsoluteToLocal(iso);
  } catch {
    return null;
  }
}

function PageSettingsForm({
  page,
  onDeleted,
}: {
  page: PageRow;
  onDeleted: () => void;
}) {
  const [form, setForm] = useState<PageInput>(() => toInput(page));
  const [error, setError] = useState<string | null>(null);
  const [scheduleOn, setScheduleOn] = useState(
    () => Boolean(page.scheduledPublishDate || page.scheduledUnpublishDate),
  );
  const update = useUpdatePage();
  const remove = useDeletePage();

  function set<K extends keyof PageInput>(key: K, value: PageInput[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  // Published and Disabled are mutually exclusive (a disabled page cannot be published).
  function setPublished(value: boolean) {
    setForm((f) => ({ ...f, published: value, disabled: value ? false : f.disabled }));
  }
  function setDisabled(value: boolean) {
    setForm((f) => ({ ...f, disabled: value, published: value ? false : f.published }));
  }

  // Toggling scheduling off clears any scheduled dates.
  function toggleSchedule(value: boolean) {
    setScheduleOn(value);
    if (!value) {
      setForm((f) => ({ ...f, scheduledPublishDate: null, scheduledUnpublishDate: null }));
    }
  }

  async function save() {
    setError(null);
    try {
      // Parent + sort order are owned by the tree (drag-and-drop); keep the page's live values.
      await update.mutateAsync({
        id: page.id,
        input: { ...form, parentPageId: page.parentPageId, sortOrder: page.sortOrder },
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Save failed.");
    }
  }

  async function del() {
    if (!window.confirm(`Delete “${page.title}”? Any child pages are re-parented, not deleted.`)) return;
    setError(null);
    try {
      await remove.mutateAsync(page.id);
      onDeleted();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Delete failed.");
    }
  }

  return (
    <Panel title={`Settings - ${page.title}`}>
      {error ? (
        <div className="mb-4 rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</div>
      ) : null}

      <form
        className="space-y-6"
        onSubmit={(e) => {
          e.preventDefault();
          void save();
        }}
      >
        <Section title="General">
          <TextRow label="Title" value={form.title} onChange={(v) => set("title", v)} />
          <TextRow label="Slug" value={form.slug} onChange={(v) => set("slug", v)} />
          <SelectRow
            label="Page type"
            value={form.pageType}
            onChange={(v) => set("pageType", v)}
            options={PAGE_TYPES.map((t) => ({ id: t, label: t }))}
            wide
          />
        </Section>

        {form.pageType === "UrlRedirect" ? (
          <TextRow
            label="Target URL"
            value={form.targetUrl ?? ""}
            onChange={(v) => set("targetUrl", v || null)}
            wide
            placeholder="https://…"
          />
        ) : null}
        {form.pageType === "File" ? (
          <TextRow
            label="File reference"
            value={form.fileReference ?? ""}
            onChange={(v) => set("fileReference", v || null)}
            wide
            placeholder="asset id / path"
          />
        ) : null}

        <Section title="SEO">
          <TextRow label="Meta title" value={form.metaTitle ?? ""} onChange={(v) => set("metaTitle", v || null)} />
          <TextRow
            label="SEO keywords"
            value={form.seoKeywords ?? ""}
            onChange={(v) => set("seoKeywords", v || null)}
            placeholder="comma,separated"
          />
          <TextRow
            label="Meta description"
            value={form.metaDescription ?? ""}
            onChange={(v) => set("metaDescription", v || null)}
            wide
          />
          <TextRow
            label="Canonical URL"
            value={form.canonicalUrl ?? ""}
            onChange={(v) => set("canonicalUrl", v || null)}
            wide
          />
        </Section>

        <Section title="Publishing">
          <CheckRow label="Published" checked={form.published} onChange={setPublished} />
          <CheckRow label="Disabled" checked={form.disabled} onChange={setDisabled} />
          {form.published ? (
            <>
              <CheckRow
                label="Display in menu"
                checked={form.displayInMenu}
                onChange={(v) => set("displayInMenu", v)}
              />
              <CheckRow label="Schedule publishing" checked={scheduleOn} onChange={toggleSchedule} />
              {scheduleOn ? (
                <>
                  <DatePickerRow
                    label="Scheduled publish"
                    value={form.scheduledPublishDate}
                    onChange={(v) => set("scheduledPublishDate", v)}
                  />
                  <DatePickerRow
                    label="Scheduled unpublish"
                    value={form.scheduledUnpublishDate}
                    onChange={(v) => set("scheduledUnpublishDate", v)}
                  />
                </>
              ) : null}
            </>
          ) : null}
        </Section>

        <div className="flex items-center gap-2">
          <Button onPress={() => void save()} isDisabled={update.isPending}>
            <Save className="size-4" />
            {update.isPending ? "Saving…" : "Save"}
          </Button>
          <Button variant="ghost" onPress={() => void del()} isDisabled={remove.isPending} className="text-danger">
            <Trash2 className="size-4" />
            Delete
          </Button>
        </div>
      </form>
    </Panel>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div>
      <h3 className="text-muted mb-3 text-xs font-semibold uppercase tracking-wide">{title}</h3>
      <div className="grid items-end gap-3 sm:grid-cols-2">{children}</div>
    </div>
  );
}

function TextRow({
  label,
  value,
  onChange,
  wide,
  placeholder,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  wide?: boolean;
  placeholder?: string;
}) {
  return (
    <TextField className={`flex flex-col gap-1 text-sm ${wide ? "sm:col-span-2" : ""}`} value={value} onChange={onChange}>
      <Label className="text-foreground font-medium">{label}</Label>
      <Input placeholder={placeholder} variant="secondary" />
    </TextField>
  );
}

function SelectRow({
  label,
  value,
  onChange,
  options,
  wide,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: { id: string; label: string }[];
  wide?: boolean;
}) {
  return (
    <Select
      aria-label={label}
      className={`flex flex-col gap-1 text-sm ${wide ? "sm:col-span-2" : ""}`}
      variant="secondary"
      placeholder="Select…"
      value={value}
      onChange={(v) => {
        if (v != null) onChange(String(v));
      }}
    >
      <Label className="text-foreground font-medium">{label}</Label>
      <Select.Trigger>
        <Select.Value />
        <Select.Indicator />
      </Select.Trigger>
      <Select.Popover>
        <ListBox>
          {options.map((o) => (
            <ListBox.Item key={o.id} id={o.id} textValue={o.label}>
              {o.label}
              <ListBox.ItemIndicator />
            </ListBox.Item>
          ))}
        </ListBox>
      </Select.Popover>
    </Select>
  );
}

function CheckRow({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <Checkbox variant="secondary" isSelected={checked} onChange={onChange}>
      <Checkbox.Control>
        <Checkbox.Indicator />
      </Checkbox.Control>
      <Checkbox.Content>
        <Label className="text-foreground font-medium">{label}</Label>
      </Checkbox.Content>
    </Checkbox>
  );
}

function DatePickerRow({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string | null;
  onChange: (value: string | null) => void;
}) {
  return (
    <DatePicker
      className="flex flex-col gap-1 text-sm"
      granularity="minute"
      hideTimeZone
      placeholderValue={now(getLocalTimeZone())}
      value={toDateValue(value)}
      onChange={(v) => onChange(v ? (v as ZonedDateTime).toDate().toISOString() : null)}
    >
      <Label className="text-foreground font-medium">{label}</Label>
      <DateField.Group fullWidth variant="secondary">
        <DateField.Input>{(segment) => <DateField.Segment segment={segment} />}</DateField.Input>
        <DateField.Suffix>
          <DatePicker.Trigger>
            <DatePicker.TriggerIndicator />
          </DatePicker.Trigger>
        </DateField.Suffix>
      </DateField.Group>
      <DatePicker.Popover>
        <Calendar aria-label={label}>
          <Calendar.Header>
            <Calendar.YearPickerTrigger>
              <Calendar.YearPickerTriggerHeading />
              <Calendar.YearPickerTriggerIndicator />
            </Calendar.YearPickerTrigger>
            <Calendar.NavButton slot="previous" />
            <Calendar.NavButton slot="next" />
          </Calendar.Header>
          <Calendar.Grid>
            <Calendar.GridHeader>{(day) => <Calendar.HeaderCell>{day}</Calendar.HeaderCell>}</Calendar.GridHeader>
            <Calendar.GridBody>{(date) => <Calendar.Cell date={date} />}</Calendar.GridBody>
          </Calendar.Grid>
          <Calendar.YearPickerGrid>
            <Calendar.YearPickerGridBody>
              {({ year }) => <Calendar.YearPickerCell year={year} />}
            </Calendar.YearPickerGridBody>
          </Calendar.YearPickerGrid>
        </Calendar>
      </DatePicker.Popover>
    </DatePicker>
  );
}
