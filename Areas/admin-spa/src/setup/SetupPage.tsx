import { useEffect, useState } from "react";
import { Check, Eye, EyeOff } from "lucide-react";
import { Button, Card, Description, FieldError, Form, Input, InputGroup, Label, TextField, ErrorMessage } from "@heroui/react";

async function fetchToken(): Promise<string> {
  const res = await fetch("/setup/antiforgery", { credentials: "include" });
  const data = (await res.json()) as { token: string };
  return data.token;
}

function validateEmail(value: string): string | null {
  return /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(value) ? null : "Please enter a valid email address.";
}

function validatePassword(value: string): string | null {
  if (value.length < 8) return "Password must be at least 8 characters.";
  if (!/[A-Za-z]/.test(value)) return "Password must contain at least one letter.";
  if (!/[0-9]/.test(value)) return "Password must contain at least one number.";
  return null;
}

/** A password TextField using HeroUI InputGroup with a show/hide eye toggle in the suffix. */
function PasswordField({
  name,
  label,
  placeholder,
  description,
  validate,
}: {
  name: string;
  label: string;
  placeholder: string;
  description?: string;
  validate?: (value: string) => string | null;
}) {
  const [isVisible, setIsVisible] = useState(false);
  return (
    <TextField isRequired name={name} validate={validate}>
      <Label>{label}</Label>
      <InputGroup variant="secondary">
        <InputGroup.Input
          type={isVisible ? "text" : "password"}
          placeholder={placeholder}
          autoComplete="new-password"
        />
        <InputGroup.Suffix className="pr-0">
          <Button
            isIconOnly
            aria-label={isVisible ? "Hide password" : "Show password"}
            size="sm"
            variant="ghost"
            onPress={() => setIsVisible(!isVisible)}
          >
            {isVisible ? <Eye className="size-4" /> : <EyeOff className="size-4" />}
          </Button>
        </InputGroup.Suffix>
      </InputGroup>
      {description && <Description>{description}</Description>}
      <FieldError />
    </TextField>
  );
}

/**
 * First-run setup wizard (installation_setup.md), rendered standalone at /setup (no admin layout).
 * Uses the HeroUI <Form>/<TextField> primitives for field-level validation, then posts to the
 * anonymous /setup install API with antiforgery (X-CSRF-TOKEN) and lands on /admin.
 */
export default function SetupPage() {
  const [token, setToken] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    fetchToken().then(setToken).catch(() => setToken(null));
  }, []);

  async function install(body: {
    firstName: string;
    lastName: string;
    email: string;
    password: string;
    confirmPassword: string;
  }) {
    if (body.password !== body.confirmPassword) {
      setError("Password and confirmation do not match.");
      return;
    }

    setSubmitting(true);
    try {
      const csrf = token ?? (await fetchToken());
      setToken(csrf);
      const res = await fetch("/setup", {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf },
        body: JSON.stringify(body),
      });

      if (res.ok) {
        const data = (await res.json()) as { redirect?: string };
        window.location.href = data.redirect || "/admin";
        return;
      }

      const data = (await res.json().catch(() => ({}))) as { error?: string };
      setError(data.error || `Setup failed (${res.status}).`);
    } catch {
      setError("Network error. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="grid min-h-screen place-items-center">
      <Card className="w-full max-w-sm">
        <Card.Header>
          <Card.Title>Welcome - let's get set up</Card.Title>
          <Card.Description>
            Create the first administrator. This account becomes the <strong>Super Admin</strong>.
          </Card.Description>
        </Card.Header>
        <Card.Content>
          <Form
            className="flex flex-col gap-4"
            onSubmit={(e) => {
            e.preventDefault();
            setError(null);
            const fd = new FormData(e.currentTarget);
              void install({
                firstName: String(fd.get("firstName") ?? ""),
                lastName: String(fd.get("lastName") ?? ""),
                email: String(fd.get("email") ?? ""),
                password: String(fd.get("password") ?? ""),
                confirmPassword: String(fd.get("confirmPassword") ?? ""),
              });
            }}
          >
            <div className="grid grid-cols-2 gap-3">
              <TextField name="firstName">
                <Label>First name</Label>
                <Input placeholder="Ada" autoComplete="given-name" variant="secondary"/>
              </TextField>
              <TextField name="lastName">
                <Label>Last name</Label>
                <Input placeholder="Lovelace" autoComplete="family-name" variant="secondary"/>
              </TextField>
            </div>

            <TextField isRequired name="email" type="email" validate={validateEmail}>
              <Label>Email</Label>
              <Input placeholder="you@example.com" autoComplete="username" variant="secondary"/>
              <FieldError />
            </TextField>

            <PasswordField
              name="password"
              label="Password"
              placeholder="Enter a password"
              description="At least 8 characters, including a letter and a number."
              validate={validatePassword}
            />

            <PasswordField name="confirmPassword" label="Confirm password" placeholder="Re-enter your password" />

            <Button type="submit" isDisabled={submitting} className="w-full">
              <Check className="size-4" />
              {submitting ? "Creating…" : "Create admin & install"}
            </Button>
            <ErrorMessage>{!!error && <>{error}</>}</ErrorMessage>
          </Form>
        </Card.Content>
      </Card>
    </div>
  );
}
