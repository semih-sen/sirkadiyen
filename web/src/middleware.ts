import { NextResponse } from 'next/server';
import type { NextRequest } from 'next/server';

// Next's rewrite proxy forwards x-forwarded-host but NOT x-forwarded-proto
// (verified against next/dist/server/lib/router-utils/proxy-request.js). The dev
// edge terminates TLS, so we tell the backend the original scheme explicitly here.
// The backend's UseForwardedHeaders then sets Request.IsHttps, which the Secure
// cookies and the antiforgery SSL guard require (ADR-066).
export function middleware(request: NextRequest) {
  const requestHeaders = new Headers(request.headers);
  requestHeaders.set('x-forwarded-proto', 'https');
  return NextResponse.next({ request: { headers: requestHeaders } });
}

export const config = { matcher: ['/api/:path*', '/health/:path*'] };
