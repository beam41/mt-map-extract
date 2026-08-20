# Publishing to the live wiki

The generator (`wiki/`) only ever writes local `.txt` files to `out/wiki/` — there is no
write path anywhere in the codebase (`LiveWiki.cs` is read-only: it fetches the `image =`
line via `?do=export_raw` for the infobox merge). This doc covers the **manual/ad-hoc**
publish flow used to push a generated page straight to `https://wiki.aseanmotorclub.com/`
when asked to — plain HTTP, no browser required.

## The mechanism

DokuWiki's edit form is a stateless, unauthenticated HTML form POST on this instance — no
login, no session cookie, no CSRF token (`sectok` comes back empty and isn't enforced).
Two requests, no browser/DOM automation needed:

1. **GET** `https://wiki.aseanmotorclub.com/doku.php?id={pageid}&do=edit` — the response
   HTML has the edit form's hidden inputs baked in: `sectok`, `id`, `rev`, `date`,
   `prefix`, `suffix`, `changecheck`, `target`. Scrape them (a couple of regexes suffice).
2. **POST** `https://wiki.aseanmotorclub.com/{pageid}?do=edit` (page-path URL, not
   `doku.php`) as `application/x-www-form-urlencoded`, with every hidden field from step 1
   passed through **unmodified**, plus `wikitext=<full new page content>` and
   `do[save]=Save`. Success is an HTTP **302** redirect to `https://wiki.aseanmotorclub.com/{pageid}`.

`curl` example (verified 2026-08-20, `list_of_parts`):

```bash
curl -s "https://wiki.aseanmotorclub.com/doku.php?id=list_of_parts&do=edit" -o edit.html
# scrape sectok/id/rev/date/prefix/suffix/changecheck/target out of edit.html, then:
curl -s -o resp.html -w "%{http_code} -> %{redirect_url}\n" \
  --data-urlencode "id=list_of_parts" \
  --data-urlencode "rev=0" \
  --data-urlencode "date=<from edit.html>" \
  --data-urlencode "prefix=." \
  --data-urlencode "suffix=" \
  --data-urlencode "changecheck=<from edit.html>" \
  --data-urlencode "target=section" \
  --data-urlencode "summary=" \
  --data-urlencode "wikitext@out/wiki/list_of_parts.txt" \
  --data-urlencode "do[save]=Save" \
  "https://wiki.aseanmotorclub.com/list_of_parts?do=edit"
```

Verify by diffing `curl -s "https://wiki.aseanmotorclub.com/{pageid}?do=export_raw"`
against the local `out/wiki/{...}.txt` — should be byte-identical.

## Gotchas

- **`changecheck`/`date` must be fresh**: they come from the *same* GET response used for
  the POST — reusing stale values (e.g. from an earlier session) can trip DokuWiki's
  edit-conflict detection. GET immediately before POST.
- **`prefix`/`suffix` are DokuWiki's own bookkeeping**, not something to hand-construct —
  pass through whatever the GET returned (`prefix="."`, `suffix=""` for a normal full-page
  edit). Don't guess at their semantics.
- **`do[save]` needs bracket-safe encoding** — `curl --data-urlencode "do[save]=Save"`
  handles it; hand-rolled URL-encoding must percent-encode `[`/`]` (`do%5Bsave%5D=Save`).
- **Page id uses `:` as the namespace separator** in both the URL and the `id` field,
  matching `out/wiki/`'s directory nesting with `/` → `:` (e.g.
  `out/wiki/parts/buslicense0/installable_vehicles.txt` → id
  `parts:buslicense0:installable_vehicles`).
- **No browser/session needed at all** — an earlier attempt drove a headless Chromium tab
  (DOM `value` setter + dispatched `input`/`change` events + a real button click) to submit
  this same form; it worked but was pure overhead. A plain `curl` GET+POST round-trip does
  the identical thing with two requests and no browser process.

## What's safe to overwrite this way

Only the generator's own **bot-owned** pages: `list_of_*.txt`, `vehicle_comparison.txt`,
and every `{ns}:{slug}:auto_infobox` / `{slug}:auto_details` / `{slug}:installable_*`
subpage (see `.agents/knowledge/wiki-pages.md` for the full split-page architecture).
**Never** push over a hand-curated live shell page (the flat `{ns}:{slug}` page a human
curator owns via `{{page>...}}` transclusion) — that's the one boundary the whole
split-page design exists to protect.
