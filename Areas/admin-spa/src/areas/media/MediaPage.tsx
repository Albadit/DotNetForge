import { useMedia, type MediaRow } from "../../shared/hooks";
import { PageHeader, Loading, ErrorState, DataTable, StatusPill, formatDate, type Column } from "../../shared/ui";

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

const columns: Column<MediaRow>[] = [
  { key: "name", header: "File", render: (m) => <span className="font-medium text-slate-900">{m.fileName}</span> },
  { key: "type", header: "Type", render: (m) => <code className="text-xs text-slate-500">{m.contentType}</code> },
  { key: "size", header: "Size", render: (m) => formatSize(m.sizeBytes) },
  {
    key: "access",
    header: "Access",
    render: (m) => (m.isPublic ? <StatusPill tone="ok">Public</StatusPill> : <StatusPill tone="muted">Private</StatusPill>),
  },
  { key: "uploaded", header: "Uploaded", render: (m) => formatDate(m.uploadedDate) },
];

export default function MediaPage() {
  const { data, isLoading, error } = useMedia();
  return (
    <>
      <PageHeader title="Media" description="Files in the active tenant." />
      {isLoading ? (
        <Loading />
      ) : error ? (
        <ErrorState error={error} />
      ) : (
        <DataTable columns={columns} rows={data ?? []} empty="No media uploaded yet." />
      )}
    </>
  );
}
