# getcmd.com

Static site for getcmd, served by Cloudflare Pages straight from this directory. No framework, no build step, no JavaScript. Pages is connected to the GitHub repo and deploys `site/` on every push to `main`.

| Path | What |
|---|---|
| `index.html`, `style.css`, `favicon.svg` | The landing page |
| `install`, `install.ps1` | Copies of `../install.sh` and `../install.ps1`; CI fails if they differ |
| `_headers`, `_redirects` | Content types, caching, security headers, `/gh` and `/releases` shortcuts |
| `robots.txt`, `sitemap.xml` | Crawler files |

## One-time setup

1. Cloudflare dashboard → **Workers & Pages** → **Create** → **Pages** → **Connect to Git**, authorise Cloudflare on GitHub and pick `getCMD-com/getcmd`.
2. Build settings: production branch `main`, framework preset **None**, build command empty, build output directory `site`.
3. Deploy. The first build gives you `getcmd.pages.dev`.
4. **Custom domains** → add `getcmd.com` and `www.getcmd.com`. The zone must already be on Cloudflare; Pages creates the DNS records.

Every push to `main` redeploys, including pushes that do not touch `site/`. That is harmless.

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
