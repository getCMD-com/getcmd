// POST /api/subscribe: stores {email, ts} in the SUBSCRIBERS KV namespace.
// Accepts a plain form post (redirects back to the page) or JSON (answers JSON).
// Without the KV binding it still answers 200 and logs, so the page never breaks.

const MAX_EMAIL_LENGTH = 254;

export async function onRequestPost({ request, env }) {
  const contentType = request.headers.get("content-type") || "";
  const wantsJson = contentType.includes("application/json");

  let email = "";
  if (wantsJson) {
    email = String((await request.json().catch(() => ({}))).email || "");
  } else {
    email = String((await request.formData()).get("email") || "");
  }
  email = email.trim().toLowerCase();

  const valid = email.length <= MAX_EMAIL_LENGTH && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
  if (!valid) {
    return wantsJson
      ? Response.json({ ok: false, error: "invalid email" }, { status: 400 })
      : new Response("That doesn't look like an email address.", { status: 400 });
  }

  const ts = new Date().toISOString();
  if (env.SUBSCRIBERS) {
    // One key per sign-up, so nothing is overwritten and the list is easy to export.
    await env.SUBSCRIBERS.put(`sub:${ts}:${crypto.randomUUID()}`, JSON.stringify({ email, ts }));
  } else {
    console.log(`SUBSCRIBERS binding missing; not stored: ${email} at ${ts}`);
  }

  if (wantsJson) {
    return Response.json({ ok: true });
  }

  return Response.redirect(new URL("/#thanks", request.url).toString(), 303);
}

export function onRequestGet() {
  return new Response("POST an email field to this endpoint.", { status: 405, headers: { Allow: "POST" } });
}
