'use client';
import Link from 'next/link';
export default function ErrorPage({ reset }: { reset: () => void }) {
  return <main className="error-page"><h1>We couldn’t open the workspace</h1><p>The back office may be temporarily unavailable. Please try again.</p><button className="button button-primary" onClick={reset}>Try again</button><Link href="/login">Return to sign in</Link></main>;
}
