# getcmd.com

Static site for getcmd, deployed to Cloudflare Pages by `.github/workflows/site.yml` on every push to `main` that touches `site/**`. No framework, no build step, no JavaScript.

| Path | What |
|---|---|
| `index.html`, `style.css`, `favicon.svg` | The landing page |
| `install`, `install.ps1` | Copies of `../install.sh` and `../install.ps1`; CI fails if they differ |
| `_headers`, `_redirects` | Content types, caching, security headers, `/gh` and `/releases` shortcuts |
| `robots.txt`, `sitemap.xml` | Crawler files |

## One-time setup

1. **Pages project.** Cloudflare dashboard → Workers & Pages → Create → Pages → "Upload assets" (direct upload, not Git) → name it `getcmd`. The first upload can be anything; the workflow replaces it.
2. **API token.** My Profile → API Tokens → Create Token → template "Edit Cloudflare Workers" (it covers Pages). Add it to the GitHub repo as the `CLOUDFLARE_API_TOKEN` secret, and the account id (Workers & Pages overview, right-hand column) as `CLOUDFLARE_ACCOUNT_ID`.
3. **Custom domain.** Pages project → Custom domains → add `getcmd.com` and `www.getcmd.com`. The zone must already be on Cloudflare; Pages creates the DNS records.
4. Push a change under `site/` (or re-run the workflow from the Actions tab) to deploy.

## Local preview

```sh
cd site
npx wrangler pages dev .
```

## Updating the install scripts

Edit `../install.sh` or `../install.ps1`, then copy them here:

```sh
cp install.sh site/install && cp install.ps1 site/install.ps1
```
