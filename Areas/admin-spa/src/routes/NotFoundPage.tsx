import { Link } from "react-router-dom";

export function NotFoundPage() {
  return (
    <div className="py-20 text-center">
      <h1 className="text-2xl font-semibold text-slate-900">Page not found</h1>
      <p className="mt-2 text-slate-500">This admin route doesn't exist.</p>
      <Link to="/admin/dashboard" className="mt-4 inline-block text-indigo-600 hover:underline">
        Back to dashboard
      </Link>
    </div>
  );
}
