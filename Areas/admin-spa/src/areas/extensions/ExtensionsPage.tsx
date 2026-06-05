import { RefreshCw } from "lucide-react";
import { Button } from "@heroui/react";
import { useExtensions, type ExtensionRow } from "../../shared/hooks";
import { PageHeader, Loading, ErrorState, DataTable, StatusPill, type Column } from "../../shared/ui";

const columns: Column<ExtensionRow>[] = [
  {
    key: "name",
    header: "Name",
    render: (x) => (
      <div>
        <div className="font-medium text-slate-900">{x.name}</div>
        <div className="text-xs text-slate-400">{x.id}</div>
      </div>
    ),
  },
  { key: "type", header: "Type", render: (x) => x.type },
  { key: "version", header: "Version", render: (x) => x.version },
  { key: "author", header: "Author", render: (x) => x.author },
  {
    key: "manifest",
    header: "Manifest",
    render: (x) => (x.validManifest ? <StatusPill tone="ok">valid</StatusPill> : <StatusPill tone="warn">invalid</StatusPill>),
  },
  { key: "status", header: "Status", render: (x) => <StatusPill tone="muted">{x.status}</StatusPill> },
];

export default function ExtensionsPage() {
  const { data, isLoading, error, refetch, isFetching } = useExtensions();

  return (
    <>
      <PageHeader
        title="Extensions"
        description="Installed extensions and on-disk extensions discovered under extensions/."
        actions={
          <Button isDisabled={isFetching} onPress={() => refetch()}>
            <RefreshCw size={15} />
            Refresh
          </Button>
        }
      />
      {isLoading ? (
        <Loading />
      ) : error ? (
        <ErrorState error={error} />
      ) : (
        <DataTable columns={columns} rows={data ?? []} empty="No extensions found." />
      )}
    </>
  );
}
