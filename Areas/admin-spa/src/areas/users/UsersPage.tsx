import { useUsers, type UserRow } from "../../shared/hooks";
import { PageHeader, Loading, ErrorState, DataTable, StatusPill, formatDate, type Column } from "../../shared/ui";

const columns: Column<UserRow>[] = [
  { key: "email", header: "Email", render: (u) => <span className="font-medium text-slate-900">{u.email}</span> },
  { key: "name", header: "Name", render: (u) => u.name || "-" },
  { key: "roles", header: "Roles", render: (u) => u.roles.join(", ") || "-" },
  {
    key: "status",
    header: "Status",
    render: (u) =>
      u.status === "Enabled" ? <StatusPill tone="ok">Enabled</StatusPill> : <StatusPill tone="warn">Disabled</StatusPill>,
  },
  { key: "lastLogin", header: "Last login", render: (u) => formatDate(u.lastLogin) },
];

export default function UsersPage() {
  const { data, isLoading, error } = useUsers();
  return (
    <>
      <PageHeader title="Users" description="Accounts in the active tenant." />
      {isLoading ? (
        <Loading />
      ) : error ? (
        <ErrorState error={error} />
      ) : (
        <DataTable columns={columns} rows={data ?? []} empty="No users yet." />
      )}
    </>
  );
}
