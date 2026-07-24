// @ts-check

/**
 * Local development topology (ADR-052, "cross-site frontend deployment must
 * explicitly revisit CORS, credentialed requests and SameSite policy rather than
 * weakening cookies implicitly").
 *
 * We deliberately do NOT run the frontend cross-origin against Kestrel. Instead
 * the Next.js dev server is the only origin the browser talks to, and it proxies
 * every `/api/*` request to the backend server-side:
 *
 *     Browser --HTTPS--> https://localhost:3000  (this Next dev server)
 *                            |
 *                            +-- /api/:path*  --> BACKEND_ORIGIN (Kestrel)
 *
 * Consequences, and why this needs no backend change:
 *   - Same-origin from the browser's point of view, so there is NO CORS to
 *     configure and NO SameSite relaxation. The `SameSite=Lax`/`Strict` cookies
 *     work unchanged.
 *   - The browser<->Next edge is HTTPS (`next dev --experimental-https`), so the
 *     backend's `Secure` + `__Host-` session and antiforgery cookies are accepted
 *     and stored exactly as in production. Kestrel itself can stay on plain HTTP
 *     locally; only this edge terminates TLS, mirroring the production reverse
 *     proxy in front of Kestrel.
 *   - Next forwards Set-Cookie back to the browser and forwards the request
 *     cookies onward, so the whole cookie/CSRF loop is exercised for real.
 *
 * BACKEND_ORIGIN defaults to the HTTP Kestrel URL from
 * src/Sirkadiyen.Api/Properties/launchSettings.json. Point it at the HTTPS URL
 * (https://localhost:7080) only if you also make Node trust the ASP.NET dev cert.
 */
const backendOrigin = process.env.BACKEND_ORIGIN ?? 'http://localhost:5080';

/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  async rewrites() {
    return [
      {
        source: '/api/:path*',
        destination: `${backendOrigin}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
