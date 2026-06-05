import { usePages, type PageRow } from "../../shared/hooks";
import { PageHeader, Loading, ErrorState, DataTable, StatusPill, formatDate, type Column } from "../../shared/ui";

const columns: Column<PageRow>[] = [
  { key: "title", header: "Title", render: (p) => <span className="font-medium text-slate-900">{p.title}</span> },
  { key: "slug", header: "Slug", render: (p) => <code className="text-xs text-slate-500">/{p.slug}</code> },
  { key: "type", header: "Type", render: (p) => p.type },
  {
    key: "status",
    header: "Status",
    render: (p) =>
      p.published ? <StatusPill tone="ok">Published</StatusPill> : <StatusPill tone="muted">Draft</StatusPill>,
  },
  { key: "menu", header: "In menu", render: (p) => (p.displayInMenu ? "Yes" : "No") },
  { key: "updated", header: "Updated", render: (p) => formatDate(p.updatedDate) },
];

export default function ContentPage() {
  const { data, isLoading, error } = usePages();
  return (
    <>
      <PageHeader title="Content" description="Pages in the active tenant." />
      {isLoading ? (
        <Loading />
      ) : error ? (
        <ErrorState error={error} />
      ) : (
        <DataTable columns={columns} rows={data ?? []} empty="No pages yet." />
      )}
    </>
  );
}
